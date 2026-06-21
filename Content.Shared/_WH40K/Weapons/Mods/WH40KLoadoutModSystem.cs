using System.Linq;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Weapons.Mods;

/// <summary>
/// Bridges the loadout system with the weapon-mod system.
/// Provides the data and the entity-mutation logic used by the lobby UI
/// (the gear button on weapon loadouts) and by the spawn pipeline
/// (so selected mods are installed on the real weapon when a player joins).
/// </summary>
public sealed partial class WH40KLoadoutModSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedWH40KWeaponModSystem _weaponMods = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>
    /// Returns the weapon entity prototype id that a loadout equips, or null if the loadout
    /// does not equip a weapon with a mod host.
    /// Checks <see cref="LoadoutPrototype.DummyEntity"/> first (reliable for weapon loadouts),
    /// then falls back to the first equipment entry.
    /// </summary>
    public EntProtoId? GetWeaponProtoForLoadout(LoadoutPrototype loadout)
    {
        if (loadout.DummyEntity is { } dummy && HasModHost(dummy))
            return dummy;

        foreach (var (_, ent) in loadout.Equipment)
        {
            if (HasModHost(ent))
                return ent;
        }

        return null;
    }

    /// <summary>
    /// Returns true if the given entity prototype has a <see cref="WH40KWeaponModHostComponent"/>.
    /// </summary>
    public bool HasModHost(EntProtoId protoId)
    {
        if (!_prototype.TryIndex(protoId, out var proto))
            return false;

        return proto.TryGetComponent<WH40KWeaponModHostComponent>(out _, _compFactory);
    }

    /// <summary>
    /// Returns the slot definitions of the weapon's mod host, or null if the weapon has no host.
    /// </summary>
    public List<WH40KWeaponModSlotDefinition>? GetModSlots(EntProtoId weaponProto)
    {
        if (!_prototype.TryIndex(weaponProto, out var proto))
            return null;

        if (!proto.TryGetComponent<WH40KWeaponModHostComponent>(out var host, _compFactory))
            return null;

        return host.SlotDefinitions;
    }

    /// <summary>
    /// Returns, for each slot definition on the weapon, the list of mod prototype ids that
    /// are compatible with that slot (matching the slot's whitelist tags + slot type).
    /// Mods are discovered by scanning all entity prototypes that have a <see cref="WH40KWeaponModComponent"/>
    /// and at least one of the whitelist tags.
    /// </summary>
    public Dictionary<string, List<EntProtoId>> GetAvailableMods(EntProtoId weaponProto)
    {
        var result = new Dictionary<string, List<EntProtoId>>();
        var slots = GetModSlots(weaponProto);
        if (slots == null)
            return result;

        var allMods = new List<(string Id, WH40KWeaponModComponent Mod, HashSet<ProtoId<TagPrototype>> Tags)>();
        foreach (var p in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!p.TryGetComponent<WH40KWeaponModComponent>(out var mod, _compFactory))
                continue;

            if (!p.TryGetComponent<TagComponent>(out var modTags, _compFactory))
                continue;

            allMods.Add((p.ID, mod, modTags.Tags));
        }

        foreach (var slot in slots)
        {
            var slotId = WH40KWeaponModHelper.GetSlotId(slot.Id);
            var compatible = new List<EntProtoId>();

            if (slot.Whitelist?.Tags is { } tags)
            {
                var tagSet = new HashSet<ProtoId<TagPrototype>>(tags);
                foreach (var (id, mod, modTags) in allMods)
                {
                    if (mod.SlotType != slot.SlotType)
                        continue;

                    if (modTags.Overlaps(tagSet))
                        compatible.Add(new EntProtoId(id));
                }
            }

            result[slotId] = compatible;
        }

        return result;
    }

    /// <summary>
    /// Installs the given mods (slot id -> mod proto id) onto an already-spawned weapon entity.
    /// Silently skips slots that don't exist or mods that fail to insert.
    /// Used both by the lobby preview (nullspace dummy) and by the real spawn pipeline.
    /// </summary>
    public void ApplyModsToEntity(EntityUid weapon, Dictionary<string, string> selectedMods)
    {
        if (!TryComp<WH40KWeaponModHostComponent>(weapon, out var host))
            return;

        foreach (var (slotId, modProtoId) in selectedMods)
        {
            if (!host.ModSlots.TryGetValue(slotId, out var slot))
                continue;

            if (slot.Item != null)
                continue;

            var modEntity = Spawn(modProtoId, Transform(weapon).Coordinates);
            _itemSlots.TryInsert(weapon, slot, modEntity, null);
        }

        _weaponMods.RefreshHost(weapon, host);
    }

    /// <summary>
    /// Called by <see cref="SharedStationSpawningSystem.EquipRoleLoadout"/> right after a loadout's
    /// gear has been equipped onto a player. Finds the weapon (in inventory or hands) that matches
    /// the loadout's weapon prototype and installs the selected mods onto it.
    /// </summary>
    public void ApplySelectedModsToEquipped(EntityUid player, LoadoutPrototype loadoutProto, Dictionary<string, string> selectedMods)
    {
        if (selectedMods.Count == 0)
            return;

        var weaponProto = GetWeaponProtoForLoadout(loadoutProto);
        if (weaponProto == null)
            return;

        var weaponUid = FindEquippedWeapon(player, weaponProto.Value);
        if (weaponUid == null)
            return;

        ApplyModsToEntity(weaponUid.Value, selectedMods);
    }

    /// <summary>
    /// Searches the player's inventory slots and hands for an entity whose prototype id matches
    /// the given weapon prototype id and has a <see cref="WH40KWeaponModHostComponent"/>.
    /// </summary>
    private EntityUid? FindEquippedWeapon(EntityUid player, EntProtoId weaponProto)
    {
        var protoId = weaponProto.ToString();

        if (TryComp<InventoryComponent>(player, out var inventory))
        {
            var slotEnumerator = _inventory.GetSlotEnumerator((player, inventory));
            while (slotEnumerator.MoveNext(out _, out var slotDef))
            {
                if (slotDef == null)
                    continue;

                if (_inventory.TryGetSlotEntity(player, slotDef.Name, out var slotEnt, inventoryComponent: inventory))
                {
                    if (MatchWeapon(slotEnt.Value, protoId))
                        return slotEnt;
                }
            }
        }

        if (TryComp<HandsComponent>(player, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld((player, hands)))
            {
                if (MatchWeapon(held, protoId))
                    return held;
            }
        }

        return null;
    }

    private bool MatchWeapon(EntityUid ent, string protoId)
    {
        if (!TryComp<WH40KWeaponModHostComponent>(ent, out _))
            return false;

        var meta = MetaData(ent);
        return meta.EntityPrototype?.ID == protoId;
    }
}
