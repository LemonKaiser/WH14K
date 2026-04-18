using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Raised after the shared psyker-role shutdown path begins on server.
/// Used to chain server-only cleanup without duplicating ComponentShutdown subscriptions.
/// </summary>
public sealed class WH40KPsykerRoleShutdownEvent : EntityEventArgs
{
    public EntityUid User { get; }

    public WH40KPsykerRoleShutdownEvent(EntityUid user)
    {
        User = user;
    }
}
