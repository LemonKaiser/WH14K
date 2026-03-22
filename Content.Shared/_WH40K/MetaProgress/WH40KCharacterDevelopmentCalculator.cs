using System.Collections.Generic;

namespace Content.Shared._WH40K.MetaProgress;

public static class WH40KCharacterDevelopmentCalculator
{
    public static WH40KCharacterDevelopmentModifierSet Calculate(IReadOnlyCollection<string> openedNodeIds)
    {
        var result = new WH40KCharacterDevelopmentModifierSet();
        var opened = new HashSet<string>(openedNodeIds, StringComparer.Ordinal);

        var hungerDecayReduction = 0f;
        var thirstDecayReduction = 0f;
        var hungerSatiationBonus = 0f;
        var thirstSatiationBonus = 0f;
        var eatSpeedBonus = 0f;
        var sprintDrainReduction = 0f;
        var walkRecoveryBonus = 0f;
        var staminaCooldownReduction = 0f;
        var maxSaturationBonus = 0f;
        var suffocationDamageReduction = 0f;
        var bloodRefreshBonus = 0f;
        var bleedReductionBonus = 0f;
        var bloodlossThresholdReduction = 0f;
        var maxBloodVolumeBonus = 0f;
        var toxinFilterBonus = 0f;
        var staminaIncomingDamageReduction = 0f;
        var staminaCritThresholdBonus = 0f;
        var forceStandReduction = 0f;
        var staminaAfterCritRecoveryBonus = 0f;
        var staminaCritStunTimeReduction = 0f;
        var knockdownStandUpTimeReduction = 0f;
        var selfHealPenaltyReduction = 0f;
        var selfMedicalDelayReduction = 0f;
        var selfHealingEffectBonus = 0f;
        var drunkDurationReduction = 0f;
        var jitterDurationReduction = 0f;
        var drowsinessDurationReduction = 0f;
        var vomitSlowdownDurationReduction = 0f;

        if (opened.Contains("brain-root"))
        {
            staminaCritThresholdBonus += 0.05f;
            forceStandReduction += 0.05f;
        }

        if (opened.Contains("brain-u1"))
            staminaAfterCritRecoveryBonus += 0.10f;

        if (opened.Contains("brain-u2"))
            knockdownStandUpTimeReduction += 0.10f;

        if (opened.Contains("brain-u3"))
        {
            forceStandReduction += 0.15f;
            staminaAfterCritRecoveryBonus += 0.15f;
        }

        if (opened.Contains("brain-d1"))
            staminaIncomingDamageReduction += 0.10f;

        if (opened.Contains("brain-d2"))
            staminaCritThresholdBonus += 0.10f;

        if (opened.Contains("brain-d3"))
        {
            staminaIncomingDamageReduction += 0.15f;
            staminaCritStunTimeReduction += 0.10f;
        }

        if (opened.Contains("stomach-root"))
        {
            hungerDecayReduction += 0.15f;
            thirstDecayReduction += 0.05f;
        }

        if (opened.Contains("stomach-u1"))
            hungerSatiationBonus += 0.10f;

        if (opened.Contains("stomach-u2"))
            hungerSatiationBonus += 0.15f;

        if (opened.Contains("stomach-u3"))
            result.StomachImpulseUnlocked = true;

        if (opened.Contains("stomach-d1"))
            eatSpeedBonus += 0.10f;

        if (opened.Contains("stomach-d2"))
            eatSpeedBonus += 0.15f;

        if (opened.Contains("stomach-d3"))
            result.WarFurnaceUnlocked = true;

        if (opened.Contains("lungs-root"))
        {
            maxSaturationBonus += 0.05f;
            sprintDrainReduction += 0.05f;
        }

        if (opened.Contains("lungs-u1"))
            maxSaturationBonus += 0.10f;

        if (opened.Contains("lungs-u2"))
            suffocationDamageReduction += 0.10f;

        if (opened.Contains("lungs-u3"))
        {
            suffocationDamageReduction += 0.15f;
            maxSaturationBonus += 0.10f;
        }

        if (opened.Contains("lungs-d1"))
            walkRecoveryBonus += 0.10f;

        if (opened.Contains("lungs-d2"))
            staminaCooldownReduction += 0.15f;

        if (opened.Contains("lungs-d3"))
        {
            sprintDrainReduction += 0.10f;
            staminaCooldownReduction += 0.10f;
        }

        if (opened.Contains("kidneys-root"))
            thirstDecayReduction += 0.10f;

        if (opened.Contains("kidneys-u1"))
            toxinFilterBonus += 0.10f;

        if (opened.Contains("kidneys-u2"))
            toxinFilterBonus += 0.15f;

        if (opened.Contains("kidneys-u3"))
            result.KidneyPurgeUnlocked = true;

        if (opened.Contains("kidneys-d1"))
            thirstSatiationBonus += 0.10f;

        if (opened.Contains("kidneys-d2"))
            thirstSatiationBonus += 0.15f;

        if (opened.Contains("kidneys-d3"))
            bloodlossThresholdReduction += 0.10f;

        if (opened.Contains("heart-root"))
        {
            bloodRefreshBonus += 0.05f;
            bleedReductionBonus += 0.05f;
        }

        if (opened.Contains("heart-u1"))
            bloodRefreshBonus += 0.10f;

        if (opened.Contains("heart-u2"))
            bloodlossThresholdReduction += 0.10f;

        if (opened.Contains("heart-u3"))
            bloodRefreshBonus += 0.15f;

        if (opened.Contains("heart-d1"))
            bleedReductionBonus += 0.10f;

        if (opened.Contains("heart-d2"))
            bleedReductionBonus += 0.15f;

        if (opened.Contains("heart-d3"))
        {
            bloodlossThresholdReduction += 0.15f;
            maxBloodVolumeBonus += 0.05f;
        }

        if (opened.Contains("liver-root"))
        {
            selfHealPenaltyReduction += 0.05f;
            selfMedicalDelayReduction += 0.05f;
        }

        if (opened.Contains("liver-u1"))
            selfHealPenaltyReduction += 0.10f;

        if (opened.Contains("liver-u2"))
            selfHealingEffectBonus += 0.10f;

        if (opened.Contains("liver-u3"))
        {
            selfHealingEffectBonus += 0.10f;
            selfHealPenaltyReduction += 0.15f;
        }

        if (opened.Contains("liver-d1"))
        {
            drunkDurationReduction += 0.10f;
            jitterDurationReduction += 0.10f;
        }

        if (opened.Contains("liver-d2"))
        {
            drowsinessDurationReduction += 0.10f;
            vomitSlowdownDurationReduction += 0.10f;
        }

        if (opened.Contains("liver-d3"))
        {
            drunkDurationReduction += 0.15f;
            jitterDurationReduction += 0.15f;
            drowsinessDurationReduction += 0.15f;
            vomitSlowdownDurationReduction += 0.15f;
        }

        result.HungerDecayMultiplier = ClampMultiplier(1f - hungerDecayReduction);
        result.ThirstDecayMultiplier = ClampMultiplier(1f - thirstDecayReduction);
        result.HungerSatiationMultiplier = ClampMultiplier(1f + hungerSatiationBonus);
        result.ThirstSatiationMultiplier = ClampMultiplier(1f + thirstSatiationBonus);
        result.EatDelayMultiplier = ClampMultiplier(1f - eatSpeedBonus);
        result.StaminaSprintDrainMultiplier = ClampMultiplier(1f - sprintDrainReduction);
        result.StaminaWalkRecoveryMultiplier = ClampMultiplier(1f + walkRecoveryBonus);
        result.StaminaCooldownMultiplier = ClampMultiplier(1f - staminaCooldownReduction);
        result.MaxSaturationMultiplier = ClampMultiplier(1f + maxSaturationBonus);
        result.SuffocationDamageMultiplier = ClampMultiplier(1f - suffocationDamageReduction);
        result.BloodRefreshMultiplier = ClampMultiplier(1f + bloodRefreshBonus);
        result.BleedReductionMultiplier = ClampMultiplier(1f + bleedReductionBonus);
        result.BloodlossThresholdMultiplier = ClampMultiplier(1f - bloodlossThresholdReduction);
        result.MaxBloodVolumeMultiplier = ClampMultiplier(1f + maxBloodVolumeBonus);
        result.ToxinFilterMultiplier = ClampMultiplier(1f + toxinFilterBonus);
        result.StaminaIncomingDamageMultiplier = ClampMultiplier(1f - staminaIncomingDamageReduction);
        result.StaminaCritThresholdMultiplier = ClampMultiplier(1f + staminaCritThresholdBonus);
        result.ForceStandStaminaMultiplier = ClampMultiplier(1f - forceStandReduction);
        result.StaminaAfterCritRecoveryMultiplier = ClampMultiplier(1f + staminaAfterCritRecoveryBonus);
        result.StaminaCritStunTimeMultiplier = ClampMultiplier(1f - staminaCritStunTimeReduction);
        result.KnockdownStandUpTimeMultiplier = ClampMultiplier(1f - knockdownStandUpTimeReduction);
        result.SelfHealPenaltyMultiplier = ClampMultiplier(1f - selfHealPenaltyReduction);
        result.SelfMedicalDelayMultiplier = ClampMultiplier(1f - selfMedicalDelayReduction);
        result.SelfHealingEffectMultiplier = ClampMultiplier(1f + selfHealingEffectBonus);
        result.DrunkDurationMultiplier = ClampMultiplier(1f - drunkDurationReduction);
        result.JitterDurationMultiplier = ClampMultiplier(1f - jitterDurationReduction);
        result.DrowsinessDurationMultiplier = ClampMultiplier(1f - drowsinessDurationReduction);
        result.VomitSlowdownDurationMultiplier = ClampMultiplier(1f - vomitSlowdownDurationReduction);

        return result;
    }

    private static float ClampMultiplier(float value)
    {
        return MathF.Max(0.01f, value);
    }
}
