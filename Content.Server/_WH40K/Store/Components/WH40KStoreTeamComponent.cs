namespace Content.Server._WH40K.Store.Components;

[RegisterComponent]
public sealed partial class WH40KStoreTeamComponent : Component
{
    [DataField(required: true)]
    public string TeamId = string.Empty;
}
