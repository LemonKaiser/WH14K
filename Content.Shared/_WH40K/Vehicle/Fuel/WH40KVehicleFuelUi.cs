using System;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Vehicle.Fuel;

[Serializable, NetSerializable]
public enum WH40KVehicleFuelTerminalUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class WH40KVehicleFuelTerminalBuiState : BoundUserInterfaceState
{
    public ProtoId<CargoAccountPrototype> Account { get; }
    public bool Powered { get; }
    public bool AutoIntakeEnabled { get; }
    public bool AutoRefuelEnabled { get; }
    public float BufferAmount { get; }
    public float BufferCapacity { get; }
    public string SourceName { get; }
    public float SourceAmount { get; }
    public float SourceCapacity { get; }
    public string VehicleName { get; }
    public float VehicleFuelAmount { get; }
    public float VehicleFuelCapacity { get; }
    public WH40KVehicleEngineState VehicleEngineState { get; }
    public WH40KVehicleServiceState VehicleServiceState { get; }
    public float VehicleServiceRatio { get; }

    public WH40KVehicleFuelTerminalBuiState(
        ProtoId<CargoAccountPrototype> account,
        bool powered,
        bool autoIntakeEnabled,
        bool autoRefuelEnabled,
        float bufferAmount,
        float bufferCapacity,
        string sourceName,
        float sourceAmount,
        float sourceCapacity,
        string vehicleName,
        float vehicleFuelAmount,
        float vehicleFuelCapacity,
        WH40KVehicleEngineState vehicleEngineState,
        WH40KVehicleServiceState vehicleServiceState,
        float vehicleServiceRatio)
    {
        Account = account;
        Powered = powered;
        AutoIntakeEnabled = autoIntakeEnabled;
        AutoRefuelEnabled = autoRefuelEnabled;
        BufferAmount = bufferAmount;
        BufferCapacity = bufferCapacity;
        SourceName = sourceName;
        SourceAmount = sourceAmount;
        SourceCapacity = sourceCapacity;
        VehicleName = vehicleName;
        VehicleFuelAmount = vehicleFuelAmount;
        VehicleFuelCapacity = vehicleFuelCapacity;
        VehicleEngineState = vehicleEngineState;
        VehicleServiceState = vehicleServiceState;
        VehicleServiceRatio = vehicleServiceRatio;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KVehicleFuelTerminalToggleAutoIntakeMessage : BoundUserInterfaceMessage
{
    public bool Enabled { get; }

    public WH40KVehicleFuelTerminalToggleAutoIntakeMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KVehicleFuelTerminalToggleAutoRefuelMessage : BoundUserInterfaceMessage
{
    public bool Enabled { get; }

    public WH40KVehicleFuelTerminalToggleAutoRefuelMessage(bool enabled)
    {
        Enabled = enabled;
    }
}
