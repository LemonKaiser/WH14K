using System;
using System.Collections.Generic;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.MetaProgress;
using Content.Server.Ghost;
using Content.Server.Ghost.Roles.Components;
using Content.Server.KillTracking;
using Content.Server.Mind;
using Content.Server.Zombies;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Magic;
using Content.Shared.Magic.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Psyker;
using Robust.Server.Player;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

public sealed class WH40KChaosNurgleGiftAbilitySystem : EntitySystem
{
    private const string NurgleMiasmaAction = "ActionWH40KChaosNurgleMiasma";
    private const string NurgleAcidSpitAction = "ActionWH40KChaosNurgleRepulse";
    private const string NurgleCorpseRiseAction = "ActionWH40KChaosNurgleCorpseBloom";
    private const float NurgleMiasmaBaseCooldownSeconds = 180f;
    private const string HereticTeamId = "Heretics";

    private static readonly EntProtoId AcidSpitTierZeroProjectile = "WH40KProjectileChaosNurgleAcidSpit";
    private static readonly EntProtoId AcidSpitTierOneProjectile = "WH40KProjectileChaosNurgleAcidSpitTier1";
    private static readonly EntProtoId AcidSpitTierTwoProjectile = "WH40KProjectileChaosNurgleAcidSpitTier2";
    private static readonly EntProtoId AcidSpitTierThreeProjectile = "WH40KProjectileChaosNurgleAcidSpitTier3";
    private static readonly EntProtoId AcidSpitExProjectile = "WH40KProjectileChaosNurgleAcidSpitEx";

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThresholds = default!;
    [Dependency] private readonly GhostSystem _ghost = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly WH40KTeamNpcFactionSystem _teamNpcFactions = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ZombieSystem _zombie = default!;

    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, WH40KChaosNurgleCorpseRiseActionEvent>(OnNurgleCorpseRise);
        SubscribeLocalEvent<WH40KConfirmedEliminationEvent>(OnConfirmedElimination);
        SubscribeLocalEvent<WH40KChaosNurgleRuntimeComponent, ComponentShutdown>(OnRuntimeShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var buffQuery = EntityQueryEnumerator<WH40KChaosNurgleMiasmaBlessingComponent, DamageableComponent>();
        while (buffQuery.MoveNext(out var uid, out var buff, out var damageable))
        {
            if (buff.ExpiresAt != TimeSpan.Zero && now >= buff.ExpiresAt)
            {
                RemCompDeferred<WH40KChaosNurgleMiasmaBlessingComponent>(uid);
                continue;
            }

            if (now < buff.NextTickAt || _mobState.IsDead(uid))
                continue;

            buff.NextTickAt = now + TimeSpan.FromSeconds(1);
            _damageable.HealEvenly((uid, damageable), FixedPoint2.New(-buff.HealPerSecond), origin: uid, ignoreGlobalModifiers: true);
        }

        var query = EntityQueryEnumerator<WH40KChaosGiftProgressionComponent>();
        while (query.MoveNext(out var uid, out var progression))
        {
            if (progression.AttunedPatron != WH40KChaosPatron.Nurgle)
                continue;

            var runtime = EnsureComp<WH40KChaosNurgleRuntimeComponent>(uid);
            ApplyPassiveThresholdScaling(uid, progression, runtime);

            if (now < runtime.NextPassiveRegenAt)
                continue;

            runtime.NextPassiveRegenAt = now + TimeSpan.FromSeconds(1);
            TryApplyPassiveRotRegen(uid, progression);
        }
    }

    private void OnRuntimeShutdown(Entity<WH40KChaosNurgleRuntimeComponent> ent, ref ComponentShutdown args)
    {
        RestoreBaselineThresholds(ent.Owner, ent.Comp);
    }

    public bool TryHandleInstantSpawnSpell(Entity<WH40KChaosGiftRoleComponent> ent, ref InstantSpawnSpellEvent args)
    {
        if (!TryGetNurgleProgression(ent.Owner, out var progression))
            return false;

        var actionPrototype = MetaData(args.Action.Owner).EntityPrototype?.ID;
        if (!string.Equals(actionPrototype, NurgleMiasmaAction, StringComparison.Ordinal))
            return false;

        ApplyTieredCooldown(
            args.Performer,
            args.Action,
            NurgleMiasmaBaseCooldownSeconds,
            progression.KhorneGiftOneCooldownTier,
            NurgleMiasmaBaseCooldownSeconds);
        var giftOneExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 1);
        ApplyMiasmaBlessing(
            args.Performer,
            GetMiasmaRadius(progression.KhorneGiftOneUtilityTier),
            GetMiasmaDuration(progression.KhorneGiftOneUtilityTier, giftOneExUnlocked),
            GetMiasmaHeal(progression.KhorneGiftOnePowerTier, giftOneExUnlocked));

        return true;
    }

    public bool TryHandleProjectileSpell(Entity<WH40KChaosGiftRoleComponent> ent, ref ProjectileSpellEvent args)
    {
        if (!TryGetNurgleProgression(ent.Owner, out var progression))
            return false;

        var actionPrototype = MetaData(args.Action.Owner).EntityPrototype?.ID;
        if (!string.Equals(actionPrototype, NurgleAcidSpitAction, StringComparison.Ordinal))
            return false;

        ApplyTieredCooldown(args.Performer, args.Action, 11f, progression.KhorneGiftTwoCooldownTier);
        var giftTwoExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 2);
        args.Prototype = ResolveAcidProjectile(
            progression.KhorneGiftTwoPowerTier,
            progression.KhorneGiftTwoUtilityTier,
            giftTwoExUnlocked);

        return true;
    }

    private void OnNurgleCorpseRise(Entity<WH40KChaosGiftRoleComponent> ent, ref WH40KChaosNurgleCorpseRiseActionEvent args)
    {
        if (!TryGetNurgleProgression(args.Performer, out var progression))
            return;

        var actionPrototype = MetaData(args.Action.Owner).EntityPrototype?.ID;
        if (!string.Equals(actionPrototype, NurgleCorpseRiseAction, StringComparison.Ordinal))
            return;

        if (!_mobState.IsDead(args.Target))
            return;

        ApplyTieredCooldown(args.Performer, args.Action, 35f, progression.KhorneGiftThreeCooldownTier);
        DetachCorpseRiseMind(args.Target);
        _zombie.ZombifyEntity(args.Target);
        ConfigureCorpseRiseZombie(args.Target);

        if (TryComp<DamageableComponent>(args.Target, out var damageable))
        {
            var giftThreeExUnlocked = WH40KChaosLeaderRuntimeRules.IsGiftExUnlocked(progression, 3);
            var healAmount = FixedPoint2.New(GetCorpseRiseHeal(
                progression.KhorneGiftThreePowerTier,
                progression.KhorneGiftThreeUtilityTier,
                giftThreeExUnlocked));
            _damageable.HealEvenly((args.Target, damageable), -healAmount, origin: args.Performer, ignoreGlobalModifiers: true);
        }

        args.Handled = true;
    }

    private void DetachCorpseRiseMind(EntityUid target)
    {
        var mindId = _mind.GetMind(target);
        if (mindId is not { } resolvedMindId || !TryComp<MindComponent>(resolvedMindId, out var mind))
            return;

        if (mind.UserId is { } userId &&
            _player.TryGetSessionById(userId, out _) &&
            _ghost.SpawnGhost((resolvedMindId, mind), Transform(target).Coordinates, false) != null)
        {
            return;
        }

        _mind.TransferTo(resolvedMindId, null, createGhost: false, mind: mind);
    }

    private void ConfigureCorpseRiseZombie(EntityUid target)
    {
        RemComp<GhostTakeoverAvailableComponent>(target);
        RemComp<GhostRoleComponent>(target);
        RemComp<GhostRoleRaffleComponent>(target);
        EnsureComp<NonSpreaderZombieComponent>(target);

        var teamMember = EnsureComp<WH40KTeamMemberComponent>(target);
        teamMember.TeamId = HereticTeamId;

        var factionIcon = EnsureComp<WH40KTeamBattleFactionIconComponent>(target);
        if (!string.Equals(factionIcon.TeamId, HereticTeamId, StringComparison.OrdinalIgnoreCase))
        {
            factionIcon.TeamId = HereticTeamId;
            Dirty(target, factionIcon);
        }

        _teamNpcFactions.ApplyTeamFaction(target, HereticTeamId);
    }

    private void OnConfirmedElimination(WH40KConfirmedEliminationEvent ev)
    {
        if (ev.Suicide)
            return;

        var source = ev.Primary;
        if (!_player.TryGetSessionById(source.PlayerId, out var session) || session.AttachedEntity is not { Valid: true } killer)
            return;

        if (!TryComp<WH40KChaosGiftProgressionComponent>(killer, out var progression) ||
            progression.AttunedPatron != WH40KChaosPatron.Nurgle ||
            progression.KhornePassiveSpeedTier <= 0)
        {
            return;
        }

        var runtime = EnsureComp<WH40KChaosNurgleRuntimeComponent>(killer);
        var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);
        runtime.KillStacks = Math.Min(
            GetKillStackCap(progression.KhornePassiveSpeedTier, passiveExUnlocked),
            runtime.KillStacks + 1);
    }

    private void ApplyMiasmaBlessing(EntityUid performer, float radius, float duration, float healPerSecond)
    {
        var now = _timing.CurTime;
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(performer).Coordinates, radius, _nearby, LookupFlags.Dynamic | LookupFlags.Uncontained);
        _nearby.Add(performer);

        foreach (var target in _nearby)
        {
            if (!IsNurgleFollower(target))
                continue;

            var buff = EnsureComp<WH40KChaosNurgleMiasmaBlessingComponent>(target);
            var nextExpiry = now + TimeSpan.FromSeconds(duration);
            if (nextExpiry > buff.ExpiresAt)
                buff.ExpiresAt = nextExpiry;
            buff.NextTickAt = now;
            buff.HealPerSecond = MathF.Max(buff.HealPerSecond, healPerSecond);
        }
    }

    private void TryApplyPassiveRotRegen(EntityUid uid, WH40KChaosGiftProgressionComponent progression)
    {
        var healPerSecond = GetPassiveRotRegen(
            progression.KhornePassiveMeleeTier,
            WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression));
        if (healPerSecond <= 0f || !TryComp<DamageableComponent>(uid, out var damageable))
            return;

        _damageable.HealEvenly((uid, damageable), FixedPoint2.New(-healPerSecond), origin: uid, ignoreGlobalModifiers: true);
    }

    private void ApplyPassiveThresholdScaling(EntityUid uid, WH40KChaosGiftProgressionComponent progression, WH40KChaosNurgleRuntimeComponent runtime)
    {
        var killTier = progression.AttunedPatron == WH40KChaosPatron.Nurgle ? progression.KhornePassiveSpeedTier : (byte) 0;
        var healthTier = progression.AttunedPatron == WH40KChaosPatron.Nurgle ? progression.KhornePassiveHealthTier : (byte) 0;
        var passiveExUnlocked = WH40KChaosLeaderRuntimeRules.IsPassiveExUnlocked(progression);
        var bonusPerKill = GetKillBonusPerKill(killTier, passiveExUnlocked);
        var bonusHealth = runtime.KillStacks * bonusPerKill;

        if (runtime.AppliedKillTier == killTier &&
            runtime.AppliedHealthTier == healthTier &&
            runtime.AppliedKillStacks == runtime.KillStacks &&
            runtime.BaselineCaptured)
        {
            return;
        }

        EnsureRuntimeBaseline(uid, runtime);
        if (runtime.BaselineThresholds.Count == 0 || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        var multiplier = GetPassiveHealthMultiplier(healthTier, passiveExUnlocked);
        var scaled = new SortedDictionary<FixedPoint2, MobState>();
        foreach (var (threshold, state) in runtime.BaselineThresholds)
        {
            var value = threshold;
            if (state != MobState.Alive && threshold > 0)
                value = threshold * multiplier + FixedPoint2.New(bonusHealth);

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
        runtime.AppliedKillTier = killTier;
        runtime.AppliedHealthTier = healthTier;
        runtime.AppliedKillStacks = runtime.KillStacks;
    }

    private void EnsureRuntimeBaseline(EntityUid uid, WH40KChaosNurgleRuntimeComponent runtime)
    {
        if (runtime.BaselineCaptured)
            return;

        if (TryComp<MobThresholdsComponent>(uid, out var thresholds))
            runtime.BaselineThresholds = new SortedDictionary<FixedPoint2, MobState>(thresholds.Thresholds);

        runtime.BaselineCaptured = true;
    }

    private void RestoreBaselineThresholds(EntityUid uid, WH40KChaosNurgleRuntimeComponent runtime)
    {
        if (runtime.BaselineThresholds.Count == 0 || !TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        foreach (var (threshold, state) in runtime.BaselineThresholds)
        {
            _mobThresholds.SetMobStateThreshold(uid, threshold, state, thresholds);
        }

        _mobThresholds.VerifyThresholds(uid, thresholds);
    }

    private void ApplyTieredCooldown(
        EntityUid performer,
        Entity<ActionComponent> action,
        float baseSeconds,
        byte tier,
        float minimumSeconds = 0.1f)
    {
        var duration = MathF.Max(0.1f, baseSeconds * WH40KChaosGiftUpgradeMath.CooldownMultiplier(tier));
        if (TryComp<WH40KChaosTzeentchAuraBuffComponent>(performer, out var tzeentchBuff) &&
            tzeentchBuff.CooldownExpiresAt > _timing.CurTime &&
            tzeentchBuff.CooldownMultiplier < 1f)
        {
            duration *= tzeentchBuff.CooldownMultiplier;
        }

        duration = MathF.Max(minimumSeconds, duration);
        _actions.SetUseDelay((action.Owner, action.Comp), TimeSpan.FromSeconds(duration));
    }

    private bool TryGetNurgleProgression(EntityUid uid, out WH40KChaosGiftProgressionComponent progression)
    {
        progression = null!;

        if (!TryComp<WH40KChaosGiftProgressionComponent>(uid, out var found) || found == null)
            return false;

        if (found.AttunedPatron != WH40KChaosPatron.Nurgle)
            return false;

        progression = found;
        return true;
    }

    private bool IsNurgleFollower(EntityUid uid)
    {
        return HasComp<WH40KChaosGiftRoleComponent>(uid) &&
               TryComp<WH40KChaosGiftProgressionComponent>(uid, out var progression) &&
               progression.AttunedPatron == WH40KChaosPatron.Nurgle;
    }

    private static EntProtoId ResolveAcidProjectile(byte powerTier, byte utilityTier, bool exUnlocked)
    {
        if (exUnlocked)
            return AcidSpitExProjectile;

        var score = powerTier + utilityTier;
        if (score >= 5)
            return AcidSpitTierThreeProjectile;
        if (score >= 3)
            return AcidSpitTierTwoProjectile;
        if (score >= 1)
            return AcidSpitTierOneProjectile;

        return AcidSpitTierZeroProjectile;
    }

    private static float GetMiasmaHeal(byte tier, bool exUnlocked)
    {
        var heal = tier switch
        {
            1 => 1.8f,
            2 => 2.6f,
            3 => 3.6f,
            _ => 1.3f,
        };

        return exUnlocked ? heal + 1f : heal;
    }

    private static float GetMiasmaRadius(byte tier)
    {
        return tier switch
        {
            1 => 5.5f,
            2 => 6.5f,
            3 => 7.5f,
            _ => 4.75f,
        };
    }

    private static float GetMiasmaDuration(byte tier, bool exUnlocked)
    {
        var seconds = tier switch
        {
            1 => 12f,
            2 => 15f,
            3 => 18f,
            _ => 10f,
        };

        return exUnlocked ? seconds + 4f : seconds;
    }

    private static float GetCorpseBloomHeal(byte tier, bool exUnlocked)
    {
        var heal = tier switch
        {
            1 => 1.8f,
            2 => 2.4f,
            3 => 3.2f,
            _ => 1.4f,
        };

        return exUnlocked ? heal + 1f : heal;
    }

    private static float GetCorpseBloomRadius(byte tier, bool exUnlocked)
    {
        var radius = tier switch
        {
            1 => 5.5f,
            2 => 6.5f,
            3 => 7.5f,
            _ => 5f,
        };

        return exUnlocked ? radius + 1f : radius;
    }

    private static float GetCorpseBloomDuration(byte tier, bool exUnlocked)
    {
        var seconds = tier switch
        {
            1 => 12f,
            2 => 15f,
            3 => 18f,
            _ => 10f,
        };

        return exUnlocked ? seconds + 4f : seconds;
    }

    private static int GetKillBonusPerKill(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 3,
            2 => 5,
            3 => 7,
            _ => 0,
        };

        return exUnlocked && amount > 0 ? amount + 2 : amount;
    }

    private static int GetKillStackCap(byte tier, bool exUnlocked)
    {
        var amount = tier switch
        {
            1 => 3,
            2 => 5,
            3 => 7,
            _ => 1,
        };

        return exUnlocked ? amount + 1 : amount;
    }

    private static float GetPassiveHealthMultiplier(byte tier, bool exUnlocked)
    {
        var multiplier = tier switch
        {
            1 => 1.05f,
            2 => 1.10f,
            3 => 1.16f,
            _ => 1f,
        };

        return exUnlocked ? multiplier + 0.05f : multiplier;
    }

    private static float GetPassiveRotRegen(byte tier, bool exUnlocked)
    {
        var regen = tier switch
        {
            1 => 0.9f,
            2 => 1.4f,
            3 => 1.9f,
            _ => 0f,
        };

        return exUnlocked ? regen + 0.6f : regen;
    }

    private static float GetCorpseRiseHeal(byte powerTier, byte utilityTier, bool exUnlocked)
    {
        var amount = powerTier switch
        {
            1 => 40f,
            2 => 60f,
            3 => 82f,
            _ => 30f,
        };

        amount += utilityTier switch
        {
            1 => 8f,
            2 => 14f,
            3 => 22f,
            _ => 0f,
        };

        return exUnlocked ? amount + 28f : amount;
    }
}

[RegisterComponent]
public sealed partial class WH40KChaosNurgleRuntimeComponent : Component
{
    public bool BaselineCaptured;
    public SortedDictionary<FixedPoint2, MobState> BaselineThresholds = new();
    public int KillStacks;
    public int AppliedKillStacks;
    public byte AppliedKillTier;
    public byte AppliedHealthTier;
    public TimeSpan NextPassiveRegenAt;
}

[RegisterComponent]
public sealed partial class WH40KChaosNurgleMiasmaBlessingComponent : Component
{
    public TimeSpan ExpiresAt;
    public TimeSpan NextTickAt;
    public float HealPerSecond;
}
