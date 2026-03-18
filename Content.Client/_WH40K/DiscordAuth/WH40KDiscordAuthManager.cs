using Content.Shared._WH40K.DiscordAuth;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Client._WH40K.DiscordAuth;

public sealed class WH40KDiscordAuthManager : ISharedWH40KDiscordAuthManager
{
    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;

    public bool TryGetSnapshot(NetUserId userId, out WH40KDiscordAuthSnapshot snapshot)
    {
        var system = _entitySystems.GetEntitySystem<WH40KDiscordAuthSystem>();
        system.EnsureSnapshot();
        return system.TryGetCachedSnapshot(out snapshot);
    }
}
