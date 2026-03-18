using System;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Fulton;

[Serializable, NetSerializable]
public enum WH40KFultonExtractionState : byte
{
    Pending = 0,
    Extracted = 1,
    Failed = 2,
}

[RegisterComponent]
public sealed partial class WH40KActiveFultonExtractionComponent : Component
{
    [ViewVariables]
    public EntityUid? User;

    [ViewVariables]
    public string TeamId = string.Empty;

    [ViewVariables]
    public int ExtractionId;

    [ViewVariables]
    public WH40KFultonExtractionState State = WH40KFultonExtractionState.Pending;

    [ViewVariables]
    public TimeSpan NextStateAt;

    [ViewVariables]
    public TimeSpan ExtractedCleanupDelay = TimeSpan.FromSeconds(1.5);

    [ViewVariables]
    public TimeSpan FailedCleanupDelay = TimeSpan.FromSeconds(6);

    [ViewVariables]
    public SoundSpecifier? ExtractedSound;

    [ViewVariables]
    public SoundSpecifier? FailedSound;

    [ViewVariables]
    public EntityCoordinates ReturnCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public int FrontReward;

    [ViewVariables]
    public int CommandReward;

    [ViewVariables]
    public bool CompleteMissionCargoOnExtract;

    [ViewVariables]
    public bool RemoveOnExtract = true;

    [ViewVariables]
    public string Label = string.Empty;

    [ViewVariables]
    public EntityUid Effect = EntityUid.Invalid;
}
