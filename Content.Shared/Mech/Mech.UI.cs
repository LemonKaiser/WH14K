using Robust.Shared.Serialization;

using Content.Shared.Mech.Module.Components;

namespace Content.Shared.Mech;

[Serializable, NetSerializable]
public enum MechUiKey : byte
{
    Key,
    Equipment
}

/// <summary>
/// Fan states for the mech air system
/// </summary>
[Serializable, NetSerializable]
public enum MechFanState : byte
{
    Off,
    On,
    Idle,
    Na
}

/// <summary>
/// How the mech air tank is connected to the cabin life-support loop.
/// </summary>
[Serializable, NetSerializable]
public enum MechTankMode : byte
{
    Supply,
    Refill
}

/// <summary>
/// Event raised to collect BUI states for each of the mech's equipment items
/// </summary>
public sealed class MechEquipmentUiStateReadyEvent : EntityEventArgs
{
    public Dictionary<NetEntity, BoundUserInterfaceState> States = new();
}

/// <summary>
/// Event raised to relay an equipment ui message
/// </summary>
public sealed class MechEquipmentUiMessageRelayEvent(MechEquipmentUiMessage message) : EntityEventArgs
{
    public MechEquipmentUiMessage Message = message;
}

/// <summary>
/// UI event raised to remove a piece of equipment from a mech
/// </summary>
[Serializable, NetSerializable]
public sealed class MechEquipmentRemoveMessage(NetEntity equipment) : BoundUserInterfaceMessage
{
    public NetEntity Equipment = equipment;
}

/// <summary>
/// UI event raised to remove a passive module from a mech
/// </summary>
[Serializable, NetSerializable]
public sealed class MechModuleRemoveMessage(NetEntity module) : BoundUserInterfaceMessage
{
    public NetEntity Module = module;
}

/// <summary>
/// base for all mech ui messages
/// </summary>
[Serializable, NetSerializable]
public abstract class MechEquipmentUiMessage : BoundUserInterfaceMessage
{
    public NetEntity Equipment;
}

/// <summary>
/// event raised for the grabber equipment to eject an item from it's storage
/// </summary>
[Serializable, NetSerializable]
public sealed class MechGrabberEjectMessage : MechEquipmentUiMessage
{
    public NetEntity Item;

    public MechGrabberEjectMessage(NetEntity equipment, NetEntity uid)
    {
        Equipment = equipment;
        Item = uid;
    }
}

/// <summary>
/// Event raised for the soundboard equipment to play a sound from its component
/// </summary>
[Serializable, NetSerializable]
public sealed class MechSoundboardPlayMessage : MechEquipmentUiMessage
{
    public int Sound;

    public MechSoundboardPlayMessage(NetEntity equipment, int sound)
    {
        Equipment = equipment;
        Sound = sound;
    }
}

/// <summary>
/// Event raised to toggle the mech air tank connection.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechTankToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

/// <summary>
/// Event raised to swap the air tank between feeding the cabin and being refilled by the fan.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechTankModeMessage(MechTankMode mode) : BoundUserInterfaceMessage
{
    public MechTankMode Mode = mode;
}

/// <summary>
/// Event raised to set the pressure the tank supply tries to maintain in the cabin.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechTankPressureMessage(float pressure) : BoundUserInterfaceMessage
{
    public float Pressure = pressure;
}

/// <summary>
/// Event raised to toggle the fan state of a mech
/// </summary>
[Serializable, NetSerializable]
public sealed class MechFanToggleMessage(bool isActive) : BoundUserInterfaceMessage
{
    public bool IsActive = isActive;
}

/// <summary>
/// Event raised to toggle the fan module's filter on/off
/// </summary>
[Serializable, NetSerializable]
public sealed class MechFilterToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

/// <summary>
/// Event raised to toggle the fan compressor, allowing tank refills above ambient pressure.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechFanCompressorToggleMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

/// <summary>
/// Event raised to select equipment in the radial menu
/// </summary>
[Serializable, NetSerializable]
public sealed class MechEquipmentSelectMessage(NetEntity? equipment) : BoundUserInterfaceMessage
{
    public NetEntity? Equipment = equipment;
}

/// <summary>
/// BUI state for mechs that also contains all equipment ui states.
/// </summary>
/// <remarks>
///    ⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⡠⢐⠤⢃⢰⠐⡄⣀⠀⠀
///    ⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⢀⠔⣨⠀⢁⠁⠐⡐⠠⠜⠐⠀
///    ⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠔⠐⢀⡁⣀⠔⡌⠡⢀⢐⠁⠀
///    ⠀⠀⠀⠀⢀⠔⠀⡂⡄⠠⢀⡀⠀⣄⡀⠠⠤⠴⡋⠑⡠⠀⠔⠐⢂⠕⢀⡂⠀⠀
///    ⠀⠀⠀⡔⠁⠠⡐⠁⠀⠀⠀⢘⠀⠀⠀⠀⠠⠀⠈⠪⠀⠑⠡⣃⠈⠤⡈⠀⠀⠀
///    ⠀⠀⠨⠀⠄⡒⠀⡂⢈⠀⣀⢌⠀⠀⠁⡈⠀⢆⢀⠀⡀⠉⠒⢆⠑⠀⠀⠀⠀⠀
///    ⠀⠀⠀⡁⠐⠠⠐⡀⠀⢀⣀⠣⡀⠢⡀⠀⢀⡃⠰⠀⠈⠠⢁⠎⠀⠀⠀⠀⠀⠀
///    ⠀⠀⠀⠅⠒⣈⢣⠠⠈⠕⠁⠱⠄⢤⠈⠪⠡⠎⢘⠈⡁⢙⠈⠀⠀⠀⠀⠀⠀⠀
///    ⠀⠀⠀⠃⠀⢡⠀⠧⠀⡇⠀⠀⠀⠀⠀⠀⠀⠀⠀⢕⡈⠌⠀⠀⠀⠀⠀⠀⠀⠀
///    ⠀⠀⠀⠀⠀⠀⠈⡀⡀⡆⠀⠀⠀⠀⠀⠀⠀⠀⠀⡰⠀⡐⠀⠀⠀⠀⠀⠀⠀⠀
///    ⠀⠀⠀⠀⠀⠀⠀⢈⢂⡀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠸⠀⡃⠀⠀⠀⠀⠀⠀⠀⠀
///    ⠀⠀⠀⠀⠀⠀⠀⠎⠐⢅⠀⠀⠀⠀⠀⠀⠀⠀⠀⢐⠅⠚⠄⠀⠀⠀⠀⠀⠀⠀
///    ⠀⠀⢈⠩⠈⠀⠐⠁⠀⢀⠀⠄⡂⠒⠐⠀⠆⠁⠰⠠⠀⢅⠈⠐⠄⢁⢡⠀⠀⠀
///    ⠀⠀⢈⡀⠰⡁⠀⠁⠴⠁⠔⠀⠀⠄⠄⡁⠀⠂⠀⠢⠠⠁⠀⠠⠈⠂⠬⠀⠀⠀
///    ⠀⠀⠠⡂⢄⠤⠒⣁⠐⢕⢀⡈⡐⡠⠄⢐⠀⠈⠠⠈⡀⠂⢀⣀⠰⠁⠠⠀⠀
/// trojan horse bui state⠀
/// </remarks>
[Serializable, NetSerializable]
public sealed class MechBoundUiState : BoundUserInterfaceState
{
    public List<NetEntity> Equipment = new();
    public List<NetEntity> Modules = new();
    public bool IsAirtight;
    public bool TankEnabled;
    public MechTankMode TankMode;
    public float TankTargetPressure;
    public float TankMaxTargetPressure;
    public bool FanActive;
    public MechFanState FanState = MechFanState.Off;
    public bool FilterEnabled;
    public bool CompressorEnabled;
    public float CabinPressureLevel;
    public float CabinTemperature;
    public float GasAmountLiters;
    public float TankPressure;

    // Lock system
    public bool DnaLockRegistered;
    public bool DnaLockActive;
    public bool CardLockRegistered;
    public bool CardLockActive;
    public string? OwnerDna;
    public string? OwnerJobTitle;
    public bool IsLocked;

    // Passive modules presence
    public bool HasFanModule;
    public bool HasGasModule;

    // Module capacity
    public int ModuleSpaceMax;
    public int ModuleSpaceUsed;

    // Whether a pilot is currently seated in the mech
    public bool PilotPresent;

    // Mech stats for UI synchronization
    public float Integrity;
    public float MaxIntegrity;
    public float Energy;
    public float MaxEnergy;
    public float EnergyDrainRate;
    public bool CanAirtight;
    public int EquipmentUsed;
    public int MaxEquipmentAmount;
    public bool IsBroken;
    public Dictionary<NetEntity, BoundUserInterfaceState> EquipmentUiStates = new();
}

[Serializable, NetSerializable]
public sealed class MechGrabberUiState : BoundUserInterfaceState
{
    public List<NetEntity> Contents = new();
    public int MaxContents;
}

[Serializable, NetSerializable]
public sealed class MechGeneratorUiState : BoundUserInterfaceState
{
    public float ChargeCurrent;
    public float ChargeMax;
    public MechGenerationType GenerationType;
    public bool TeslaPoweredNearby;

    public bool HasFuel;
    public string? FuelName;
    public float FuelAmount;
    public float FuelCapacity;
}

/// <summary>
/// Event raised for mech fuel generator modules to eject their stored fuel.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechGeneratorEjectFuelMessage : MechEquipmentUiMessage
{
    public MechGeneratorEjectFuelMessage(NetEntity equipment)
    {
        Equipment = equipment;
    }
}

/// <summary>
/// BUI state for mech weapons that can recharge from the mech battery.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechWeaponRechargeUiState : BoundUserInterfaceState
{
    public bool AutoRecharge;
}

/// <summary>
/// Event raised to toggle whether a mech weapon recharges from the mech battery.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechWeaponRechargeToggleMessage(NetEntity equipment, bool enabled) : BoundUserInterfaceMessage
{
    public NetEntity Equipment = equipment;
    public bool Enabled = enabled;
}

/// <summary>
/// Server acknowledgement for a weapon recharge toggle. Allows the client to update only the affected equipment row.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechWeaponRechargeStateMessage(NetEntity equipment, bool enabled, float energyDrainRate) : BoundUserInterfaceMessage
{
    public NetEntity Equipment = equipment;
    public bool Enabled = enabled;
    public float EnergyDrainRate = energyDrainRate;
}

/// <summary>
/// List of sound collection ids to be localized and displayed.
/// </summary>
[Serializable, NetSerializable]
public sealed class MechSoundboardUiState : BoundUserInterfaceState
{
    public List<string> Sounds = new();
}
