using Robust.Shared.Network;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Imperium psyker bootstrap is kept separate from chaos runtime wiring.
/// </summary>
public sealed class SharedWH40KPsykerRoleBootstrapSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KPsykerRoleComponent, ComponentStartup>(OnPsykerRoleStartup);
        SubscribeLocalEvent<WH40KPsykerRoleComponent, ComponentShutdown>(OnPsykerRoleShutdown);
    }

    private void OnPsykerRoleStartup(EntityUid uid, WH40KPsykerRoleComponent component, ref ComponentStartup args)
    {
        if (!_netManager.IsServer)
            return;

        EnsureComp<WH40KWarpResourceComponent>(uid);
        EnsureComp<WH40KWarpInstabilityComponent>(uid);
        EnsureComp<WH40KPsykerProgressionComponent>(uid);
        EnsureComp<WH40KPsykerAstralProgressionComponent>(uid);
        EnsureComp<WH40KPsykerStarterActionLoadoutComponent>(uid);
    }

    private void OnPsykerRoleShutdown(EntityUid uid, WH40KPsykerRoleComponent component, ref ComponentShutdown args)
    {
        if (!_netManager.IsServer)
            return;

        RaiseLocalEvent(uid, new WH40KPsykerRoleShutdownEvent(uid));
    }
}
