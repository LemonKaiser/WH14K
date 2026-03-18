using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.MetaProgress;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KGhostDecorationVisualComponent : Component
{
    [DataField("ghostRsiPath"), AutoNetworkedField]
    public string GhostRsiPath = string.Empty;

    [DataField("ghostState"), AutoNetworkedField]
    public string GhostState = string.Empty;
}
