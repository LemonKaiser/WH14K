using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mech.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Construction.Interaction;

public sealed class RepairableEntityRepair : InteractionTest
{
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    [Test]
    public async Task RepairWH40KVehicleWithWelder()
    {
        await SpawnTarget("WH40KVehicleMotorbikeImperium");
        await DamageTarget(60);

        await InteractUsing(Weld);

        Assert.That(GetTotalDamage(), Is.EqualTo(FixedPoint2.Zero));
    }

    [Test]
    public async Task RepairBorgWithWelder()
    {
        await SpawnTarget("BorgChassisGeneric");
        await DamageTarget(20);

        await InteractUsing(Weld);

        Assert.That(GetTotalDamage(), Is.EqualTo(FixedPoint2.Zero));
    }

    [Test]
    public async Task RepairBrokenSentinelWithWelder()
    {
        await SpawnTarget("MechSentinelBatteryAutogun");
        await DamageTarget(350);

        Assert.That(SEntMan.GetComponent<MechComponent>(STarget!.Value).Broken, Is.True);

        await InteractUsing(Weld);

        Assert.That(GetTotalDamage(), Is.EqualTo(FixedPoint2.Zero));
        Assert.That(SEntMan.GetComponent<MechComponent>(STarget!.Value).Broken, Is.False);
    }

    private async Task DamageTarget(int amount)
    {
        var sys = SEntMan.System<DamageableSystem>();
        var damageType = Server.ProtoMan.Index(BluntDamageType);
        var damage = new DamageSpecifier(damageType, FixedPoint2.New(amount));

        Assert.That(GetTotalDamage(), Is.EqualTo(FixedPoint2.Zero));
        await Server.WaitPost(() => sys.TryChangeDamage(SEntMan.GetEntity(Target).Value, damage, ignoreResistances: true));
        await RunTicks(5);
        Assert.That(GetTotalDamage(), Is.GreaterThan(FixedPoint2.Zero));
    }

    private FixedPoint2 GetTotalDamage()
    {
        var sys = SEntMan.System<DamageableSystem>();
        return sys.GetTotalDamage(STarget!.Value);
    }
}
