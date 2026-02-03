using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class WH40KTeamMemberComponent : Component
{
    [DataField("teamId")]
    public string TeamId = string.Empty;
}
