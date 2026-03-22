using System;

namespace Content.Shared._WH40K.MetaProgress;

public sealed class WH40KCharacterDevelopmentModifierSet
{
    public float HungerDecayMultiplier { get; set; } = 1f;
    public float ThirstDecayMultiplier { get; set; } = 1f;
    public float HungerSatiationMultiplier { get; set; } = 1f;
    public float ThirstSatiationMultiplier { get; set; } = 1f;
    public float EatDelayMultiplier { get; set; } = 1f;
    public float StaminaSprintDrainMultiplier { get; set; } = 1f;
    public float StaminaWalkRecoveryMultiplier { get; set; } = 1f;
    public float StaminaCooldownMultiplier { get; set; } = 1f;
    public float MaxSaturationMultiplier { get; set; } = 1f;
    public float SuffocationDamageMultiplier { get; set; } = 1f;
    public float BloodRefreshMultiplier { get; set; } = 1f;
    public float BleedReductionMultiplier { get; set; } = 1f;
    public float BloodlossThresholdMultiplier { get; set; } = 1f;
    public float MaxBloodVolumeMultiplier { get; set; } = 1f;
    public float ToxinFilterMultiplier { get; set; } = 1f;
    public float StaminaIncomingDamageMultiplier { get; set; } = 1f;
    public float StaminaCritThresholdMultiplier { get; set; } = 1f;
    public float ForceStandStaminaMultiplier { get; set; } = 1f;
    public float StaminaAfterCritRecoveryMultiplier { get; set; } = 1f;
    public float StaminaCritStunTimeMultiplier { get; set; } = 1f;
    public float KnockdownStandUpTimeMultiplier { get; set; } = 1f;
    public float SelfHealPenaltyMultiplier { get; set; } = 1f;
    public float SelfMedicalDelayMultiplier { get; set; } = 1f;
    public float SelfHealingEffectMultiplier { get; set; } = 1f;
    public float DrunkDurationMultiplier { get; set; } = 1f;
    public float JitterDurationMultiplier { get; set; } = 1f;
    public float DrowsinessDurationMultiplier { get; set; } = 1f;
    public float VomitSlowdownDurationMultiplier { get; set; } = 1f;
    public bool StomachImpulseUnlocked { get; set; }
    public bool WarFurnaceUnlocked { get; set; }
    public bool KidneyPurgeUnlocked { get; set; }

    public bool HasAnyEffect()
    {
        return !CloseTo(HungerDecayMultiplier, 1f) ||
               !CloseTo(ThirstDecayMultiplier, 1f) ||
               !CloseTo(HungerSatiationMultiplier, 1f) ||
               !CloseTo(ThirstSatiationMultiplier, 1f) ||
               !CloseTo(EatDelayMultiplier, 1f) ||
               !CloseTo(StaminaSprintDrainMultiplier, 1f) ||
               !CloseTo(StaminaWalkRecoveryMultiplier, 1f) ||
               !CloseTo(StaminaCooldownMultiplier, 1f) ||
               !CloseTo(MaxSaturationMultiplier, 1f) ||
               !CloseTo(SuffocationDamageMultiplier, 1f) ||
               !CloseTo(BloodRefreshMultiplier, 1f) ||
               !CloseTo(BleedReductionMultiplier, 1f) ||
               !CloseTo(BloodlossThresholdMultiplier, 1f) ||
               !CloseTo(MaxBloodVolumeMultiplier, 1f) ||
               !CloseTo(ToxinFilterMultiplier, 1f) ||
               !CloseTo(StaminaIncomingDamageMultiplier, 1f) ||
               !CloseTo(StaminaCritThresholdMultiplier, 1f) ||
               !CloseTo(ForceStandStaminaMultiplier, 1f) ||
               !CloseTo(StaminaAfterCritRecoveryMultiplier, 1f) ||
               !CloseTo(StaminaCritStunTimeMultiplier, 1f) ||
               !CloseTo(KnockdownStandUpTimeMultiplier, 1f) ||
               !CloseTo(SelfHealPenaltyMultiplier, 1f) ||
               !CloseTo(SelfMedicalDelayMultiplier, 1f) ||
               !CloseTo(SelfHealingEffectMultiplier, 1f) ||
               !CloseTo(DrunkDurationMultiplier, 1f) ||
               !CloseTo(JitterDurationMultiplier, 1f) ||
               !CloseTo(DrowsinessDurationMultiplier, 1f) ||
               !CloseTo(VomitSlowdownDurationMultiplier, 1f) ||
               StomachImpulseUnlocked ||
               WarFurnaceUnlocked ||
               KidneyPurgeUnlocked;
    }

    private static bool CloseTo(float left, float right)
    {
        return MathF.Abs(left - right) <= 0.0001f;
    }
}
