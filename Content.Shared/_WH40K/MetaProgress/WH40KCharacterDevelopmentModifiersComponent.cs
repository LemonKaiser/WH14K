using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.MetaProgress;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WH40KCharacterDevelopmentModifiersComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField]
    public float HungerDecayMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float ThirstDecayMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float HungerSatiationMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float ThirstSatiationMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float EatDelayMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float StaminaSprintDrainMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float StaminaWalkRecoveryMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float StaminaCooldownMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float MaxSaturationMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float SuffocationDamageMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float BloodRefreshMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float BleedReductionMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float BloodlossThresholdMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float MaxBloodVolumeMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float ToxinFilterMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float StaminaIncomingDamageMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float StaminaCritThresholdMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float ForceStandStaminaMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float StaminaAfterCritRecoveryMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float StaminaCritStunTimeMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float KnockdownStandUpTimeMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float SelfHealPenaltyMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float SelfMedicalDelayMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float SelfHealingEffectMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float DrunkDurationMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float JitterDurationMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float DrowsinessDurationMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float VomitSlowdownDurationMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public bool StomachImpulseUnlocked;

    [DataField, AutoNetworkedField]
    public bool WarFurnaceUnlocked;

    [DataField, AutoNetworkedField]
    public bool KidneyPurgeUnlocked;
}
