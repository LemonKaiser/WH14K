using Robust.Shared.Timing;

namespace Content.Server._WH40K.Weapons.ServoSkulls.Components;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WH40KServoSkullMobComponent : Component
{
    [DataField("followSpeed")]
    public float FollowSpeed = 4f;

    [DataField("chargeSpeed")]
    public float ChargeSpeed = 8f;

    [DataField("followStopRange")]
    public float FollowStopRange = 1.25f;

    [DataField("chargeStopRange")]
    public float ChargeStopRange = 0.25f;

    [DataField("hostileDetectionRange")]
    public float HostileDetectionRange = 6f;

    [DataField("scanInterval")]
    public float ScanInterval = 0.2f;

    [DataField("triggerOnHostile")]
    public bool TriggerOnHostile;

    [ViewVariables]
    public EntityUid? OwnerEntity;

    [ViewVariables]
    public EntityUid? FollowTarget;

    [ViewVariables]
    public EntityUid? HostileTarget;

    [ViewVariables]
    public EntityUid? CurrentMovementTarget;

    [ViewVariables]
    public string TeamId = string.Empty;

    [ViewVariables]
    public float AppliedBaseSpeed;

    [AutoPausedField]
    public TimeSpan NextScanTime;
}
