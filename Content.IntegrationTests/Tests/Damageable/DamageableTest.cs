using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

#pragma warning disable CS0618
namespace Content.IntegrationTests.Tests.Damageable
{
    [TestFixture]
    [TestOf(typeof(DamageableComponent))]
    [TestOf(typeof(DamageableSystem))]
    public sealed class DamageableTest
    {
        private const string TestDamageableEntityId = "TestDamageableEntityId";
        private const string TestGroup1 = "TestGroup1";
        private const string TestGroup2 = "TestGroup2";
        private const string TestGroup3 = "TestGroup3";
        private const string TestDamage1 = "TestDamage1";
        private const string TestDamage2a = "TestDamage2a";
        private const string TestDamage2b = "TestDamage2b";

        private const string TestDamage3a = "TestDamage3a";

        private const string TestDamage3b = "TestDamage3b";
        private const string TestDamage3c = "TestDamage3c";

        [TestPrototypes]
        private const string Prototypes = $@"
# Define some damage groups
- type: damageType
  id: {TestDamage1}
  name: damage-type-blunt

- type: damageType
  id: {TestDamage2a}
  name: damage-type-blunt

- type: damageType
  id: {TestDamage2b}
  name: damage-type-blunt

- type: damageType
  id: {TestDamage3a}
  name: damage-type-blunt

- type: damageType
  id: {TestDamage3b}
  name: damage-type-blunt

- type: damageType
  id: {TestDamage3c}
  name: damage-type-blunt

# Define damage Groups with 1,2,3 damage types
- type: damageGroup
  id: {TestGroup1}
  name: damage-group-brute
  damageTypes:
    - {TestDamage1}

- type: damageGroup
  id: {TestGroup2}
  name: damage-group-brute
  damageTypes:
    - {TestDamage2a}
    - {TestDamage2b}

- type: damageGroup
  id: {TestGroup3}
  name: damage-group-brute
  damageTypes:
    - {TestDamage3a}
    - {TestDamage3b}
    - {TestDamage3c}

# This container should not support TestDamage1 or TestDamage2b
- type: damageContainer
  id: testDamageContainer
  supportedGroups:
    - {TestGroup3}
  supportedTypes:
    - {TestDamage2a}

- type: entity
  id: {TestDamageableEntityId}
  name: {TestDamageableEntityId}
  components:
  - type: Damageable
    damageContainer: testDamageContainer
";

        [Test]
        public async Task TestDamageableComponents()
        {
            await using var pair = await PoolManager.GetServerClient();
            var server = pair.Server;

            var sEntityManager = server.ResolveDependency<IEntityManager>();
            var sEntitySystemManager = server.ResolveDependency<IEntitySystemManager>();

            EntityUid sDamageableEntity = default;
            DamageableComponent sDamageableComponent = null;
            DamageableSystem sDamageableSystem = null;

            FixedPoint2 typeDamage;
            var supportedGroupTypes = new[] { TestDamage3a, TestDamage3b, TestDamage3c };

            var map = await pair.CreateTestMap();

            await server.WaitPost(() =>
            {
                var coordinates = map.MapCoords;

                sDamageableEntity = sEntityManager.SpawnEntity(TestDamageableEntityId, coordinates);
                sDamageableComponent = sEntityManager.GetComponent<DamageableComponent>(sDamageableEntity);
                sDamageableSystem = sEntitySystemManager.GetEntitySystem<DamageableSystem>();
            });

            await server.WaitRunTicks(5);

            await server.WaitAssertion(() =>
            {
                var uid = sDamageableEntity;

                // Check that grouped damage totals are tracked correctly across supported damage types.
                var damageToDeal = FixedPoint2.New(supportedGroupTypes.Length * 5);
                var damage = CreateDamage(
                    (TestDamage3a, 5),
                    (TestDamage3b, 5),
                    (TestDamage3c, 5));

                sDamageableSystem.ChangeDamage(uid, damage, true);

                Assert.Multiple(() =>
                {
                    Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(damageToDeal));
                    Assert.That(GetGroupDamage(sDamageableSystem, uid, sDamageableComponent, TestGroup3), Is.EqualTo(damageToDeal));
                    foreach (var type in supportedGroupTypes)
                    {
                        Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict.TryGetValue(type, out typeDamage));
                        Assert.That(typeDamage, Is.EqualTo(FixedPoint2.New(5)));
                    }
                });

                // Heal
                sDamageableSystem.ChangeDamage(uid, -damage);

                Assert.Multiple(() =>
                {
                    Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.Zero));
                    Assert.That(GetGroupDamage(sDamageableSystem, uid, sDamageableComponent, TestGroup3), Is.EqualTo(FixedPoint2.Zero));
                    foreach (var type in supportedGroupTypes)
                    {
                        Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict.TryGetValue(type, out typeDamage));
                        Assert.That(typeDamage, Is.EqualTo(FixedPoint2.Zero));
                    }
                });

                // Check that grouped totals remain correct with uneven per-type damage.
                damage = CreateDamage(
                    (TestDamage3a, 4.66f),
                    (TestDamage3b, 4.67f),
                    (TestDamage3c, 4.67f));
                sDamageableSystem.ChangeDamage(uid, damage, true);

                Assert.Multiple(() =>
                {
                    Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.New(14)));
                    Assert.That(GetGroupDamage(sDamageableSystem, uid, sDamageableComponent, TestGroup3), Is.EqualTo(FixedPoint2.New(14)));
                    Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict[TestDamage3a], Is.EqualTo(FixedPoint2.New(4.66f)));
                    Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict[TestDamage3b], Is.EqualTo(FixedPoint2.New(4.67f)));
                    Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict[TestDamage3c], Is.EqualTo(FixedPoint2.New(4.67f)));
                });

                // Heal
                sDamageableSystem.ChangeDamage(uid, -damage);

                Assert.Multiple(() =>
                {
                    Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.Zero));
                    Assert.That(GetGroupDamage(sDamageableSystem, uid, sDamageableComponent, TestGroup3), Is.EqualTo(FixedPoint2.Zero));
                    foreach (var type in supportedGroupTypes)
                    {
                        Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict.TryGetValue(type, out typeDamage));
                        Assert.That(typeDamage, Is.EqualTo(FixedPoint2.Zero));
                    }

                    // Test that unsupported groups return false when setting/getting damage (and don't change damage)
                    Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.Zero));
                });
                damage = CreateDamage((TestDamage1, 10), (TestDamage2b, 10));
                sDamageableSystem.ChangeDamage(uid, damage, true);

                Assert.Multiple(() =>
                {
                    Assert.That(sDamageableSystem.GetDamagePerGroup((uid, sDamageableComponent)).TryGetValue(TestGroup1, out _), Is.False);
                    Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict.TryGetValue(TestDamage1, out typeDamage), Is.False);
                    Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.Zero));
                });

                // Test SetAll and ClearAll function
                sDamageableSystem.SetAllDamage((sDamageableEntity, sDamageableComponent), 10);
                Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.New(10 * GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict.Count)));
                sDamageableSystem.SetAllDamage((sDamageableEntity, sDamageableComponent), 0);
                Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.Zero));
                sDamageableSystem.SetAllDamage((sDamageableEntity, sDamageableComponent), 10);
                Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.New(10 * GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict.Count)));
                sDamageableSystem.ClearAllDamage((sDamageableEntity, sDamageableComponent));
                Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.Zero));

                // Test 'wasted' healing
                sDamageableSystem.ChangeDamage(uid, CreateDamage((TestDamage3a, 5)));
                sDamageableSystem.ChangeDamage(uid, CreateDamage((TestDamage3b, 7)));
                sDamageableSystem.ChangeDamage(uid, CreateDamage(
                    (TestDamage3a, -3.66f),
                    (TestDamage3b, -3.67f),
                    (TestDamage3c, -3.67f)));

                Assert.Multiple(() =>
                {
                    Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict[TestDamage3a], Is.EqualTo(FixedPoint2.New(1.34)));
                    Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict[TestDamage3b], Is.EqualTo(FixedPoint2.New(3.33)));
                    Assert.That(GetAllDamage(sDamageableSystem, uid, sDamageableComponent).DamageDict[TestDamage3c], Is.EqualTo(FixedPoint2.New(0)));
                });

                // Test Over-Healing
                sDamageableSystem.ChangeDamage(uid, CreateDamage(
                    (TestDamage3a, -100),
                    (TestDamage3b, -100),
                    (TestDamage3c, -100)));
                Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.Zero));

                // Test that if no health change occurred, returns false
                sDamageableSystem.ChangeDamage(uid, CreateDamage(
                    (TestDamage3a, -100),
                    (TestDamage3b, -100),
                    (TestDamage3c, -100)));
                Assert.That(GetTotalDamage(sDamageableSystem, uid, sDamageableComponent), Is.EqualTo(FixedPoint2.Zero));
            });
            await pair.CleanReturnAsync();
        }

        private static DamageSpecifier CreateDamage(params (string Type, float Amount)[] entries)
        {
            var damage = new DamageSpecifier();

            foreach (var (type, amount) in entries)
            {
                damage.DamageDict[type] = FixedPoint2.New(amount);
            }

            return damage;
        }

        private static DamageSpecifier GetAllDamage(DamageableSystem system, EntityUid uid, DamageableComponent component)
        {
            return system.GetAllDamage((uid, component));
        }

        private static FixedPoint2 GetGroupDamage(DamageableSystem system, EntityUid uid, DamageableComponent component, string groupId)
        {
            return system.GetDamagePerGroup((uid, component)).TryGetValue(groupId, out var damage)
                ? damage
                : FixedPoint2.Zero;
        }

        private static FixedPoint2 GetTotalDamage(DamageableSystem system, EntityUid uid, DamageableComponent component)
        {
            return system.GetTotalDamage((uid, component));
        }
    }
}
