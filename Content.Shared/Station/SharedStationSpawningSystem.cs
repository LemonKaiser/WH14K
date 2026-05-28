using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Collections;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared.Station;

public abstract partial class SharedStationSpawningSystem : EntitySystem
{
    [Dependency] protected IPrototypeManager PrototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] protected InventorySystem InventorySystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedTransformSystem _xformSystem = default!;

    [Dependency] private EntityQuery<HandsComponent> _handsQuery = default!;
    [Dependency] private EntityQuery<InventoryComponent> _inventoryQuery = default!;
    [Dependency] private EntityQuery<StorageComponent> _storageQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    /// <summary>
    ///     Equips the data from a `RoleLoadout` onto an entity.
    /// </summary>
    public void EquipRoleLoadout(EntityUid entity, RoleLoadout loadout, RoleLoadoutPrototype roleProto)
    {
        foreach (var items in EnumerateValidSelectedLoadouts(loadout, roleProto))
        {
            if (!PrototypeManager.TryIndex(items.Prototype, out var loadoutProto))
            {
                Log.Error($"Unable to find loadout prototype for {items.Prototype}");
                continue;
            }

            EquipStartingGear(entity, loadoutProto, raiseEvent: false);
        }

        EquipRoleName(entity, loadout, roleProto);
    }

    public HashSet<string> GetLoadoutEquipmentOverrides(RoleLoadout? loadout, RoleLoadoutPrototype? roleProto)
    {
        var overriddenSlots = new HashSet<string>();

        if (loadout == null || roleProto == null)
            return overriddenSlots;

        foreach (var items in EnumerateValidSelectedLoadouts(loadout, roleProto))
        {
            if (!PrototypeManager.TryIndex(items.Prototype, out var loadoutProto))
                continue;

            CollectEquipmentSlots(overriddenSlots, loadoutProto.StartingGear);
            CollectEquipmentSlots(overriddenSlots, (IEquipmentLoadout) loadoutProto);
        }

        return overriddenSlots;
    }

    private static IEnumerable<Loadout> EnumerateValidSelectedLoadouts(RoleLoadout loadout, RoleLoadoutPrototype roleProto)
    {
        foreach (var groupId in roleProto.Groups)
        {
            if (!loadout.SelectedLoadouts.TryGetValue(groupId, out var selections))
                continue;

            foreach (var selection in selections)
            {
                yield return selection;
            }
        }
    }

    /// <summary>
    /// Applies the role's name as applicable to the entity.
    /// </summary>
    public void EquipRoleName(EntityUid entity, RoleLoadout loadout, RoleLoadoutPrototype roleProto)
    {
        string? name = null;

        if (roleProto.CanCustomizeName)
        {
            name = loadout.EntityName;
        }

        if (string.IsNullOrEmpty(name) && PrototypeManager.Resolve(roleProto.NameDataset, out var nameData))
        {
            name = Loc.GetString(_random.Pick(nameData.Values));
        }

        if (!string.IsNullOrEmpty(name))
        {
            _metadata.SetEntityName(entity, name);
        }
    }

    public void EquipStartingGear(EntityUid entity, LoadoutPrototype loadout, bool raiseEvent = true)
    {
        EquipStartingGear(entity, loadout.StartingGear, raiseEvent);
        EquipStartingGear(entity, (IEquipmentLoadout) loadout, raiseEvent);
    }

    /// <summary>
    /// <see cref="EquipStartingGear(Robust.Shared.GameObjects.EntityUid,System.Nullable{Robust.Shared.Prototypes.ProtoId{Content.Shared.Roles.StartingGearPrototype}},bool)"/>
    /// </summary>
    public void EquipStartingGear(EntityUid entity, ProtoId<StartingGearPrototype>? startingGear, bool raiseEvent = true)
    {
        PrototypeManager.Resolve(startingGear, out var gearProto);
        EquipStartingGear(entity, gearProto, raiseEvent);
    }

    public void EquipStartingGear(EntityUid entity, ProtoId<StartingGearPrototype>? startingGear, ISet<string>? excludedSlots, bool raiseEvent = true)
    {
        PrototypeManager.Resolve(startingGear, out var gearProto);
        EquipStartingGear(entity, gearProto, excludedSlots, raiseEvent);
    }

    /// <summary>
    /// <see cref="EquipStartingGear(Robust.Shared.GameObjects.EntityUid,System.Nullable{Robust.Shared.Prototypes.ProtoId{Content.Shared.Roles.StartingGearPrototype}},bool)"/>
    /// </summary>
    public void EquipStartingGear(EntityUid entity, StartingGearPrototype? startingGear, bool raiseEvent = true)
    {
        EquipStartingGear(entity, (IEquipmentLoadout?) startingGear, raiseEvent);
    }

    public void EquipStartingGear(EntityUid entity, StartingGearPrototype? startingGear, ISet<string>? excludedSlots, bool raiseEvent = true)
    {
        EquipStartingGear(entity, (IEquipmentLoadout?) startingGear, excludedSlots, raiseEvent);
    }

    /// <summary>
    /// Equips starting gear onto the given entity.
    /// </summary>
    /// <param name="entity">Entity to load out.</param>
    /// <param name="startingGear">Starting gear to use.</param>
    /// <param name="raiseEvent">Should we raise the event for equipped. Set to false if you will call this manually</param>
    public void EquipStartingGear(EntityUid entity, IEquipmentLoadout? startingGear, bool raiseEvent = true)
    {
        EquipStartingGear(entity, startingGear, null, raiseEvent);
    }

    public void EquipStartingGear(EntityUid entity, IEquipmentLoadout? startingGear, ISet<string>? excludedSlots, bool raiseEvent = true)
    {
        if (startingGear == null)
            return;

        var xform = _xformQuery.GetComponent(entity);

        if (InventorySystem.TryGetSlots(entity, out var slotDefinitions))
        {
            foreach (var slot in slotDefinitions)
            {
                if (excludedSlots != null && excludedSlots.Contains(slot.Name))
                    continue;

                var equipmentStr = startingGear.GetGear(slot.Name);
                if (!string.IsNullOrEmpty(equipmentStr))
                {
                    var equipmentEntity = Spawn(equipmentStr, xform.Coordinates);
                    InventorySystem.TryEquip(entity, equipmentEntity, slot.Name, silent: true, force: true);
                }
            }
        }

        if (_handsQuery.TryComp(entity, out var handsComponent))
        {
            var inhand = startingGear.Inhand;
            var coords = xform.Coordinates;
            foreach (var prototype in inhand)
            {
                var inhandEntity = Spawn(prototype, coords);

                if (_handsSystem.TryGetEmptyHand((entity, handsComponent), out var emptyHand))
                {
                    _handsSystem.TryPickup(entity, inhandEntity, emptyHand, checkActionBlocker: false, handsComp: handsComponent);
                }
            }
        }

        if (startingGear.Storage.Count > 0)
        {
            var coords = _xformSystem.GetMapCoordinates(entity);
            _inventoryQuery.TryComp(entity, out var inventoryComp);

            foreach (var (slotName, entProtos) in startingGear.Storage)
            {
                if (entProtos == null || entProtos.Count == 0)
                    continue;

                if (inventoryComp != null &&
                    InventorySystem.TryGetSlotEntity(entity, slotName, out var slotEnt, inventoryComponent: inventoryComp) &&
                    _storageQuery.TryComp(slotEnt, out var storage))
                {
                    foreach (var entProto in entProtos)
                    {
                        var spawnedEntity = Spawn(entProto, coords);
                        if (_storage.Insert(slotEnt.Value, spawnedEntity, out _, storageComp: storage, playSound: false))
                            continue;

                        Del(spawnedEntity);
                        InventorySystem.SpawnItemOnEntity(entity, entProto);
                    }

                    continue;
                }

                foreach (var entProto in entProtos)
                {
                    InventorySystem.SpawnItemOnEntity(entity, entProto);
                }
            }
        }

        if (raiseEvent)
        {
            var ev = new StartingGearEquippedEvent(entity);
            RaiseLocalEvent(entity, ref ev);
        }
    }

    private void CollectEquipmentSlots(HashSet<string> slots, ProtoId<StartingGearPrototype>? gearId)
    {
        if (!PrototypeManager.Resolve(gearId, out var gearProto))
            return;

        CollectEquipmentSlots(slots, (IEquipmentLoadout) gearProto);
    }

    private static void CollectEquipmentSlots(HashSet<string> slots, IEquipmentLoadout gear)
    {
        foreach (var slot in gear.Equipment.Keys)
        {
            slots.Add(slot);
        }
    }

    /// <summary>
    ///     Gets all the gear for a given slot when passed a loadout.
    /// </summary>
    /// <param name="loadout">The loadout to look through.</param>
    /// <param name="slot">The slot that you want the clothing for.</param>
    /// <returns>
    ///     If there is a value for the given slot, it will return the proto id for that slot.
    ///     If nothing was found, will return null
    /// </returns>
    public string? GetGearForSlot(RoleLoadout? loadout, string slot)
    {
        if (loadout == null)
            return null;

        foreach (var group in loadout.SelectedLoadouts)
        {
            foreach (var items in group.Value)
            {
                if (!PrototypeManager.Resolve(items.Prototype, out var loadoutPrototype))
                    return null;

                var gear = ((IEquipmentLoadout) loadoutPrototype).GetGear(slot);
                if (gear != string.Empty)
                    return gear;
            }
        }

        return null;
    }
}
