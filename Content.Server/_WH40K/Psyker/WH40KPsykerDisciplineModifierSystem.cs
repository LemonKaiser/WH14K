using System;
using System.Linq;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Magic.Events;
using Content.Shared._WH40K.Psyker;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Applies purchased astral nodes to the real psyker kit:
/// warp pool, action tuning and persistent passive bonuses.
/// </summary>
public sealed partial class WH40KPsykerDisciplineModifierSystem : EntitySystem
{
    public const string AstralAnchorNode = "PsykerAstralAnchor";
    public const string LitanyOfControlNode = "PsykerLitanyOfControl";
    public const string SoulTetherNode = "PsykerSoulTether";
    public const string KineticPalmNode = "PsykerKineticPalm";
    public const string KineticBastionNode = "PsykerKineticBastion";
    public const string AegisPatternNode = "PsykerAegisPattern";
    public const string IronHaloTraceNode = "PsykerIronHaloTrace";
    public const string WarpSenseNode = "PsykerWarpSense";
    public const string ThreadReadingNode = "PsykerThreadReading";
    public const string HemostasisNode = "PsykerHemostasis";
    public const string SecondPulseNode = "PsykerSecondPulse";

    private const string AstralProjectionAction = "ActionWH40KPsykerAstralProjection";
    private const string TelekineticRepulseAction = "ActionWH40KPsykerTelekineticRepulse";
    private const string WarpKnockAction = "ActionWH40KPsykerWarpKnock";
    private const string AegisWallAction = "ActionWH40KPsykerAegisWall";
    private const string WarpBlinkAction = "ActionWH40KPsykerWarpBlink";
    private const string VeilSmokeAction = "ActionWH40KPsykerVeilSmoke";
    private const string MindShuntAction = "ActionWH40KPsykerMindShunt";
    private const string BiomanticSurgeAction = "ActionWH40KPsykerBiomanticSurge";

    private const float BaseWarpMaxCharge = 100f;
    private const float BaseWarpRegen = 3f;
    private const float SoulTetherMaxChargeBonus = 10f;
    private const float SecondPulseMaxChargeBonus = 20f;
    private const float SoulTetherRegenBonus = 0.35f;
    private const float WarpSenseRegenBonus = 0.40f;
    private const float HemostasisRegenBonus = 0.70f;

    private const float AstralBaseWarpCost = 12f;
    private const float AstralLitanyWarpCost = 7f;
    private const float AstralBaseUseDelay = 8f;
    private const float AstralLitanyUseDelay = 6f;
    private const float AstralBaseEntryInstability = 2f;
    private const float AstralLitanyEntryInstability = 1f;
    private const float AstralBaseForcedWakeInstability = 4f;
    private const float AstralTetherForcedWakeInstability = 2f;

    private const float RepulseBaseUseDelay = 24f;
    private const float RepulsePalmUseDelay = 20f;
    private const float RepulseBastionUseDelay = 17f;
    private const float RepulseBaseWarpCost = 18f;
    private const float RepulsePalmWarpCost = 15f;
    private const float RepulseBastionWarpCost = 15f;
    private const float RepulseBaseInstability = 6f;
    private const float RepulsePalmInstability = 4f;
    private const float RepulseBastionInstability = 3f;

    private const float KnockBaseUseDelay = 12f;
    private const float KnockBastionUseDelay = 9f;
    private const float KnockBaseWarpCost = 10f;
    private const float KnockBastionWarpCost = 8f;
    private const float KnockBaseInstability = 4f;
    private const float KnockBastionInstability = 2f;

    private const float AegisBaseUseDelay = 16f;
    private const float AegisPatternUseDelay = 14f;
    private const float IronHaloUseDelay = 11f;
    private const float AegisBaseWarpCost = 16f;
    private const float AegisPatternWarpCost = 15f;
    private const float IronHaloWarpCost = 17f;
    private const float AegisBaseInstability = 6f;
    private const float AegisPatternInstability = 5f;
    private const float IronHaloInstability = 6f;

    private const float BlinkBaseUseDelay = 14f;
    private const float BlinkWarpSenseUseDelay = 11f;
    private const float BlinkBaseWarpCost = 20f;
    private const float BlinkWarpSenseWarpCost = 17f;
    private const float BlinkBaseInstability = 8f;
    private const float BlinkWarpSenseInstability = 5f;

    private const float VeilBaseUseDelay = 14f;
    private const float VeilLitanyUseDelay = 12f;
    private const float VeilBaseWarpCost = 12f;
    private const float VeilLitanyWarpCost = 10f;
    private const float VeilBaseInstability = 7f;
    private const float VeilLitanyInstability = 5f;

    private const float MindShuntBaseUseDelay = 150f;
    private const float MindShuntThreadUseDelay = 130f;
    private const float MindShuntBaseWarpCost = 30f;
    private const float MindShuntThreadWarpCost = 27f;
    private const float MindShuntBaseInstability = 16f;
    private const float MindShuntThreadInstability = 14f;

    private const float BiomanticSurgeBaseUseDelay = 26f;
    private const float BiomanticSurgeHemostasisUseDelay = 22f;
    private const float BiomanticSurgeSecondPulseUseDelay = 18f;
    private const float BiomanticSurgeBaseWarpCost = 16f;
    private const float BiomanticSurgeHemostasisWarpCost = 14f;
    private const float BiomanticSurgeSecondPulseWarpCost = 12f;
    private const float BiomanticSurgeBaseInstability = 6f;
    private const float BiomanticSurgeHemostasisInstability = 4f;
    private const float BiomanticSurgeSecondPulseInstability = 3f;
    private const float BiomanticSurgeBaseHeal = 26f;
    private const float BiomanticSurgeHemostasisHeal = 36f;
    private const float BiomanticSurgeSecondPulseHeal = 54f;

    private const float LitanyInstabilityMultiplier = 0.92f;
    private const float WarpSenseInstabilityMultiplier = 0.85f;
    private const float KineticPalmUseDelayMultiplier = 0.96f;
    private const float ThreadReadingUseDelayMultiplier = 0.90f;
    private const float ThreadReadingWarpCostMultiplier = 0.90f;

    private const float KineticPalmMovementMultiplier = 1.04f;
    private const float SecondPulseMovementMultiplier = 1.08f;
    private const float KineticBastionThresholdMultiplier = 1.05f;
    private const float AegisPatternThresholdMultiplier = 1.05f;
    private const float IronHaloThresholdMultiplier = 1.08f;
    private const float HemostasisThresholdMultiplier = 1.06f;
    private const float KineticBastionDamageTakenMultiplier = 0.97f;
    private const float AegisPatternDamageTakenMultiplier = 0.96f;
    private const float IronHaloDamageTakenMultiplier = 0.94f;

    private static readonly TimeSpan AstralBaseFatigueDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AstralTetherFatigueDuration = TimeSpan.FromSeconds(10);

    private const string AegisPatternWallPrototype = "WH40KWallForceImperialAegisTier1";
    private const string IronHaloWallPrototype = "WH40KWallForceImperialAegisTier2";
    private const string BaseAegisWallPrototype = "WH40KWallForceShieldImperialBase";

    [Dependency] private  SharedActionsSystem _actions = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  WH40KPsykerDisciplineRuntimeSystem _runtime = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerRoleShutdownEvent>(OnPsykerRoleShutdown);
    }

    public void RefreshDisciplineState(
        EntityUid uid,
        WH40KPsykerStarterActionLoadoutComponent loadout,
        WH40KPsykerAstralProgressionComponent progression)
    {
        ApplyWarpResourceModifiers(uid, progression);
        ApplyPassiveRuntimeModifiers(uid, progression);

        foreach (var actionUid in loadout.GrantedActions)
        {
            if (!actionUid.IsValid() || Deleted(actionUid))
                continue;

            RefreshActionModifier(uid, actionUid, progression);
        }
    }

    public void ResetDisciplineState(EntityUid uid)
    {
        if (TryComp<WH40KWarpResourceComponent>(uid, out var warp))
        {
            var changed = false;

            if (!MathHelper.CloseToPercent(warp.MaxCharge, BaseWarpMaxCharge))
            {
                warp.MaxCharge = BaseWarpMaxCharge;
                changed = true;
            }

            if (!MathHelper.CloseToPercent(warp.RegenPerSecond, BaseWarpRegen))
            {
                warp.RegenPerSecond = BaseWarpRegen;
                changed = true;
            }

            if (warp.CurrentCharge > warp.MaxCharge)
            {
                warp.CurrentCharge = warp.MaxCharge;
                changed = true;
            }

            if (changed)
                Dirty(uid, warp);
        }

        _runtime.ResetRuntimeState(uid);
    }

    public bool HasUnlockedNode(EntityUid uid, string nodeId)
    {
        return TryComp<WH40KPsykerAstralProgressionComponent>(uid, out var progression) &&
               HasUnlockedNode(progression, nodeId);
    }

    public TimeSpan GetAstralFadeDuration(EntityUid uid)
    {
        return HasUnlockedNode(uid, LitanyOfControlNode)
            ? TimeSpan.FromSeconds(3)
            : TimeSpan.FromSeconds(4);
    }

    public TimeSpan GetAstralMinimumDuration(EntityUid uid)
    {
        return HasUnlockedNode(uid, SoulTetherNode)
            ? TimeSpan.FromSeconds(0.35f)
            : TimeSpan.FromSeconds(1f);
    }

    public float GetAstralEntryInstabilityContribution(EntityUid uid)
    {
        return HasUnlockedNode(uid, LitanyOfControlNode)
            ? AstralLitanyEntryInstability
            : AstralBaseEntryInstability;
    }

    public float GetAstralForcedWakeInstabilityContribution(EntityUid uid)
    {
        return HasUnlockedNode(uid, SoulTetherNode)
            ? AstralTetherForcedWakeInstability
            : AstralBaseForcedWakeInstability;
    }

    public TimeSpan GetAstralFatigueDuration(EntityUid uid)
    {
        return HasUnlockedNode(uid, SoulTetherNode)
            ? AstralTetherFatigueDuration
            : AstralBaseFatigueDuration;
    }

    public float GetBiomanticSurgeHealAmount(EntityUid uid)
    {
        if (!TryComp<WH40KPsykerAstralProgressionComponent>(uid, out var progression))
            return BiomanticSurgeBaseHeal;

        return GetBiomanticSurgeHealAmount(progression);
    }

    private void OnPsykerRoleShutdown(WH40KPsykerRoleShutdownEvent args)
    {
        ResetDisciplineState(args.User);
    }

    private void ApplyWarpResourceModifiers(EntityUid uid, WH40KPsykerAstralProgressionComponent progression)
    {
        if (!TryComp<WH40KWarpResourceComponent>(uid, out var warp))
            return;

        var maxCharge = BaseWarpMaxCharge;
        var regen = BaseWarpRegen;

        if (HasUnlockedNode(progression, SoulTetherNode))
        {
            maxCharge += SoulTetherMaxChargeBonus;
            regen += SoulTetherRegenBonus;
        }

        if (HasUnlockedNode(progression, WarpSenseNode))
            regen += WarpSenseRegenBonus;

        if (HasUnlockedNode(progression, HemostasisNode))
            regen += HemostasisRegenBonus;

        if (HasUnlockedNode(progression, SecondPulseNode))
            maxCharge += SecondPulseMaxChargeBonus;

        var changed = false;

        if (!MathHelper.CloseToPercent(warp.MaxCharge, maxCharge))
        {
            warp.MaxCharge = maxCharge;
            changed = true;
        }

        if (!MathHelper.CloseToPercent(warp.RegenPerSecond, regen))
        {
            warp.RegenPerSecond = regen;
            changed = true;
        }

        if (warp.CurrentCharge > warp.MaxCharge)
        {
            warp.CurrentCharge = warp.MaxCharge;
            changed = true;
        }

        if (changed)
            Dirty(uid, warp);
    }

    private void ApplyPassiveRuntimeModifiers(EntityUid uid, WH40KPsykerAstralProgressionComponent progression)
    {
        var movement = 1f;
        var threshold = 1f;
        var damageTaken = 1f;

        if (HasUnlockedNode(progression, KineticPalmNode))
            movement *= KineticPalmMovementMultiplier;

        if (HasUnlockedNode(progression, SecondPulseNode))
            movement *= SecondPulseMovementMultiplier;

        if (HasUnlockedNode(progression, KineticBastionNode))
        {
            threshold *= KineticBastionThresholdMultiplier;
            damageTaken *= KineticBastionDamageTakenMultiplier;
        }

        if (HasUnlockedNode(progression, AegisPatternNode))
        {
            threshold *= AegisPatternThresholdMultiplier;
            damageTaken *= AegisPatternDamageTakenMultiplier;
        }

        if (HasUnlockedNode(progression, IronHaloTraceNode))
        {
            threshold *= IronHaloThresholdMultiplier;
            damageTaken *= IronHaloDamageTakenMultiplier;
        }

        if (HasUnlockedNode(progression, HemostasisNode))
            threshold *= HemostasisThresholdMultiplier;

        _runtime.ApplyRuntimeState(uid, movement, threshold, damageTaken);
    }

    private void RefreshActionModifier(EntityUid performer, EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var actionPrototype = MetaData(actionUid).EntityPrototype?.ID;
        if (string.IsNullOrWhiteSpace(actionPrototype))
            return;

        switch (actionPrototype)
        {
            case AstralProjectionAction:
                ConfigureAstralProjectionAction(actionUid, progression);
                break;
            case TelekineticRepulseAction:
                ConfigureRepulseAction(actionUid, progression);
                break;
            case WarpKnockAction:
                ConfigureKnockAction(actionUid, progression);
                break;
            case AegisWallAction:
                ConfigureAegisAction(actionUid, progression);
                break;
            case WarpBlinkAction:
                ConfigureBlinkAction(actionUid, progression);
                break;
            case VeilSmokeAction:
                ConfigureVeilAction(actionUid, progression);
                break;
            case MindShuntAction:
                ConfigureMindShuntAction(actionUid, progression);
                break;
            case BiomanticSurgeAction:
                ConfigureBiomanticSurgeAction(actionUid, progression);
                break;
        }
    }

    private void ConfigureAstralProjectionAction(EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var useDelay = HasUnlockedNode(progression, LitanyOfControlNode)
            ? AstralLitanyUseDelay
            : AstralBaseUseDelay;
        var warpCost = HasUnlockedNode(progression, LitanyOfControlNode)
            ? AstralLitanyWarpCost
            : AstralBaseWarpCost;

        SetActionUseDelay(actionUid, useDelay);
        SetWarpCost(actionUid, warpCost, instabilityGain: 0f);

        var fatigueRemaining = progression.AstralFatigueUntil - _timing.CurTime;
        if (fatigueRemaining > TimeSpan.Zero)
            _actions.SetIfBiggerCooldown(actionUid, fatigueRemaining);
    }

    private void ConfigureRepulseAction(EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var useDelay = RepulseBaseUseDelay;
        var warpCost = RepulseBaseWarpCost;
        var instability = RepulseBaseInstability;

        if (HasUnlockedNode(progression, KineticPalmNode))
        {
            useDelay = RepulsePalmUseDelay;
            warpCost = RepulsePalmWarpCost;
            instability = RepulsePalmInstability;
        }

        if (HasUnlockedNode(progression, KineticBastionNode))
        {
            useDelay = RepulseBastionUseDelay;
            warpCost = RepulseBastionWarpCost;
            instability = RepulseBastionInstability;
        }

        ConfigureActionWithGlobals(actionUid, progression, useDelay, warpCost, instability);
    }

    private void ConfigureKnockAction(EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var useDelay = HasUnlockedNode(progression, KineticBastionNode)
            ? KnockBastionUseDelay
            : KnockBaseUseDelay;
        var warpCost = HasUnlockedNode(progression, KineticBastionNode)
            ? KnockBastionWarpCost
            : KnockBaseWarpCost;
        var instability = HasUnlockedNode(progression, KineticBastionNode)
            ? KnockBastionInstability
            : KnockBaseInstability;

        ConfigureActionWithGlobals(actionUid, progression, useDelay, warpCost, instability);
    }

    private void ConfigureAegisAction(EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var useDelay = AegisBaseUseDelay;
        var warpCost = AegisBaseWarpCost;
        var instability = AegisBaseInstability;
        var wallPrototype = BaseAegisWallPrototype;

        if (HasUnlockedNode(progression, AegisPatternNode))
        {
            useDelay = AegisPatternUseDelay;
            warpCost = AegisPatternWarpCost;
            instability = AegisPatternInstability;
            wallPrototype = AegisPatternWallPrototype;
        }

        if (HasUnlockedNode(progression, IronHaloTraceNode))
        {
            useDelay = IronHaloUseDelay;
            warpCost = IronHaloWarpCost;
            instability = IronHaloInstability;
            wallPrototype = IronHaloWallPrototype;
        }

        ConfigureActionWithGlobals(actionUid, progression, useDelay, warpCost, instability);
        SetInstantSpawnPrototype(actionUid, wallPrototype);
    }

    private void ConfigureBlinkAction(EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var useDelay = HasUnlockedNode(progression, WarpSenseNode)
            ? BlinkWarpSenseUseDelay
            : BlinkBaseUseDelay;
        var warpCost = HasUnlockedNode(progression, WarpSenseNode)
            ? BlinkWarpSenseWarpCost
            : BlinkBaseWarpCost;
        var instability = HasUnlockedNode(progression, WarpSenseNode)
            ? BlinkWarpSenseInstability
            : BlinkBaseInstability;

        ConfigureActionWithGlobals(actionUid, progression, useDelay, warpCost, instability);
    }

    private void ConfigureVeilAction(EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var useDelay = HasUnlockedNode(progression, LitanyOfControlNode)
            ? VeilLitanyUseDelay
            : VeilBaseUseDelay;
        var warpCost = HasUnlockedNode(progression, LitanyOfControlNode)
            ? VeilLitanyWarpCost
            : VeilBaseWarpCost;
        var instability = HasUnlockedNode(progression, LitanyOfControlNode)
            ? VeilLitanyInstability
            : VeilBaseInstability;

        ConfigureActionWithGlobals(actionUid, progression, useDelay, warpCost, instability);
    }

    private void ConfigureMindShuntAction(EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var useDelay = HasUnlockedNode(progression, ThreadReadingNode)
            ? MindShuntThreadUseDelay
            : MindShuntBaseUseDelay;
        var warpCost = HasUnlockedNode(progression, ThreadReadingNode)
            ? MindShuntThreadWarpCost
            : MindShuntBaseWarpCost;
        var instability = HasUnlockedNode(progression, ThreadReadingNode)
            ? MindShuntThreadInstability
            : MindShuntBaseInstability;

        ConfigureActionWithGlobals(actionUid, progression, useDelay, warpCost, instability);
    }

    private void ConfigureBiomanticSurgeAction(EntityUid actionUid, WH40KPsykerAstralProgressionComponent progression)
    {
        var useDelay = BiomanticSurgeBaseUseDelay;
        var warpCost = BiomanticSurgeBaseWarpCost;
        var instability = BiomanticSurgeBaseInstability;

        if (HasUnlockedNode(progression, HemostasisNode))
        {
            useDelay = BiomanticSurgeHemostasisUseDelay;
            warpCost = BiomanticSurgeHemostasisWarpCost;
            instability = BiomanticSurgeHemostasisInstability;
        }

        if (HasUnlockedNode(progression, SecondPulseNode))
        {
            useDelay = BiomanticSurgeSecondPulseUseDelay;
            warpCost = BiomanticSurgeSecondPulseWarpCost;
            instability = BiomanticSurgeSecondPulseInstability;
        }

        ConfigureActionWithGlobals(actionUid, progression, useDelay, warpCost, instability);
    }

    private void ConfigureActionWithGlobals(
        EntityUid actionUid,
        WH40KPsykerAstralProgressionComponent progression,
        float baseUseDelay,
        float baseWarpCost,
        float baseInstability)
    {
        SetActionUseDelay(actionUid, ApplyGlobalUseDelay(progression, baseUseDelay));
        SetWarpCost(
            actionUid,
            ApplyGlobalWarpCost(progression, baseWarpCost),
            ApplyGlobalInstability(progression, baseInstability));
    }

    private void SetActionUseDelay(EntityUid actionUid, float seconds)
    {
        if (!TryComp<ActionComponent>(actionUid, out var action))
            return;

        _actions.SetUseDelay((actionUid, action), TimeSpan.FromSeconds(MathF.Max(0.1f, seconds)));
    }

    private void SetWarpCost(EntityUid actionUid, float warpCost, float instabilityGain)
    {
        if (!TryComp<WH40KWarpActionCostComponent>(actionUid, out var cost))
            return;

        var changed = false;

        warpCost = MathF.Max(0f, warpCost);
        instabilityGain = MathF.Max(0f, instabilityGain);

        if (!MathHelper.CloseToPercent(cost.WarpChargeCost, warpCost))
        {
            cost.WarpChargeCost = warpCost;
            changed = true;
        }

        if (!MathHelper.CloseToPercent(cost.InstabilityGain, instabilityGain))
        {
            cost.InstabilityGain = instabilityGain;
            changed = true;
        }

        if (changed)
            Dirty(actionUid, cost);
    }

    private void SetInstantSpawnPrototype(EntityUid actionUid, string prototype)
    {
        if (!TryComp<InstantActionComponent>(actionUid, out var instant) ||
            instant.Event is not InstantSpawnSpellEvent spawnEvent)
        {
            return;
        }

        spawnEvent.Prototype = prototype;
    }

    private static bool HasUnlockedNode(WH40KPsykerAstralProgressionComponent progression, string nodeId)
    {
        return progression.UnlockedNodes.Any(unlocked => string.Equals(unlocked, nodeId, StringComparison.Ordinal));
    }

    private static float GetBiomanticSurgeHealAmount(WH40KPsykerAstralProgressionComponent progression)
    {
        if (HasUnlockedNode(progression, SecondPulseNode))
            return BiomanticSurgeSecondPulseHeal;

        if (HasUnlockedNode(progression, HemostasisNode))
            return BiomanticSurgeHemostasisHeal;

        return BiomanticSurgeBaseHeal;
    }

    private static float ApplyGlobalUseDelay(WH40KPsykerAstralProgressionComponent progression, float value)
    {
        var multiplier = 1f;

        if (HasUnlockedNode(progression, KineticPalmNode))
            multiplier *= KineticPalmUseDelayMultiplier;

        if (HasUnlockedNode(progression, ThreadReadingNode))
            multiplier *= ThreadReadingUseDelayMultiplier;

        return MathF.Max(0.1f, value * multiplier);
    }

    private static float ApplyGlobalWarpCost(WH40KPsykerAstralProgressionComponent progression, float value)
    {
        var multiplier = 1f;

        if (HasUnlockedNode(progression, ThreadReadingNode))
            multiplier *= ThreadReadingWarpCostMultiplier;

        return MathF.Max(0f, value * multiplier);
    }

    private static float ApplyGlobalInstability(WH40KPsykerAstralProgressionComponent progression, float instability)
    {
        var multiplier = 1f;

        if (HasUnlockedNode(progression, LitanyOfControlNode))
            multiplier *= LitanyInstabilityMultiplier;

        if (HasUnlockedNode(progression, WarpSenseNode))
            multiplier *= WarpSenseInstabilityMultiplier;

        return MathF.Max(0f, instability * multiplier);
    }
}
