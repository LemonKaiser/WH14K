using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.RepulseAttract;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared._WH40K.Psyker;
using Content.Server.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Khorne runtime backend:
/// - applies active gift scaling (CD/power/utility + EX) for repulse/jump/dash;
/// - handles dash-through-hit behavior and EX triple-dash cadence;
/// - applies passive path effects (speed, max HP, melee damage) from base values.
/// </summary>
public sealed class WH40KChaosKhorneGiftAbilitySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly RepulseAttractSystem _repulseAttract = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly MobThresholdSystem _mobThresholds = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private readonly HashSet<EntityUid> _dashTargets = new();
    private readonly HashSet<EntityUid> _impactTargets = new();

    private static readonly ProtoId<DamageTypePrototype> SlashDamageType = "Slash";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    private DamageTypePrototype _slashDamage = default!;
    private DamageTypePrototype _bluntDamage = default!;

    private const float RepulseBaseCooldown = 18f;
    private const float JumpBaseCooldown = 9f;
    private const float DashBaseCooldown = 13f;
    private const float JumpThrowSpeed = 12f;
    private const float JumpExExplosionRadius = 2.4f;
    private const float JumpExExplosionDamage = 5.5f;
    private const float JumpExExplosionRepulseSpeed = 2f;
    private const float DashHitPadding = 0.15f;

    public override void Initialize()
    {
        _slashDamage = _prototype.Index(SlashDamageType);
        _bluntDamage = _prototype.Index(BluntDamageType);

        SubscribeLocalEvent<WH40KChaosKhorneRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
        SubscribeLocalEvent<WH40KChaosKhorneRuntimeComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);

        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosKhorneRepulseActionEvent>(OnKhorneRepulse);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosKhorneGravityJumpActionEvent>(OnKhorneJump);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosKhorneDashActionEvent>(OnKhorneDash);

        SubscribeLocalEvent<WH40KChaosKhorneJumpMarkerComponent, LandEvent>(OnKhorneJumpResolved);
        SubscribeLocalEvent<WH40KChaosKhorneJumpMarkerComponent, StopThrowEvent>(OnKhorneJumpResolved);
        SubscribeLocalEvent<WH40KChaosKhorneJumpMarkerComponent, StartCollideEvent>(OnKhorneJumpResolved);
        SubscribeLocalEvent<WH40KChaosKhorneDashActionComponent, ActionPerformedEvent>(OnKhorneDashActionPerformed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KChaosGiftProgressionComponent>();
        while (query.MoveNext(out var uid, out var progression))
        {
            var runtime = EnsureComp<WH40KChaosKhorneRuntimeComponent>(uid);

            if (runtime.JumpSpeedBuffExpiresAt != TimeSpan.Zero && now >= runtime.JumpSpeedBuffExpiresAt)
            {
                runtime.JumpSpeedBuffExpiresAt = TimeSpan.Zero;
                runtime.JumpSpeedBuffMultiplier = 1f;
                _movementSpeed.RefreshMovementSpeedModifiers(uid);
            }
        }
    }

    private void OnRuntimeShutdown(Entity<WH40KChaosKhorneRuntimeComponent> ent, ref ComponentShutdown args)
    {
    }

    private void OnRefreshMovementSpeed(Entity<WH40KChaosKhorneRuntimeComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryGetKhorneProgression(ent.Owner, out _))
            return;

        var jumpBuff = 1f;
        if (ent.Comp.JumpSpeedBuffExpiresAt > _timing.CurTime)
        {
            jumpBuff = MathF.Max(1f, ent.Comp.JumpSpeedBuffMultiplier);
        }

        var total = MathF.Max(0.1f, jumpBuff);
        args.ModifySpeed(total, total, MovementSpeedModifierLayer.Status);
    }

    private void OnKhorneRepulse(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosKhorneRepulseActionEvent args)
    {
        if (!TryGetKhorneProgression(args.Performer, out var progression))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, RepulseBaseCooldown, progression.KhorneGiftOneCooldownTier);

        var giftOneExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1);
        var speed = GetRepulseSpeed(progression.KhorneGiftOnePowerTier, giftOneExUnlocked);
        var range = GetRepulseRange(progression.KhorneGiftOneUtilityTier);
        var map = _transform.GetMapCoordinates(args.Performer);

        args.Handled = _repulseAttract.TryRepulseAttract(map, args.Performer, speed, range, layer: CollisionGroup.GhostImpassable);
    }

    private void OnKhorneJump(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosKhorneGravityJumpActionEvent args)
    {
        if (!TryGetKhorneProgression(args.Performer, out var progression))
            return;

        if (_gravity.IsWeightless(args.Performer) || _standing.IsDown(args.Performer))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, JumpBaseCooldown, progression.KhorneGiftTwoCooldownTier);

        var xform = Transform(args.Performer);
        var distance = GetJumpRange(progression.KhorneGiftTwoUtilityTier);
        var velocity = xform.LocalRotation.ToWorldVec() * distance;
        var destination = xform.Coordinates.Offset(velocity);

        _throwing.TryThrow(args.Performer, destination, JumpThrowSpeed, args.Performer);
        if (!HasComp<ThrownItemComponent>(args.Performer))
            return;

        var speedBuffMultiplier = GetJumpSpeedBuffMultiplier(progression.KhorneGiftTwoPowerTier);
        var giftTwoExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 2);
        var marker = EnsureComp<WH40KChaosKhorneJumpMarkerComponent>(args.Performer);
        marker.SpeedBuffMultiplier = speedBuffMultiplier;
        marker.SpeedBuffDuration = TimeSpan.FromSeconds(6);
        marker.ExExplosionEnabled = giftTwoExUnlocked;

        args.Handled = true;
    }

    private void OnKhorneJumpResolved(Entity<WH40KChaosKhorneJumpMarkerComponent> ent, ref LandEvent args)
    {
        ResolveJumpImpact(ent.Owner, ent.Comp);
    }

    private void OnKhorneJumpResolved(Entity<WH40KChaosKhorneJumpMarkerComponent> ent, ref StopThrowEvent args)
    {
        ResolveJumpImpact(ent.Owner, ent.Comp);
    }

    private void OnKhorneJumpResolved(Entity<WH40KChaosKhorneJumpMarkerComponent> ent, ref StartCollideEvent args)
    {
        ResolveJumpImpact(ent.Owner, ent.Comp);
    }

    private void ResolveJumpImpact(EntityUid uid, WH40KChaosKhorneJumpMarkerComponent marker)
    {
        if (TerminatingOrDeleted(uid))
            return;

        if (TryComp<WH40KChaosGiftProgressionComponent>(uid, out var progression) &&
            progression.AttunedPatron == WH40KChaosPatron.Khorne)
        {
            var runtime = EnsureComp<WH40KChaosKhorneRuntimeComponent>(uid);
            runtime.JumpSpeedBuffMultiplier = MathF.Max(1f, marker.SpeedBuffMultiplier);
            runtime.JumpSpeedBuffExpiresAt = _timing.CurTime + marker.SpeedBuffDuration;
            _movementSpeed.RefreshMovementSpeedModifiers(uid);

            if (marker.ExExplosionEnabled)
                TriggerJumpExplosion(uid);
        }

        RemCompDeferred<WH40KChaosKhorneJumpMarkerComponent>(uid);
    }

    private void TriggerJumpExplosion(EntityUid caster)
    {
        _impactTargets.Clear();
        var center = _transform.GetMapCoordinates(caster);
        _lookup.GetEntitiesInRange(
            center.MapId,
            center.Position,
            JumpExExplosionRadius,
            _impactTargets,
            LookupFlags.Dynamic | LookupFlags.Uncontained);

        var damage = new DamageSpecifier(_bluntDamage, FixedPoint2.New(JumpExExplosionDamage));
        foreach (var target in _impactTargets)
        {
            if (target == caster || TerminatingOrDeleted(target))
                continue;

            if (!TryComp<DamageableComponent>(target, out var damageable))
                continue;

            _damageable.TryChangeDamage((target, damageable), damage, origin: caster);
        }

        _repulseAttract.TryRepulseAttract(
            center,
            caster,
            speed: JumpExExplosionRepulseSpeed,
            range: JumpExExplosionRadius,
            layer: CollisionGroup.GhostImpassable);
    }

    private void OnKhorneDash(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosKhorneDashActionEvent args)
    {
        if (!TryGetKhorneProgression(args.Performer, out var progression))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, DashBaseCooldown, progression.KhorneGiftThreeCooldownTier);

        var runtime = EnsureComp<WH40KChaosKhorneRuntimeComponent>(args.Performer);
        var giftThreeExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 3);
        if (giftThreeExUnlocked)
        {
            if (runtime.DashComboRemaining <= 0)
                runtime.DashComboRemaining = 3;

            runtime.DashComboRemaining--;
        }
        else
        {
            runtime.DashComboRemaining = 0;
        }

        var start = _transform.GetMapCoordinates(args.Performer);
        var target = _transform.ToMapCoordinates(args.Target);
        if (target.MapId != start.MapId)
            return;

        var direction = target.Position - start.Position;
        if (direction.LengthSquared() <= 0.0001f)
            return;

        var maxRange = GetDashRange(progression.KhorneGiftThreeUtilityTier);
        var end = FindDashEndpoint(args.Performer, start, direction, maxRange);
        if ((end.Position - start.Position).LengthSquared() < 0.05f)
            return;

        var dashVector = end.Position - start.Position;

        // Dash direction must be deterministic (cursor-driven), not affected by prior movement inertia.
        if (TryComp<PhysicsComponent>(args.Performer, out var performerPhysics))
            _physics.SetLinearVelocity(args.Performer, Vector2.Zero, body: performerPhysics);

        _throwing.TryThrow(
            args.Performer,
            dashVector,
            baseThrowSpeed: 20f,
            user: null,
            recoil: false,
            playSound: false,
            doSpin: false);

        if (!HasComp<ThrownItemComponent>(args.Performer))
            return;

        ApplyDashPathDamage(args.Performer, start, end, progression.KhorneGiftThreePowerTier, giftThreeExUnlocked);
        args.Handled = true;
    }

    private void OnKhorneDashActionPerformed(Entity<WH40KChaosKhorneDashActionComponent> ent, ref ActionPerformedEvent args)
    {
        if (!TryComp<WH40KChaosKhorneRuntimeComponent>(args.Performer, out var runtime))
            return;

        if (runtime.DashComboRemaining <= 0)
            return;

        if (!TryComp<ActionComponent>(ent.Owner, out var action))
            return;

        _actions.RemoveCooldown((ent.Owner, action));
    }

    private MapCoordinates FindDashEndpoint(EntityUid caster, MapCoordinates start, Vector2 direction, float maxRange)
    {
        var step = 0.25f;
        var norm = Vector2.Normalize(direction);
        var best = start;

        for (var travelled = step; travelled <= maxRange + 0.001f; travelled += step)
        {
            var candidate = new MapCoordinates(start.Position + norm * travelled, start.MapId);
            if (!_interaction.InRangeUnobstructed(
                    start,
                    candidate,
                    maxRange,
                    CollisionGroup.Impassable | CollisionGroup.InteractImpassable,
                    e => e == caster))
                break;

            best = candidate;
        }

        return best;
    }

    private void ApplyDashPathDamage(EntityUid caster, MapCoordinates start, MapCoordinates end, byte powerTier, bool exUnlocked)
    {
        _dashTargets.Clear();
        var max = (end.Position - start.Position).Length() + 1f;
        _lookup.GetEntitiesInRange(
            start.MapId,
            start.Position,
            max,
            _dashTargets,
            LookupFlags.Dynamic | LookupFlags.Uncontained);

        var damageAmount = 16f * GetDashDamageMultiplier(powerTier);
        var damage = new DamageSpecifier(_slashDamage, FixedPoint2.New(damageAmount));

        foreach (var target in _dashTargets)
        {
            if (target == caster || TerminatingOrDeleted(target))
                continue;

            if (!TryComp<MobStateComponent>(target, out var mob) || _mobState.IsDead(target, mob))
                continue;

            if (!TryComp<DamageableComponent>(target, out var damageable))
                continue;

            var targetPos = _transform.GetMapCoordinates(target);
            if (targetPos.MapId != start.MapId)
                continue;

            if (!DoesDashIntersectTarget(start.Position, end.Position, target))
                continue;

            _damageable.TryChangeDamage((target, damageable), damage, origin: caster);
            _stun.TryKnockdown(target, TimeSpan.FromSeconds(exUnlocked ? 2.6f : 2f), true, false, false, true);
            _stun.TryAddStunDuration(target, TimeSpan.FromSeconds(exUnlocked ? 1.2f : 0.8f));
        }
    }

    private bool DoesDashIntersectTarget(Vector2 start, Vector2 end, EntityUid target)
    {
        var xform = Transform(target);
        var bounds = _lookup.GetAABBNoContainer(target, xform.Coordinates.Position, xform.LocalRotation).Enlarged(DashHitPadding);
        return SegmentIntersectsBox(start, end, bounds);
    }

    private void ApplyTieredCooldown(EntityUid performer, Entity<ActionComponent> action, float baseSeconds, byte tier)
    {
        var duration = MathF.Max(0.1f, baseSeconds * WH40KChaosGiftUpgradeMath.CooldownMultiplier(tier));
        if (TryComp<WH40KChaosTzeentchAuraBuffComponent>(performer, out var tzeentchBuff) &&
            tzeentchBuff.CooldownExpiresAt > _timing.CurTime &&
            tzeentchBuff.CooldownMultiplier < 1f)
        {
            duration *= tzeentchBuff.CooldownMultiplier;
        }

        _actions.SetUseDelay((action.Owner, action.Comp), TimeSpan.FromSeconds(duration));
    }

    private void ApplyPassiveSpeedTierScaling(
        EntityUid uid,
        WH40KChaosGiftProgressionComponent progression,
        WH40KChaosKhorneRuntimeComponent runtime)
    {
        var desiredTier = progression.AttunedPatron == WH40KChaosPatron.Khorne
            ? progression.KhornePassiveSpeedTier
            : (byte) 0;

        if (runtime.AppliedPassiveSpeedTier == desiredTier)
            return;

        runtime.AppliedPassiveSpeedTier = desiredTier;
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void ApplyPassiveHealthThresholdScaling(
        EntityUid uid,
        WH40KChaosGiftProgressionComponent progression,
        WH40KChaosKhorneRuntimeComponent runtime)
    {
        var desiredTier = progression.AttunedPatron == WH40KChaosPatron.Khorne
            ? progression.KhornePassiveHealthTier
            : (byte) 0;

        if (runtime.AppliedPassiveHealthTier == desiredTier && runtime.BaselineCaptured)
            return;

        EnsureRuntimeBaseline(uid, runtime);
        if (runtime.BaselineThresholds.Count == 0 || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        var multiplier = GetPassiveHealthMultiplier(desiredTier);
        var scaled = new SortedDictionary<FixedPoint2, MobState>();
        foreach (var (threshold, state) in runtime.BaselineThresholds)
        {
            var value = threshold;
            if (state != MobState.Alive && threshold > 0)
                value = threshold * multiplier;

            while (scaled.ContainsKey(value))
            {
                value += FixedPoint2.New(0.01f);
            }

            scaled[value] = state;
        }

        foreach (var (threshold, state) in scaled)
        {
            _mobThresholds.SetMobStateThreshold(uid, threshold, state, thresholds);
        }

        _mobThresholds.VerifyThresholds(uid, thresholds);
        runtime.AppliedPassiveHealthTier = desiredTier;
    }

    private void EnsureRuntimeBaseline(EntityUid uid, WH40KChaosKhorneRuntimeComponent runtime)
    {
        if (runtime.BaselineCaptured)
            return;

        if (TryComp<MovementSpeedModifierComponent>(uid, out var movement))
        {
            runtime.BaseWalkSpeed = movement.BaseWalkSpeed;
            runtime.BaseSprintSpeed = movement.BaseSprintSpeed;
        }

        if (TryComp<MobThresholdsComponent>(uid, out var thresholds))
            runtime.BaselineThresholds = new SortedDictionary<FixedPoint2, MobState>(thresholds.Thresholds);

        runtime.BaselineCaptured = true;
    }

    private void RestoreBaselineThresholds(EntityUid uid, WH40KChaosKhorneRuntimeComponent runtime)
    {
        if (runtime.BaselineThresholds.Count == 0 || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        foreach (var (threshold, state) in runtime.BaselineThresholds)
        {
            _mobThresholds.SetMobStateThreshold(uid, threshold, state, thresholds);
        }

        _mobThresholds.VerifyThresholds(uid, thresholds);
    }

    private bool TryGetKhorneProgression(EntityUid uid, out WH40KChaosGiftProgressionComponent progression)
    {
        progression = null!;

        if (!HasComp<WH40KChaosGiftRoleComponent>(uid))
            return false;

        if (!TryComp<WH40KChaosGiftProgressionComponent>(uid, out var found) || found == null)
            return false;

        progression = found;
        return progression.AttunedPatron == WH40KChaosPatron.Khorne;
    }

    private static float GetRepulseSpeed(byte tier, bool exPull)
    {
        var speed = tier switch
        {
            1 => 13f,
            2 => 16f,
            3 => 20f,
            _ => 10f,
        };

        return exPull ? -speed : speed;
    }

    private static float GetRepulseRange(byte tier)
    {
        return tier switch
        {
            1 => 6.5f,
            2 => 8f,
            3 => 9.5f,
            _ => 5f,
        };
    }

    private static float GetJumpRange(byte tier)
    {
        return tier switch
        {
            1 => 6.5f,
            2 => 8f,
            3 => 9.5f,
            _ => 5f,
        };
    }

    private static float GetJumpSpeedBuffMultiplier(byte tier)
    {
        return tier switch
        {
            1 => 1.15f,
            2 => 1.30f,
            3 => 1.45f,
            _ => 1.0f,
        };
    }

    private static float GetDashRange(byte tier)
    {
        return tier switch
        {
            1 => 6f,
            2 => 7.5f,
            3 => 9f,
            _ => 4.5f,
        };
    }

    private static float GetDashDamageMultiplier(byte tier)
    {
        return tier switch
        {
            1 => 1.2f,
            2 => 1.45f,
            3 => 1.8f,
            _ => 1f,
        };
    }

    private static float GetPassiveSpeedMultiplier(byte tier)
    {
        return tier switch
        {
            1 => 1.05f,
            2 => 1.10f,
            3 => 1.15f,
            _ => 1f,
        };
    }

    private static float GetPassiveHealthMultiplier(byte tier)
    {
        return tier switch
        {
            1 => 1.05f,
            2 => 1.10f,
            3 => 1.15f,
            _ => 1f,
        };
    }

    private static float GetPassiveMeleeMultiplier(byte tier)
    {
        return tier switch
        {
            1 => 1.10f,
            2 => 1.25f,
            3 => 1.50f,
            _ => 1f,
        };
    }

    private static bool SegmentIntersectsBox(Vector2 start, Vector2 end, Box2 box)
    {
        var tMin = 0f;
        var tMax = 1f;
        var delta = end - start;

        return ClipSegmentAxis(-delta.X, start.X - box.Left, ref tMin, ref tMax) &&
               ClipSegmentAxis(delta.X, box.Right - start.X, ref tMin, ref tMax) &&
               ClipSegmentAxis(-delta.Y, start.Y - box.Bottom, ref tMin, ref tMax) &&
               ClipSegmentAxis(delta.Y, box.Top - start.Y, ref tMin, ref tMax);
    }

    private static bool ClipSegmentAxis(float p, float q, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(p) <= 0.0001f)
            return q >= 0f;

        var ratio = q / p;
        if (p < 0f)
        {
            if (ratio > tMax)
                return false;

            if (ratio > tMin)
                tMin = ratio;
        }
        else
        {
            if (ratio < tMin)
                return false;

            if (ratio < tMax)
                tMax = ratio;
        }

        return true;
    }
}
