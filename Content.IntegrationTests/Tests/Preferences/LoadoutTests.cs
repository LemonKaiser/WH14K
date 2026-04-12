using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.Station.Systems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class LoadoutTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: playTimeTracker
  id: PlayTimeLoadoutTester

- type: playTimeTracker
  id: PlayTimeLoadoutTesterStorage

- type: playTimeTracker
  id: PlayTimeLoadoutTesterFallback

- type: playTimeTracker
  id: PlayTimeLoadoutTesterReplace

- type: loadout
  id: TestJumpsuit
  equipment:
    jumpsuit: ClothingUniformJumpsuitColorGrey

- type: loadoutGroup
  id: LoadoutTesterJumpsuit
  name: generic-unknown
  loadouts:
  - TestJumpsuit

- type: roleLoadout
  id: JobLoadoutTester
  groups:
  - LoadoutTesterJumpsuit

- type: job
  id: LoadoutTester
  playTimeTracker: PlayTimeLoadoutTester

- type: startingGear
  id: LoadoutTesterStorageGear
  equipment:
    belt: guardbelt

- type: loadout
  id: TestBeltStorageItem
  storage:
    belt:
    - Cigarette

- type: loadoutGroup
  id: LoadoutTesterStorageGroup
  name: generic-unknown
  minLimit: 1
  loadouts:
  - TestBeltStorageItem

- type: roleLoadout
  id: JobLoadoutTesterStorage
  groups:
  - LoadoutTesterStorageGroup

- type: job
  id: LoadoutTesterStorage
  playTimeTracker: PlayTimeLoadoutTesterStorage
  startingGear: LoadoutTesterStorageGear

- type: startingGear
  id: LoadoutTesterFallbackGear
  equipment:
    belt: ClothingBeltUtilityEngineering
    back: ClothingBackpack

- type: loadout
  id: TestBeltFallbackItem
  storage:
    belt:
    - CombatKnife

- type: loadoutGroup
  id: LoadoutTesterFallbackGroup
  name: generic-unknown
  minLimit: 1
  loadouts:
  - TestBeltFallbackItem

- type: roleLoadout
  id: JobLoadoutTesterFallback
  groups:
  - LoadoutTesterFallbackGroup

- type: job
  id: LoadoutTesterFallback
  playTimeTracker: PlayTimeLoadoutTesterFallback
  startingGear: LoadoutTesterFallbackGear

- type: startingGear
  id: LoadoutTesterReplaceGear
  equipment:
    gloves: ClothingHandsGlovesColorWhite

- type: loadout
  id: TestGloveReplacement
  equipment:
    gloves: ClothingHandsGlovesColorBlack

- type: loadoutGroup
  id: LoadoutTesterReplaceGroup
  name: generic-unknown
  minLimit: 1
  loadouts:
  - TestGloveReplacement

- type: roleLoadout
  id: JobLoadoutTesterReplace
  groups:
  - LoadoutTesterReplaceGroup

- type: job
  id: LoadoutTesterReplace
  playTimeTracker: PlayTimeLoadoutTesterReplace
  startingGear: LoadoutTesterReplaceGear
";

    private readonly Dictionary<string, EntProtoId> _expectedEquipment = new()
    {
        ["jumpsuit"] = "ClothingUniformJumpsuitColorGrey"
    };

    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
    };

    /// <summary>
    /// Checks that an empty loadout still spawns with default gear and not naked.
    /// </summary>
    [Test]
    public async Task TestEmptyLoadout()
    {
        var pair = Pair;
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();

        // Check that an empty role loadout spawns gear
        var stationSystem = entManager.System<StationSpawningSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var profile = new HumanoidCharacterProfile();

            profile.SetLoadout(new RoleLoadout("LoadoutTester"));

            var tester = stationSystem.SpawnPlayerMob(testMap.GridCoords, job: "LoadoutTester", profile, station: null);

            var slotQuery = inventorySystem.GetSlotEnumerator(tester);
            var checkedCount = 0;
            while (slotQuery.NextItem(out var item, out var slot))
            {
                // Make sure the slot is valid
                Assert.That(_expectedEquipment.TryGetValue(slot.Name, out var expectedItem), $"Spawned item in unexpected slot: {slot.Name}");

                // Make sure that the item is the right one
                var meta = entManager.GetComponent<MetaDataComponent>(item);
                Assert.That(meta.EntityPrototype.ID, Is.EqualTo(expectedItem.Id), $"Spawned wrong item in slot {slot.Name}!");

                checkedCount++;
            }
            // Make sure the number of items is the same
            Assert.That(checkedCount, Is.EqualTo(_expectedEquipment.Count), "Number of items does not match expected!");

            entManager.DeleteEntity(tester);
        });
    }

    [Test]
    public async Task TestLoadoutStorageUsesStartingGearContainer()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings()
        {
            Dirty = true,
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var stationSystem = entManager.System<StationSpawningSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var profile = new HumanoidCharacterProfile();
            var tester = stationSystem.SpawnPlayerMob(testMap.GridCoords, job: "LoadoutTesterStorage", profile, station: null);

            Assert.That(HasDescendantWithPrototype(entManager, tester, "Cigarette"), Is.True,
                "Loadout storage items should use containers from job starting gear.");

            entManager.DeleteEntity(tester);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestLoadoutStorageFallsBackWhenTargetContainerRejectsItem()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings()
        {
            Dirty = true,
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var stationSystem = entManager.System<StationSpawningSystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var profile = new HumanoidCharacterProfile();
            var tester = stationSystem.SpawnPlayerMob(testMap.GridCoords, job: "LoadoutTesterFallback", profile, station: null);

            Assert.That(HasDescendantWithPrototype(entManager, tester, "CombatKnife"), Is.True,
                "Rejected loadout storage items should be moved into the rest of the inventory instead of being dropped or lost.");

            entManager.DeleteEntity(tester);
        });

        await pair.CleanReturnAsync();
    }

      [Test]
      public async Task TestLoadoutEquipmentReplacesJobStartingGearWithoutDroppingOldItem()
      {
        var pair = await PoolManager.GetServerClient(new PoolSettings()
        {
          Dirty = true,
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var stationSystem = entManager.System<StationSpawningSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
          var profile = new HumanoidCharacterProfile();
          profile.SetLoadout(new RoleLoadout("LoadoutTesterReplace"));

          var tester = stationSystem.SpawnPlayerMob(testMap.GridCoords, job: "LoadoutTesterReplace", profile, station: null);

          Assert.That(inventorySystem.TryGetSlotEntity(tester, "gloves", out var gloves), Is.True,
            "Expected replacement gloves to be equipped in the gloves slot.");

          Assert.That(entManager.GetComponent<MetaDataComponent>(gloves!.Value).EntityPrototype?.ID,
            Is.EqualTo("ClothingHandsGlovesColorBlack"),
            "Loadout gloves should replace the job starting gloves in-slot.");

          var testerMapId = entManager.GetComponent<TransformComponent>(tester).MapID;
          var whiteGloveCount = 0;
          var query = entManager.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
          while (query.MoveNext(out _, out var meta, out var xform))
          {
            if (xform.MapID != testerMapId)
              continue;

            if (meta.EntityPrototype?.ID == "ClothingHandsGlovesColorWhite")
              whiteGloveCount++;
          }

          Assert.That(whiteGloveCount, Is.EqualTo(0),
            "Default job gloves should not be spawned and dropped when a loadout overrides the same slot.");

          entManager.DeleteEntity(tester);
        });

        await pair.CleanReturnAsync();
      }

    private static bool HasDescendantWithPrototype(IEntityManager entManager, EntityUid root, string prototypeId)
    {
        var queue = new Queue<EntityUid>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current != root)
            {
                var meta = entManager.GetComponent<MetaDataComponent>(current);
                if (meta.EntityPrototype?.ID == prototypeId)
                    return true;
            }

            var children = entManager.GetComponent<TransformComponent>(current).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                queue.Enqueue(child);
            }
        }

        return false;
    }
}
