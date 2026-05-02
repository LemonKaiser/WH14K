using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Cinematic;

[Prototype("wh40kCinematic")]
public sealed partial class WH40KCinematicPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("allowRepeat")]
    public bool AllowRepeat;

    [DataField("queueMode")]
    public WH40KCinematicQueueMode QueueMode = WH40KCinematicQueueMode.Queue;

    [DataField("worldFreezeMode")]
    public WH40KCinematicWorldFreezeMode WorldFreezeMode = WH40KCinematicWorldFreezeMode.None;

    [DataField("lockAudienceOnStart")]
    public bool LockAudienceOnStart = true;

    [DataField("priority")]
    public int Priority;

    [DataField("ghostAudiencePolicy")]
    public WH40KCinematicGhostAudiencePolicy GhostAudiencePolicy = WH40KCinematicGhostAudiencePolicy.MirrorAudience;

    [DataField("globalLocalConflictPolicy")]
    public WH40KCinematicGlobalLocalConflictPolicy GlobalLocalConflictPolicy = WH40KCinematicGlobalLocalConflictPolicy.InterruptLocals;

    [DataField("godmodeWhileAudienceLocked")]
    public bool GodmodeWhileAudienceLocked;

    [DataField("defaultDrawFov")]
    public bool? DefaultDrawFov;

    [DataField("defaultDrawLight")]
    public bool? DefaultDrawLight;

    [DataField("debugLabel")]
    public string? DebugLabel;

    [DataField("restoreInputDelay")]
    public float RestoreInputDelaySeconds;

    [DataField("defaultWaitTimeout")]
    public float? DefaultWaitTimeoutSeconds;

    [DataField("steps", required: true)]
    public List<WH40KCinematicStepDefinition> Steps = new();
}

[DataDefinition]
public sealed partial class WH40KCinematicStepDefinition
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("type")]
    public WH40KCinematicStepType Type = WH40KCinematicStepType.Marker;

    [DataField("waitMode")]
    public WH40KCinematicWaitMode WaitMode = WH40KCinematicWaitMode.Instant;

    [DataField("duration")]
    public float DurationSeconds;

    [DataField("timeout")]
    public float TimeoutSeconds;

    [DataField("debugLabel")]
    public string? DebugLabel;

    [DataField("contextId")]
    public string? ContextId;

    [DataField("cameraPoint")]
    public string? CameraPointId;

    [DataField("cameraSource")]
    public WH40KCinematicCameraSource CameraSource = WH40KCinematicCameraSource.FixedPoint;

    [DataField("optionalCameraPoint")]
    public bool OptionalCameraPoint = true;

    [DataField("cameraTransition")]
    public WH40KCinematicCameraTransitionMode CameraTransition = WH40KCinematicCameraTransitionMode.Cut;

    [DataField("cameraEasing")]
    public WH40KCinematicCameraTransitionEasing CameraEasing = WH40KCinematicCameraTransitionEasing.Linear;

    [DataField("blendDuration")]
    public float BlendDurationSeconds;

    [DataField("cameraZoom")]
    public float? CameraZoom;

    [DataField("cameraRotation")]
    public float? CameraRotationDegrees;

    [DataField("drawFov")]
    public bool? DrawFov;

    [DataField("drawLight")]
    public bool? DrawLight;

    [DataField("shake")]
    public float ShakeIntensity;

    [DataField("audienceLock")]
    public WH40KCinematicAudienceLockDirective AudienceLock = WH40KCinematicAudienceLockDirective.Inherit;

    [DataField("waitSignals")]
    public List<string> WaitSignals = new();

    [DataField("waitEntitySets")]
    public List<string> WaitEntitySets = new();

    [DataField("waitConditionMode")]
    public WH40KCinematicWaitConditionAggregationMode WaitConditionMode = WH40KCinematicWaitConditionAggregationMode.All;

    [DataField("actions")]
    public List<WH40KCinematicActionDefinition> Actions = new();
}
