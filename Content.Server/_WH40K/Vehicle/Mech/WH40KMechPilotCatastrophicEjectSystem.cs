using Content.Server.Mech.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Mech.Components;
using Content.Shared.Stunnable;
using Content.Shared.Vehicle.Components;
using Content.Shared._WH40K.Vehicle.Mech;

namespace Content.Server._WH40K.Vehicle.Mech;

public sealed partial class WH40KMechPilotCatastrophicEjectSystem : EntitySystem
{
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  SharedStunSystem _stun = default!;

    public override void Initialize()
    {
#pragma warning disable CS0618
        SubscribeLocalEvent<WH40KMechPilotCatastrophicEjectComponent, DamageChangedEvent>(
            OnDamageChanged,
            before: [typeof(MechSystem)]);
#pragma warning restore CS0618
    }

#pragma warning disable CS0618
    private void OnDamageChanged(Entity<WH40KMechPilotCatastrophicEjectComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased ||
            !TryComp<MechComponent>(ent.Owner, out var mech) ||
            mech.Broken ||
            mech.Integrity <= 0 ||
            !TryComp<VehicleComponent>(ent.Owner, out var vehicle) ||
            vehicle.Operator is not { } pilot)
        {
            return;
        }

        var currentIntegrity = mech.MaxIntegrity - _damageable.GetTotalDamage((ent.Owner, args.Damageable));
#pragma warning restore CS0618
        if (currentIntegrity > 0)
            return;

        var stunDuration = TimeSpan.FromSeconds(ent.Comp.StunSeconds);
        if (stunDuration > TimeSpan.Zero)
        {
            _stun.TryKnockdown(pilot, stunDuration, force: true);
            _stun.TryAddStunDuration(pilot, stunDuration);
        }

        if (!ent.Comp.Damage.Empty)
            _damageable.TryChangeDamage(pilot, ent.Comp.Damage, origin: ent.Owner);
    }
}
