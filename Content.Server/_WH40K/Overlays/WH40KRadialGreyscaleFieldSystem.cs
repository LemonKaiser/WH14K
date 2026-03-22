using System;
using System.Collections.Generic;
using Content.Shared._WH40K.Overlays;
using Content.Shared.Ghost;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.GameTicking;
using Content.Shared.Trigger.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Overlays;

/// <summary>
/// Applies time-dilation gameplay effects for WH40K radial grayscale zones.
/// Inside a zone, movers are slowed and dynamic physics velocities are scaled down.
/// </summary>
public sealed class WH40KRadialGreyscaleFieldSystem : EntitySystem
{
    private const float UpdateInterval = 0.02f;
    private const float Epsilon = 0.0001f;
    private const float MinEffectiveMeleeMultiplier = 0.25f;
    private static readonly TimeSpan NetworkSyncInterval = TimeSpan.FromMilliseconds(200);
    private static readonly ProtoId<TagPrototype> HandGrenadeTag = "HandGrenade";

    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private float _accumulator;

    private readonly HashSet<EntityUid> _nearby = new();
    private readonly Dictionary<EntityUid, float> _desiredMovement = new();
    private readonly Dictionary<EntityUid, float> _desiredPhysics = new();
    private readonly Dictionary<EntityUid, float> _desiredTimedDespawn = new();
    private readonly Dictionary<EntityUid, float> _desiredGrenadeTimer = new();
    private readonly Dictionary<EntityUid, float> _appliedMovement = new();
    private readonly Dictionary<EntityUid, float> _appliedPhysics = new();
    private readonly Dictionary<EntityUid, TimeSpan> _syncedGrenadeTimers = new();
    private readonly Dictionary<EntityUid, TimeSpan> _syncedThrownLandTimes = new();
    private readonly Dictionary<EntityUid, TimeSpan> _syncedMeleeCooldowns = new();
    private readonly Dictionary<EntityUid, float> _desiredMeleeCooldowns = new();
    private readonly List<EntityUid> _toRemove = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var tickDelta = _accumulator;
        _accumulator = 0f;
        BuildDesiredEffects();
        ApplyMovementEffects();
        ApplyPhysicsEffects();
        ApplyTimedDespawnEffects(tickDelta);
        ApplyGrenadeTimerEffects(tickDelta);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        // Best-effort cleanup if system is removed during runtime.
        foreach (var (uid, multiplier) in _appliedPhysics)
        {
            if (Deleted(uid))
                continue;

            ScalePhysicsVelocity(uid, 1.0f / Math.Max(multiplier, 0.01f));
        }

        foreach (var uid in _appliedMovement.Keys)
        {
            if (Deleted(uid))
                continue;

            if (HasComp<WH40KTimeDilationSlowedComponent>(uid))
                RemComp<WH40KTimeDilationSlowedComponent>(uid);

            _movement.RefreshMovementSpeedModifiers(uid);
        }

        _appliedPhysics.Clear();
        _appliedMovement.Clear();
        _syncedGrenadeTimers.Clear();
        _syncedThrownLandTimes.Clear();
        _syncedMeleeCooldowns.Clear();
        _desiredMeleeCooldowns.Clear();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent _)
    {
        _accumulator = 0f;
        _desiredMovement.Clear();
        _desiredPhysics.Clear();
        _desiredTimedDespawn.Clear();
        _desiredGrenadeTimer.Clear();
        _appliedMovement.Clear();
        _appliedPhysics.Clear();
        _syncedGrenadeTimers.Clear();
        _syncedThrownLandTimes.Clear();
        _syncedMeleeCooldowns.Clear();
        _desiredMeleeCooldowns.Clear();
        _nearby.Clear();
        _toRemove.Clear();
    }

    private void BuildDesiredEffects()
    {
        _desiredMovement.Clear();
        _desiredPhysics.Clear();
        _desiredTimedDespawn.Clear();
        _desiredGrenadeTimer.Clear();

        var query = EntityQueryEnumerator<WH40KRadialGreyscaleComponent, TransformComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        while (query.MoveNext(out var zoneUid, out var zone, out var zoneXform))
        {
            if (zoneXform.MapID == MapId.Nullspace)
                continue;

            var radius = Math.Max(0.05f, zone.Radius);
            var radiusSquared = radius * radius;
            var zoneWorld = _transform.GetWorldPosition(zoneXform, xformQuery);
            var speedMult = Math.Clamp(zone.MovementSpeedMultiplier, 0.01f, 1.0f);
            var physicsMult = Math.Clamp(zone.PhysicsVelocityMultiplier, 0.01f, 1.0f);
            var grenadeMult = Math.Clamp(zone.GrenadeFuseTimerMultiplier, 0.01f, 1.0f);

            _nearby.Clear();
            _lookup.GetEntitiesInRange(zoneXform.Coordinates, radius, _nearby,
                LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

            foreach (var target in _nearby)
            {
                if (target == zoneUid || Deleted(target))
                    continue;

                if (!xformQuery.TryGetComponent(target, out var targetXform) ||
                    targetXform.MapID != zoneXform.MapID)
                {
                    continue;
                }

                var targetWorld = _transform.GetWorldPosition(targetXform, xformQuery);
                if ((targetWorld - zoneWorld).LengthSquared() > radiusSquared)
                    continue;

                if (speedMult < 1.0f &&
                    !HasComp<GhostComponent>(target) &&
                    HasComp<MovementSpeedModifierComponent>(target))
                {
                    AccumulateMin(_desiredMovement, target, speedMult);
                }

                if (physicsMult < 1.0f &&
                    TryComp<PhysicsComponent>(target, out var body) &&
                    body.BodyType != BodyType.Static &&
                    body.BodyType != BodyType.KinematicController)
                {
                    AccumulateMin(_desiredPhysics, target, physicsMult);
                }

                if (physicsMult < 1.0f &&
                    HasComp<TimedDespawnComponent>(target))
                {
                    AccumulateMin(_desiredTimedDespawn, target, physicsMult);
                }

                if (grenadeMult < 1.0f &&
                    HasComp<ActiveTimerTriggerComponent>(target) &&
                    HasComp<TimerTriggerComponent>(target) &&
                    _tag.HasTag(target, HandGrenadeTag))
                {
                    AccumulateMin(_desiredGrenadeTimer, target, grenadeMult);
                }
            }
        }
    }

    private void ApplyMovementEffects()
    {
        _desiredMeleeCooldowns.Clear();

        // Drop deleted entities from movement tracking and fully restore entities that left all zones.
        _toRemove.Clear();
        foreach (var uid in _appliedMovement.Keys)
        {
            if (Deleted(uid) || !_desiredMovement.ContainsKey(uid))
                _toRemove.Add(uid);
        }

        foreach (var uid in _toRemove)
        {
            if (Deleted(uid))
            {
                _appliedMovement.Remove(uid);
                continue;
            }

            if (_appliedMovement.TryGetValue(uid, out var currentMultiplier))
            {
                ScaleMeleeCooldown(uid, GetEffectiveMeleeMultiplier(currentMultiplier), 1.0f);
            }

            if (HasComp<WH40KTimeDilationSlowedComponent>(uid))
                RemComp<WH40KTimeDilationSlowedComponent>(uid);

            _movement.RefreshMovementSpeedModifiers(uid);
            _appliedMovement.Remove(uid);
        }

        // Apply movement slowdown immediately when entering / remaining in zone.
        foreach (var (uid, desiredMultiplier) in _desiredMovement)
        {
            if (Deleted(uid))
                continue;

            var currentMultiplier = _appliedMovement.TryGetValue(uid, out var current)
                ? current
                : 1.0f;

            var slow = EnsureComp<WH40KTimeDilationSlowedComponent>(uid);
            var nextMeleeMultiplier = GetEffectiveMeleeMultiplier(desiredMultiplier);
            if (MathF.Abs(slow.SpeedMultiplier - desiredMultiplier) > Epsilon ||
                MathF.Abs(slow.MeleeAttackRateMultiplier - nextMeleeMultiplier) > Epsilon)
            {
                slow.SpeedMultiplier = desiredMultiplier;
                slow.MeleeAttackRateMultiplier = nextMeleeMultiplier;
                Dirty(uid, slow);
            }

            if (MathF.Abs(currentMultiplier - desiredMultiplier) > Epsilon)
            {
                ScaleMeleeCooldown(uid, GetEffectiveMeleeMultiplier(currentMultiplier), nextMeleeMultiplier);
                _movement.RefreshMovementSpeedModifiers(uid);
            }

            _appliedMovement[uid] = desiredMultiplier;
        }

        PruneSyncedTimes(_syncedMeleeCooldowns, _desiredMeleeCooldowns);
    }

    private void ApplyTimedDespawnEffects(float tickDelta)
    {
        if (tickDelta <= 0f)
            return;

        foreach (var (uid, desiredMultiplier) in _desiredTimedDespawn)
        {
            if (Deleted(uid) || !TryComp<TimedDespawnComponent>(uid, out var timedDespawn))
                continue;

            var multiplier = Math.Clamp(desiredMultiplier, 0.01f, 1.0f);
            var compensation = tickDelta * (1f - multiplier);
            if (compensation <= Epsilon)
                continue;

            timedDespawn.Lifetime += compensation;
        }
    }

    private void ApplyGrenadeTimerEffects(float tickDelta)
    {
        if (tickDelta <= 0f)
            return;

        PruneSyncedTimes(_syncedGrenadeTimers, _desiredGrenadeTimer);

        foreach (var (uid, desiredMultiplier) in _desiredGrenadeTimer)
        {
            if (Deleted(uid) ||
                !TryComp<ActiveTimerTriggerComponent>(uid, out _) ||
                !TryComp<TimerTriggerComponent>(uid, out var timer))
            {
                continue;
            }

            var multiplier = Math.Clamp(desiredMultiplier, 0.01f, 1.0f);
            var compensationSeconds = tickDelta * (1f - multiplier);
            if (compensationSeconds <= Epsilon)
                continue;

            var compensation = TimeSpan.FromSeconds(compensationSeconds);
            timer.NextTrigger += compensation;
            timer.NextBeep += compensation;

            if (ShouldDirtySyncedTime(_syncedGrenadeTimers, uid, timer.NextTrigger))
                Dirty(uid, timer);
        }
    }

    private void ApplyPhysicsEffects()
    {
        PruneSyncedTimes(_syncedThrownLandTimes, _desiredPhysics);

        _toRemove.Clear();
        foreach (var uid in _appliedPhysics.Keys)
        {
            if (Deleted(uid) || !_desiredPhysics.ContainsKey(uid))
                _toRemove.Add(uid);
        }

        foreach (var uid in _toRemove)
        {
            if (_appliedPhysics.TryGetValue(uid, out var currentMultiplier) && !Deleted(uid))
                TransitionPhysicsState(uid, currentMultiplier, 1.0f);

            _appliedPhysics.Remove(uid);
        }

        foreach (var (uid, desiredMultiplier) in _desiredPhysics)
        {
            if (Deleted(uid))
                continue;

            if (!_appliedPhysics.TryGetValue(uid, out var currentMultiplier))
            {
                TransitionPhysicsState(uid, 1.0f, desiredMultiplier);
                _appliedPhysics[uid] = desiredMultiplier;
                continue;
            }

            if (MathF.Abs(currentMultiplier - desiredMultiplier) <= Epsilon)
                continue;

            TransitionPhysicsState(uid, currentMultiplier, desiredMultiplier);
            _appliedPhysics[uid] = desiredMultiplier;
        }
    }

    private void TransitionPhysicsState(EntityUid uid, float fromMultiplier, float toMultiplier)
    {
        var from = Math.Clamp(fromMultiplier, 0.01f, 1.0f);
        var to = Math.Clamp(toMultiplier, 0.01f, 1.0f);
        var ratio = to / from;

        ScalePhysicsVelocity(uid, ratio);
        ScaleThrownLandTime(uid, from, to);
    }

    private void ScalePhysicsVelocity(EntityUid uid, float ratio)
    {
        if (Deleted(uid) ||
            !TryComp<PhysicsComponent>(uid, out var body) ||
            body.BodyType == BodyType.Static ||
            body.BodyType == BodyType.KinematicController)
        {
            return;
        }

        var safeRatio = Math.Max(ratio, 0.01f);
        _physics.SetLinearVelocity(uid, body.LinearVelocity * safeRatio, body: body);
        _physics.SetAngularVelocity(uid, body.AngularVelocity * safeRatio, body: body);
    }

    private void ScaleThrownLandTime(EntityUid uid, float fromMultiplier, float toMultiplier)
    {
        if (!TryComp<ThrownItemComponent>(uid, out var thrown) || thrown.LandTime == null)
            return;

        var now = _timing.CurTime;
        var remaining = thrown.LandTime.Value - now;
        if (remaining <= TimeSpan.Zero)
            return;

        var scale = fromMultiplier / Math.Max(toMultiplier, 0.01f);
        var adjustedTicks = Math.Max(1L, (long) (remaining.Ticks * scale));
        thrown.LandTime = now + TimeSpan.FromTicks(adjustedTicks);

        if (ShouldDirtySyncedTime(_syncedThrownLandTimes, uid, thrown.LandTime.Value))
            Dirty(uid, thrown);
    }

    private void ScaleMeleeCooldown(EntityUid userUid, float fromMultiplier, float toMultiplier)
    {
        if (Deleted(userUid))
            return;

        var from = Math.Clamp(fromMultiplier, 0.01f, 1.0f);
        var to = Math.Clamp(toMultiplier, 0.01f, 1.0f);
        var scale = from / to;
        if (MathF.Abs(scale - 1.0f) <= Epsilon)
            return;

        var now = _timing.CurTime;
        var scaledWeapons = new HashSet<EntityUid>();

        void TryScaleWeapon(EntityUid weaponUid)
        {
            if (!scaledWeapons.Add(weaponUid))
                return;

            if (!TryComp<MeleeWeaponComponent>(weaponUid, out var melee))
                return;

            _desiredMeleeCooldowns[weaponUid] = 1f;

            var remaining = melee.NextAttack - now;
            if (remaining <= TimeSpan.Zero)
                return;

            var adjustedTicks = Math.Max(1L, (long) (remaining.Ticks * scale));
            melee.NextAttack = now + TimeSpan.FromTicks(adjustedTicks);

            if (ShouldDirtySyncedTime(_syncedMeleeCooldowns, weaponUid, melee.NextAttack))
                Dirty(weaponUid, melee);
        }

        // Unarmed melee can use the user entity itself as a weapon source.
        TryScaleWeapon(userUid);

        if (TryComp<HandsComponent>(userUid, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld((userUid, hands)))
            {
                TryScaleWeapon(held);
            }
        }

        // Gloves can provide melee weapons when no in-hand weapon is used.
        if (_inventory.TryGetSlotEntity(userUid, "gloves", out var gloves))
            TryScaleWeapon(gloves.Value);
    }

    private static void AccumulateMin(Dictionary<EntityUid, float> map, EntityUid uid, float value)
    {
        if (!map.TryGetValue(uid, out var current))
        {
            map[uid] = value;
            return;
        }

        if (value < current)
            map[uid] = value;
    }

    private void PruneSyncedTimes(Dictionary<EntityUid, TimeSpan> syncedValues, Dictionary<EntityUid, float> desiredValues)
    {
        _toRemove.Clear();

        foreach (var uid in syncedValues.Keys)
        {
            if (Deleted(uid) || !desiredValues.ContainsKey(uid))
                _toRemove.Add(uid);
        }

        foreach (var uid in _toRemove)
        {
            syncedValues.Remove(uid);
        }
    }

    private bool ShouldDirtySyncedTime(
        Dictionary<EntityUid, TimeSpan> syncedValues,
        EntityUid uid,
        TimeSpan currentValue)
    {
        if (!syncedValues.TryGetValue(uid, out var lastSynced))
        {
            syncedValues[uid] = currentValue;
            return true;
        }

        if (Math.Abs((currentValue - lastSynced).TotalMilliseconds) < NetworkSyncInterval.TotalMilliseconds)
            return false;

        syncedValues[uid] = currentValue;
        return true;
    }

    private static float GetEffectiveMeleeMultiplier(float value)
    {
        return Math.Clamp(value, MinEffectiveMeleeMultiplier, 1.0f);
    }

}
