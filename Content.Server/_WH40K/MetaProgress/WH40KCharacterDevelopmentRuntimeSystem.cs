using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Damage.Systems;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared._WH40K.MetaProgress;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.MetaProgress;

public sealed partial class WH40KCharacterDevelopmentRuntimeSystem : EntitySystem
{
    [Dependency] private  WH40KCharacterDevelopmentAbilitySystem _abilities = default!;
    [Dependency] private  HungerSystem _hunger = default!;
    [Dependency] private  IPlayerManager _players = default!;
    [Dependency] private  WH40KMetaProgressSystem _metaProgress = default!;
    [Dependency] private  BloodstreamSystem _bloodstream = default!;
    [Dependency] private  RespiratorSystem _respirator = default!;
    [Dependency] private  StaminaSystem _staminaSystem = default!;
    [Dependency] private  ThirstSystem _thirst = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(
            OnPlayerSpawnComplete,
            after: new[] { typeof(WH40KTeamBattleRuleSystem) });
        _metaProgress.SnapshotPushed += OnSnapshotPushed;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _metaProgress.SnapshotPushed -= OnSnapshotPushed;
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        ApplyFromUser(ev.Player.UserId, ev.Entity);
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        RestoreDefaults(ev.Entity);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        ApplyFromUser(ev.Player.UserId, ev.Mob);
    }

    private void OnSnapshotPushed(NetUserId userId, WH40KMetaProgressSnapshot snapshot)
    {
        if (!_players.TryGetSessionById(userId, out var session))
            return;

        if (session.AttachedEntity is not { Valid: true } attached)
            return;

        ApplySnapshot(attached, snapshot);
    }

    private void ApplyFromUser(NetUserId userId, EntityUid uid)
    {
        var snapshot = _metaProgress.GetSnapshot(userId);
        ApplySnapshot(uid, snapshot);
    }

    private void ApplySnapshot(EntityUid uid, WH40KMetaProgressSnapshot snapshot)
    {
        var modifiers = WH40KCharacterDevelopmentCalculator.Calculate(snapshot.Development.OpenedNodeIds);
        ApplyModifiers(uid, modifiers);
    }

    public void RefreshStaminaProfileModifiers(EntityUid uid)
    {
        if (!_players.TryGetSessionByEntity(uid, out var session))
            return;

        if (!TryComp(uid, out StaminaComponent? stamina))
            return;

        var modifiers = WH40KCharacterDevelopmentCalculator
            .Calculate(_metaProgress.GetSnapshot(session.UserId).Development.OpenedNodeIds);

        if (!HasDirectStaminaOverrides(modifiers))
            return;

        var baseline = EnsureComp<WH40KCharacterDevelopmentBaselineComponent>(uid);

        if (!baseline.StaminaCaptured)
        {
            baseline.StaminaCaptured = true;
            baseline.StaminaCooldown = stamina.Cooldown;
            baseline.StaminaAfterCritDecayMultiplier = stamina.AfterCritDecayMultiplier;
            baseline.StaminaForceStandStamina = stamina.ForceStandStamina;
            baseline.StaminaStunTime = stamina.StunTime;
        }

        baseline.StaminaSprintDrain = stamina.SprintDrain;
        baseline.StaminaWalkRecovery = stamina.WalkRecovery;
        ApplyStamina(uid, stamina, baseline, modifiers);
    }

    private void ApplyModifiers(EntityUid uid, WH40KCharacterDevelopmentModifierSet modifiers)
    {
        var baseline = EnsureComp<WH40KCharacterDevelopmentBaselineComponent>(uid);

        if (!modifiers.HasAnyEffect())
        {
            RestoreDefaults(uid, baseline);
            return;
        }

        var hadModifierComp = TryComp(uid, out WH40KCharacterDevelopmentModifiersComponent? modifierComp);
        modifierComp ??= EnsureComp<WH40KCharacterDevelopmentModifiersComponent>(uid);

        var hadStomachImpulse = modifierComp.StomachImpulseUnlocked;
        var hadWarFurnace = modifierComp.WarFurnaceUnlocked;
        var hadKidneyPurge = modifierComp.KidneyPurgeUnlocked;
        var modifierChanged = ApplyModifierComponentValues(modifierComp, modifiers);

        if (!hadModifierComp || modifierChanged)
            Dirty(uid, modifierComp);

        if ((!hadModifierComp && (modifierComp.StomachImpulseUnlocked || modifierComp.WarFurnaceUnlocked || modifierComp.KidneyPurgeUnlocked)) ||
            hadStomachImpulse != modifierComp.StomachImpulseUnlocked ||
            hadWarFurnace != modifierComp.WarFurnaceUnlocked ||
            hadKidneyPurge != modifierComp.KidneyPurgeUnlocked)
        {
            _abilities.SyncAbilities(uid, modifierComp);
        }

        if (TryComp(uid, out HungerComponent? hunger))
            ApplyHunger(uid, hunger, baseline, modifiers.HungerDecayMultiplier);

        if (TryComp(uid, out ThirstComponent? thirst))
            ApplyThirst(uid, thirst, baseline, modifiers.ThirstDecayMultiplier);

        if (TryComp(uid, out StaminaComponent? stamina))
            ApplyStamina(uid, stamina, baseline, modifiers);

        if (TryComp(uid, out RespiratorComponent? respirator))
            ApplyRespirator(uid, respirator, baseline, modifiers);

        if (TryComp(uid, out BloodstreamComponent? bloodstream))
            ApplyBloodstream(uid, bloodstream, baseline, modifiers);
    }

    private void RestoreDefaults(EntityUid uid, WH40KCharacterDevelopmentBaselineComponent? baseline = null)
    {
        _abilities.ClearAbilities(uid);

        if (!Resolve(uid, ref baseline, false))
        {
            RemComp<WH40KCharacterDevelopmentModifiersComponent>(uid);
            return;
        }

        if (TryComp(uid, out HungerComponent? hunger) && baseline.HungerCaptured)
        {
            _hunger.SetBaseDecayRate(uid, baseline.HungerBaseDecayRate, hunger);
        }

        if (TryComp(uid, out ThirstComponent? thirst) && baseline.ThirstCaptured)
        {
            _thirst.SetBaseDecayRate(uid, baseline.ThirstBaseDecayRate, thirst);
        }

        if (TryComp(uid, out StaminaComponent? stamina) && baseline.StaminaCaptured)
        {
            stamina.SprintDrain = baseline.StaminaSprintDrain;
            stamina.WalkRecovery = baseline.StaminaWalkRecovery;
            stamina.Cooldown = baseline.StaminaCooldown;
            stamina.AfterCritDecayMultiplier = baseline.StaminaAfterCritDecayMultiplier;
            stamina.ForceStandStamina = baseline.StaminaForceStandStamina;
            stamina.StunTime = baseline.StaminaStunTime;
            _staminaSystem.RefreshStaminaCritThreshold((uid, stamina));
            Dirty(uid, stamina);
        }

        if (TryComp(uid, out RespiratorComponent? respirator) && baseline.RespiratorCaptured)
        {
            _respirator.SetRespiratorProfile(uid, baseline.RespiratorMaxSaturation, new DamageSpecifier(baseline.RespiratorDamage), respirator: respirator);
        }

        if (TryComp(uid, out BloodstreamComponent? bloodstream) && baseline.BloodstreamCaptured)
        {
            _bloodstream.SetBloodstreamProfile(
                (uid, bloodstream),
                baseline.BloodRefreshAmount,
                baseline.BleedReductionAmount,
                baseline.BloodlossThreshold,
                baseline.MaxBloodVolumeModifier);
        }

        RemComp<WH40KCharacterDevelopmentModifiersComponent>(uid);
    }

    private void ApplyHunger(EntityUid uid, HungerComponent hunger, WH40KCharacterDevelopmentBaselineComponent baseline, float multiplier)
    {
        if (!baseline.HungerCaptured)
        {
            baseline.HungerCaptured = true;
            baseline.HungerBaseDecayRate = hunger.BaseDecayRate;
        }

        var targetBaseDecayRate = baseline.HungerBaseDecayRate * multiplier;
        if (CloseTo(hunger.BaseDecayRate, targetBaseDecayRate))
            return;

        _hunger.SetBaseDecayRate(uid, targetBaseDecayRate, hunger);
    }

    private void ApplyThirst(EntityUid uid, ThirstComponent thirst, WH40KCharacterDevelopmentBaselineComponent baseline, float multiplier)
    {
        if (!baseline.ThirstCaptured)
        {
            baseline.ThirstCaptured = true;
            baseline.ThirstBaseDecayRate = thirst.BaseDecayRate;
        }

        var targetBaseDecayRate = baseline.ThirstBaseDecayRate * multiplier;
        if (CloseTo(thirst.BaseDecayRate, targetBaseDecayRate))
            return;

        _thirst.SetBaseDecayRate(uid, targetBaseDecayRate, thirst);
    }

    private void ApplyStamina(
        EntityUid uid,
        StaminaComponent stamina,
        WH40KCharacterDevelopmentBaselineComponent baseline,
        WH40KCharacterDevelopmentModifierSet modifiers)
    {
        if (!baseline.StaminaCaptured)
        {
            baseline.StaminaCaptured = true;
            baseline.StaminaSprintDrain = stamina.SprintDrain;
            baseline.StaminaWalkRecovery = stamina.WalkRecovery;
            baseline.StaminaCooldown = stamina.Cooldown;
            baseline.StaminaAfterCritDecayMultiplier = stamina.AfterCritDecayMultiplier;
            baseline.StaminaForceStandStamina = stamina.ForceStandStamina;
            baseline.StaminaStunTime = stamina.StunTime;
        }

        var changed = false;
        changed |= SetIfDifferent(ref stamina.SprintDrain, baseline.StaminaSprintDrain * modifiers.StaminaSprintDrainMultiplier);
        changed |= SetIfDifferent(ref stamina.WalkRecovery, baseline.StaminaWalkRecovery * modifiers.StaminaWalkRecoveryMultiplier);
        changed |= SetIfDifferent(ref stamina.Cooldown, baseline.StaminaCooldown * modifiers.StaminaCooldownMultiplier);
        changed |= SetIfDifferent(ref stamina.AfterCritDecayMultiplier, baseline.StaminaAfterCritDecayMultiplier * modifiers.StaminaAfterCritRecoveryMultiplier);
        changed |= SetIfDifferent(ref stamina.ForceStandStamina, baseline.StaminaForceStandStamina * modifiers.ForceStandStaminaMultiplier);
        changed |= SetIfDifferent(ref stamina.StunTime, ScaleTimeSpan(baseline.StaminaStunTime, modifiers.StaminaCritStunTimeMultiplier));

        var previousCritThreshold = stamina.CritThreshold;
        _staminaSystem.RefreshStaminaCritThreshold((uid, stamina));
        changed |= !CloseTo(previousCritThreshold, stamina.CritThreshold);

        if (changed)
            Dirty(uid, stamina);
    }

    private void ApplyRespirator(
        EntityUid uid,
        RespiratorComponent respirator,
        WH40KCharacterDevelopmentBaselineComponent baseline,
        WH40KCharacterDevelopmentModifierSet modifiers)
    {
        if (!baseline.RespiratorCaptured)
        {
            baseline.RespiratorCaptured = true;
            baseline.RespiratorMaxSaturation = respirator.MaxSaturation;
            baseline.RespiratorDamage = new DamageSpecifier(respirator.Damage);
        }
        _respirator.SetRespiratorProfile(
            uid,
            baseline.RespiratorMaxSaturation * modifiers.MaxSaturationMultiplier,
            ScaleDamageSpecifier(baseline.RespiratorDamage, modifiers.SuffocationDamageMultiplier),
            refillIfPreviouslyFull: true,
            respirator);
    }

    private void ApplyBloodstream(
        EntityUid uid,
        BloodstreamComponent bloodstream,
        WH40KCharacterDevelopmentBaselineComponent baseline,
        WH40KCharacterDevelopmentModifierSet modifiers)
    {
        if (!baseline.BloodstreamCaptured)
        {
            baseline.BloodstreamCaptured = true;
            baseline.BloodRefreshAmount = bloodstream.BloodRefreshAmount;
            baseline.BleedReductionAmount = bloodstream.BleedReductionAmount;
            baseline.BloodlossThreshold = bloodstream.BloodlossThreshold;
            baseline.MaxBloodVolumeModifier = bloodstream.MaxVolumeModifier;
        }

        var targetRefreshAmount = FixedPoint2.New(baseline.BloodRefreshAmount.Float() * modifiers.BloodRefreshMultiplier);
        var targetBleedReductionAmount = baseline.BleedReductionAmount * modifiers.BleedReductionMultiplier;
        var targetBloodlossThreshold = baseline.BloodlossThreshold * modifiers.BloodlossThresholdMultiplier;
        var targetMaxVolumeModifier = baseline.MaxBloodVolumeModifier * modifiers.MaxBloodVolumeMultiplier;

        if (bloodstream.BloodRefreshAmount == targetRefreshAmount &&
            CloseTo(bloodstream.BleedReductionAmount, targetBleedReductionAmount) &&
            CloseTo(bloodstream.BloodlossThreshold, targetBloodlossThreshold) &&
            CloseTo(bloodstream.MaxVolumeModifier, targetMaxVolumeModifier))
        {
            return;
        }

        _bloodstream.SetBloodstreamProfile(
            (uid, bloodstream),
            targetRefreshAmount,
            targetBleedReductionAmount,
            targetBloodlossThreshold,
            targetMaxVolumeModifier);
    }

    private static bool ApplyModifierComponentValues(
        WH40KCharacterDevelopmentModifiersComponent modifierComp,
        WH40KCharacterDevelopmentModifierSet modifiers)
    {
        var changed = false;
        changed |= SetIfDifferent(ref modifierComp.HungerDecayMultiplier, modifiers.HungerDecayMultiplier);
        changed |= SetIfDifferent(ref modifierComp.ThirstDecayMultiplier, modifiers.ThirstDecayMultiplier);
        changed |= SetIfDifferent(ref modifierComp.HungerSatiationMultiplier, modifiers.HungerSatiationMultiplier);
        changed |= SetIfDifferent(ref modifierComp.ThirstSatiationMultiplier, modifiers.ThirstSatiationMultiplier);
        changed |= SetIfDifferent(ref modifierComp.EatDelayMultiplier, modifiers.EatDelayMultiplier);
        changed |= SetIfDifferent(ref modifierComp.StaminaSprintDrainMultiplier, modifiers.StaminaSprintDrainMultiplier);
        changed |= SetIfDifferent(ref modifierComp.StaminaWalkRecoveryMultiplier, modifiers.StaminaWalkRecoveryMultiplier);
        changed |= SetIfDifferent(ref modifierComp.StaminaCooldownMultiplier, modifiers.StaminaCooldownMultiplier);
        changed |= SetIfDifferent(ref modifierComp.MaxSaturationMultiplier, modifiers.MaxSaturationMultiplier);
        changed |= SetIfDifferent(ref modifierComp.SuffocationDamageMultiplier, modifiers.SuffocationDamageMultiplier);
        changed |= SetIfDifferent(ref modifierComp.BloodRefreshMultiplier, modifiers.BloodRefreshMultiplier);
        changed |= SetIfDifferent(ref modifierComp.BleedReductionMultiplier, modifiers.BleedReductionMultiplier);
        changed |= SetIfDifferent(ref modifierComp.BloodlossThresholdMultiplier, modifiers.BloodlossThresholdMultiplier);
        changed |= SetIfDifferent(ref modifierComp.MaxBloodVolumeMultiplier, modifiers.MaxBloodVolumeMultiplier);
        changed |= SetIfDifferent(ref modifierComp.ToxinFilterMultiplier, modifiers.ToxinFilterMultiplier);
        changed |= SetIfDifferent(ref modifierComp.StaminaIncomingDamageMultiplier, modifiers.StaminaIncomingDamageMultiplier);
        changed |= SetIfDifferent(ref modifierComp.StaminaCritThresholdMultiplier, modifiers.StaminaCritThresholdMultiplier);
        changed |= SetIfDifferent(ref modifierComp.ForceStandStaminaMultiplier, modifiers.ForceStandStaminaMultiplier);
        changed |= SetIfDifferent(ref modifierComp.StaminaAfterCritRecoveryMultiplier, modifiers.StaminaAfterCritRecoveryMultiplier);
        changed |= SetIfDifferent(ref modifierComp.StaminaCritStunTimeMultiplier, modifiers.StaminaCritStunTimeMultiplier);
        changed |= SetIfDifferent(ref modifierComp.KnockdownStandUpTimeMultiplier, modifiers.KnockdownStandUpTimeMultiplier);
        changed |= SetIfDifferent(ref modifierComp.SelfHealPenaltyMultiplier, modifiers.SelfHealPenaltyMultiplier);
        changed |= SetIfDifferent(ref modifierComp.SelfMedicalDelayMultiplier, modifiers.SelfMedicalDelayMultiplier);
        changed |= SetIfDifferent(ref modifierComp.SelfHealingEffectMultiplier, modifiers.SelfHealingEffectMultiplier);
        changed |= SetIfDifferent(ref modifierComp.DrunkDurationMultiplier, modifiers.DrunkDurationMultiplier);
        changed |= SetIfDifferent(ref modifierComp.JitterDurationMultiplier, modifiers.JitterDurationMultiplier);
        changed |= SetIfDifferent(ref modifierComp.DrowsinessDurationMultiplier, modifiers.DrowsinessDurationMultiplier);
        changed |= SetIfDifferent(ref modifierComp.VomitSlowdownDurationMultiplier, modifiers.VomitSlowdownDurationMultiplier);
        changed |= SetIfDifferent(ref modifierComp.StomachImpulseUnlocked, modifiers.StomachImpulseUnlocked);
        changed |= SetIfDifferent(ref modifierComp.WarFurnaceUnlocked, modifiers.WarFurnaceUnlocked);
        changed |= SetIfDifferent(ref modifierComp.KidneyPurgeUnlocked, modifiers.KidneyPurgeUnlocked);
        return changed;
    }

    private static bool HasDirectStaminaOverrides(WH40KCharacterDevelopmentModifierSet modifiers)
    {
        return !CloseTo(modifiers.StaminaSprintDrainMultiplier, 1f) ||
               !CloseTo(modifiers.StaminaWalkRecoveryMultiplier, 1f) ||
               !CloseTo(modifiers.StaminaCooldownMultiplier, 1f) ||
               !CloseTo(modifiers.StaminaCritThresholdMultiplier, 1f) ||
               !CloseTo(modifiers.ForceStandStaminaMultiplier, 1f) ||
               !CloseTo(modifiers.StaminaAfterCritRecoveryMultiplier, 1f) ||
               !CloseTo(modifiers.StaminaCritStunTimeMultiplier, 1f);
    }

    private static bool SetIfDifferent(ref float current, float next)
    {
        if (CloseTo(current, next))
            return false;

        current = next;
        return true;
    }

    private static bool SetIfDifferent(ref bool current, bool next)
    {
        if (current == next)
            return false;

        current = next;
        return true;
    }

    private static bool SetIfDifferent(ref TimeSpan current, TimeSpan next)
    {
        if (current == next)
            return false;

        current = next;
        return true;
    }

    private static bool CloseTo(float left, float right)
    {
        return MathF.Abs(left - right) <= 0.0001f;
    }

    private static DamageSpecifier ScaleDamageSpecifier(DamageSpecifier source, float multiplier)
    {
        var scaled = new DamageSpecifier();

        foreach (var (type, value) in source.DamageDict)
        {
            var result = value.Float() * multiplier;
            if (MathF.Abs(result) <= 0.0001f)
                continue;

            scaled.DamageDict[type] = FixedPoint2.New(result);
        }

        return scaled;
    }

    private static TimeSpan ScaleTimeSpan(TimeSpan time, float multiplier)
    {
        if (time <= TimeSpan.Zero)
            return time;

        var ticks = (long) MathF.Round(time.Ticks * multiplier);
        return TimeSpan.FromTicks(Math.Max(1L, ticks));
    }
}
