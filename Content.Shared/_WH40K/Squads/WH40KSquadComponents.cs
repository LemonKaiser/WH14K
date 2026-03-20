using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Squads;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KSquadLeaderComponent : Component
{
    [DataField]
    public EntProtoId ActionPrototype = "ActionWH40KOpenSquadConsole";

    [DataField]
    public int MaxMembers = 5;

    [DataField, AutoNetworkedField]
    public bool SquadActive;

    [DataField, AutoNetworkedField]
    public string TeamId = string.Empty;

    [ViewVariables]
    public EntityUid? ActionEntity;

    [ViewVariables]
    public EntityUid? ControllerEntity;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KSquadAssignableComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? AssignedLeader;

    [DataField, AutoNetworkedField]
    public byte AssignedSlot;

    [DataField, AutoNetworkedField]
    public string TeamId = string.Empty;
}

[RegisterComponent]
public sealed partial class WH40KSquadConsoleComponent : Component
{
    [ViewVariables]
    public EntityUid? Leader;
}
