using Robust.Shared.GameStates;

namespace Content.Shared.Mech.Equipment.Components;

/// <summary>
/// Allows a mech-mounted battery weapon to recharge from the mech power cell.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MechEquipmentAutoRechargeComponent : Component
{
    /// <summary>
    /// Whether the equipment should pull charge from the mech while installed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    /// How much mech energy is spent for one unit of equipment battery charge.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float EnergyMultiplier = 3f;

    /// <summary>
    /// How long it takes to restore one weapon charge unit, usually one shot.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SecondsPerCharge = 30f;
}
