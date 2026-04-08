using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Tank;

[Serializable, NetSerializable]
public enum WH40KTankUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum WH40KTankModuleType : byte
{
    Engine = 0,
    Tracks = 1,
    Turret = 2,
    MainGun = 3,
    Coaxial = 4,
}

[Serializable, NetSerializable]
public enum WH40KTankModuleStatus : byte
{
    Operational = 0,
    Damaged = 1,
    Critical = 2,
    Destroyed = 3,
}

[Serializable, NetSerializable]
public sealed class WH40KTankCrewEntry
{
    public WH40KTankCrewRole Role { get; }
    public string OccupantName { get; }
    public bool Occupied { get; }

    public WH40KTankCrewEntry(WH40KTankCrewRole role, string occupantName, bool occupied)
    {
        Role = role;
        OccupantName = occupantName;
        Occupied = occupied;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KTankModuleEntry
{
    public WH40KTankModuleType Module { get; }
    public float IntegrityFraction { get; }
    public WH40KTankModuleStatus Status { get; }

    public WH40KTankModuleEntry(
        WH40KTankModuleType module,
        float integrityFraction,
        WH40KTankModuleStatus status)
    {
        Module = module;
        IntegrityFraction = integrityFraction;
        Status = status;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KTankBuiState : BoundUserInterfaceState
{
    public string TankName { get; }
    public string MainWeaponLocKey { get; }
    public string CoaxialWeaponLocKey { get; }
    public string MainAmmoLocKey { get; }
    public string CoaxialAmmoLocKey { get; }
    public float HullIntegrityFraction { get; }
    public bool EngineRunning { get; }
    public float FuelCurrent { get; }
    public float FuelCapacity { get; }
    public float FuelFraction { get; }
    public bool HasAimPoint { get; }
    public bool PendingMainGunFire { get; }
    public bool PendingCoaxialFire { get; }
    public bool HasCoaxialWeapon { get; }
    public int MainGunAmmoCount { get; }
    public int MainGunAmmoCapacity { get; }
    public float MainGunReloadTimeLeft { get; }
    public int CoaxialAmmoCount { get; }
    public int CoaxialAmmoCapacity { get; }
    public float CoaxialReloadTimeLeft { get; }
    public WH40KTankCrewEntry[] Crew { get; }
    public WH40KTankModuleEntry[] Modules { get; }

    public WH40KTankBuiState(
        string tankName,
        string mainWeaponLocKey,
        string coaxialWeaponLocKey,
        string mainAmmoLocKey,
        string coaxialAmmoLocKey,
        float hullIntegrityFraction,
        bool engineRunning,
        float fuelCurrent,
        float fuelCapacity,
        float fuelFraction,
        bool hasAimPoint,
        bool pendingMainGunFire,
        bool pendingCoaxialFire,
        bool hasCoaxialWeapon,
        int mainGunAmmoCount,
        int mainGunAmmoCapacity,
        float mainGunReloadTimeLeft,
        int coaxialAmmoCount,
        int coaxialAmmoCapacity,
        float coaxialReloadTimeLeft,
        WH40KTankCrewEntry[] crew,
        WH40KTankModuleEntry[] modules)
    {
        TankName = tankName;
        MainWeaponLocKey = mainWeaponLocKey;
        CoaxialWeaponLocKey = coaxialWeaponLocKey;
        MainAmmoLocKey = mainAmmoLocKey;
        CoaxialAmmoLocKey = coaxialAmmoLocKey;
        HullIntegrityFraction = hullIntegrityFraction;
        EngineRunning = engineRunning;
        FuelCurrent = fuelCurrent;
        FuelCapacity = fuelCapacity;
        FuelFraction = fuelFraction;
        HasAimPoint = hasAimPoint;
        PendingMainGunFire = pendingMainGunFire;
        PendingCoaxialFire = pendingCoaxialFire;
        HasCoaxialWeapon = hasCoaxialWeapon;
        MainGunAmmoCount = mainGunAmmoCount;
        MainGunAmmoCapacity = mainGunAmmoCapacity;
        MainGunReloadTimeLeft = mainGunReloadTimeLeft;
        CoaxialAmmoCount = coaxialAmmoCount;
        CoaxialAmmoCapacity = coaxialAmmoCapacity;
        CoaxialReloadTimeLeft = coaxialReloadTimeLeft;
        Crew = crew ?? Array.Empty<WH40KTankCrewEntry>();
        Modules = modules ?? Array.Empty<WH40KTankModuleEntry>();
    }
}
