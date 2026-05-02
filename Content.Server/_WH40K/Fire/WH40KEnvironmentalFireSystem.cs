using System;
using System.Collections.Generic;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Decals;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Content.Shared._WH40K.Fire;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Fire;

public sealed class WH40KEnvironmentalFireSystem : EntitySystem
{
    private const float BurnDurationMultiplier = 2f;
    private const float IntervalMultiplier = 2f;
    private const float FallbackGrassBurnTimeSeconds = 3f;
    private const float FallbackWoodBurnTimeSeconds = 5f;
    private const int FallbackSpreadRadius = 1;
    private const float FallbackSpreadIntervalSeconds = 1f;
    private const float FallbackSpreadFireStacks = 1f;
    private const float FallbackContactIgniteIntervalSeconds = 0.45f;
    private const float FallbackContactFireStacks = 1.5f;
    private const float FallbackHotspotTemperature = 900f;
    private const float FallbackHotspotVolume = 15f;
    private const float MinimumAirHeatTemperature = 340f;
    private const float MaximumAirHeatTemperature = 650f;
    private const float AirHeatTemperatureFactor = 0.4f;
    private const float VegetationDecalCleanupPadding = 0.5f;
    private static readonly EntProtoId DefaultTileFireEffectPrototype = "WH40KTileFireEffect";
    private static readonly ProtoId<ContentTileDefinition> BurnedGrassAshTile = "WH40KFloorBurnedGrassAsh";
    private static readonly ProtoId<ContentTileDefinition> BurnedPlanetGrassAshTile = "WH40KFloorBurnedPlanetGrassAsh";
    private static readonly ProtoId<ContentTileDefinition> BurnedWoodAshTile = "WH40KFloorBurnedWoodAsh";

    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TileSystem _tiles = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    private readonly Dictionary<string, ConfiguredBurnRuleData> _configuredBurnRulesByTileId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, BurnRuleData> _burnRulesByTileType = new();
    private readonly HashSet<int> _nonBurnableTileTypes = new();
    private readonly Dictionary<BurningTileKey, BurningTileState> _burningTiles = new();
    private readonly List<KeyValuePair<BurningTileKey, BurningTileState>> _burningTileSnapshot = new();
    private readonly HashSet<EntityUid> _burningEntities = new();
    private readonly List<EntityUid> _burningEntitySnapshot = new();
    private readonly List<CompletedTileBurn> _completedTiles = new();
    private readonly List<CompletedEntityBurn> _completedEntities = new();
    private readonly List<StaleBurningTile> _staleBurningTiles = new();
    private readonly HashSet<EntityUid> _tileEntities = new();
    private readonly Dictionary<int, List<Vector2i>> _areaOffsets = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<WH40KFireConsumableComponent, ComponentStartup>(OnFireConsumableStartup);
        SubscribeLocalEvent<WH40KFireConsumableComponent, ComponentShutdown>(OnFireConsumableShutdown);
        SubscribeLocalEvent<WH40KFireConsumableComponent, IgnitedEvent>(OnFireConsumableIgnited);
        SubscribeLocalEvent<WH40KFireConsumableComponent, ExtinguishedEvent>(OnFireConsumableExtinguished);
        CacheBurnableTiles();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WH40KBurnableTilePrototype>())
            CacheBurnableTiles();
    }

    private void OnFireConsumableStartup(Entity<WH40KFireConsumableComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<FlammableComponent>(ent, out var flammable) && flammable.OnFire)
            _burningEntities.Add(ent.Owner);
    }

    private void OnFireConsumableShutdown(Entity<WH40KFireConsumableComponent> ent, ref ComponentShutdown args)
    {
        _burningEntities.Remove(ent.Owner);
    }

    private void OnFireConsumableIgnited(Entity<WH40KFireConsumableComponent> ent, ref IgnitedEvent args)
    {
        _burningEntities.Add(ent.Owner);
    }

    private void OnFireConsumableExtinguished(Entity<WH40KFireConsumableComponent> ent, ref ExtinguishedEvent args)
    {
        _burningEntities.Remove(ent.Owner);
    }

    private void CacheBurnableTiles()
    {
        _configuredBurnRulesByTileId.Clear();
        _burnRulesByTileType.Clear();
        _nonBurnableTileTypes.Clear();

        foreach (var proto in _prototype.EnumeratePrototypes<WH40KBurnableTilePrototype>())
        {
            _configuredBurnRulesByTileId[proto.Tile.Id] = new ConfiguredBurnRuleData(
                proto.ResultTile.Id,
                IsBurnedVegetationTileId(proto.ResultTile.Id),
                proto.BurnTimeSeconds,
                proto.SpreadRadius,
                proto.SpreadIntervalSeconds,
                proto.SpreadFireStacks,
                proto.ContactIgniteIntervalSeconds,
                proto.ContactFireStacks,
                proto.HotspotTemperature,
                proto.HotspotVolume,
                proto.FireEffectPrototype);
        }
    }

    public bool TryIgniteBurnableTile(EntityUid gridUid, Vector2i indices, EntityUid? ignitionSource = null)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid) ||
            !_map.TryGetTileRef(gridUid, grid, indices, out var tileRef) ||
            tileRef.Tile.IsEmpty ||
            !TryGetBurnRule(tileRef, out var burnRule))
        {
            return false;
        }

        var key = new BurningTileKey(gridUid, tileRef.GridIndices);
        if (_burningTiles.ContainsKey(key))
            return true;

        _burningTiles[key] = CreateBurningTileState(burnRule, delayInitialSpread: true);
        ExposeHotspot(gridUid, tileRef.GridIndices, burnRule.HotspotTemperature, burnRule.HotspotVolume, ignitionSource);
        return true;
    }

    public override void Update(float frameTime)
    {
        UpdateBurningTiles(frameTime);
        UpdateBurningEntities(frameTime);
    }

    private void UpdateBurningTiles(float frameTime)
    {
        _completedTiles.Clear();
        _staleBurningTiles.Clear();
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<GridAtmosphereComponent, MapGridComponent>();
        while (query.MoveNext(out var gridUid, out var atmos, out var grid))
        {
            foreach (var hotspotTile in atmos.HotspotTiles)
            {
                if (!hotspotTile.Hotspot.Valid ||
                    !_map.TryGetTileRef(gridUid, grid, hotspotTile.GridIndices, out var tileRef) ||
                    tileRef.Tile.IsEmpty ||
                    !TryGetBurnRule(tileRef, out var burnRule))
                {
                    continue;
                }

                var key = new BurningTileKey(gridUid, hotspotTile.GridIndices);
                if (_burningTiles.ContainsKey(key))
                    continue;

                _burningTiles[key] = CreateBurningTileState(burnRule, delayInitialSpread: true);
            }
        }

        _burningTileSnapshot.Clear();
        foreach (var entry in _burningTiles)
        {
            _burningTileSnapshot.Add(entry);
        }

        foreach (var entry in _burningTileSnapshot)
        {
            var key = entry.Key;
            var state = entry.Value;

            if (!TryComp<MapGridComponent>(key.GridUid, out var grid) ||
                !_map.TryGetTileRef(key.GridUid, grid, key.Indices, out var tileRef) ||
                tileRef.Tile.IsEmpty)
            {
                _staleBurningTiles.Add(new StaleBurningTile(key, state));
                continue;
            }

            if (tileRef.Tile.TypeId != state.SourceTileTypeId)
            {
                _staleBurningTiles.Add(new StaleBurningTile(key, state));
                continue;
            }

            EnsureTileFireEffect(key.GridUid, grid, key.Indices, ref state);
            state.ProgressSeconds += frameTime;

            if (now >= state.NextContactIgniteTime)
            {
                var ignitionSource = state.FireEffectUid ?? key.GridUid;
                IgniteFlammableEntities(key.GridUid, key.Indices, state.ContactFireStacks, ignitionSource, environmentalOnly: false);
                state.NextContactIgniteTime = now + TimeSpan.FromSeconds(state.ContactIgniteIntervalSeconds);
            }

            if (now >= state.NextSpreadTime)
            {
                SpreadFromBurningTile(key.GridUid, key.Indices, state);
                state.NextSpreadTime = now + TimeSpan.FromSeconds(state.SpreadIntervalSeconds);
            }

            _burningTiles[key] = state;

            if (state.ProgressSeconds < state.BurnTimeSeconds)
                continue;

            _completedTiles.Add(new CompletedTileBurn(key.GridUid, key.Indices, state.SourceTileTypeId, state.ResultTileTypeId, state.CleanupVegetationDecals));
            _staleBurningTiles.Add(new StaleBurningTile(key, state));
        }

        foreach (var stale in _staleBurningTiles)
        {
            CleanupBurningTileState(stale.State);
            _burningTiles.Remove(stale.Key);
        }

        foreach (var completed in _completedTiles)
        {
            if (!TryComp<MapGridComponent>(completed.GridUid, out var grid) ||
                !_map.TryGetTileRef(completed.GridUid, grid, completed.Indices, out var tileRef) ||
                tileRef.Tile.IsEmpty)
            {
                continue;
            }

            if (tileRef.Tile.TypeId != completed.SourceTileTypeId)
                continue;

            var replacement = (ContentTileDefinition) _tileDefinitions[completed.ResultTileTypeId];
            _tiles.ReplaceTile(tileRef, replacement, completed.GridUid, grid);

            if (completed.CleanupVegetationDecals)
                CleanupVegetationDecals(completed.GridUid, completed.Indices);
        }
    }

    private void UpdateBurningEntities(float frameTime)
    {
        _completedEntities.Clear();
        _burningEntitySnapshot.Clear();

        foreach (var uid in _burningEntities)
        {
            _burningEntitySnapshot.Add(uid);
        }

        foreach (var uid in _burningEntitySnapshot)
        {
            if (!TryComp<WH40KFireConsumableComponent>(uid, out var consumable) ||
                !TryComp<FlammableComponent>(uid, out var flammable))
            {
                _burningEntities.Remove(uid);
                continue;
            }

            var xform = Transform(uid);

            if (!flammable.OnFire)
            {
                _burningEntities.Remove(uid);

                if (consumable.ResetProgressOnExtinguish)
                    consumable.BurnAccumulatedSeconds = 0f;

                consumable.NextHotspotExposeTime = TimeSpan.Zero;
                consumable.NextSpreadTime = TimeSpan.Zero;
                continue;
            }

            if (xform.GridUid is { } baseGridUid &&
                TryComp<MapGridComponent>(baseGridUid, out var baseGrid) &&
                _map.TryGetTileRef(baseGridUid, baseGrid, xform.Coordinates, out var baseTileRef))
            {
                TryIgniteBurnableTile(baseGridUid, baseTileRef.GridIndices, uid);
            }

            if (consumable.NextSpreadTime == TimeSpan.Zero)
                consumable.NextSpreadTime = _timing.CurTime + TimeSpan.FromSeconds(ScaleInterval(consumable.SpreadIntervalSeconds));

            var scaledBurnTime = ScaleBurnDuration(consumable.BurnTimeSeconds);
            consumable.BurnAccumulatedSeconds += frameTime;

            if (xform.GridUid is { } gridUid &&
                TryComp<MapGridComponent>(gridUid, out var grid) &&
                _timing.CurTime >= consumable.NextHotspotExposeTime &&
                _map.TryGetTileRef(gridUid, grid, xform.Coordinates, out var tileRef))
            {
                ExposeHotspot(gridUid, tileRef.GridIndices, consumable.HotspotTemperature, consumable.HotspotVolume, uid);
                consumable.NextHotspotExposeTime = _timing.CurTime + TimeSpan.FromSeconds(ScaleInterval(consumable.HotspotExposeIntervalSeconds));
            }

            if (xform.GridUid is { } spreadGridUid &&
                TryComp<MapGridComponent>(spreadGridUid, out var spreadGrid) &&
                _timing.CurTime >= consumable.NextSpreadTime &&
                _map.TryGetTileRef(spreadGridUid, spreadGrid, xform.Coordinates, out var spreadTileRef))
            {
                SpreadFromBurningEntity(uid, spreadGridUid, spreadTileRef.GridIndices, consumable);
                consumable.NextSpreadTime = _timing.CurTime + TimeSpan.FromSeconds(ScaleInterval(consumable.SpreadIntervalSeconds));
            }

            if (consumable.BurnAccumulatedSeconds < scaledBurnTime)
                continue;

            _completedEntities.Add(new CompletedEntityBurn(uid, xform.Coordinates, consumable.ResultPrototype, consumable.DeleteOnBurn));
            consumable.BurnAccumulatedSeconds = 0f;
            consumable.NextHotspotExposeTime = TimeSpan.Zero;
            consumable.NextSpreadTime = TimeSpan.Zero;
        }

        foreach (var completed in _completedEntities)
        {
            if (Deleted(completed.Uid))
                continue;

            if (TryComp<FlammableComponent>(completed.Uid, out var flammable))
                _flammable.Extinguish(completed.Uid, flammable);

            if (completed.ResultPrototype is { } resultPrototype)
                Spawn(resultPrototype, completed.Coordinates);

            if (completed.DeleteOnBurn)
                QueueDel(completed.Uid);
        }
    }

    private BurningTileState CreateBurningTileState(BurnRuleData burnRule, bool delayInitialSpread)
    {
        var scaledSpreadInterval = ScaleInterval(burnRule.SpreadIntervalSeconds);
        var scaledContactIgniteInterval = ScaleInterval(burnRule.ContactIgniteIntervalSeconds);

        return new BurningTileState(
            burnRule.SourceTileTypeId,
            burnRule.ResultTileTypeId,
            burnRule.CleanupVegetationDecals,
            ScaleBurnDuration(burnRule.BurnTimeSeconds),
            burnRule.SpreadRadius,
            scaledSpreadInterval,
            burnRule.SpreadFireStacks,
            scaledContactIgniteInterval,
            burnRule.ContactFireStacks,
            burnRule.HotspotTemperature,
            burnRule.HotspotVolume,
            burnRule.FireEffectPrototype)
        {
            NextSpreadTime = delayInitialSpread
                ? _timing.CurTime + TimeSpan.FromSeconds(scaledSpreadInterval)
                : TimeSpan.Zero,
            NextContactIgniteTime = delayInitialSpread
                ? _timing.CurTime + TimeSpan.FromSeconds(scaledContactIgniteInterval)
                : TimeSpan.Zero
        };
    }

    private void SpreadFromBurningTile(EntityUid gridUid, Vector2i origin, BurningTileState state)
    {
        ExposeHotspot(gridUid, origin, state.HotspotTemperature, state.HotspotVolume, null);

        foreach (var offset in GetAreaOffsets(state.SpreadRadius))
        {
            var tile = origin + offset;

            if (offset != Vector2i.Zero)
                TryIgniteBurnableTile(gridUid, tile);

            IgniteFlammableEntities(gridUid, tile, state.SpreadFireStacks, gridUid, environmentalOnly: true);
        }
    }

    private void SpreadFromBurningEntity(EntityUid source, EntityUid gridUid, Vector2i origin, WH40KFireConsumableComponent consumable)
    {
        foreach (var offset in GetAreaOffsets(consumable.SpreadRadius))
        {
            var tile = origin + offset;
            TryIgniteBurnableTile(gridUid, tile, source);
            IgniteFlammableEntities(gridUid, tile, consumable.SpreadFireStacks, source, environmentalOnly: true);
        }

        ExposeHotspot(gridUid, origin, consumable.SpreadHotspotTemperature, consumable.SpreadHotspotVolume, source);
    }

    private void ExposeHotspot(EntityUid gridUid, Vector2i tile, float temperature, float volume, EntityUid? source)
    {
        if (temperature <= 0f || volume <= 0f)
            return;

        _atmosphere.HotspotExpose(gridUid, tile, temperature, volume, source, true);
        HeatTileAir(gridUid, tile, temperature);
    }

    private void HeatTileAir(EntityUid gridUid, Vector2i tile, float sourceTemperature)
    {
        if (!TryComp<GridAtmosphereComponent>(gridUid, out var atmos) ||
            !TryComp<GasTileOverlayComponent>(gridUid, out var overlay))
        {
            return;
        }

        var air = _atmosphere.GetTileMixture((gridUid, atmos, overlay), null, tile, excite: true);
        if (air is not { Immutable: false })
            return;

        var targetTemperature = Math.Clamp(sourceTemperature * AirHeatTemperatureFactor, MinimumAirHeatTemperature, MaximumAirHeatTemperature);
        if (air.Temperature >= targetTemperature)
            return;

        air.Temperature = targetTemperature;
    }

    private void IgniteFlammableEntities(EntityUid gridUid, Vector2i tile, float fireStacks, EntityUid ignitionSource, bool environmentalOnly)
    {
        if (fireStacks <= 0f || !TryComp<MapGridComponent>(gridUid, out var grid) || !_map.TryGetTileRef(gridUid, grid, tile, out var tileRef))
            return;

        _tileEntities.Clear();
        _lookup.GetEntitiesInTile(tileRef, _tileEntities, LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries);

        foreach (var uid in _tileEntities)
        {
            if (environmentalOnly && !TryComp<WH40KFireConsumableComponent>(uid, out _))
                continue;

            if (!TryComp<FlammableComponent>(uid, out var flammable))
                continue;

            _flammable.AdjustFireStacks(uid, fireStacks, flammable, ignite: false);
            _flammable.Ignite(uid, ignitionSource, flammable);
        }
    }

    private void EnsureTileFireEffect(EntityUid gridUid, MapGridComponent grid, Vector2i tile, ref BurningTileState state)
    {
        if (state.FireEffectUid is { } existing && !Deleted(existing))
            return;

        state.FireEffectUid = Spawn(state.FireEffectPrototype, _map.ToCenterCoordinates(gridUid, tile, grid));
    }

    private void CleanupBurningTileState(BurningTileState state)
    {
        if (state.FireEffectUid is { } effectUid && !Deleted(effectUid))
            QueueDel(effectUid);
    }

    private bool TryGetBurnRule(TileRef tileRef, out BurnRuleData burnRule)
    {
        var tileTypeId = tileRef.Tile.TypeId;
        if (_burnRulesByTileType.TryGetValue(tileTypeId, out burnRule))
            return true;

        if (_nonBurnableTileTypes.Contains(tileTypeId))
            return false;

        var tileDefinition = (ContentTileDefinition) _tileDefinitions[tileTypeId];
        if (TryGetConfiguredBurnRule(tileDefinition, out burnRule))
        {
            _burnRulesByTileType[tileTypeId] = burnRule;
            return true;
        }

        if (TryInferBurnRule(tileDefinition, out burnRule))
        {
            _burnRulesByTileType[tileTypeId] = burnRule;
            return true;
        }

        _nonBurnableTileTypes.Add(tileTypeId);
        return false;
    }

    private bool TryGetConfiguredBurnRule(ContentTileDefinition tileDefinition, out BurnRuleData burnRule)
    {
        if (!_configuredBurnRulesByTileId.TryGetValue(tileDefinition.ID, out var configured))
        {
            burnRule = default;
            return false;
        }

        return TryResolveBurnRule(
            tileDefinition,
            configured.ResultTileId,
            configured.CleanupVegetationDecals,
            configured.BurnTimeSeconds,
            configured.SpreadRadius,
            configured.SpreadIntervalSeconds,
            configured.SpreadFireStacks,
            configured.ContactIgniteIntervalSeconds,
            configured.ContactFireStacks,
            configured.HotspotTemperature,
            configured.HotspotVolume,
            configured.FireEffectPrototype,
            out burnRule);
    }

    private bool TryInferBurnRule(ContentTileDefinition tileDefinition, out BurnRuleData burnRule)
    {
        var tileId = tileDefinition.ID;

        if (tileId.Contains("Burned", StringComparison.OrdinalIgnoreCase))
        {
            burnRule = default;
            return false;
        }

        if (IsGrassLikeTileId(tileId))
        {
            return TryResolveBurnRule(
                tileDefinition,
                IsPlanetGrassLikeTileId(tileId) ? BurnedPlanetGrassAshTile.Id : BurnedGrassAshTile.Id,
                true,
                FallbackGrassBurnTimeSeconds,
                FallbackSpreadRadius,
                FallbackSpreadIntervalSeconds,
                FallbackSpreadFireStacks,
                FallbackContactIgniteIntervalSeconds,
                FallbackContactFireStacks,
                FallbackHotspotTemperature,
                FallbackHotspotVolume,
                DefaultTileFireEffectPrototype,
                out burnRule);
        }

        if (IsWoodLikeTileId(tileId))
        {
            return TryResolveBurnRule(
                tileDefinition,
                BurnedWoodAshTile.Id,
                false,
                FallbackWoodBurnTimeSeconds,
                FallbackSpreadRadius,
                FallbackSpreadIntervalSeconds,
                FallbackSpreadFireStacks,
                FallbackContactIgniteIntervalSeconds,
                FallbackContactFireStacks,
                FallbackHotspotTemperature,
                FallbackHotspotVolume,
                DefaultTileFireEffectPrototype,
                out burnRule);
        }

        burnRule = default;
        return false;
    }

    private bool TryResolveBurnRule(
        ContentTileDefinition sourceTile,
        string resultTileId,
        bool cleanupVegetationDecals,
        float burnTimeSeconds,
        int spreadRadius,
        float spreadIntervalSeconds,
        float spreadFireStacks,
        float contactIgniteIntervalSeconds,
        float contactFireStacks,
        float hotspotTemperature,
        float hotspotVolume,
        EntProtoId fireEffectPrototype,
        out BurnRuleData burnRule)
    {
        if (!_tileDefinitions.TryGetDefinition(resultTileId, out var resultTileDef) ||
            resultTileDef is not ContentTileDefinition resultTile)
        {
            burnRule = default;
            return false;
        }

        burnRule = new BurnRuleData(
            sourceTile.TileId,
            resultTile.TileId,
            cleanupVegetationDecals,
            burnTimeSeconds,
            spreadRadius,
            spreadIntervalSeconds,
            spreadFireStacks,
            contactIgniteIntervalSeconds,
            contactFireStacks,
            hotspotTemperature,
            hotspotVolume,
            fireEffectPrototype);
        return true;
    }

    private static bool IsGrassLikeTileId(string tileId)
    {
        return tileId.Contains("Grass", StringComparison.OrdinalIgnoreCase) ||
               tileId.Contains("MattedGrass", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlanetGrassLikeTileId(string tileId)
    {
        return tileId.Contains("Planet", StringComparison.OrdinalIgnoreCase) ||
               tileId.Contains("CMPlanet", StringComparison.OrdinalIgnoreCase) ||
               tileId.Contains("MattedGrass", StringComparison.OrdinalIgnoreCase) ||
               tileId.Contains("BeachGrass", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWoodLikeTileId(string tileId)
    {
        return tileId.Contains("Wood", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<Vector2i> GetAreaOffsets(int radius)
    {
        radius = Math.Max(0, radius);

        if (_areaOffsets.TryGetValue(radius, out var offsets))
            return offsets;

        offsets = new List<Vector2i>((radius * 2 + 1) * (radius * 2 + 1));

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) > radius)
                    continue;

                offsets.Add(new Vector2i(x, y));
            }
        }

        _areaOffsets[radius] = offsets;
        return offsets;
    }

    private void CleanupVegetationDecals(EntityUid gridUid, Vector2i tileIndices)
    {
        var bounds = new Box2(
            tileIndices.X - VegetationDecalCleanupPadding,
            tileIndices.Y - VegetationDecalCleanupPadding,
            tileIndices.X + 1f + VegetationDecalCleanupPadding,
            tileIndices.Y + 1f + VegetationDecalCleanupPadding);
        var decals = _decals.GetDecalsIntersecting(gridUid, bounds);

        foreach (var (index, decal) in decals)
        {
            if (!ShouldRemoveVegetationDecal(decal.Id))
                continue;

            _decals.RemoveDecal(gridUid, index);
        }
    }

    private bool ShouldRemoveVegetationDecal(string decalId)
    {
        if (!_prototype.TryIndex<DecalPrototype>(decalId, out var decal))
            return false;

        return HasVegetationTag(decal) || IsVegetationDecalId(decal.ID);
    }

    private static bool HasVegetationTag(DecalPrototype decal)
    {
        foreach (var tag in decal.Tags)
        {
            if (string.Equals(tag, "flora", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsVegetationDecalId(string decalId)
    {
        return decalId.StartsWith("Bush", StringComparison.OrdinalIgnoreCase) ||
               decalId.StartsWith("Grass", StringComparison.OrdinalIgnoreCase) ||
               decalId.StartsWith("Flowers", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBurnedVegetationTileId(string tileId)
    {
        return string.Equals(tileId, BurnedGrassAshTile.Id, StringComparison.Ordinal) ||
               string.Equals(tileId, BurnedPlanetGrassAshTile.Id, StringComparison.Ordinal);
    }

    private static float ScaleBurnDuration(float seconds)
    {
        return Math.Max(0.1f, seconds * BurnDurationMultiplier);
    }

    private static float ScaleInterval(float seconds)
    {
        return Math.Max(0.1f, seconds * IntervalMultiplier);
    }

    private readonly record struct BurningTileKey(EntityUid GridUid, Vector2i Indices);

    private record struct BurningTileState(
        int SourceTileTypeId,
        int ResultTileTypeId,
        bool CleanupVegetationDecals,
        float BurnTimeSeconds,
        int SpreadRadius,
        float SpreadIntervalSeconds,
        float SpreadFireStacks,
        float ContactIgniteIntervalSeconds,
        float ContactFireStacks,
        float HotspotTemperature,
        float HotspotVolume,
        EntProtoId FireEffectPrototype)
    {
        public float ProgressSeconds { get; set; }
        public TimeSpan NextSpreadTime { get; set; }
        public TimeSpan NextContactIgniteTime { get; set; }
        public EntityUid? FireEffectUid { get; set; }
    }

    private readonly record struct BurnRuleData(
        int SourceTileTypeId,
        int ResultTileTypeId,
        bool CleanupVegetationDecals,
        float BurnTimeSeconds,
        int SpreadRadius,
        float SpreadIntervalSeconds,
        float SpreadFireStacks,
        float ContactIgniteIntervalSeconds,
        float ContactFireStacks,
        float HotspotTemperature,
        float HotspotVolume,
        EntProtoId FireEffectPrototype);

    private readonly record struct ConfiguredBurnRuleData(
        string ResultTileId,
        bool CleanupVegetationDecals,
        float BurnTimeSeconds,
        int SpreadRadius,
        float SpreadIntervalSeconds,
        float SpreadFireStacks,
        float ContactIgniteIntervalSeconds,
        float ContactFireStacks,
        float HotspotTemperature,
        float HotspotVolume,
        EntProtoId FireEffectPrototype);

    private readonly record struct StaleBurningTile(BurningTileKey Key, BurningTileState State);

    private readonly record struct CompletedTileBurn(
        EntityUid GridUid,
        Vector2i Indices,
        int SourceTileTypeId,
        int ResultTileTypeId,
        bool CleanupVegetationDecals);

    private readonly record struct CompletedEntityBurn(
        EntityUid Uid,
        EntityCoordinates Coordinates,
        EntProtoId? ResultPrototype,
        bool DeleteOnBurn);
}
