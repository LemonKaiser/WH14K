using Content.Server.GameTicking.Rules.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.MurderMystery;

public sealed partial class WH40KMurderMysteryRuleSystem
{
    /// <summary>
    /// Replaces the fixed job startingGear with random clothing drawn from the
    /// rule's pools. Strips the job-issued slots first so the random picks can
    /// actually occupy them (see lessons.md - Gun Game occupied-slot bug).
    /// </summary>
    private void EquipRandomClothing(EntityUid mob, WH40KMurderMysteryRuleComponent rule)
    {
        EntityUid? savedPda = null;
        if (_inventory.TryGetSlotEntity(mob, "id", out var pda))
            savedPda = pda;

        foreach (var slot in JobIssuedClothingSlots)
            StripSlot(mob, slot);

        if (rule.JumpsuitPool.Count > 0)
            TryEquipRandom(mob, rule.JumpsuitPool, "jumpsuit");

        if (savedPda is { } savedPdaEnt && !TerminatingOrDeleted(savedPdaEnt))
            _inventory.TryEquip(mob, savedPdaEnt, "id", force: true, silent: true);

        if (rule.ShoesPool.Count > 0)
            TryEquipRandom(mob, rule.ShoesPool, "shoes");

        if (rule.BackPool.Count > 0)
            TryEquipRandom(mob, rule.BackPool, "back");

        if (rule.GlassesPool.Count > 0 && _random.Prob(rule.GlassesChance))
            TryEquipRandom(mob, rule.GlassesPool, "eyes");

        if (rule.HeadPool.Count > 0 && _random.Prob(rule.HeadChance))
            TryEquipRandom(mob, rule.HeadPool, "head");

        if (rule.GlovesPool.Count > 0 && _random.Prob(rule.GlovesChance))
            TryEquipRandom(mob, rule.GlovesPool, "gloves");

        if (rule.MaskPool.Count > 0 && _random.Prob(rule.MaskChance))
            TryEquipRandom(mob, rule.MaskPool, "mask");

        if (rule.OuterClothingPool.Count > 0 && _random.Prob(rule.OuterClothingChance))
            TryEquipRandom(mob, rule.OuterClothingPool, "outerClothing");
    }

    private static readonly string[] JobIssuedClothingSlots =
    {
        "jumpsuit",
        "shoes",
        "eyes",
        "head",
        "gloves",
        "mask",
        "outerClothing",
        "neck",
        "belt",
    };

    private void StripSlot(EntityUid mob, string slot)
    {
        if (!_inventory.TryGetSlotEntity(mob, slot, out var existing) || existing == null)
            return;

        _inventory.TryUnequip(mob, slot, force: true, silent: true);
        Del(existing.Value);
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
            QueueDel(item);
    }
}
