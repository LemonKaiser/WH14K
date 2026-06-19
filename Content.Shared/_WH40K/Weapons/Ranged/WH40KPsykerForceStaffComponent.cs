using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Weapons.Ranged;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KPsykerForceStaffComponent : Component
{
    [DataField("shotInstability")]
    [AutoNetworkedField]
    public float ShotInstability = 15f;

    [DataField("popup")]
    public LocId Popup = "wh40k-psyker-force-staff-user-required";
}
