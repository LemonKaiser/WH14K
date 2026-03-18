namespace Content.Server._WH40K.Research.Components;

[RegisterComponent]
public sealed partial class WH40KResearchPointChipComponent : Component
{
    [DataField("pointsPerUnit")]
    public int PointsPerUnit = 1000;
}
