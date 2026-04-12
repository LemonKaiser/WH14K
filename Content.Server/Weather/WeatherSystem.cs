using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Inventory;
using Content.Shared.Jittering;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Medical;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Tag;
using Content.Shared.Weather;
using Robust.Server.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server.Weather;

public sealed class WeatherSystem : SharedWeatherSystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedEmpSystem _emp = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedJitteringSystem _jittering = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly MovementModStatusSystem _movement = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly PvsOverrideSystem _pvs = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly VomitSystem _vomit = default!;

    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    private static readonly ProtoId<TagPrototype> HardsuitTag = "Hardsuit";
    private const string GellarTremorWeatherId = "WHGellarTremor";

    private readonly List<(EntityUid Uid, MapGridComponent Grid)> _gridBuffer = new();
    private readonly List<EntityUid> _mobLocalBuffer = new();
    private readonly List<EntityUid> _structureCandidateBuffer = new();
    private readonly List<EntityUid> _structureDamageBuffer = new();
    private readonly List<EntityUid> _ambientCandidateBuffer = new();
    private readonly List<(EntityUid Uid, float Duration)> _blinkApplyBuffer = new();
    private readonly List<EntityUid> _ambientBuffer = new();
    private readonly HashSet<EntityUid> _empTargetsBuffer = new();
    private readonly Dictionary<EntityUid, TimeSpan> _pendingBlinkStop = new();
    private readonly List<EntityUid> _blinkStopBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        _mobQuery = GetEntityQuery<MobStateComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        SubscribeLocalEvent<WeatherStatusEffectComponent, ComponentInit>(OnCompInit);
        SubscribeLocalEvent<WeatherStatusEffectComponent, ComponentShutdown>(OnCompShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdatePendingBlinkStop();

        var query = EntityQueryEnumerator<WeatherStatusEffectComponent, StatusEffectComponent>();
        while (query.MoveNext(out var uid, out var weather, out var status))
        {
            if (status.AppliedTo == null || !status.Applied)
                continue;

            RunWeather((uid, weather, status));
        }
    }

    private void OnCompInit(Entity<WeatherStatusEffectComponent> ent, ref ComponentInit args)
    {
        _pvs.AddGlobalOverride(ent);
    }

    private void OnCompShutdown(Entity<WeatherStatusEffectComponent> ent, ref ComponentShutdown args)
    {
        TryComp(ent.Owner, out TransformComponent? xform);
        TryComp(ent.Owner, out MetaDataComponent? meta);
        _container.TryRemoveFromContainer((ent.Owner, xform, meta), force: true);
        _pvs.RemoveGlobalOverride(ent);
    }

    private void UpdatePendingBlinkStop()
    {
        if (_pendingBlinkStop.Count == 0)
            return;

        var now = Timing.CurTime;
        _blinkStopBuffer.Clear();

        foreach (var (uid, stopTime) in _pendingBlinkStop)
        {
            if (stopTime > now)
                continue;

            RemComp<BlinkingPoweredLightComponent>(uid);

            _blinkStopBuffer.Add(uid);
        }

        foreach (var uid in _blinkStopBuffer)
        {
            _pendingBlinkStop.Remove(uid);
        }
    }

    private void RunWeather(Entity<WeatherStatusEffectComponent, StatusEffectComponent> weatherEnt)
    {
        var weather = weatherEnt.Comp1;

        if (weather.Effects is { } local && TryTick(ref weather.NextLocalEffectTick, local.TickInterval))
            ApplyLocalEffects(weatherEnt, local);

        if (weather.GlobalEffects is { } global && TryTick(ref weather.NextGlobalEffectTick, global.TickInterval))
            ApplyGlobalEffects(weatherEnt, global);
    }

    private bool TryTick(ref TimeSpan nextTick, float intervalSeconds)
    {
        var now = Timing.CurTime;

        if (intervalSeconds <= 0f)
            intervalSeconds = 0.1f;

        if (nextTick > now)
            return false;

        nextTick = now + TimeSpan.FromSeconds(intervalSeconds);
        return true;
    }

    private void ApplyLocalEffects(Entity<WeatherStatusEffectComponent, StatusEffectComponent> weatherEnt, WeatherLocalEffects effects)
    {
        if (weatherEnt.Comp2.AppliedTo is not { } weatherMap)
            return;

        var mapId = Transform(weatherMap).MapID;
        ApplyTileEffects(mapId, weatherEnt.Comp1, effects);

        _mobLocalBuffer.Clear();
        var mobEnumerator = EntityQueryEnumerator<TransformComponent, MobStateComponent>();
        while (mobEnumerator.MoveNext(out var uid, out var xform, out _))
        {
            if (xform.MapID != mapId)
                continue;

            _mobLocalBuffer.Add(uid);
        }

        foreach (var uid in _mobLocalBuffer)
        {
            if (!TryComp(uid, out TransformComponent? xform) || xform.MapID != mapId)
                continue;

            if (!CanWeatherAffectEntity(uid, weatherEnt.Comp1, xform))
                continue;

            var protectedFromWeather = IsProtectedFromWeather(uid, effects);

            if (!protectedFromWeather && effects.Slowdown is { } slowdown)
            {
                var slowdownDurationSeconds = MathF.Max(0.1f, MathF.Max(slowdown.Duration, effects.TickInterval + 0.05f));
                _movement.TryUpdateMovementSpeedModDuration(uid,
                    slowdown.StatusEffect,
                    TimeSpan.FromSeconds(slowdownDurationSeconds),
                    slowdown.WalkModifier,
                    slowdown.SprintModifier);
            }

            if (!protectedFromWeather && effects.MobDamage is { } mobDamage && Chance(effects.MobDamageChance))
            {
                _damageable.TryChangeDamage(uid, mobDamage, true, origin: weatherMap);
            }

            if (Prototype(weatherEnt.Owner)?.ID == "WHSporeDrift" && !protectedFromWeather)
            {
                if (Chance(0.18f))
                    _jittering.DoJitter(uid, TimeSpan.FromSeconds(2.5f), true, 12f, 4.5f, true);

                if (Chance(0.04f))
                    _vomit.Vomit(uid, -5f, -5f);
            }

            if (effects.Wind is { } wind &&
                Chance(wind.Chance) &&
                _physicsQuery.TryComp(uid, out var physics) &&
                (physics.BodyType & BodyType.Static) == 0)
            {
                var directionDegrees = GetWindDirectionDegrees(weatherEnt.Comp1, wind);
                var angle = MathF.PI / 180f * directionDegrees;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                if (direction != Vector2.Zero)
                    direction = Vector2.Normalize(direction);

                _physics.ApplyLinearImpulse(uid, direction * (wind.Impulse * physics.Mass), body: physics);
            }
        }

        if (effects.StructureDamage is null)
            return;

        _structureCandidateBuffer.Clear();
        var structEnumerator = EntityQueryEnumerator<TransformComponent, DamageableComponent>();
        while (structEnumerator.MoveNext(out var uid, out var xform, out _))
        {
            if (xform.MapID != mapId || !xform.Anchored || _mobQuery.HasComp(uid))
                continue;

            _structureCandidateBuffer.Add(uid);
        }

        _structureDamageBuffer.Clear();
        foreach (var uid in _structureCandidateBuffer)
        {
            if (!TryComp(uid, out TransformComponent? xform) ||
                !xform.Anchored ||
                xform.MapID != mapId ||
                _mobQuery.HasComp(uid))
            {
                continue;
            }

            if (!CanWeatherAffectEntity(uid, weatherEnt.Comp1, xform))
                continue;

            if (!Chance(effects.StructureDamageChance))
                continue;

            _structureDamageBuffer.Add(uid);
        }

        if (_structureDamageBuffer.Count == 0)
            return;

        var damageTargets = _structureDamageBuffer.ToArray();
        foreach (var uid in damageTargets)
        {
            if (!TryComp<DamageableComponent>(uid, out var damageable))
                continue;

            _damageable.TryChangeDamage((uid, damageable), effects.StructureDamage, true, origin: weatherMap);
        }
    }

    private void ApplyGlobalEffects(Entity<WeatherStatusEffectComponent, StatusEffectComponent> weatherEnt, WeatherGlobalEffects effects)
    {
        if (weatherEnt.Comp2.AppliedTo is not { } weatherMap)
            return;

        var mapId = Transform(weatherMap).MapID;

        if (effects.LightFlicker is { } flicker)
        {
            var forceAllLightsFlicker = Prototype(weatherEnt.Owner)?.ID == GellarTremorWeatherId;
            var shouldRunFlickerPass = !forceAllLightsFlicker || Chance(flicker.Chance);
            if (shouldRunFlickerPass)
            {
                _blinkApplyBuffer.Clear();
                var lightEnumerator = EntityQueryEnumerator<TransformComponent, PoweredLightComponent>();
                while (lightEnumerator.MoveNext(out var lightUid, out var xform, out _))
                {
                    if (xform.MapID != mapId)
                        continue;

                    if (!forceAllLightsFlicker && !Chance(flicker.Chance))
                        continue;

                    var blinkDurationSeconds = _random.NextFloat(
                        MathF.Min(flicker.DurationMin, flicker.DurationMax),
                        MathF.Max(flicker.DurationMin, flicker.DurationMax));

                    _blinkApplyBuffer.Add((lightUid, MathF.Max(0.1f, blinkDurationSeconds)));
                }

                foreach (var (lightUid, duration) in _blinkApplyBuffer)
                {
                    if (!TryComp<PoweredLightComponent>(lightUid, out _))
                        continue;

                    var blinking = EnsureComp<BlinkingPoweredLightComponent>(lightUid);
                    blinking.StopBlinkingTime = Timing.CurTime + TimeSpan.FromSeconds(duration);
                    Dirty(lightUid, blinking);
                    _pendingBlinkStop[lightUid] = blinking.StopBlinkingTime!.Value;
                }
            }
        }

        if (effects.Ambient?.Sound is not { } sound)
            return;

        var ambientChance = effects.Ambient.Chance;
        _ambientCandidateBuffer.Clear();
        _ambientBuffer.Clear();

        var ambientEnumerator = EntityQueryEnumerator<TransformComponent, MobStateComponent>();
        while (ambientEnumerator.MoveNext(out var uid, out var xform, out _))
        {
            if (xform.MapID != mapId)
                continue;

            _ambientCandidateBuffer.Add(uid);
        }

        foreach (var uid in _ambientCandidateBuffer)
        {
            if (!TryComp(uid, out TransformComponent? xform) || xform.MapID != mapId)
                continue;

            if (!Chance(ambientChance) || !CanWeatherAffectEntity(uid, weatherEnt.Comp1, xform))
                continue;

            _ambientBuffer.Add(uid);
        }

        if (_ambientBuffer.Count == 0)
            return;

        var targetUid = _ambientBuffer[_random.Next(_ambientBuffer.Count)];
        _audio.PlayPvs(sound, targetUid, AudioParams.Default.WithVariation(0.1f));
    }

    private bool IsProtectedFromWeather(EntityUid uid, WeatherLocalEffects effects)
    {
        if (!effects.ProtectedByGasMask && !effects.ProtectedByHardsuit)
            return false;

        if (!TryComp<InventoryComponent>(uid, out var inv) || !TryComp<ContainerManagerComponent>(uid, out var containerManager))
            return false;

        if (effects.ProtectedByGasMask &&
            _inventory.TryGetSlotEntity(uid, "mask", out var mask, inv, containerManager) &&
            HasComp<BreathToolComponent>(mask.Value))
        {
            return true;
        }

        if (!effects.ProtectedByHardsuit)
            return false;

        return IsHardsuitProtectionInSlot(uid, "outerClothing", inv, containerManager) ||
               IsHardsuitProtectionInSlot(uid, "head", inv, containerManager);
    }

    private bool IsHardsuitProtectionInSlot(
        EntityUid uid,
        string slot,
        InventoryComponent inv,
        ContainerManagerComponent containerManager)
    {
        if (!_inventory.TryGetSlotEntity(uid, slot, out var equipped, inv, containerManager))
            return false;

        return HasComp<PressureProtectionComponent>(equipped.Value) || _tag.HasTag(equipped.Value, HardsuitTag);
    }

    private void ApplyTileEffects(MapId mapId, WeatherStatusEffectComponent weather, WeatherLocalEffects effects)
    {
        if (effects.HazardSpawn == null && effects.Puddle == null && effects.Emp == null)
            return;

        CollectMapGrids(mapId);
        if (_gridBuffer.Count == 0)
            return;

        foreach (var (gridUid, grid) in _gridBuffer)
        {
            if (effects.HazardSpawn is { } hazard &&
                Chance(hazard.Chance) &&
                TryGetRandomAffectedTileOnGrid(gridUid, grid, weather, out var hazardTile))
            {
                var coords = new EntityCoordinates(gridUid, hazardTile.GridIndices + grid.TileSizeHalfVector);
                TrySpawnHazardAt(coords, gridUid, hazard);
            }

            if (effects.Puddle is { } puddle &&
                Chance(puddle.Chance) &&
                TryGetRandomAffectedTileOnGrid(gridUid, grid, weather, out var puddleTile))
            {
                TrySpawnPuddleAt(gridUid, grid, puddleTile, puddle);
            }

            if (effects.Emp is { } emp &&
                Chance(emp.Chance) &&
                TryGetRandomAffectedTileOnGrid(gridUid, grid, weather, out var empTile))
            {
                var coords = new EntityCoordinates(gridUid, empTile.GridIndices + grid.TileSizeHalfVector);
                ApplyWeatherEmpAt(coords, weather, emp);
            }
        }
    }

    private void ApplyWeatherEmpAt(EntityCoordinates coords, WeatherStatusEffectComponent weather, WeatherEmpData emp)
    {
        var duration = TimeSpan.FromSeconds(MathF.Max(0.1f, emp.Duration));
        var range = MathF.Max(0f, emp.Range);

        _empTargetsBuffer.Clear();
        _lookup.GetEntitiesInRange(coords, range, _empTargetsBuffer);

        foreach (var uid in _empTargetsBuffer)
        {
            if (!TryComp(uid, out TransformComponent? xform))
                continue;

            if (!CanWeatherAffectEntity(uid, weather, xform))
                continue;

            _emp.TryEmpEffects(uid, emp.EnergyConsumption, duration);
        }

        Spawn(SharedEmpSystem.EmpPulseEffectPrototype, coords);
        _audio.PlayPvs(SharedEmpSystem.EmpSound, coords);
    }

    private void CollectMapGrids(MapId mapId)
    {
        _gridBuffer.Clear();

        var gridEnumerator = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridEnumerator.MoveNext(out var gridUid, out var grid, out var xform))
        {
            if (xform.MapID != mapId || grid.LocalAABB.Size == Vector2.Zero)
                continue;

            _gridBuffer.Add((gridUid, grid));
        }
    }

    private bool TryGetRandomAffectedTileOnGrid(
        EntityUid gridUid,
        MapGridComponent grid,
        WeatherStatusEffectComponent weather,
        out TileRef tileRef)
    {
        var bounds = grid.LocalAABB;
        var minX = (int) MathF.Floor(bounds.Left);
        var maxX = (int) MathF.Ceiling(bounds.Right) - 1;
        var minY = (int) MathF.Floor(bounds.Bottom);
        var maxY = (int) MathF.Ceiling(bounds.Top) - 1;

        if (maxX < minX || maxY < minY)
        {
            tileRef = default;
            return false;
        }

        for (var i = 0; i < 24; i++)
        {
            var indices = new Vector2i(_random.Next(minX, maxX + 1), _random.Next(minY, maxY + 1));
            var tile = _mapSystem.GetTileRef(gridUid, grid, indices);
            if (tile.Tile.IsEmpty)
                continue;

            if (!CanWeatherAffect(gridUid, grid, tile, weather: weather))
                continue;

            tileRef = tile;
            return true;
        }

        tileRef = default;
        return false;
    }

    private void TrySpawnPuddleAt(
        EntityUid gridUid,
        MapGridComponent grid,
        TileRef tileRef,
        WeatherPuddleData puddle)
    {
        if (!puddle.AllowDuplicates)
        {
            var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tileRef.GridIndices);
            while (anchoredEnumerator.MoveNext(out var anchored))
            {
                if (HasComp<PuddleComponent>(anchored.Value))
                    return;
            }
        }

        var coords = new EntityCoordinates(gridUid, tileRef.GridIndices + grid.TileSizeHalfVector);
        if (!_puddle.TrySpillAt(coords, new Solution(puddle.Reagent, FixedPoint2.New(puddle.Quantity)), out var puddleUid, sound: false))
            return;

        if (puddle.Lifetime <= 0f)
            return;

        var timedDespawn = EnsureComp<TimedDespawnComponent>(puddleUid);
        timedDespawn.Lifetime = MathF.Max(1f, puddle.Lifetime);
    }

    private float GetWindDirectionDegrees(WeatherStatusEffectComponent weather, WeatherWindData wind)
    {
        if (wind.RandomDirection)
            return _random.NextFloat() * 360f;

        if (wind.DirectionChangeInterval <= 0f)
            return wind.DirectionDegrees;

        var now = Timing.CurTime;
        if (weather.CurrentWindDirectionDegrees == null || now >= weather.NextWindDirectionChangeTick)
        {
            weather.CurrentWindDirectionDegrees = _random.NextFloat() * 360f;
            weather.NextWindDirectionChangeTick = now + TimeSpan.FromSeconds(MathF.Max(1f, wind.DirectionChangeInterval));
        }

        return weather.CurrentWindDirectionDegrees.Value;
    }

    private void TrySpawnHazardAt(EntityCoordinates coordinates, EntityUid? gridUid, WeatherHazardSpawnData hazard)
    {
        if (!hazard.AllowDuplicates &&
            gridUid != null &&
            TryComp<MapGridComponent>(gridUid.Value, out var gridComp) &&
            _mapSystem.TryGetTileRef(gridUid.Value, gridComp, coordinates, out var tileRef))
        {
            var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid.Value, gridComp, tileRef.GridIndices);
            while (anchoredEnumerator.MoveNext(out var anchored))
            {
                var prototypeId = MetaData(anchored.Value).EntityPrototype?.ID;
                if (prototypeId != null && prototypeId == hazard.Prototype.Id)
                    return;
            }
        }

        Spawn(hazard.Prototype, coordinates);
    }

    private bool Chance(float chance)
    {
        return _random.Prob(Math.Clamp(chance, 0f, 1f));
    }
}
