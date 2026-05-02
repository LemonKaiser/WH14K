using System;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Cinematic;

[Serializable, NetSerializable]
public enum WH40KCinematicWaitMode : byte
{
    Instant,
    Duration,
    AwaitCompletion,
    AwaitCompletionOrTimeout,
    AwaitSignal,
    AwaitSignalOrTimeout,
    AwaitEntitySetEmpty,
    Terminal
}

[Serializable, NetSerializable]
public enum WH40KCinematicStepType : byte
{
    Marker,
    Shot,
    EndCinematic
}

[Serializable, NetSerializable]
public enum WH40KCinematicQueueMode : byte
{
    Queue,
    IgnoreIfBusy
}

[Serializable, NetSerializable]
public enum WH40KCinematicWorldFreezeMode : byte
{
    None,
    PauseMap,
    LockPlayersOnly
}

[Serializable, NetSerializable]
public enum WH40KCinematicGhostAudiencePolicy : byte
{
    Never,
    MirrorAudience,
    OnlyGhosts,
    IncludeAllGhosts
}

[Serializable, NetSerializable]
public enum WH40KCinematicGlobalLocalConflictPolicy : byte
{
    InterruptLocals,
    SkipPlayersWithLocalRuns
}

[Serializable, NetSerializable]
public enum WH40KCinematicSoundDeliveryScope : byte
{
    Audience,
    Pvs,
    Radius,
    Map,
    Broadcast
}

[Serializable, NetSerializable]
public enum WH40KCinematicCameraTransitionMode : byte
{
    Cut,
    Blend
}

[Serializable, NetSerializable]
public enum WH40KCinematicCameraSource : byte
{
    FixedPoint,
    PlayerEntity,
    TriggerUserEntity,
    AttachedEntity
}

[Serializable, NetSerializable]
public enum WH40KCinematicCameraTransitionEasing : byte
{
    Linear,
    SineInOut,
    QuadInOut,
    CubicInOut,
    BackOut,
    BounceOut,
    ExpoInOut
}

[Serializable, NetSerializable]
public enum WH40KCinematicAudienceLockDirective : byte
{
    Inherit,
    Lock,
    Unlock
}

[Serializable, NetSerializable]
public enum WH40KCinematicWaitConditionAggregationMode : byte
{
    All,
    Any
}

[Serializable, NetSerializable]
public enum WH40KCinematicSceneTransferMode : byte
{
    CameraOnly,
    TeleportParticipants
}

[Serializable, NetSerializable]
public enum WH40KCinematicSceneCleanupPolicy : byte
{
    DestroyOnFinish,
    KeepAlive
}

[Serializable, NetSerializable]
public enum WH40KCinematicSceneReturnPolicy : byte
{
    OriginalPosition,
    ReturnAnchor,
    None
}

[Serializable, NetSerializable]
public sealed class WH40KCinematicShotNetState
{
    public string? CameraPointId { get; }
    public NetCoordinates Coordinates { get; }
    public float Zoom { get; }
    public float RotationDegrees { get; }
    public WH40KCinematicCameraTransitionMode TransitionMode { get; }
    public WH40KCinematicCameraTransitionEasing TransitionEasing { get; }
    public float BlendDurationSeconds { get; }
    public float ShakeIntensity { get; }
    public bool? DrawFovOverride { get; }
    public bool? DrawLightOverride { get; }

    public WH40KCinematicShotNetState(
        string? cameraPointId,
        NetCoordinates coordinates,
        float zoom,
        float rotationDegrees,
        WH40KCinematicCameraTransitionMode transitionMode,
        WH40KCinematicCameraTransitionEasing transitionEasing,
        float blendDurationSeconds,
        float shakeIntensity,
        bool? drawFovOverride,
        bool? drawLightOverride)
    {
        CameraPointId = cameraPointId;
        Coordinates = coordinates;
        Zoom = zoom;
        RotationDegrees = rotationDegrees;
        TransitionMode = transitionMode;
        TransitionEasing = transitionEasing;
        BlendDurationSeconds = blendDurationSeconds;
        ShakeIntensity = shakeIntensity;
        DrawFovOverride = drawFovOverride;
        DrawLightOverride = drawLightOverride;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCinematicNetState
{
    public int RunSerial { get; }
    public string CinematicId { get; }
    public int ActiveStepIndex { get; }
    public string ActiveStepId { get; }
    public WH40KCinematicStepType ActiveStepType { get; }
    public WH40KCinematicWaitMode ActiveWaitMode { get; }
    public float RemainingSeconds { get; }
    public int QueueLength { get; }
    public bool AudienceLocked { get; }
    public float AudienceShakeIntensity { get; }
    public WH40KCinematicShotNetState? ActiveShot { get; }

    public WH40KCinematicNetState(
        int runSerial,
        string cinematicId,
        int activeStepIndex,
        string activeStepId,
        WH40KCinematicStepType activeStepType,
        WH40KCinematicWaitMode activeWaitMode,
        float remainingSeconds,
        int queueLength,
        bool audienceLocked,
        float audienceShakeIntensity,
        WH40KCinematicShotNetState? activeShot)
    {
        RunSerial = runSerial;
        CinematicId = cinematicId;
        ActiveStepIndex = activeStepIndex;
        ActiveStepId = activeStepId;
        ActiveStepType = activeStepType;
        ActiveWaitMode = activeWaitMode;
        RemainingSeconds = remainingSeconds;
        QueueLength = queueLength;
        AudienceLocked = audienceLocked;
        AudienceShakeIntensity = audienceShakeIntensity;
        ActiveShot = activeShot;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCinematicStateEvent : EntityEventArgs
{
    public WH40KCinematicNetState State { get; }

    public WH40KCinematicStateEvent(WH40KCinematicNetState state)
    {
        State = state;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCinematicStoppedEvent : EntityEventArgs
{
    public int RunSerial { get; }
    public string CinematicId { get; }
    public bool Completed { get; }
    public string Reason { get; }
    public int RemainingQueueLength { get; }
    public float UnlockDelaySeconds { get; }

    public WH40KCinematicStoppedEvent(
        int runSerial,
        string cinematicId,
        bool completed,
        string reason,
        int remainingQueueLength,
        float unlockDelaySeconds)
    {
        RunSerial = runSerial;
        CinematicId = cinematicId;
        Completed = completed;
        Reason = reason;
        RemainingQueueLength = remainingQueueLength;
        UnlockDelaySeconds = unlockDelaySeconds;
    }
}
