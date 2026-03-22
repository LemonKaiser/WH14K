using System;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._WH40K.MetaProgress;

[RegisterComponent]
public sealed partial class WH40KCharacterDevelopmentBaselineComponent : Component
{
    public bool HungerCaptured;
    public float HungerBaseDecayRate;

    public bool ThirstCaptured;
    public float ThirstBaseDecayRate;

    public bool StaminaCaptured;
    public float StaminaSprintDrain;
    public float StaminaWalkRecovery;
    public float StaminaCooldown;
    public float StaminaAfterCritDecayMultiplier;
    public float StaminaForceStandStamina;
    public TimeSpan StaminaStunTime;

    public bool RespiratorCaptured;
    public float RespiratorMaxSaturation;
    public DamageSpecifier RespiratorDamage = new();

    public bool BloodstreamCaptured;
    public FixedPoint2 BloodRefreshAmount;
    public float BleedReductionAmount;
    public float BloodlossThreshold;
    public float MaxBloodVolumeModifier;
}
