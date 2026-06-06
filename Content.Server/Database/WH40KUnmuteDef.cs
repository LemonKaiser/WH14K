using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed class WH40KUnmuteDef
{
    public int MuteId { get; }
    public NetUserId? UnmutingAdmin { get; }
    public DateTimeOffset UnmuteTime { get; }

    public WH40KUnmuteDef(int muteId, NetUserId? unmutingAdmin, DateTimeOffset unmuteTime)
    {
        MuteId = muteId;
        UnmutingAdmin = unmutingAdmin;
        UnmuteTime = unmuteTime;
    }
}
