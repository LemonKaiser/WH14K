using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Atmos;

/// <summary>
///     Simulates open sky on planetary maps by pulling unroofed tiles toward the map's ambient atmosphere.
/// </summary>
[UsedImplicitly]
public sealed partial class WH40KOutdoorAtmosphereSystem : EntitySystem
{
    [Dependency] private  AtmosphereSystem _atmos = default!;
    [Dependency] private  IConfigurationManager _cfg = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IMapManager _mapManager = default!;
    [Dependency] private  ITileDefinitionManager _tileDefs = default!;
    [Dependency] private  SharedMapSystem _map = default!;
    [Dependency] private  SharedRoofSystem _roof = default!;

    private const float MinIntervalSeconds = 0.05f;
    private const float GasDeltaEpsilon = 0.05f;
    private const float TemperatureDeltaEpsilon = 0.5f;

    private readonly Dictionary<(MapId MapId, Vector2i Position), bool> _roofedCache = new();
    private List<Entity<MapGridComponent>> _roofedGridBuffer = new();

    private float _accumulator;
    private bool _enabled = true;
    private float _intervalSeconds = 2.0f;
    private float _blendFactor = 0.5f;
    private float _temperatureBlendFactor = 0.25f;
    private GameTick _roofedCacheTick = GameTick.Zero;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(AtmosphereSystem));

        Subs.CVar(_cfg, CCVars.WH40KOutdoorAtmosphereEnabled, value => _enabled = value, true);
        Subs.CVar(_cfg,
            CCVars.WH40KOutdoorAtmosphereIntervalSeconds,
            value => _intervalSeconds = MathF.Max(MinIntervalSeconds, value),
            true);
        Subs.CVar(_cfg,
            CCVars.WH40KOutdoorAtmosphereBlendFactor,
            value => _blendFactor = Math.Clamp(value, 0f, 1f),
            true);
        Subs.CVar(_cfg,
            CCVars.WH40KOutdoorAtmosphereTemperatureBlendFactor,
            value => _temperatureBlendFactor = Math.Clamp(value, 0f, 1f),
            true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        _accumulator += frameTime;
        if (_accumulator < _intervalSeconds)
            return;

        _accumulator %= _intervalSeconds;

        var query = EntityQueryEnumerator<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var gridUid, out var gridAtmos, out var overlay, out var mapGrid, out var xform))
        {
            if (HasComp<ImplicitRoofComponent>(gridUid))
                continue;

            if (xform.MapUid is not { } mapUid)
                continue;

            if (!TryComp<MapAtmosphereComponent>(mapUid, out var mapAtmos) || mapAtmos.Space)
                continue;

            NormalizeOutdoorTiles(
                (gridUid, gridAtmos, overlay, mapGrid),
                (mapUid, mapAtmos));
        }
    }

    private void NormalizeOutdoorTiles(
        Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent> grid,
        Entity<MapAtmosphereComponent> map)
    {
        var target = map.Comp.Mixture;

        foreach (var tile in grid.Comp1.Tiles.Values)
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp3, tile.GridIndices, out var tileRef) ||
                tileRef.Tile.IsEmpty)
            {
                continue;
            }

            var current = _atmos.GetTileMixture((grid.Owner, grid.Comp1, grid.Comp2), (map.Owner, map.Comp), tile.GridIndices);
            if (current is not { Immutable: false })
                continue;

            if (!NeedsOutdoorRefresh(current, target))
                continue;

            if (IsRoovedTile(grid.Owner, grid.Comp3, tile.GridIndices))
                continue;

            var mutable = _atmos.GetTileMixture((grid.Owner, grid.Comp1, grid.Comp2), (map.Owner, map.Comp), tile.GridIndices, excite: true);
            if (mutable is not { Immutable: false })
                continue;

            BlendToward(mutable, target);
        }
    }

    private bool NeedsOutdoorRefresh(GasMixture current, GasMixture target)
    {
        for (var gasIndex = 0; gasIndex < Atmospherics.TotalNumberOfGases; gasIndex++)
        {
            if (MathF.Abs(target.GetMoles(gasIndex) - current.GetMoles(gasIndex)) >= GasDeltaEpsilon)
                return true;
        }

        return MathF.Abs(target.Temperature - current.Temperature) >= TemperatureDeltaEpsilon;
    }

    private void BlendToward(GasMixture current, GasMixture target)
    {
        for (var gasIndex = 0; gasIndex < Atmospherics.TotalNumberOfGases; gasIndex++)
        {
            var delta = target.GetMoles(gasIndex) - current.GetMoles(gasIndex);
            if (MathF.Abs(delta) < GasDeltaEpsilon)
                continue;

            current.AdjustMoles(gasIndex, delta * _blendFactor);
        }

        var temperatureDelta = target.Temperature - current.Temperature;
        if (MathF.Abs(temperatureDelta) < TemperatureDeltaEpsilon)
            return;

        current.Temperature += temperatureDelta * _temperatureBlendFactor;
    }

    private bool IsRoovedTile(EntityUid gridUid, MapGridComponent grid, Vector2i tileIndices)
    {
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

            var tileDef = (ContentTileDefinition) _tileDefs[tileRef.Tile.TypeId];
            if (tileDef.MapAtmosphere)
                continue;

            _roofedCache[cacheKey] = true;
            return true;
        }

        _roofedCache[cacheKey] = false;
        return false;
    }
}
