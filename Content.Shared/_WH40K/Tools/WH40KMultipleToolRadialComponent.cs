using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Tools;

[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KMultipleToolRadialComponent : Component
{
}

[Serializable, NetSerializable]
public enum WH40KMultipleToolRadialUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class WH40KMultipleToolRadialSelectMessage(uint entry) : BoundUserInterfaceMessage
{
    public readonly uint Entry = entry;
}
