using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed class WH40KMuteDef
{
    public int? Id { get; }
    public NetUserId UserId { get; }
    public WH40KMuteType Type { get; }
    public string Reason { get; }
    public NetUserId? MutingAdmin { get; }
    public DateTimeOffset MuteTime { get; }
    public DateTimeOffset? ExpirationTime { get; }
    public WH40KUnmuteDef? Unmute { get; }

    public WH40KMuteDef(
        int? id,
        NetUserId userId,
        WH40KMuteType type,
        string reason,
        NetUserId? mutingAdmin,
        DateTimeOffset muteTime,
        DateTimeOffset? expirationTime,
        WH40KUnmuteDef? unmute)
    {
        Id = id;
        UserId = userId;
        Type = type;
        Reason = reason;
        MutingAdmin = mutingAdmin;
        MuteTime = muteTime;
        ExpirationTime = expirationTime;
        Unmute = unmute;
    }

    public bool IsActive(DateTimeOffset now)
    {
        return Unmute == null && (ExpirationTime == null || ExpirationTime > now);
    }
}
