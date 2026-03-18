using Content.Shared.Actions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Waypointer.Events;

[ByRefEvent]
public sealed partial class ActionManageWaypointersEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class WaypointersToggledMessage(bool isActive) : BoundUserInterfaceMessage
{
    public bool IsActive = isActive;
}

[Serializable, NetSerializable]
public sealed class WaypointerStatusChangedMessage(ProtoId<WaypointerPrototype> waypointer) : BoundUserInterfaceMessage
{
    public ProtoId<WaypointerPrototype> ToggledWaypointerProtoId = waypointer;
}
