using System;
using System.Collections.Generic;
using Content.Shared._WH40K.Overlays;
using Content.Shared._WH40K.Psyker;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Overlays;

/// <summary>
/// Stable gameplay logic for WH40K radial grayscale time fields.
/// Movement slow is component-driven, while physics/timers are only rescaled on enter/exit.
/// </summary>
public sealed partial class WH40KRadialGreyscaleFieldSystem : EntitySystem
{
    private const float UpdateInterval = 0.1f;
    private const float Epsilon = 0.0001f;
    private static readonly TimeSpan NetworkSyncInterval = TimeSpan.FromMilliseconds(200);
    private static readonly ProtoId<TagPrototype> HandGrenadeTag = "HandGrenade";

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    private float _accumulator;

    private readonly HashSet<EntityUid> _nearby = new();
    private readonly Dictionary<EntityUid, MovementSlowState> _desiredMovement = new();
    private readonly Dictionary<EntityUid, float> _desiredPhysics = new();
    private readonly Dictionary<EntityUid, float> _desiredTimedDespawn = new();
    private readonly Dictionary<EntityUid, float> _desiredGrenadeTimer = new();
    private readonly Dictionary<EntityUid, float> _appliedPhysics = new();
    private readonly Dictionary<EntityUid, TimeSpan> _syncedGrenadeTimers = new();
    private readonly Dictionary<EntityUid, TimeSpan> _syncedThrownLandTimes = new();
    private readonly List<EntityUid> _toRemove = new();

    private readonly record struct MovementSlowState(float SpeedMultiplier, float MeleeAttackRateMultiplier);

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

        foreach (var (uid, multiplier) in _appliedPhysics)
        {
            if (!Deleted(uid))
                TransitionPhysicsState(uid, multiplier, 1f);
        }

        var query = EntityQueryEnumerator<WH40KTimeDilationSlowedComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (Deleted(uid))
                continue;

            RemComp<WH40KTimeDilationSlowedComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }

        ClearTracking();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent _)
    {
        _accumulator = 0f;
        ClearTracking();
        _nearby.Clear();
        _toRemove.Clear();
    }

    private void ClearTracking()
    {
        _desiredMovement.Clear();
        _desiredPhysics.Clear();
        _desiredTimedDespawn.Clear();
        _desiredGrenadeTimer.Clear();
        _appliedPhysics.Clear();
        _syncedGrenadeTimers.Clear();
        _syncedThrownLandTimes.Clear();
    }

    private void BuildDesiredEffects()
    {
        _desiredMovement.Clear();
        _desiredPhysics.Clear();
        _desiredTimedDespawn.Clear();
        _desiredGrenadeTimer.Clear();

        var query = EntityQueryEnumerator<WH40KRadialGreyscaleComponent, WH40KTimeDilationFieldComponent, TransformComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        while (query.MoveNext(out var zoneUid, out var radial, out var logic, out var zoneXform))
        {
            if (zoneXform.MapID == MapId.Nullspace)
                continue;

            var radius = Math.Max(0.05f, radial.Radius);
            var radiusSquared = radius * radius;
            var zoneWorld = _transform.GetWorldPosition(zoneXform, xformQuery);
            var movementMult = Math.Clamp(logic.MovementSpeedMultiplier, 0.01f, 1f);
            var meleeMult = Math.Clamp(logic.MeleeAttackRateMultiplier, 0.01f, 1f);
            var physicsMult = Math.Clamp(logic.PhysicsVelocityMultiplier, 0.01f, 1f);
            var despawnMult = Math.Clamp(logic.TimedDespawnMultiplier, 0.01f, 1f);
            var grenadeMult = Math.Clamp(logic.GrenadeFuseTimerMultiplier, 0.01f, 1f);

            _nearby.Clear();
            _lookup.GetEntitiesInRange(
                zoneXform.Coordinates,
                radius,
                _nearby,
                LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

            foreach (var target in _nearby)
            {
                if (Deleted(target) || ShouldIgnoreTarget(zoneUid, logic, target))
                    continue;

                if (!xformQuery.TryGetComponent(target, out var targetXform) ||
                    targetXform.MapID != zoneXform.MapID)
                {
                    continue;
                }

                var targetWorld = _transform.GetWorldPosition(targetXform, xformQuery);
                if ((targetWorld - zoneWorld).LengthSquared() > radiusSquared)
                    continue;

                if (movementMult < 1f && HasComp<MovementSpeedModifierComponent>(target))
                    AccumulateMovement(target, movementMult, meleeMult);

                if (physicsMult < 1f &&
                    TryComp<PhysicsComponent>(target, out var body) &&
                    body.BodyType != BodyType.Static &&
                    body.BodyType != BodyType.KinematicController)
                {
                    AccumulateMin(_desiredPhysics, target, physicsMult);
                }

                if (despawnMult < 1f && HasComp<TimedDespawnComponent>(target))
                    AccumulateMin(_desiredTimedDespawn, target, despawnMult);

                if (grenadeMult < 1f &&
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
        _toRemove.Clear();

        var query = EntityQueryEnumerator<WH40KTimeDilationSlowedComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (Deleted(uid) || !_desiredMovement.ContainsKey(uid))
                _toRemove.Add(uid);
        }

        foreach (var uid in _toRemove)
        {
            if (Deleted(uid))
                continue;

            RemComp<WH40KTimeDilationSlowedComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }

        foreach (var (uid, desired) in _desiredMovement)
        {
            if (Deleted(uid))
                continue;

            var existed = TryComp<WH40KTimeDilationSlowedComponent>(uid, out var slowed);
            slowed ??= EnsureComp<WH40KTimeDilationSlowedComponent>(uid);

            var changed = !existed ||
                          MathF.Abs(slowed.SpeedMultiplier - desired.SpeedMultiplier) > Epsilon ||
                          MathF.Abs(slowed.MeleeAttackRateMultiplier - desired.MeleeAttackRateMultiplier) > Epsilon;
            if (!changed)
                continue;

            slowed.SpeedMultiplier = desired.SpeedMultiplier;
            slowed.MeleeAttackRateMultiplier = desired.MeleeAttackRateMultiplier;
            Dirty(uid, slowed);
            _movement.RefreshMovementSpeedModifiers(uid);
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
                TransitionPhysicsState(uid, currentMultiplier, 1f);

            _appliedPhysics.Remove(uid);
        }

        foreach (var (uid, desiredMultiplier) in _desiredPhysics)
        {
            if (Deleted(uid))
                continue;

            if (!_appliedPhysics.TryGetValue(uid, out var currentMultiplier))
            {
                TransitionPhysicsState(uid, 1f, desiredMultiplier);
                _appliedPhysics[uid] = desiredMultiplier;
                continue;
            }

            if (MathF.Abs(currentMultiplier - desiredMultiplier) <= Epsilon)
                continue;

            TransitionPhysicsState(uid, currentMultiplier, desiredMultiplier);
            _appliedPhysics[uid] = desiredMultiplier;
        }
    }

    private void ApplyTimedDespawnEffects(float tickDelta)
    {
        if (tickDelta <= 0f)
            return;

        foreach (var (uid, desiredMultiplier) in _desiredTimedDespawn)
        {
            if (Deleted(uid) || !TryComp<TimedDespawnComponent>(uid, out var timedDespawn))
                continue;

            var multiplier = Math.Clamp(desiredMultiplier, 0.01f, 1f);
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

            var multiplier = Math.Clamp(desiredMultiplier, 0.01f, 1f);
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

    private bool ShouldIgnoreTarget(EntityUid zoneUid, WH40KTimeDilationFieldComponent logic, EntityUid target)
    {
        if (target == zoneUid)
            return true;

        if (logic.IgnoreOwner && logic.Caster == target)
            return true;

        if (!logic.AffectGhosts && HasComp<GhostComponent>(target))
            return true;

        if (logic.ImmunePatron != WH40KChaosPatron.None &&
            HasComp<WH40KChaosGiftRoleComponent>(target) &&
            TryComp<WH40KChaosGiftProgressionComponent>(target, out var progression) &&
            progression.AttunedPatron == logic.ImmunePatron)
        {
            return true;
        }

        return false;
    }

    private void TransitionPhysicsState(EntityUid uid, float fromMultiplier, float toMultiplier)
    {
        var from = Math.Clamp(fromMultiplier, 0.01f, 1f);
        var to = Math.Clamp(toMultiplier, 0.01f, 1f);
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

    private void AccumulateMovement(EntityUid uid, float speedMultiplier, float meleeMultiplier)
    {
        var next = new MovementSlowState(
            Math.Clamp(speedMultiplier, 0.01f, 1f),
            Math.Clamp(meleeMultiplier, 0.01f, 1f));

        if (!_desiredMovement.TryGetValue(uid, out var current))
        {
            _desiredMovement[uid] = next;
            return;
        }

        _desiredMovement[uid] = new MovementSlowState(
            Math.Min(current.SpeedMultiplier, next.SpeedMultiplier),
            Math.Min(current.MeleeAttackRateMultiplier, next.MeleeAttackRateMultiplier));
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
}
