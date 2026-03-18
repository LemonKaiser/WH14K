using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._RMC14.Waypointer.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActiveWaypointerComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField]
    public EntProtoId ActionProtoId = "RMCActionManageWaypointers";

    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<WaypointerPrototype>, bool>? WaypointerProtoIds;

    [DataField, AutoNetworkedField]
    public bool Active = true;

    [DataField]
    public ResPath RadialMenuIconPath = new("_RMC14/Markers/Waypointers/waypointer_action.rsi");
}
