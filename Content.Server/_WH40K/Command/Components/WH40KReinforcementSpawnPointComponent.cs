namespace Content.Server._WH40K.Command.Components;

[RegisterComponent]
public sealed partial class WH40KReinforcementSpawnPointComponent : Component
{
    [DataField(required: true)]
    public string TeamId = string.Empty;
}
