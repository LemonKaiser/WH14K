using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._WH40K.HeavyBolter;

/// <summary>
/// Prevents heavy-bolter magazine interactions while the emplacement is folded.
/// Runs on both client and server to suppress invalid prediction and server-side inserts.
/// </summary>
public sealed class SharedWH40KHeavyBolterFoldedInteractionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KHeavyBolterComponent, ItemSlotInsertAttemptEvent>(OnItemSlotInsertAttempt);
        SubscribeLocalEvent<WH40KHeavyBolterComponent, ItemSlotEjectAttemptEvent>(OnItemSlotEjectAttempt);
    }

    private void OnItemSlotInsertAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Slot.ID != SharedGunSystem.MagazineSlot)
            return;

        var interactable = IsInteractableForMagazine(bolter);
        if (interactable)
            return;

        args.Cancelled = true;
    }

    private void OnItemSlotEjectAttempt(Entity<WH40KHeavyBolterComponent> bolter, ref ItemSlotEjectAttemptEvent args)
    {
        if (args.Slot.ID != SharedGunSystem.MagazineSlot)
            return;

        var interactable = IsInteractableForMagazine(bolter);
        if (interactable)
            return;

        args.Cancelled = true;
    }

    private bool IsInteractableForMagazine(Entity<WH40KHeavyBolterComponent> bolter)
    {
        // Strap enabled is networked and tracks deployed operable state for client-side prediction too.
        if (TryComp<StrapComponent>(bolter, out var strap))
            return strap.Enabled;

        // Fallback for edge cases where strap component is not available yet.
        return bolter.Comp.Deployed;
    }
}
