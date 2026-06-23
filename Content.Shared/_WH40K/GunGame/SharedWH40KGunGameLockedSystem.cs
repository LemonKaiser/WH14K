using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Storage;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.GunGame;

public sealed partial class SharedWH40KGunGameLockedSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KGunGameLockedComponent, FellDownThrowAttemptEvent>(OnFellDownThrowAttempt);
        SubscribeLocalEvent<WH40KGunGameLockedComponent, ThrowItemAttemptEvent>(OnThrowItemAttempt);
        SubscribeLocalEvent<WH40KGunGameLockedComponent, ContainerGettingInsertedAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<HandsComponent, DropAttemptEvent>(OnDropAttempt);
    }

    private static void OnFellDownThrowAttempt(Entity<WH40KGunGameLockedComponent> ent, ref FellDownThrowAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private static void OnThrowItemAttempt(Entity<WH40KGunGameLockedComponent> ent, ref ThrowItemAttemptEvent args)
    {
        if (ent.Comp.BlockThrow)
            args.Cancelled = true;
    }

    private void OnInsertAttempt(Entity<WH40KGunGameLockedComponent> ent, ref ContainerGettingInsertedAttemptEvent args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            args.Cancel();
    }

    private void OnDropAttempt(EntityUid uid, HandsComponent comp, CancellableEntityEventArgs args)
    {
        if (_hands.TryGetActiveItem((uid, comp), out var activeItem)
            && HasComp<WH40KGunGameLockedComponent>(activeItem))
        {
            args.Cancel();
        }
    }
}
