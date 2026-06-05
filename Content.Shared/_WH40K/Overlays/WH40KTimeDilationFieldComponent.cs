using Content.Shared._WH40K.Psyker;

namespace Content.Shared._WH40K.Overlays;

/// <summary>
/// Server-side gameplay rules for WH40K radial time-dilation fields.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KTimeDilationFieldComponent : Component
{
    [DataField]
    public EntityUid? Caster;

    [DataField]
    public bool IgnoreOwner = true;

    [DataField]
    public bool AffectGhosts;

    [DataField]
    public WH40KChaosPatron ImmunePatron = WH40KChaosPatron.None;

    [DataField]
    public float MovementSpeedMultiplier = 0.7f;

    [DataField]
    public float MeleeAttackRateMultiplier = 0.7f;

    [DataField]
    public float PhysicsVelocityMultiplier = 0.7f;

    [DataField]
    public float TimedDespawnMultiplier = 0.7f;

    [DataField]
    public float GrenadeFuseTimerMultiplier = 0.7f;
}
