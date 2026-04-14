using Content.Shared.Atmos;
using Content.Shared.Mech;
using Robust.Shared.GameStates;

namespace Content.Shared.Mech.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MechCabinAirComponent : Component
{
    /// <summary>
    /// Target pressure for the mech cabin (kPa).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TargetPressure = Atmospherics.OneAtmosphere; // ~101.3 kPa

    /// <summary>
    /// Highest cabin target pressure the UI is allowed to request from the tank supply.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxTargetPressure = Atmospherics.OneAtmosphere;

    /// <summary>
    /// Maximum tank gas volume moved into the cabin per second while supply mode is enabled.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TankSupplyRate = 5f;

    /// <summary>
    /// Whether the installed air tank is connected to the cabin life-support loop.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool TankEnabled = true;

    /// <summary>
    /// Whether the tank is feeding the cabin or being refilled by the fan.
    /// </summary>
    [DataField, AutoNetworkedField]
    public MechTankMode TankMode = MechTankMode.Supply;

    /// <summary>
    /// Internal cabin air mixture separate from any attached gas cylinder.
    /// </summary>
    [DataField, AutoNetworkedField]
    public GasMixture Air { get; set; } = new(50f) { Temperature = Atmospherics.T20C };
}
