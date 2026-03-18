using Robust.Shared.Network;

namespace Content.Shared._WH40K.DiscordAuth;

public interface ISharedWH40KDiscordAuthManager
{
    bool TryGetSnapshot(NetUserId userId, out WH40KDiscordAuthSnapshot snapshot);
}
