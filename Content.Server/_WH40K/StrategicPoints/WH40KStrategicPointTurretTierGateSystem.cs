using Content.Shared.Turrets;
using Content.Server.Turrets;
using Content.Shared._WH40K.StrategicPoints;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;


namespace Content.Server._WH40K.StrategicPoints;

public sealed partial class WH40KStrategicPointTurretTierGateSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityManager _entMan = default!;
    [Dependency] private DeployableTurretSystem _deployableTurretSystem = default!;


    public override void Initialize()
    {
        base.Initialize();


        SubscribeLocalEvent<WH40KStrategicPointUpgradedEvent>(OnStrategicPointUpgraded);
        SubscribeLocalEvent<WH40KStrategicPointBuiltEvent>(OnStrategicPointBuilt);


    }



    private void OnStrategicPointShutdown(Entity<WH40KStrategicPointComponent> ent, ref ComponentShutdown args)
    {
    }

    private void OnStrategicPointBuilt(WH40KStrategicPointBuiltEvent ev)
    {
        TryApplyGate(ev.PointUid);
    }

    private void OnStrategicPointUpgraded(WH40KStrategicPointUpgradedEvent ev)
    {
        TryApplyGate(ev.PointUid);
    }

    private void TryApplyGate(EntityUid pointUid)
    {
        if (!TryComp<WH40KStrategicPointTurretTierGateComponent>(pointUid, out var gate) ||
            !TryComp<WH40KStrategicPointComponent>(pointUid, out var point) ||
            !TryComp<DeployableTurretComponent>(pointUid, out var turret))
        {
            return;
        }

        // Gate matches only for required point tier/profile.
        var match = point.Tier == gate.RequiredTier;


        var enabled = match ? gate.TurretEnabledOnMatch : gate.TurretEnabledOnMismatch;

        _deployableTurretSystem.TrySetState(new Entity<DeployableTurretComponent>(pointUid, turret), enabled);
        DirtyField(pointUid, turret, nameof(DeployableTurretComponent.Enabled));



    }
}
