using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Raised after shared chaos-role startup baseline is initialized on server.
/// Used to chain server-only startup steps without duplicating ComponentStartup subscriptions.
/// </summary>
public sealed class WH40KChaosRoleStartupEvent : EntityEventArgs
{
    public EntityUid User { get; }

    public WH40KChaosRoleStartupEvent(EntityUid user)
    {
        User = user;
    }
}
