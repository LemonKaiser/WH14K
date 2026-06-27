using Content.Server.GameTicking.Rules.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Interaction.Components;
using Content.Shared.PDA;
using Content.Shared._WH40K.GunGame;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    private void EquipRandomClothing(EntityUid mob, WH40KGunGameRuleComponent rule)
    {
        EntityUid? savedPda = null;
        if (_inventory.TryGetSlotEntity(mob, "id", out var pda))
            savedPda = pda;

        if (rule.JumpsuitPool.Count > 0)
            TryEquipRandom(mob, rule.JumpsuitPool, "jumpsuit");

        if (savedPda is { } savedPdaEnt && !TerminatingOrDeleted(savedPdaEnt))
            _inventory.TryEquip(mob, savedPdaEnt, "id", force: true, silent: true);

        if (rule.ShoesPool.Count > 0)
            TryEquipRandom(mob, rule.ShoesPool, "shoes");

        if (rule.GlassesPool.Count > 0 && _random.Prob(rule.GlassesChance))
            TryEquipRandom(mob, rule.GlassesPool, "eyes");

        if (rule.HeadPool.Count > 0 && _random.Prob(rule.HeadChance))
            TryEquipRandom(mob, rule.HeadPool, "head");

        if (rule.GlovesPool.Count > 0 && _random.Prob(rule.GlovesChance))
            TryEquipRandom(mob, rule.GlovesPool, "gloves");

        if (rule.BackPool.Count > 0)
            TryEquipRandom(mob, rule.BackPool, "back");

        ProtectEquippedInventory(mob);
    }

    private void TryEquipRandom(EntityUid mob, List<EntProtoId> pool, string slot)
    {
        if (_inventory.TryGetSlotEntity(mob, slot, out var existing) && existing != null)
        {
            _inventory.TryUnequip(mob, slot, force: true, silent: true);
            Del(existing.Value);
        }

        var protoId = _random.Pick(pool);
        var item = Spawn(protoId, _transform.GetMapCoordinates(mob));

        if (!_inventory.TryEquip(mob, item, slot, force: true, silent: true))
        {
            QueueDel(item);
            return;
        }
    }

    private void ProtectEquippedInventory(EntityUid mob)
    {
        if (!TryComp<InventoryComponent>(mob, out var inventory))
            return;

        foreach (var slot in inventory.Slots)
        {
            if (!_inventory.TryGetSlotEntity(mob, slot.Name, out var item, inventory) || item == null)
                continue;

            if (slot.Name is not ("id" or "ears") && !HasComp<ClothingComponent>(item.Value))
                continue;

            ProtectEquippedItem(item.Value);
        }
    }

    private void ProtectEquippedItem(EntityUid item)
    {
        var unremovable = EnsureComp<UnremoveableComponent>(item);
        unremovable.DeleteOnDrop = false;

        var gunGameLock = EnsureComp<WH40KGunGameLockedComponent>(item);
        gunGameLock.BlockInteractUsing = true;
        Dirty(item, gunGameLock);

        ProtectPdaContents(item);
    }

    private void ProtectPdaContents(EntityUid item)
    {
        if (!HasComp<PdaComponent>(item))
            return;

        ProtectPdaSlotItem(item, PdaComponent.PdaIdSlotId);
        ProtectPdaSlotItem(item, PdaComponent.PdaPenSlotId);
        ProtectPdaSlotItem(item, PdaComponent.PdaPaiSlotId);
    }

    private void ProtectPdaSlotItem(EntityUid pda, string slotId)
    {
        if (!_itemSlots.TryGetSlot(pda, slotId, out var slot) || slot.Item is not { } contained)
            return;

        var unremovable = EnsureComp<UnremoveableComponent>(contained);
        unremovable.DeleteOnDrop = false;
    }
}
