namespace Content.Server._WH40K.Tau.Drones.Components;

[RegisterComponent]
public sealed partial class WH40KTauGunDroneComponent : Component
{
    [DataField("fireEnabled")]
    public bool FireEnabled = true;

    [DataField("fireRange")]
    public float FireRange = 7f;

    [DataField("acquisitionRange")]
    public float AcquisitionRange = 9f;

    [DataField("ownerLeashRange")]
    public float OwnerLeashRange = 10f;

    [DataField("followFormationRadius")]
    public float FollowFormationRadius = 1.1f;

    [DataField("combatFormationRadius")]
    public float CombatFormationRadius = 4.25f;

    [DataField("minimumCombatRange")]
    public float MinimumCombatRange = 2.75f;

    [DataField("preferredCombatRange")]
    public float PreferredCombatRange = 4.25f;

    [DataField("scanInterval")]
    public float ScanInterval = 0.25f;

    [DataField("nextScanTime")]
    public TimeSpan NextScanTime = TimeSpan.Zero;

    [ViewVariables]
    public EntityUid? CurrentTarget;
}
