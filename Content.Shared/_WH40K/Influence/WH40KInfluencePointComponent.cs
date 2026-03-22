using System;
using Content.Shared._WH40K.GameMode;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Influence;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KInfluencePointComponent : Component
{
    /// <summary>
    /// Capture radius around this point.
    /// </summary>
    [DataField("captureRadius"), AutoNetworkedField]
    public float CaptureRadius = 8f;

    /// <summary>
    /// Time with sufficient control advantage required to capture.
    /// </summary>
    [DataField("captureTimeSeconds"), AutoNetworkedField]
    public float CaptureTimeSeconds = 18f;

    /// <summary>
    /// Base capture speed in "capture seconds" per real second.
    /// Effective speed scales with local control advantage on point.
    /// </summary>
    [DataField("captureSpeedPerSecond")]
    public float CaptureSpeedPerSecond = 1f;

    /// <summary>
    /// Upper limit for capture speed scaling from control advantage.
    /// </summary>
    [DataField("maxCaptureSpeedMultiplier")]
    public float MaxCaptureSpeedMultiplier = 3f;

    /// <summary>
    /// Progress decay speed when no valid team is present on point.
    /// </summary>
    [DataField("captureDecayPerSecond")]
    public float CaptureDecayPerSecond = 0.5f;

    /// <summary>
    /// Interval for passive point gain while controlled.
    /// </summary>
    [DataField("rewardIntervalSeconds")]
    public float RewardIntervalSeconds = 15f;

    /// <summary>
    /// Base frontline points awarded per reward interval.
    /// Real value is multiplied by current phase economy multiplier.
    /// </summary>
    [DataField("frontPointsPerInterval")]
    public int FrontPointsPerInterval = 1;

    /// <summary>
    /// Optional starting owner set in prototype/map.
    /// </summary>
    [DataField("ownerTeamId"), ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public string? OwnerTeamId;

    /// <summary>
    /// Auto-assigned tactical callsign used by the tactical map and objective listings.
    /// </summary>
    [DataField("callsign"), ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public string? Callsign;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public string? CapturingTeamId;

    [ViewVariables, AutoNetworkedField]
    public float CaptureProgressSeconds;

    /// <summary>
    /// Server-only helper to throttle state replication while capture is in progress.
    /// </summary>
    [ViewVariables]
    public float LastSyncedCaptureProgressSeconds;

    /// <summary>
    /// Capture can only progress at this phase or later.
    /// </summary>
    [DataField("captureEnabledFromPhase")]
    public WH40KBattlePhase CaptureEnabledFromPhase = WH40KBattlePhase.Assault;

    /// <summary>
    /// Minimum change needed to replicate capture progress.
    /// Lower values make radial fill look smoother.
    /// </summary>
    [DataField("captureProgressSyncStep")]
    public float CaptureProgressSyncStep = 0.25f;

    [ViewVariables]
    public TimeSpan NextRewardTick;
}
