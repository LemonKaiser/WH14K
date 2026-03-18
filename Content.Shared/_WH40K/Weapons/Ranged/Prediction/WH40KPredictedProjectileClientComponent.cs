namespace Content.Shared._WH40K.Weapons.Ranged.Prediction;

[RegisterComponent]
public sealed partial class WH40KPredictedProjectileClientComponent : Component
{
    [DataField]
    public bool HitReported;
}
