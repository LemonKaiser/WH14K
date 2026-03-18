using System;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Manipulator;

[Serializable, NetSerializable]
public enum WH40KManipulatorMode : byte
{
    None = 0,
    SmartFeed = 1,
    PassThrough = 2,
}

[Serializable, NetSerializable]
public enum WH40KManipulatorStatus : byte
{
    Idle = 0,
    Busy = 1,
    WaitingForItem = 2,
    WaitingForCompatibleItem = 3,
    WaitingForReceiverCapacity = 4,
    NoPower = 5,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KConveyorManipulatorComponent : Component
{
    public const string TransferContainerId = "wh40k-manipulator-transfer";

    [DataField("transferCooldown"), AutoNetworkedField]
    public float TransferCooldown = 0.2f;

    [DataField("transferDuration"), AutoNetworkedField]
    public float TransferDuration = 0.45f;

    [DataField("arcHeight"), AutoNetworkedField]
    public float ArcHeight = 0.3f;

    [DataField("requirePowered"), AutoNetworkedField]
    public bool RequirePowered = true;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool Busy;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? ActiveItem;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public WH40KManipulatorStatus Status = WH40KManipulatorStatus.Idle;

    [ViewVariables]
    public TimeSpan NextTransferAt;

    [ViewVariables]
    public int SelectionCursor;
}
