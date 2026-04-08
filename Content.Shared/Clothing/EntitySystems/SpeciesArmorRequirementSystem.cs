using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Shared.Clothing.EntitySystems;

public sealed class SpeciesArmorRequirementSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<SpeciesArmorRequirementComponent, IsEquippingTargetAttemptEvent>(OnIsEquippingTargetAttempt);
    }

    private void OnIsEquippingTargetAttempt(Entity<SpeciesArmorRequirementComponent> ent, ref IsEquippingTargetAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<ArmorComponent>(args.Equipment, out _))
            return;

        if (TryComp<ClothingComponent>(args.Equipment, out var clothing) && (clothing.Slots & args.SlotFlags) == SlotFlags.NONE)
            return;

        if (!TryComp<HumanoidProfileComponent>(args.EquipTarget, out var profile))
        {
            args.Reason = ent.Comp.Popup;
            args.Cancel();
            return;
        }

        if (TryComp<SpeciesRestrictedClothingComponent>(args.Equipment, out var restricted) && restricted.Species.Contains(profile.Species))
            return;

        args.Reason = ent.Comp.Popup;
        args.Cancel();
    }
}
