using System.Numerics;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Cargo.Components;

[RegisterComponent]
public sealed partial class WH40KCargoBatchTrainVisualComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CargoAccountPrototype> Account = "Cargo";

    [DataField]
    public float OutboundDistance = 32f;

    [DataField]
    public float ReturnSpawnDistance = 32f;

    [DataField]
    public float ReturnSpawnDirectionX = -1f;

    [DataField]
    public float AnimationDurationSeconds = 10f;

    [DataField]
    public float ReturnLeadSeconds = 10f;

    [DataField]
    public SoundSpecifier DepartureSound = new SoundPathSpecifier("/Audio/Ambience/ambitrain3.ogg");

    [DataField]
    public SoundSpecifier ArrivalSound = new SoundPathSpecifier("/Audio/Ambience/ambitrain1.ogg");

    [DataField]
    public float DepartureSoundVolume = -5f;

    [DataField]
    public float ArrivalSoundVolume = -5f;

    [ViewVariables]
    public bool HomeInitialized;

    [ViewVariables]
    public EntityUid HomeParent;

    [ViewVariables]
    public Vector2 HomeLocalPosition;

    [ViewVariables]
    public WH40KCargoBatchTrainMotion Motion = WH40KCargoBatchTrainMotion.Idle;

    [ViewVariables]
    public TimeSpan MotionStartedAt;

    [ViewVariables]
    public Vector2 MotionStartPosition;

    [ViewVariables]
    public Vector2 MotionEndPosition;
}

public enum WH40KCargoBatchTrainMotion : byte
{
    Idle,
    Departing,
    Returning
}
