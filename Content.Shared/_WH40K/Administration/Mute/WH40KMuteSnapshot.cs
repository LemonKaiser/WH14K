using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.Mute;

[Serializable, NetSerializable]
public sealed record WH40KActiveMuteInfo(
    WH40KMuteType Type,
    string Reason,
    DateTime? ExpiresAtUtc);

[Serializable, NetSerializable]
public sealed record WH40KMuteSnapshot(
    WH40KMuteType ActiveScopes,
    WH40KActiveMuteInfo? ChatMute,
    WH40KActiveMuteInfo? AHelpMute);
