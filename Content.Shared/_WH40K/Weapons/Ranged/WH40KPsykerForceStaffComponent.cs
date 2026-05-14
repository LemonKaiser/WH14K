namespace Content.Shared._WH40K.Weapons.Ranged;

[RegisterComponent]
public sealed partial class WH40KPsykerForceStaffComponent : Component
{
    [DataField("shotInstability")]
    public float ShotInstability = 15f;

    [DataField("popup")]
    public LocId Popup = "wh40k-psyker-force-staff-user-required";
}
