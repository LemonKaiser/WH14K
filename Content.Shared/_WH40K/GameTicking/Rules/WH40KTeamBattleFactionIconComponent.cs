using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.GameTicking.Rules;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KTeamBattleFactionIconComponent : Component
{
    [DataField("teamId"), AutoNetworkedField]
    public string TeamId = string.Empty;
}
