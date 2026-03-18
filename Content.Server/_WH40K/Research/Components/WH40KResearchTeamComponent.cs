namespace Content.Server._WH40K.Research.Components;

[RegisterComponent]
public sealed partial class WH40KResearchTeamComponent : Component
{
    [DataField(required: true)]
    public string TeamId = string.Empty;
}
