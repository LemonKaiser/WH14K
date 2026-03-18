using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Waypointer.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class InnateWaypointerComponent : Component
{
    [DataField(required: true)]
    public HashSet<ProtoId<WaypointerPrototype>> WaypointerProtoIds = default!;
}
