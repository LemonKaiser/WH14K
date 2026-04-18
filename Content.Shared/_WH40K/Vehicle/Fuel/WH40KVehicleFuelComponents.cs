using System;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Vehicle.Fuel;

[Serializable]
public enum WH40KVehicleEngineState : byte
{
    Off,
    Starting,
    Running,
    Stalled,
    Disabled,
}

[Serializable]
public enum WH40KVehicleServiceState : byte
{
    Nominal,
    Worn,
    Critical,
    Disabled,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KVehicleFuelComponent : Component
{
    [DataField]
    public string FuelSolution = "vehicleFuel";

    [DataField]
    public string FuelReagent = "WH40KPromethium";

    [DataField]
    public float FullTankRuntime = 900f;

    [DataField, AutoNetworkedField]
    public float FuelLevel;

    [DataField, AutoNetworkedField]
    public float FuelCapacity = 900f;

    [DataField]
    public float LowFuelThreshold = 0.35f;

    [DataField]
    public float CriticalFuelThreshold = 0.1f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KVehicleEngineComponent : Component
{
    [DataField]
    public string ToggleAction = "ActionWH40KToggleVehicleEngine";

    [DataField]
    public EntityUid? ToggleActionEntity;

    [DataField]
    public TimeSpan StartingDelay = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan FuelTickInterval = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public WH40KVehicleEngineState State = WH40KVehicleEngineState.Off;

    [ViewVariables]
    public TimeSpan StartingCompleteAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextFuelTickAt = TimeSpan.Zero;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KVehicleHandlingHealthComponent : Component
{
    [DataField]
    public float MaxDamage = 250f;

    [DataField]
    public float WornDamage = 80f;

    [DataField]
    public float CriticalDamage = 160f;

    [DataField]
    public float DisabledDamage = 235f;

    [DataField]
    public float WornSpeedModifier = 0.9f;

    [DataField]
    public float CriticalSpeedModifier = 0.72f;

    [DataField]
    public float WornAccelerationModifier = 0.86f;

    [DataField]
    public float CriticalAccelerationModifier = 0.62f;

    [DataField]
    public float LowFuelSpeedModifier = 0.94f;

    [DataField]
    public float CriticalFuelSpeedModifier = 0.82f;

    [DataField]
    public float LowFuelAccelerationModifier = 0.9f;

    [DataField]
    public float CriticalFuelAccelerationModifier = 0.74f;

    [DataField, AutoNetworkedField]
    public WH40KVehicleServiceState ServiceState = WH40KVehicleServiceState.Nominal;

    [DataField, AutoNetworkedField]
    public float ServiceRatio = 1f;
}

[RegisterComponent]
public sealed partial class WH40KVehicleFuelTerminalComponent : Component
{
    [DataField]
    public ProtoId<CargoAccountPrototype> Account = "WH40KImperium";

    [DataField]
    public string BufferSolution = "buffer";

    [DataField]
    public string FuelReagent = "WH40KPromethium";

    [DataField]
    public float ScanRange = 3f;

    [DataField]
    public float IntakeRatePerSecond = 90f;

    [DataField]
    public float RefuelRatePerSecond = 90f;

    [DataField]
    public bool AutoIntakeEnabled;

    [DataField]
    public bool AutoRefuelEnabled;

    [DataField]
    public TimeSpan TransferInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan UiRefreshInterval = TimeSpan.FromSeconds(1);

    [ViewVariables]
    public TimeSpan NextTransferAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextUiRefresh = TimeSpan.Zero;
}

public static class WH40KVehicleFuelLoc
{
    public static string GetEngineStateLocKey(WH40KVehicleEngineState state)
    {
        return state switch
        {
            WH40KVehicleEngineState.Off => "wh40k-vehicle-engine-state-off",
            WH40KVehicleEngineState.Starting => "wh40k-vehicle-engine-state-starting",
            WH40KVehicleEngineState.Running => "wh40k-vehicle-engine-state-running",
            WH40KVehicleEngineState.Stalled => "wh40k-vehicle-engine-state-stalled",
            WH40KVehicleEngineState.Disabled => "wh40k-vehicle-engine-state-disabled",
            _ => "wh40k-vehicle-engine-state-off",
        };
    }

    public static string GetServiceStateLocKey(WH40KVehicleServiceState state)
    {
        return state switch
        {
            WH40KVehicleServiceState.Nominal => "wh40k-vehicle-service-state-nominal",
            WH40KVehicleServiceState.Worn => "wh40k-vehicle-service-state-worn",
            WH40KVehicleServiceState.Critical => "wh40k-vehicle-service-state-critical",
            WH40KVehicleServiceState.Disabled => "wh40k-vehicle-service-state-disabled",
            _ => "wh40k-vehicle-service-state-nominal",
        };
    }
}
