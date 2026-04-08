using System;
using System.Collections.Generic;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Magic;
using Content.Shared.Magic.Events;
using Content.Shared.Movement.Systems;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

public sealed class WH40KChaosTzeentchGiftAbilitySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly WH40KChaosNurgleGiftAbilitySystem _nurgle = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly EntProtoId BarrierWallTierOnePrototype = "WH40KWallForceTzeentchTier1";
    private static readonly EntProtoId BarrierWallTierTwoPrototype = "WH40KWallForceTzeentchTier2";
    private static readonly EntProtoId BarrierWallTierThreePrototype = "WH40KWallForceTzeentchTier3";
    private static readonly EntProtoId BarrierWallExPrototype = "WH40KWallForceTzeentchEx";
    private static readonly EntProtoId FireballTierOneProjectile = "WH40KProjectileChaosWarpBlastTzeentchTier1";
    private static readonly EntProtoId FireballTierTwoProjectile = "WH40KProjectileChaosWarpBlastTzeentchTier2";
    private static readonly EntProtoId FireballTierThreeProjectile = "WH40KProjectileChaosWarpBlastTzeentchTier3";
    private static readonly EntProtoId FireballExProjectile = "WH40KProjectileChaosWarpBlastTzeentchEx";

    private const string TzeentchFireballAction = "ActionWH40KChaosTzeentchFireball";
    private const string TzeentchBarrierAction = "ActionWH40KChaosTzeentchBarrier";
    private const string TzeentchAuraAction = "ActionWH40KChaosTzeentchMindTwist";

    private const float FireballBaseCooldown = 45f;
    private const float BarrierBaseCooldown = 18f;
    private const float AuraBaseCooldown = 42f;

    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, ProjectileSpellEvent>(
            OnTzeentchFireball,
            before: [typeof(SharedMagicSystem)]);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, InstantSpawnSpellEvent>(
            OnTzeentchBarrierCast,
            before: [typeof(SharedMagicSystem)]);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosTzeentchSpeedAuraActionEvent>(OnTzeentchChosenAura);
        SubscribeLocalEvent<WH40KChaosTzeentchAuraBuffComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<WH40KChaosTzeentchAuraBuffComponent, ComponentShutdown>(OnBuffShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KChaosTzeentchAuraBuffComponent>();
        while (query.MoveNext(out var uid, out var buff))
        {
            var changedSpeed = false;

            if (buff.SpeedExpiresAt != TimeSpan.Zero && now >= buff.SpeedExpiresAt)
            {
                buff.SpeedExpiresAt = TimeSpan.Zero;
                buff.SpeedMultiplier = 1f;
                changedSpeed = true;
            }

            if (buff.CooldownExpiresAt != TimeSpan.Zero && now >= buff.CooldownExpiresAt)
            {
                buff.CooldownExpiresAt = TimeSpan.Zero;
                buff.CooldownMultiplier = 1f;
            }

            if (buff.VisionExpiresAt != TimeSpan.Zero && now >= buff.VisionExpiresAt)
                RestoreVision(uid, buff);

            if (changedSpeed)
                _movementSpeed.RefreshMovementSpeedModifiers(uid);

            if (buff.SpeedExpiresAt == TimeSpan.Zero && buff.VisionExpiresAt == TimeSpan.Zero && buff.CooldownExpiresAt == TimeSpan.Zero)
                RemCompDeferred<WH40KChaosTzeentchAuraBuffComponent>(uid);
        }
    }

    private void OnTzeentchFireball(Entity<WH40KChaosGiftRoleComponent> ent, ref ProjectileSpellEvent args)
    {
        if (!TryGetTzeentchProgression(ent.Owner, args.Action.Owner, TzeentchFireballAction, out var progression))
        {
            _nurgle.TryHandleProjectileSpell(ent, ref args);
            return;
        }

        ApplyTieredCooldown(args.Performer, args.Action, FireballBaseCooldown, progression.KhorneGiftOneCooldownTier);
        var giftOneExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1);
        args.Prototype = ResolveFireballProjectile(
            progression.KhorneGiftOnePowerTier,
            progression.KhorneGiftOneUtilityTier,
            giftOneExUnlocked);
    }

    private void OnTzeentchBarrierCast(Entity<WH40KChaosGiftRoleComponent> ent, ref InstantSpawnSpellEvent args)
    {
        if (!TryGetTzeentchProgression(ent.Owner, args.Action.Owner, TzeentchBarrierAction, out var progression))
        {
            _nurgle.TryHandleInstantSpawnSpell(ent, ref args);
            return;
        }

        ApplyTieredCooldown(args.Performer, args.Action, BarrierBaseCooldown, progression.KhorneGiftTwoCooldownTier);
        var giftTwoExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 2);
        args.Prototype = ResolveBarrierWallPrototype(
            progression.KhorneGiftTwoPowerTier,
            progression.KhorneGiftTwoUtilityTier,
            giftTwoExUnlocked);
    }

    private void OnTzeentchChosenAura(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosTzeentchSpeedAuraActionEvent args)
    {
        if (!TryGetTzeentchProgression(ent.Owner, args.Action.Owner, TzeentchAuraAction, out _))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, AuraBaseCooldown, 0);
        ApplyChosenAura(args.Performer, radius: 6.5f, duration: 30f, speedMultiplier: 1.18f, cooldownMultiplier: 0.85f);
        args.Handled = true;
    }

    private void OnRefreshMovementSpeed(Entity<WH40KChaosTzeentchAuraBuffComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.SpeedExpiresAt == TimeSpan.Zero || ent.Comp.SpeedExpiresAt <= _timing.CurTime || ent.Comp.SpeedMultiplier <= 1f)
            return;

        args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier, MovementSpeedModifierLayer.Status);
    }

    private void OnBuffShutdown(Entity<WH40KChaosTzeentchAuraBuffComponent> ent, ref ComponentShutdown args)
    {
        RestoreVision(ent.Owner, ent.Comp);
    }

    private void ApplyChosenAura(EntityUid performer, float radius, float duration, float speedMultiplier, float cooldownMultiplier)
    {
        var now = _timing.CurTime;
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(performer).Coordinates, radius, _nearby, LookupFlags.Dynamic | LookupFlags.Uncontained);
        _nearby.Add(performer);

        foreach (var target in _nearby)
        {
            if (!IsTzeentchFollower(target))
                continue;

            var buff = EnsureComp<WH40KChaosTzeentchAuraBuffComponent>(target);
            var refreshSpeed = buff.SpeedMultiplier <= 1f || buff.SpeedExpiresAt <= now;
            buff.SpeedMultiplier = MathF.Max(buff.SpeedMultiplier, speedMultiplier);

            var expiresAt = now + TimeSpan.FromSeconds(duration);
            if (expiresAt > buff.SpeedExpiresAt)
                buff.SpeedExpiresAt = expiresAt;

            buff.CooldownMultiplier = MathF.Min(buff.CooldownMultiplier <= 0f ? 1f : buff.CooldownMultiplier, cooldownMultiplier);
            if (expiresAt > buff.CooldownExpiresAt)
                buff.CooldownExpiresAt = expiresAt;

            if (TryComp<EyeComponent>(target, out var eye))
            {
                if (!buff.EyeBaselineCaptured)
                {
                    buff.BaselineDrawFov = eye.DrawFov;
                    buff.BaselineDrawLight = eye.DrawLight;
                    buff.EyeBaselineCaptured = true;
                }

                _eye.SetDrawFov(target, false, eye);
                _eye.SetDrawLight((target, eye), false);
                if (expiresAt > buff.VisionExpiresAt)
                    buff.VisionExpiresAt = expiresAt;
            }

            if (refreshSpeed)
                _movementSpeed.RefreshMovementSpeedModifiers(target);
        }
    }

    private void RestoreVision(EntityUid uid, WH40KChaosTzeentchAuraBuffComponent buff)
    {
        if (!buff.EyeBaselineCaptured)
            return;

        if (TryComp<EyeComponent>(uid, out var eye))
        {
            _eye.SetDrawFov(uid, buff.BaselineDrawFov, eye);
            _eye.SetDrawLight((uid, eye), buff.BaselineDrawLight);
        }

        buff.VisionExpiresAt = TimeSpan.Zero;
        buff.EyeBaselineCaptured = false;
    }

    private void ApplyTieredCooldown(EntityUid performer, Entity<ActionComponent> action, float baseSeconds, byte tier)
    {
        var duration = MathF.Max(0.1f, baseSeconds * WH40KChaosGiftUpgradeMath.CooldownMultiplier(tier));
        if (TryComp<WH40KChaosTzeentchAuraBuffComponent>(performer, out var buff) &&
            buff.CooldownExpiresAt > _timing.CurTime &&
            buff.CooldownMultiplier < 1f)
        {
            duration *= buff.CooldownMultiplier;
        }

        _actions.SetUseDelay((action.Owner, action.Comp), TimeSpan.FromSeconds(duration));
    }

    private bool TryGetTzeentchProgression(EntityUid performer, EntityUid actionUid, string expectedActionPrototype, out WH40KChaosGiftProgressionComponent progression)
    {
        progression = null!;

        if (!TryComp<WH40KChaosGiftProgressionComponent>(performer, out var found) ||
            found == null)
        {
            return false;
        }

        if (found.AttunedPatron != WH40KChaosPatron.Tzeentch)
            return false;

        var actionPrototype = MetaData(actionUid).EntityPrototype?.ID;
        if (!string.Equals(actionPrototype, expectedActionPrototype, StringComparison.Ordinal))
            return false;

        progression = found;
        return true;
    }

    private static EntProtoId ResolveBarrierWallPrototype(byte powerTier, byte utilityTier, bool exUnlocked)
    {
        if (exUnlocked)
            return BarrierWallExPrototype;

        var score = powerTier + utilityTier;
        if (score >= 5)
            return BarrierWallTierThreePrototype;
        if (score >= 3)
            return BarrierWallTierTwoPrototype;
        if (score >= 1)
            return BarrierWallTierOnePrototype;

        return "WallForce";
    }

    private static EntProtoId ResolveFireballProjectile(byte powerTier, byte utilityTier, bool exUnlocked)
    {
        if (exUnlocked)
            return FireballExProjectile;

        var score = powerTier + utilityTier;
        if (score >= 5)
            return FireballTierThreeProjectile;
        if (score >= 3)
            return FireballTierTwoProjectile;
        if (score >= 1)
            return FireballTierOneProjectile;

        return "WH40KProjectileChaosWarpBlast";
    }

    private bool IsTzeentchFollower(EntityUid uid)
    {
        return HasComp<WH40KChaosGiftRoleComponent>(uid) &&
               TryComp<WH40KChaosGiftProgressionComponent>(uid, out var progression) &&
               progression.AttunedPatron == WH40KChaosPatron.Tzeentch;
    }
}
