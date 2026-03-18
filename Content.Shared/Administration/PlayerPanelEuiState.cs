using Content.Shared._WH40K.Administration.ScreenCheck;
using Content.Shared.Eui;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration;

[Serializable, NetSerializable]
public sealed class PlayerPanelEuiState(
    NetUserId guid,
    string username,
    TimeSpan playtime,
    int? totalNotes,
    int? totalBans,
    int? totalRoleBans,
    int sharedConnections,
    bool? whitelisted,
    bool canFreeze,
    bool frozen,
    bool canAhelp,
    bool canScreenCheck,
    bool hasActiveScreenCheck,
    string activeScreenCheckAdmin,
    DateTime activeScreenCheckSinceUtc,
    bool hasLastScreenCheck,
    string lastScreenCheckAdmin,
    DateTime lastScreenCheckAtUtc,
    ScreenCheckUiStatus lastScreenCheckStatus)
    : EuiStateBase
{
    public readonly NetUserId Guid = guid;
    public readonly string Username = username;
    public readonly TimeSpan Playtime = playtime;
    public readonly int? TotalNotes = totalNotes;
    public readonly int? TotalBans = totalBans;
    public readonly int? TotalRoleBans = totalRoleBans;
    public readonly int SharedConnections = sharedConnections;
    public readonly bool? Whitelisted = whitelisted;
    public readonly bool CanFreeze = canFreeze;
    public readonly bool Frozen = frozen;
    public readonly bool CanAhelp = canAhelp;
    public readonly bool CanScreenCheck = canScreenCheck;
    public readonly bool HasActiveScreenCheck = hasActiveScreenCheck;
    public readonly string ActiveScreenCheckAdmin = activeScreenCheckAdmin;
    public readonly DateTime ActiveScreenCheckSinceUtc = activeScreenCheckSinceUtc;
    public readonly bool HasLastScreenCheck = hasLastScreenCheck;
    public readonly string LastScreenCheckAdmin = lastScreenCheckAdmin;
    public readonly DateTime LastScreenCheckAtUtc = lastScreenCheckAtUtc;
    public readonly ScreenCheckUiStatus LastScreenCheckStatus = lastScreenCheckStatus;
}


[Serializable, NetSerializable]
public sealed class PlayerPanelFreezeMessage : EuiMessageBase
{
    public readonly bool Mute;

    public PlayerPanelFreezeMessage(bool mute = false)
    {
        Mute = mute;
    }
}

[Serializable, NetSerializable]
public sealed class PlayerPanelLogsMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class PlayerPanelDeleteMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class PlayerPanelRejuvenationMessage: EuiMessageBase;

[Serializable, NetSerializable]
public sealed class PlayerPanelFollowMessage: EuiMessageBase;

[Serializable, NetSerializable]
public sealed class PlayerPanelScreenCheckMessage : EuiMessageBase;
