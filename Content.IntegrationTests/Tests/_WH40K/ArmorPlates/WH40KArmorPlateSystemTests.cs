using Content.IntegrationTests.Fixtures;
using Content.Shared._WH40K.ArmorPlates;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WH40K.ArmorPlates;

[TestFixture]
public sealed class WH40KArmorPlateSystemTests : GameTest
{
    private const string TestArmorId = "WH40KTestArmorPlateRig";

    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: WH40KTestArmorPlateRig
  parent: ClothingOuterArmorFlakVest
  name: test armor plate rig
  components:
  - type: WH40KArmorPlateHolder
    slotCount: 3
""";

    [Test]
    public async Task InsertedPlatesRefreshArmorAndPreventDuplicateTypes()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var itemSlots = entMan.System<ItemSlotsSystem>();

            var armor = entMan.SpawnEntity(TestArmorId, coords);
            var laserT5 = entMan.SpawnEntity("WH40KArmorPlateLaserT5", coords);
            var secondLaser = entMan.SpawnEntity("WH40KArmorPlateLaserT1", coords);
            var bulletT2 = entMan.SpawnEntity("WH40KArmorPlateBulletT2", coords);

            Assert.That(itemSlots.TryInsert(armor, WH40KArmorPlateHelper.GetSlotId(1), laserT5, null), Is.True);
            Assert.That(itemSlots.TryInsert(armor, WH40KArmorPlateHelper.GetSlotId(2), secondLaser, null), Is.False);
            Assert.That(itemSlots.TryInsert(armor, WH40KArmorPlateHelper.GetSlotId(2), bulletT2, null), Is.True);

            var armorComp = entMan.GetComponent<Content.Shared.Armor.ArmorComponent>(armor);

            Assert.Multiple(() =>
            {
                Assert.That(armorComp.Modifiers.Coefficients["Heat"], Is.EqualTo(0.3f).Within(0.001f));
                Assert.That(armorComp.Modifiers.Coefficients["Piercing"], Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(armorComp.Modifiers.Coefficients["Blunt"], Is.EqualTo(0.6f).Within(0.001f));
                Assert.That(armorComp.Modifiers.Coefficients["Slash"], Is.EqualTo(0.6f).Within(0.001f));
            });
        });
    }

    [Test]
    public async Task DamageAndExplosionWearRulesFollowInstalledTypes()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var itemSlots = entMan.System<ItemSlotsSystem>();
            var inventory = entMan.System<InventorySystem>();
            var damageable = entMan.System<DamageableSystem>();

            var human = entMan.SpawnEntity("MobHuman", coords);
            var armor = entMan.SpawnEntity(TestArmorId, coords);
            var bullet = entMan.SpawnEntity("WH40KArmorPlateBulletT2", coords);
            var melee = entMan.SpawnEntity("WH40KArmorPlateMeleeT3", coords);
            var laser = entMan.SpawnEntity("WH40KArmorPlateLaserT1", coords);

            Assert.That(inventory.TryEquip(human, armor, "outerClothing"), Is.True);
            Assert.That(itemSlots.TryInsert(armor, WH40KArmorPlateHelper.GetSlotId(1), bullet, null), Is.True);
            Assert.That(itemSlots.TryInsert(armor, WH40KArmorPlateHelper.GetSlotId(2), melee, null), Is.True);

            var laserDamage = new DamageSpecifier
            {
                DamageDict = { ["Heat"] = FixedPoint2.New(10) },
            };

            Assert.That(damageable.TryChangeDamage(human, laserDamage), Is.True);

            var bulletComp = entMan.GetComponent<WH40KArmorPlateComponent>(bullet);
            var meleeComp = entMan.GetComponent<WH40KArmorPlateComponent>(melee);

            Assert.Multiple(() =>
            {
                Assert.That(bulletComp.CurrentDurability, Is.EqualTo(19));
                Assert.That(meleeComp.CurrentDurability, Is.EqualTo(29));
            });

            Assert.That(itemSlots.TryInsert(armor, WH40KArmorPlateHelper.GetSlotId(3), laser, null), Is.True);

            var bulletDamage = new DamageSpecifier
            {
                DamageDict = { ["Piercing"] = FixedPoint2.New(10) },
            };

            Assert.That(damageable.TryChangeDamage(human, bulletDamage), Is.True);

            var laserComp = entMan.GetComponent<WH40KArmorPlateComponent>(laser);

            Assert.Multiple(() =>
            {
                Assert.That(bulletComp.CurrentDurability, Is.EqualTo(18));
                Assert.That(meleeComp.CurrentDurability, Is.EqualTo(29));
                Assert.That(laserComp.CurrentDurability, Is.EqualTo(10));
            });

            var explosionEvent = new GetExplosionResistanceEvent("Default");
            entMan.EventBus.RaiseLocalEvent(human, ref explosionEvent);

            Assert.Multiple(() =>
            {
                Assert.That(bulletComp.CurrentDurability, Is.EqualTo(17));
                Assert.That(meleeComp.CurrentDurability, Is.EqualTo(28));
                Assert.That(laserComp.CurrentDurability, Is.EqualTo(9));
            });
        });
    }
}
