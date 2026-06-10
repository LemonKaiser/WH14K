#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;

namespace Content.IntegrationTests.Tests.Storage;

public sealed class FuelTankInteractionTest : InteractionTest
{
    [TestCase("WeldingFuelTankFull")]
    [TestCase("WeldingFuelTankHighCapacity")]
    public async Task WelderDoesNotExplodeFuelTank(string prototype)
    {
        await SpawnTarget(prototype);

        Assert.That(Target, Is.Not.Null);
        var tank = ToServer(Target);
        Assert.That(tank, Is.Not.Null);
        var tankUid = tank!.Value;
        var damageableSystem = SEntMan.System<DamageableSystem>();
        FixedPoint2 initialDamage = FixedPoint2.Zero;

        await Server.WaitAssertion(() =>
        {
            var hasDamageable = SEntMan.TryGetComponent(tankUid, out DamageableComponent? damageable);
            Assert.That(hasDamageable, Is.True);
            if (!hasDamageable || damageable == null)
                return;

            initialDamage = damageableSystem.GetTotalDamage((tankUid, damageable));
            Assert.That(initialDamage, Is.EqualTo(FixedPoint2.Zero));
        });

        await InteractUsing(Weld);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(tankUid), Is.True);
            var hasDamageable = SEntMan.TryGetComponent(tankUid, out DamageableComponent? damageable);
            Assert.That(hasDamageable, Is.True);
            if (!hasDamageable || damageable == null)
                return;

            Assert.That(damageableSystem.GetTotalDamage((tankUid, damageable)), Is.EqualTo(initialDamage));
        });

        AssertExists(Target);
        AssertPrototype(prototype);
    }
}
