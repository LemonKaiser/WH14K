using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Damage.Systems;
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

public sealed class WH40KCharacterDevelopmentRuntimeSystem : EntitySystem
{
    [Dependency] private readonly WH40KCharacterDevelopmentAbilitySystem _abilities = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly WH40KMetaProgressSystem _metaProgress = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly RespiratorSystem _respirator = default!;
    [Dependency] private readonly StaminaSystem _staminaSystem = default!;
    [Dependency] private readonly ThirstSystem _thirst = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
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

    private void ApplyModifiers(EntityUid uid, WH40KCharacterDevelopmentModifierSet modifiers)
    {
        var baseline = EnsureComp<WH40KCharacterDevelopmentBaselineComponent>(uid);

        if (!modifiers.HasAnyEffect())
        {
            RestoreDefaults(uid, baseline);
            return;
        }

        var modifierComp = EnsureComp<WH40KCharacterDevelopmentModifiersComponent>(uid);
        modifierComp.HungerDecayMultiplier = modifiers.HungerDecayMultiplier;
        modifierComp.ThirstDecayMultiplier = modifiers.ThirstDecayMultiplier;
        modifierComp.HungerSatiationMultiplier = modifiers.HungerSatiationMultiplier;
        modifierComp.ThirstSatiationMultiplier = modifiers.ThirstSatiationMultiplier;
        modifierComp.EatDelayMultiplier = modifiers.EatDelayMultiplier;
        modifierComp.StaminaSprintDrainMultiplier = modifiers.StaminaSprintDrainMultiplier;
        modifierComp.StaminaWalkRecoveryMultiplier = modifiers.StaminaWalkRecoveryMultiplier;
        modifierComp.StaminaCooldownMultiplier = modifiers.StaminaCooldownMultiplier;
        modifierComp.MaxSaturationMultiplier = modifiers.MaxSaturationMultiplier;
        modifierComp.SuffocationDamageMultiplier = modifiers.SuffocationDamageMultiplier;
        modifierComp.BloodRefreshMultiplier = modifiers.BloodRefreshMultiplier;
        modifierComp.BleedReductionMultiplier = modifiers.BleedReductionMultiplier;
        modifierComp.BloodlossThresholdMultiplier = modifiers.BloodlossThresholdMultiplier;
        modifierComp.MaxBloodVolumeMultiplier = modifiers.MaxBloodVolumeMultiplier;
        modifierComp.ToxinFilterMultiplier = modifiers.ToxinFilterMultiplier;
        modifierComp.StaminaIncomingDamageMultiplier = modifiers.StaminaIncomingDamageMultiplier;
        modifierComp.StaminaCritThresholdMultiplier = modifiers.StaminaCritThresholdMultiplier;
        modifierComp.ForceStandStaminaMultiplier = modifiers.ForceStandStaminaMultiplier;
        modifierComp.StaminaAfterCritRecoveryMultiplier = modifiers.StaminaAfterCritRecoveryMultiplier;
        modifierComp.StaminaCritStunTimeMultiplier = modifiers.StaminaCritStunTimeMultiplier;
        modifierComp.KnockdownStandUpTimeMultiplier = modifiers.KnockdownStandUpTimeMultiplier;
        modifierComp.SelfHealPenaltyMultiplier = modifiers.SelfHealPenaltyMultiplier;
        modifierComp.SelfMedicalDelayMultiplier = modifiers.SelfMedicalDelayMultiplier;
        modifierComp.SelfHealingEffectMultiplier = modifiers.SelfHealingEffectMultiplier;
        modifierComp.DrunkDurationMultiplier = modifiers.DrunkDurationMultiplier;
        modifierComp.JitterDurationMultiplier = modifiers.JitterDurationMultiplier;
        modifierComp.DrowsinessDurationMultiplier = modifiers.DrowsinessDurationMultiplier;
        modifierComp.VomitSlowdownDurationMultiplier = modifiers.VomitSlowdownDurationMultiplier;
        modifierComp.StomachImpulseUnlocked = modifiers.StomachImpulseUnlocked;
        modifierComp.WarFurnaceUnlocked = modifiers.WarFurnaceUnlocked;
        modifierComp.KidneyPurgeUnlocked = modifiers.KidneyPurgeUnlocked;
        Dirty(uid, modifierComp);

        _abilities.SyncAbilities(uid, modifierComp);

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

        _hunger.SetBaseDecayRate(uid, baseline.HungerBaseDecayRate * multiplier, hunger);
    }

    private void ApplyThirst(EntityUid uid, ThirstComponent thirst, WH40KCharacterDevelopmentBaselineComponent baseline, float multiplier)
    {
        if (!baseline.ThirstCaptured)
        {
            baseline.ThirstCaptured = true;
            baseline.ThirstBaseDecayRate = thirst.BaseDecayRate;
        }

        _thirst.SetBaseDecayRate(uid, baseline.ThirstBaseDecayRate * multiplier, thirst);
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

        stamina.SprintDrain = baseline.StaminaSprintDrain * modifiers.StaminaSprintDrainMultiplier;
        stamina.WalkRecovery = baseline.StaminaWalkRecovery * modifiers.StaminaWalkRecoveryMultiplier;
        stamina.Cooldown = baseline.StaminaCooldown * modifiers.StaminaCooldownMultiplier;
        stamina.AfterCritDecayMultiplier = baseline.StaminaAfterCritDecayMultiplier * modifiers.StaminaAfterCritRecoveryMultiplier;
        stamina.ForceStandStamina = baseline.StaminaForceStandStamina * modifiers.ForceStandStaminaMultiplier;
        stamina.StunTime = ScaleTimeSpan(baseline.StaminaStunTime, modifiers.StaminaCritStunTimeMultiplier);
        _staminaSystem.RefreshStaminaCritThreshold((uid, stamina));
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

        _bloodstream.SetBloodstreamProfile(
            (uid, bloodstream),
            FixedPoint2.New(baseline.BloodRefreshAmount.Float() * modifiers.BloodRefreshMultiplier),
            baseline.BleedReductionAmount * modifiers.BleedReductionMultiplier,
            baseline.BloodlossThreshold * modifiers.BloodlossThresholdMultiplier,
            baseline.MaxBloodVolumeModifier * modifiers.MaxBloodVolumeMultiplier);
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
