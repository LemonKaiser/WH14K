using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server._WH40K.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Content.Shared._WH40K.Fire;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Fire;

public sealed partial class WH40KLavaHeatSystem : EntitySystem
{
    private const float IntervalMultiplier = 2f;
    private static readonly ProtoId<TagPrototype> CatwalkTag = "Catwalk";

    [Dependency] private  AtmosphereSystem _atmosphere = default!;
    [Dependency] private  FlammableSystem _flammable = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  IMapManager _mapManager = default!;
    [Dependency] private  SharedMapSystem _map = default!;
    [Dependency] private  SharedRoofSystem _roof = default!;
    [Dependency] private  TagSystem _tags = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private  WH40KEnvironmentalFireSystem _environmentalFire = default!;

    private readonly Dictionary<(MapId MapId, Vector2i Position), bool> _roofedCache = new();
    private readonly HashSet<EntityUid> _tileEntities = new();
    private List<Entity<MapGridComponent>> _roofedGridBuffer = new();
    private GameTick _roofedCacheTick = GameTick.Zero;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(AtmosphereSystem));
        UpdatesAfter.Add(typeof(WH40KOutdoorAtmosphereSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KLavaHeatSourceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var heatSource, out var xform))
        {
            if (xform.GridUid is not { } gridUid || xform.MapUid is not { } mapUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            var origin = _transform.GetGridTilePositionOrDefault((uid, xform));
            var allowAtmosphericHeating = !heatSource.OnlyOutdoorAtmosphericHeat || !IsRoovedTile(gridUid, grid, origin);

            if (now >= heatSource.NextHeatAt)
            {
                ApplyHeatField(uid, gridUid, mapUid, origin, heatSource, allowAtmosphericHeating);
                heatSource.NextHeatAt = now + TimeSpan.FromSeconds(ScaleInterval(heatSource.HeatIntervalSeconds));
            }

            if (now >= heatSource.NextIgniteAt)
            {
                IgniteOccupantsStandingInLava(uid, gridUid, origin, heatSource);
                IgniteNearbyBurnableTiles(uid, gridUid, origin, heatSource);
                heatSource.NextIgniteAt = now + TimeSpan.FromSeconds(ScaleInterval(heatSource.IgniteIntervalSeconds));
            }
        }
    }

    private void ApplyHeatField(
        EntityUid source,
        EntityUid gridUid,
        EntityUid mapUid,
        Vector2i origin,
        WH40KLavaHeatSourceComponent heatSource,
        bool allowAtmosphericHeating)
    {
        if (!allowAtmosphericHeating)
            return;

        HeatTile(gridUid, mapUid, origin, heatSource.SourceTileMinTemperature);

        if (heatSource.ExposeSourceHotspot)
        {
            _atmosphere.HotspotExpose(
                gridUid,
                origin,
                heatSource.HotspotTemperature,
                heatSource.HotspotVolume,
                source,
                soh: true);
        }
    }

    private void HeatTile(EntityUid gridUid, EntityUid mapUid, Vector2i tile, float minTemperature)
    {
        if (minTemperature <= 0f)
            return;

        var mixture = _atmosphere.GetTileMixture(gridUid, mapUid, tile, excite: true);
        if (mixture is not { Immutable: false })
            return;

        mixture.Temperature = MathF.Max(mixture.Temperature, minTemperature);
    }

    private void IgniteNearbyBurnableTiles(EntityUid source, EntityUid gridUid, Vector2i origin, WH40KLavaHeatSourceComponent heatSource)
    {
        var radius = Math.Max(0, heatSource.BurnableTileIgniteRadius);
        if (radius == 0)
            return;

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                var tile = new Vector2i(origin.X + x, origin.Y + y);
                _environmentalFire.TryIgniteBurnableTile(gridUid, tile, source);
            }
        }
    }

    private void IgniteOccupantsStandingInLava(EntityUid source, EntityUid gridUid, Vector2i origin, WH40KLavaHeatSourceComponent heatSource)
    {
        if (heatSource.IgniteFireStacks <= 0f ||
            HasAnchoredCatwalk(gridUid, origin) ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !_map.TryGetTileRef(gridUid, grid, origin, out var tileRef))
        {
            return;
        }

        _tileEntities.Clear();
        _lookup.GetEntitiesInTile(tileRef, _tileEntities, LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries);

        foreach (var uid in _tileEntities)
        {
            if (uid == source || !TryComp<FlammableComponent>(uid, out var flammable))
                continue;

            _flammable.AdjustFireStacks(uid, heatSource.IgniteFireStacks, flammable, ignite: false);
            _flammable.Ignite(uid, source, flammable);
        }
    }

    private bool HasAnchoredCatwalk(EntityUid gridUid, Vector2i origin)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid) ||
            !_map.TryGetTileRef(gridUid, grid, origin, out var tileRef))
        {
            return false;
        }

        _tileEntities.Clear();
        _lookup.GetEntitiesInTile(tileRef, _tileEntities, LookupFlags.Static | LookupFlags.Sundries);

        foreach (var uid in _tileEntities)
        {
            if (!TryComp(uid, out TransformComponent? xform) ||
                !xform.Anchored ||
                !_tags.HasTag(uid, CatwalkTag))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool IsRoovedTile(EntityUid gridUid, MapGridComponent grid, Vector2i tileIndices)
    {
        if (HasComp<ImplicitRoofComponent>(gridUid))
            return true;

        if (TryComp<RoofComponent>(gridUid, out var roofComp) &&
            _roof.IsRooved((gridUid, grid, roofComp), tileIndices))
        {
            return true;
        }

        var mapCoords = _map.GridTileToWorld(gridUid, grid, tileIndices);
        return IsRoovedOnOverlappingGrids(mapCoords, gridUid);
    }

    private bool IsRoovedOnOverlappingGrids(MapCoordinates mapCoords, EntityUid currentGridUid)
    {
        if (_roofedCacheTick != _timing.CurTick)
        {
            _roofedCacheTick = _timing.CurTick;
            _roofedCache.Clear();
        }

        var quantizedPosition = new Vector2i(
            (int) MathF.Round(mapCoords.Position.X * 8f),
            (int) MathF.Round(mapCoords.Position.Y * 8f));
        var cacheKey = (mapCoords.MapId, quantizedPosition);
        if (_roofedCache.TryGetValue(cacheKey, out var cached))
            return cached;

        _roofedGridBuffer.Clear();
        var searchBounds = Box2.CenteredAround(mapCoords.Position, new Vector2(0.02f, 0.02f));
        _mapManager.FindGridsIntersecting(mapCoords.MapId, searchBounds, ref _roofedGridBuffer, approx: true, includeMap: false);

        foreach (var (gridUid, grid) in _roofedGridBuffer)
        {
            if (gridUid == currentGridUid)
                continue;

            var tileIndices = _map.WorldToTile(gridUid, grid, mapCoords.Position);
            if (TryComp<RoofComponent>(gridUid, out var roofComp) &&
                _roof.IsRooved((gridUid, grid, roofComp), tileIndices))
            {
                _roofedCache[cacheKey] = true;
                return true;
            }

            if (!HasComp<ImplicitRoofComponent>(gridUid) ||
                !_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef) ||
                tileRef.Tile.IsEmpty)
            {
                continue;
            }

            var tileDef = (ContentTileDefinition) _tileDefinitions[tileRef.Tile.TypeId];
            if (tileDef.MapAtmosphere)
                continue;

            _roofedCache[cacheKey] = true;
            return true;
        }

        _roofedCache[cacheKey] = false;
        return false;
    }

    private static float ScaleInterval(float seconds)
    {
        return Math.Max(0.1f, seconds * IntervalMultiplier);
    }
}
