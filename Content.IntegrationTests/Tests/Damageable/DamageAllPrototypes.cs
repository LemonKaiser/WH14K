using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Utility;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Damageable;

[TestFixture]
[TestOf(typeof(DamageableComponent))]
[TestOf(typeof(DamageableSystem))]
public sealed class DamageAllPrototypesTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly DamageableSystem _damageableSystem = default!;

    private static readonly string[] Damageables = GameDataScrounger.EntitiesWithComponent("Damageable");

    [Test]
    [TestOf(typeof(DamageableSystem))]
    [Description("Ensures all Entity Prototypes with damageable can be damaged.")]
    public async Task TestDamageableComponents()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var damageTypes = SProtoMan.EnumeratePrototypes<DamageTypePrototype>().ToArray();

            using (Assert.EnterMultipleScope())
            {
                for (var i = 0; i < Damageables.Length; i++)
                {
                    var damageable = Damageables[i];
                    var coords = map.GridCoords.Offset(new Vector2(i % 32, i / 32));
                    var entity = SSpawnAtPosition(damageable, coords);

                    try
                    {
                        // Intentionally cannot take damage, ignore it.
                        if (SEntMan.HasComponent<GodmodeComponent>(entity))
                            continue;

                        // This test only needs to prove the prototype can take at least one valid damage type.
                        var damageType = damageTypes.FirstOrDefault(type => _damageableSystem.CanBeDamagedBy(entity, type));

                        Assert.That(damageType, Is.Not.Null, $"{damageable} has Damageable but cannot be damaged by any damage type.");
                        if (damageType is null)
                            continue;

                        var damage = new DamageSpecifier(damageType, FixedPoint2.Epsilon);
                        var previousDamage = _damageableSystem.GetTotalDamage(entity);
                        _damageableSystem.ChangeDamage(entity, damage, ignoreResistances: true);
                        Assert.That(_damageableSystem.GetTotalDamage(entity), Is.EqualTo(FixedPoint2.Epsilon + previousDamage));
                        _damageableSystem.ClearAllDamage(entity);
                    }
                    finally
                    {
                        if (!SEntMan.Deleted(entity))
                            SEntMan.DeleteEntity(entity);
                    }
                }
            }
        });
    }
}
