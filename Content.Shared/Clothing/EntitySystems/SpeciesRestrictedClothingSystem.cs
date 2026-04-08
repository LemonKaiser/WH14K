using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Shared.Clothing.EntitySystems;

public sealed class SpeciesRestrictedClothingSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<SpeciesRestrictedClothingComponent, BeingEquippedAttemptEvent>(OnBeingEquippedAttempt);
    }

    private void OnBeingEquippedAttempt(Entity<SpeciesRestrictedClothingComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (TryComp<ClothingComponent>(ent, out var clothing) && (clothing.Slots & args.SlotFlags) == SlotFlags.NONE)
            return;

        if (TryComp<HumanoidProfileComponent>(args.EquipTarget, out var profile) && ent.Comp.Species.Contains(profile.Species))
            return;

        args.Reason = ent.Comp.Popup;
        args.Cancel();
    }
}