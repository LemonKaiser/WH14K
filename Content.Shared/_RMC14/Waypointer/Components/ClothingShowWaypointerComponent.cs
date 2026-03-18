using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Waypointer.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ClothingShowWaypointerComponent : Component
{
    [DataField(required: true)]
    public HashSet<ProtoId<WaypointerPrototype>> WaypointerProtoIds = default!;
}
