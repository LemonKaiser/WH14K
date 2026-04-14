using Content.Shared.FixedPoint;
using Content.Shared.Atmos;
using Robust.Shared.GameStates;

namespace Content.Shared.Mech.Module.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MechFanModuleComponent : Component
{
    /// <summary>
    /// Whether the fan is currently active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsActive;

    /// <summary>
    /// Current fan state see <see cref="MechFanState"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public MechFanState State = MechFanState.Off;

    /// <summary>
    /// How much energy the fan consumes per second when active.
    /// </summary>
    [DataField]
    public FixedPoint2 EnergyConsumption = 1.0f;

    /// <summary>
    /// Energy multiplier applied while the filter is enabled.
    /// </summary>
    [DataField]
    public float FilterEnergyMultiplier = 2f;

    /// <summary>
    /// Energy multiplier applied while the compressor is enabled in refill mode.
    /// </summary>
    [DataField]
    public float CompressorEnergyMultiplier = 4f;

    /// <summary>
    /// How much gas the fan can process per second when active.
    /// </summary>
    [DataField]
    public float GasProcessingRate = 1f;

    /// <summary>
    /// Whether the attached filter should be active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool FilterEnabled = true;

    /// <summary>
    /// Whether the fan compressor may refill a connected tank above ambient pressure.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CompressorEnabled;

    /// <summary>
    /// Pressure the compressor tries to refill connected air tanks to.
    /// </summary>
    [DataField]
    public float CompressorTargetPressure = 900f;

    /// <summary>
    /// Gases scrubbed from cabin air and filtered out of outside intake during fan operation.
    /// </summary>
    [DataField(required: true)]
    public HashSet<Gas> FilterGases = new();
}
