using System;
using Content.Shared.Administration.BanList;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Administration.Mute;

[Serializable, NetSerializable]
public sealed record WH40KSharedMute(
    int Id,
    WH40KMuteType Type,
    DateTime MuteTime,
    DateTime? ExpirationTime,
    string Reason,
    string? MutingAdminName,
    SharedUnban? Unmute);
