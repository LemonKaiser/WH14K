using System;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Magic;
using Content.Shared.Magic.Events;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Runtime tuning backend for Tzeentch gift upgrades.
/// Applies cooldown and effect scaling for barrier, mind-twist and warp-rewrite actions.
/// </summary>
public sealed class WH40KChaosTzeentchGiftAbilitySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    private static readonly EntProtoId BarrierWallTierOnePrototype = "WH40KWallForceTzeentchTier1";
    private static readonly EntProtoId BarrierWallTierTwoPrototype = "WH40KWallForceTzeentchTier2";
    private static readonly EntProtoId BarrierWallTierThreePrototype = "WH40KWallForceTzeentchTier3";
    private static readonly EntProtoId BarrierWallExPrototype = "WH40KWallForceTzeentchEx";

    private static readonly EntProtoId WarpRewriteTierOneProjectile = "WH40KProjectileChaosWarpBlastTzeentchTier1";
    private static readonly EntProtoId WarpRewriteTierTwoProjectile = "WH40KProjectileChaosWarpBlastTzeentchTier2";
    private static readonly EntProtoId WarpRewriteTierThreeProjectile = "WH40KProjectileChaosWarpBlastTzeentchTier3";
    private static readonly EntProtoId WarpRewriteExProjectile = "WH40KProjectileChaosWarpBlastTzeentchEx";

    private const string TzeentchBarrierAction = "ActionWH40KChaosTzeentchBarrier";
    private const string TzeentchMindTwistAction = "ActionWH40KChaosTzeentchMindTwist";
    private const string TzeentchWarpRewriteAction = "ActionWH40KChaosTzeentchWarpRewrite";

    private const float BarrierBaseCooldown = 16f;
    private const float MindTwistBaseCooldown = 130f;
    private const float WarpRewriteBaseCooldown = 60f;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, InstantSpawnSpellEvent>(
            OnTzeentchBarrierCast,
            before: [typeof(SharedMagicSystem)]);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, MindSwapSpellEvent>(
            OnTzeentchMindTwistCast,
            before: [typeof(SharedMagicSystem)]);
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, ProjectileSpellEvent>(
            OnTzeentchWarpRewriteCast,
            before: [typeof(SharedMagicSystem)]);
    }

    private void OnTzeentchBarrierCast(Entity<WH40KChaosGiftRoleComponent> ent, ref InstantSpawnSpellEvent args)
    {
        if (!TryGetTzeentchProgression(ent.Owner, args.Action.Owner, TzeentchBarrierAction, out var progression))
            return;

        ApplyTieredCooldown(args.Action, BarrierBaseCooldown, progression.KhorneGiftOneCooldownTier);
        args.Prototype = ResolveBarrierWallPrototype(
            progression.KhorneGiftOnePowerTier,
            progression.KhorneGiftOneUtilityTier,
            progression.KhorneGiftOneExUnlocked);
    }

    private void OnTzeentchMindTwistCast(Entity<WH40KChaosGiftRoleComponent> ent, ref MindSwapSpellEvent args)
    {
        if (!TryGetTzeentchProgression(ent.Owner, args.Action.Owner, TzeentchMindTwistAction, out var progression))
            return;

        ApplyTieredCooldown(args.Action, MindTwistBaseCooldown, progression.KhorneGiftTwoCooldownTier);

        var targetSeconds = GetMindTwistTargetStunSeconds(
            progression.KhorneGiftTwoPowerTier,
            progression.KhorneGiftTwoExUnlocked);
        var performerSeconds = GetMindTwistPerformerStunSeconds(
            progression.KhorneGiftTwoUtilityTier,
            progression.KhorneGiftTwoExUnlocked);

        args.TargetStunDuration = TimeSpan.FromSeconds(targetSeconds);
        args.PerformerStunDuration = TimeSpan.FromSeconds(performerSeconds);
    }

    private void OnTzeentchWarpRewriteCast(Entity<WH40KChaosGiftRoleComponent> ent, ref ProjectileSpellEvent args)
    {
        if (!TryGetTzeentchProgression(ent.Owner, args.Action.Owner, TzeentchWarpRewriteAction, out var progression))
            return;

        ApplyFixedCooldown(args.Action, WarpRewriteBaseCooldown);
        args.Prototype = ResolveWarpRewriteProjectile(
            progression.KhorneGiftThreePowerTier,
            progression.KhorneGiftThreeUtilityTier,
            progression.KhorneGiftThreeExUnlocked);
    }

    private void ApplyTieredCooldown(Entity<ActionComponent> action, float baseSeconds, byte tier)
    {
        var duration = MathF.Max(0.1f, baseSeconds * WH40KChaosGiftUpgradeMath.CooldownMultiplier(tier));
        _actions.SetUseDelay((action.Owner, action.Comp), TimeSpan.FromSeconds(duration));
    }

    private void ApplyFixedCooldown(Entity<ActionComponent> action, float seconds)
    {
        var duration = MathF.Max(0.1f, seconds);
        _actions.SetUseDelay((action.Owner, action.Comp), TimeSpan.FromSeconds(duration));
    }

    private bool TryGetTzeentchProgression(
        EntityUid performer,
        EntityUid actionUid,
        string expectedActionPrototype,
        out WH40KChaosGiftProgressionComponent progression)
    {
        progression = null!;

        if (!TryComp<WH40KChaosGiftProgressionComponent>(performer, out var found) || found == null)
            return false;

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

    private static EntProtoId ResolveWarpRewriteProjectile(byte powerTier, byte utilityTier, bool exUnlocked)
    {
        if (exUnlocked)
            return WarpRewriteExProjectile;

        var score = powerTier + utilityTier;
        if (score >= 5)
            return WarpRewriteTierThreeProjectile;

        if (score >= 3)
            return WarpRewriteTierTwoProjectile;

        if (score >= 1)
            return WarpRewriteTierOneProjectile;

        return "WH40KProjectileChaosWarpBlast";
    }

    private static float GetMindTwistTargetStunSeconds(byte powerTier, bool exUnlocked)
    {
        var baseSeconds = powerTier switch
        {
            1 => 12f,
            2 => 14f,
            3 => 16f,
            _ => 10f,
        };

        if (!exUnlocked)
            return baseSeconds;

        return baseSeconds + 4f;
    }

    private static float GetMindTwistPerformerStunSeconds(byte utilityTier, bool exUnlocked)
    {
        var baseSeconds = utilityTier switch
        {
            1 => 8f,
            2 => 6f,
            3 => 4f,
            _ => 10f,
        };

        if (exUnlocked)
            baseSeconds -= 2f;

        return MathF.Max(0.5f, baseSeconds);
    }
}
