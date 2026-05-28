using Robust.Shared.Network;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Chaos role bootstrap stays separate from the shared warp runtime.
/// Cult members now receive warp runtime so patron unlocks can be shared across the cult.
/// </summary>
public sealed partial class SharedWH40KChaosRoleBootstrapSystem : EntitySystem
{
    [Dependency] private  INetManager _netManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KChaosGiftRoleComponent, ComponentStartup>(OnChaosRoleStartup);
        SubscribeLocalEvent<WH40KChaosLeaderRoleComponent, ComponentStartup>(OnChaosLeaderRoleStartup);
    }

    private void OnChaosRoleStartup(EntityUid uid, WH40KChaosGiftRoleComponent component, ref ComponentStartup args)
    {
        if (!_netManager.IsServer)
            return;

        EnsureComp<WH40KChaosGiftProgressionComponent>(uid);
        EnsureChaosCultRuntime(uid);

        RaiseLocalEvent(uid, new WH40KChaosRoleStartupEvent(uid));
    }

    private void OnChaosLeaderRoleStartup(EntityUid uid, WH40KChaosLeaderRoleComponent component, ref ComponentStartup args)
    {
        if (!_netManager.IsServer || !HasComp<WH40KChaosGiftRoleComponent>(uid))
            return;

        EnsureComp<WH40KChaosGiftProgressionComponent>(uid);
        EnsureChaosCultRuntime(uid);

        RaiseLocalEvent(uid, new WH40KChaosRoleStartupEvent(uid));
    }

    private void EnsureChaosCultRuntime(EntityUid uid)
    {
        EnsureComp<WH40KWarpResourceComponent>(uid);
        EnsureComp<WH40KWarpInstabilityComponent>(uid);
        EnsureComp<WH40KChaosGiftStarterActionLoadoutComponent>(uid);
    }
}
