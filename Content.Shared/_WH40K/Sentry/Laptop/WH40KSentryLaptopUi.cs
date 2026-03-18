using System;
using System.Collections.Generic;
using Content.Shared.Turrets;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Sentry.Laptop;

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopBuiState : BoundUserInterfaceState
{
    public List<WH40KSentryLaptopTurretInfo> LinkedTurrets { get; }
    public int LinkedCount { get; }
    public int MaxLinkedCount { get; }
    public List<string> IffTeamOptions { get; }
    public List<WH40KSentryLaptopAlertInfo> Alerts { get; }

    public WH40KSentryLaptopBuiState(
        List<WH40KSentryLaptopTurretInfo> linkedTurrets,
        int linkedCount,
        int maxLinkedCount,
        List<string> iffTeamOptions,
        List<WH40KSentryLaptopAlertInfo> alerts)
    {
        LinkedTurrets = linkedTurrets;
        LinkedCount = linkedCount;
        MaxLinkedCount = maxLinkedCount;
        IffTeamOptions = iffTeamOptions;
        Alerts = alerts;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopTurretInfo
{
    public NetEntity Turret { get; }
    public string Name { get; }
    public string TeamId { get; }
    public DeployableTurretState State { get; }
    public int Ammo { get; }
    public int AmmoCapacity { get; }
    public bool Broken { get; }
    public bool PowerEnabled { get; }
    public List<string> FriendlyTeams { get; }

    public WH40KSentryLaptopTurretInfo(
        NetEntity turret,
        string name,
        string teamId,
        DeployableTurretState state,
        int ammo,
        int ammoCapacity,
        bool broken,
        bool powerEnabled,
        List<string> friendlyTeams)
    {
        Turret = turret;
        Name = name;
        TeamId = teamId;
        State = state;
        Ammo = ammo;
        AmmoCapacity = ammoCapacity;
        Broken = broken;
        PowerEnabled = powerEnabled;
        FriendlyTeams = friendlyTeams;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopAlertInfo
{
    public string Message { get; }
    public WH40KSentryLaptopAlertSeverity Severity { get; }
    public int AgeSeconds { get; }

    public WH40KSentryLaptopAlertInfo(
        string message,
        WH40KSentryLaptopAlertSeverity severity,
        int ageSeconds)
    {
        Message = message;
        Severity = severity;
        AgeSeconds = ageSeconds;
    }
}

[Serializable, NetSerializable]
public enum WH40KSentryLaptopAlertSeverity : byte
{
    Info,
    Warning,
    Critical,
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopUnlinkBuiMsg(NetEntity turret) : BoundUserInterfaceMessage
{
    public NetEntity Turret = turret;
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopUnlinkAllBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopRefreshBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopTogglePowerBuiMsg(NetEntity turret) : BoundUserInterfaceMessage
{
    public NetEntity Turret = turret;
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopSetPowerAllBuiMsg(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopResetTargetingBuiMsg(NetEntity turret) : BoundUserInterfaceMessage
{
    public NetEntity Turret = turret;
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopResetTargetingAllBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopSetIffTeamBuiMsg(NetEntity turret, string teamId, bool allowed) : BoundUserInterfaceMessage
{
    public NetEntity Turret = turret;
    public string TeamId = teamId;
    public bool Allowed = allowed;
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopSetIffTeamAllBuiMsg(string teamId, bool allowed) : BoundUserInterfaceMessage
{
    public string TeamId = teamId;
    public bool Allowed = allowed;
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopViewCameraBuiMsg(NetEntity turret) : BoundUserInterfaceMessage
{
    public NetEntity Turret = turret;
}

[Serializable, NetSerializable]
public sealed class WH40KSentryLaptopCloseCameraBuiMsg : BoundUserInterfaceMessage;
