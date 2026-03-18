namespace Content.Shared._WH40K.Combat;

[RegisterComponent]
public sealed partial class WH40KDeployableBarricadeComponent : Component
{
    [DataField("stackCost")]
    public int StackCost = 1;

    [DataField("deployTime")]
    public TimeSpan DeployTime = TimeSpan.FromSeconds(1.4);
}
