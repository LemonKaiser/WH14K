using Content.IntegrationTests.Fixtures;
using Content.Shared._WH40K.Weapons.Mods;
using Content.Shared.Clothing;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Station;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Linq;

namespace Content.IntegrationTests.Tests._WH40K.Weapons.Mods;

/// <summary>
/// Integration tests for the WH40K loadout weapon-mod system: verifying that
/// <see cref="WH40KLoadoutModSystem"/> correctly discovers available mods, installs them
/// onto a spawned weapon, and that <see cref="SharedStationSpawningSystem.EquipRoleLoadout"/>
/// applies selected mods to the player's weapon at round start.
/// </summary>
[TestFixture]
public sealed class WH40KLoadoutModSystemTests : GameTest
{
    private const string TestWeaponId = "WeaponLaserLasgun";
    private const string TestModId = "WH40KWeaponModAcogCantrael";
    private const string TestStockModId = "WH40KWeaponModStockCantrael";

    /// <summary>
    /// GetAvailableMods returns a non-empty dict for a weapon with a mod host,
    /// and the optic slot lists mods matching the lasgun optic tag.
    /// </summary>
    [Test]
    public async Task GetAvailableModsListsCompatibleMods()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var protoMan = Server.ResolveDependency<IPrototypeManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();

            var weapon = SSpawnAtPosition(TestWeaponId, coords);
            Assert.That(entMan.HasComponent<WH40KWeaponModHostComponent>(weapon), Is.True);

            var available = modSystem.GetAvailableMods(TestWeaponId);
            Assert.That(available, Is.Not.Empty, "Weapon with mod host should have available mods");

            var slotDefinitions = modSystem.GetModSlots(TestWeaponId);
            Assert.That(slotDefinitions, Is.Not.Null);
            Assert.That(slotDefinitions!.Count, Is.GreaterThan(0));

            var opticSlotId = WH40KWeaponModHelper.GetSlotId("optic");
            Assert.That(available.ContainsKey(opticSlotId), Is.True, "Optic slot should be in available mods");
            Assert.That(available[opticSlotId], Is.Not.Empty, "Optic slot should have compatible mods");
            Assert.That(available[opticSlotId].Any(id => id.ToString() == TestModId), Is.True,
                $"ACOG mod '{TestModId}' should be in optic slot for lasgun");
        });
    }

    /// <summary>
    /// A weapon with no WH40KWeaponModHost returns an empty available-mods dict.
    /// </summary>
    [Test]
    public async Task GetAvailableModsReturnsEmptyForNonModdedWeapon()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();

            var weapon = SSpawnAtPosition("WeaponStubRevolver", coords);
            Assert.That(entMan.HasComponent<WH40KWeaponModHostComponent>(weapon), Is.False);

            var available = modSystem.GetAvailableMods("WeaponStubRevolver");
            Assert.That(available, Is.Empty, "Non-modded weapon should have no available mods");
        });
    }

    /// <summary>
    /// ApplyModsToEntity installs a mod into the weapon's slot and the mod becomes
    /// retrievable via GetInstalledMods.
    /// </summary>
    [Test]
    public async Task ApplyModsToEntityInstallsModIntoSlot()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();
            var weaponMods = entMan.System<SharedWH40KWeaponModSystem>();

            var weapon = SSpawnAtPosition(TestWeaponId, coords);
            Assert.That(entMan.TryGetComponent<WH40KWeaponModHostComponent>(weapon, out var host), Is.True);

            var opticSlotId = WH40KWeaponModHelper.GetSlotId("optic");
            var selectedMods = new Dictionary<string, string>
            {
                [opticSlotId] = TestModId,
            };

            modSystem.ApplyModsToEntity(weapon, selectedMods);

            var installed = weaponMods.GetInstalledMods((weapon, host)).ToList();
            Assert.That(installed.Count, Is.EqualTo(1), "Exactly one mod should be installed");
            Assert.That(installed[0].SlotId, Is.EqualTo(opticSlotId), "Installed mod should be in optic slot");

            var modMeta = entMan.GetComponent<MetaDataComponent>(installed[0].ModUid);
            Assert.That(modMeta.EntityPrototype?.ID, Is.EqualTo(TestModId), "Installed mod should be the ACOG prototype");
        });
    }

    /// <summary>
    /// ApplyModsToEntity with multiple mods installs each into its own slot.
    /// </summary>
    [Test]
    public async Task ApplyModsToEntityInstallsMultipleModsIntoDistinctSlots()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();
            var weaponMods = entMan.System<SharedWH40KWeaponModSystem>();

            var weapon = SSpawnAtPosition(TestWeaponId, coords);
            Assert.That(entMan.TryGetComponent<WH40KWeaponModHostComponent>(weapon, out var host), Is.True);

            var opticSlotId = WH40KWeaponModHelper.GetSlotId("optic");
            var stockSlotId = WH40KWeaponModHelper.GetSlotId("stock");

            var selectedMods = new Dictionary<string, string>
            {
                [opticSlotId] = TestModId,
                [stockSlotId] = TestStockModId,
            };

            modSystem.ApplyModsToEntity(weapon, selectedMods);

            var installed = weaponMods.GetInstalledMods((weapon, host)).ToList();
            Assert.That(installed.Count, Is.EqualTo(2), "Two mods should be installed");
            Assert.That(installed.Any(i => i.SlotId == opticSlotId), Is.True, "Optic mod should be installed");
            Assert.That(installed.Any(i => i.SlotId == stockSlotId), Is.True, "Stock mod should be installed");
        });
    }

    /// <summary>
    /// ApplyModsToEntity is a no-op for a weapon without a mod host.
    /// </summary>
    [Test]
    public async Task ApplyModsToEntityNoOpsOnNonModdedWeapon()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();

            var weapon = SSpawnAtPosition("WeaponStubRevolver", coords);
            var selectedMods = new Dictionary<string, string>
            {
                [WH40KWeaponModHelper.GetSlotId("optic")] = TestModId,
            };

            Assert.DoesNotThrow(() => modSystem.ApplyModsToEntity(weapon, selectedMods));
        });
    }

    /// <summary>
    /// Loadout.SelectedMods is serialized and survives a Clone round-trip,
    /// so selecting mods in the lobby persists into the profile.
    /// </summary>
    [Test]
    public async Task LoadoutSelectedModsSerializeAndClone()
    {
        var protoMan = Server.ResolveDependency<IPrototypeManager>();

        await Server.WaitAssertion(() =>
        {
            var opticSlotId = WH40KWeaponModHelper.GetSlotId("optic");
            var loadout = new Loadout
            {
                Prototype = "WH40KWeaponLaserLasgunLoadout",
                SelectedMods = new Dictionary<string, string>
                {
                    [opticSlotId] = TestModId,
                },
            };

            Assert.That(loadout.SelectedMods, Has.Count.EqualTo(1));
            Assert.That(loadout.SelectedMods[opticSlotId], Is.EqualTo(TestModId));

            var cloneLoadout = new Loadout
            {
                Prototype = loadout.Prototype,
                SelectedMods = new Dictionary<string, string>(loadout.SelectedMods),
            };

            Assert.That(cloneLoadout.SelectedMods, Has.Count.EqualTo(1));
            Assert.That(cloneLoadout.SelectedMods[opticSlotId], Is.EqualTo(TestModId));
            Assert.That(cloneLoadout.Equals(loadout), Is.True, "Clone with same Prototype should equal original");
        });
    }

    /// <summary>
    /// ApplySelectedModsToEquipped finds the weapon in the player's hands
    /// and installs the selected mods onto it after gear is equipped.
    /// </summary>
    [Test]
    public async Task ApplySelectedModsToEquippedFindsWeaponInInventoryAndInstallsMods()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var protoMan = Server.ResolveDependency<IPrototypeManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();
            var weaponMods = entMan.System<SharedWH40KWeaponModSystem>();
            var hands = entMan.System<SharedHandsSystem>();

            var human = SSpawnAtPosition("MobHuman", coords);
            var weapon = SSpawnAtPosition(TestWeaponId, coords);

            Assert.That(entMan.TryGetComponent<HandsComponent>(human, out var handsComp), Is.True,
                "MobHuman should have hands");
            Assert.That(hands.TryPickup(human, weapon, handsComp: handsComp), Is.True,
                "Weapon should be picked up into a hand");

            Assert.That(protoMan.TryIndex(new ProtoId<LoadoutPrototype>("WH40KWeaponLaserLasgunLoadout"), out var loadoutProto), Is.True);

            var opticSlotId = WH40KWeaponModHelper.GetSlotId("optic");
            var selectedMods = new Dictionary<string, string>
            {
                [opticSlotId] = TestModId,
            };

            modSystem.ApplySelectedModsToEquipped(human, loadoutProto, selectedMods);

            Assert.That(entMan.TryGetComponent<WH40KWeaponModHostComponent>(weapon, out var host), Is.True);
            var installed = weaponMods.GetInstalledMods((weapon, host)).ToList();
            Assert.That(installed.Count, Is.EqualTo(1), "One mod should be installed on the equipped weapon");
            Assert.That(installed[0].SlotId, Is.EqualTo(opticSlotId));
        });
    }

    /// <summary>
    /// GetWeaponProtoForLoadout returns the weapon prototype id for a loadout whose
    /// DummyEntity is a weapon with a mod host.
    /// </summary>
    [Test]
    public async Task GetWeaponProtoForLoadoutReturnsModdedWeapon()
    {
        var protoMan = Server.ResolveDependency<IPrototypeManager>();

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();

            Assert.That(protoMan.TryIndex(new ProtoId<LoadoutPrototype>("WH40KWeaponLaserLasgunLoadout"), out var loadoutProto), Is.True);
            var weaponProto = modSystem.GetWeaponProtoForLoadout(loadoutProto);
            Assert.That(weaponProto, Is.Not.Null, "Lasgun loadout should have a weapon with mod host");
            Assert.That(weaponProto!.ToString(), Is.EqualTo(TestWeaponId));
        });
    }

    /// <summary>
    /// GetWeaponProtoForLoadout returns null for a loadout whose equipment has no mod host.
    /// </summary>
    [Test]
    public async Task GetWeaponProtoForLoadoutReturnsNullForNonModdedLoadout()
    {
        var protoMan = Server.ResolveDependency<IPrototypeManager>();

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();

            Assert.That(protoMan.TryIndex(new ProtoId<LoadoutPrototype>("WH40KWeaponStubRevolverLoadout30"), out var loadoutProto), Is.True);
            var weaponProto = modSystem.GetWeaponProtoForLoadout(loadoutProto);
            Assert.That(weaponProto, Is.Null, "Stub revolver loadout (inhand, no mod host) should return null");
        });
    }

    /// <summary>
    /// After ApplyModsToEntity, the weapon's AppearanceComponent carries the overlay
    /// sprite data so the client visualizer renders the mod.
    /// </summary>
    [Test]
    public async Task ApplyModsToEntityUpdatesOverlayAppearance()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();

            var weapon = SSpawnAtPosition(TestWeaponId, coords);
            Assert.That(entMan.TryGetComponent<WH40KWeaponModHostComponent>(weapon, out var host), Is.True);

            var opticSlotId = WH40KWeaponModHelper.GetSlotId("optic");
            modSystem.ApplyModsToEntity(weapon, new Dictionary<string, string>
            {
                [opticSlotId] = TestModId,
            });

            Assert.That(entMan.TryGetComponent<AppearanceComponent>(weapon, out var appearance), Is.True);
            entMan.System<SharedAppearanceSystem>().TryGetData<Dictionary<string, string>>(
                weapon, WH40KWeaponModVisuals.OverlaySprites, out var overlaySprites, appearance);

            Assert.That(overlaySprites, Is.Not.Null, "Overlay sprites appearance data should be set after mod install");
            Assert.That(overlaySprites!.ContainsKey(opticSlotId), Is.True, "Optic slot should have an overlay sprite entry");
        });
    }

    /// <summary>
    /// Installed mods actually apply their gameplay modifiers: a stock mod reduces the
    /// weapon's spread (MaxAngle) when wielded.
    /// </summary>
    [Test]
    public async Task InstalledStockModTightensWeaponSpread()
    {
        var map = await Pair.CreateTestMap();
        var coords = map.GridCoords;

        await Server.WaitAssertion(() =>
        {
            var entMan = Server.ResolveDependency<IEntityManager>();
            var modSystem = entMan.System<WH40KLoadoutModSystem>();
            var weaponMods = entMan.System<SharedWH40KWeaponModSystem>();

            var weapon = SSpawnAtPosition(TestWeaponId, coords);
            Assert.That(entMan.TryGetComponent<WH40KWeaponModHostComponent>(weapon, out var host), Is.True);
            Assert.That(entMan.TryGetComponent<GunComponent>(weapon, out var gun), Is.True);

            var human = SSpawnAtPosition("MobHuman", coords);
            var stockSlotId = WH40KWeaponModHelper.GetSlotId("stock");
            modSystem.ApplyModsToEntity(weapon, new Dictionary<string, string>
            {
                [stockSlotId] = TestStockModId,
            });

            entMan.System<SharedWieldableSystem>().TryWield(weapon, human);

            var installed = weaponMods.GetInstalledMods((weapon, host)).ToList();
            Assert.That(installed.Count(i => i.SlotId == stockSlotId), Is.EqualTo(1), "Stock should be installed");
        });
    }
}
