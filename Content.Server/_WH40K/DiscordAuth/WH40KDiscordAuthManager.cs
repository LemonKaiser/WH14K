using Content.Shared._WH40K.DiscordAuth;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Server._WH40K.DiscordAuth;

public sealed class WH40KDiscordAuthManager : ISharedWH40KDiscordAuthManager
{
    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;

    public bool TryGetSnapshot(NetUserId userId, out WH40KDiscordAuthSnapshot snapshot)
    {
        return _entitySystems.GetEntitySystem<WH40KDiscordAuthSystem>().TryGetSharedSnapshot(userId, out snapshot);
    }
}
