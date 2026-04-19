using System;
using System.Collections.Generic;
using Content.Shared._WH40K.Vehicle.Movement;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Vehicle.Combat;

[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KVehicleMountedGunComponent : Component
{
    [DataField]
    public bool RequiresRunningEngine = true;

    [DataField]
    public bool BlockWhenDisabled = true;

    [DataField]
    public bool ShowExamineText = true;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KVehicleRamComponent : Component
{
    /// <summary>
    /// Fallback speed for non-car vehicles. WH40K car movement vehicles use <see cref="MinimumImpactSpeedRatio"/>.
    /// </summary>
    [DataField]
    public float MinimumImpactSpeed = 2.75f;

    [DataField]
    public float MinimumImpactSpeedRatio = 0.25f;

    [DataField]
    public float MaxImpactScale = 2.2f;

    [DataField]
    public float SoftTargetPushImpulse = 4.75f;

    [DataField]
    public float SoftImpactVelocityDampen = 0.72f;

    [DataField]
    public float HardImpactVelocityDampen = 0.38f;

    [DataField]
    public float StaminaDamage = 55f;

    [DataField]
    public TimeSpan KnockdownTime = TimeSpan.FromSeconds(2.25);

    [DataField]
    public TimeSpan ImpactCooldown = TimeSpan.FromSeconds(0.75);

    [DataField]
    public SoundSpecifier? ImpactSound;

    [DataField]
    public DamageSpecifier SoftTargetDamage = new();

    [DataField]
    public DamageSpecifier HardTargetDamage = new();

    [DataField]
    public DamageSpecifier SelfSoftImpactDamage = new();

    [DataField]
    public DamageSpecifier SelfHardImpactDamage = new();

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> RecentImpacts = new();

    public float GetMinimumImpactSpeed(WH40KVehicleCarMovementComponent? movement)
    {
        if (movement != null && movement.MaxForwardSpeed > 0f)
            return movement.MaxForwardSpeed * Math.Clamp(MinimumImpactSpeedRatio, 0f, 1f);

        return MinimumImpactSpeed;
    }
}
