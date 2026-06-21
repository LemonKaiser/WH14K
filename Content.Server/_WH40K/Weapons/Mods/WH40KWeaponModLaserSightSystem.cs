using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared._WH40K.Weapons.Mods;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Weapons.Mods;

public sealed partial class WH40KWeaponModLaserSightSystem : SharedWH40KWeaponModLaserSightSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, GotUnequippedHandEvent>(OnGotUnequippedHand);
        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, HandSelectedEvent>(OnHandSelected);
        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, HandDeselectedEvent>(OnHandDeselected);
        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, EntInsertedIntoContainerMessage>(OnInsertedIntoContainer);
        SubscribeLocalEvent<WH40KWeaponModLaserSightComponent, EntGotRemovedFromContainerMessage>(OnRemovedFromContainer);
    }

    private void OnGotEquippedHand(Entity<WH40KWeaponModLaserSightComponent> ent, ref GotEquippedHandEvent args)
    {
        EnsureActionGranted(ent, args.User);
    }

    private void OnGotUnequippedHand(Entity<WH40KWeaponModLaserSightComponent> ent, ref GotUnequippedHandEvent args)
    {
        ClearGrantedAction(ent);
    }

    private void OnHandSelected(Entity<WH40KWeaponModLaserSightComponent> ent, ref HandSelectedEvent args)
    {
        EnsureActionGranted(ent, args.User);
    }

    private void OnHandDeselected(Entity<WH40KWeaponModLaserSightComponent> ent, ref HandDeselectedEvent args)
    {
        ClearGrantedAction(ent);
    }

    private void OnInsertedIntoContainer(Entity<WH40KWeaponModLaserSightComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!TryGetHostedGun(ent.Owner, out var gunUid) ||
            !TryGetHoldingUser(gunUid, out var user) ||
            !_hands.TryGetActiveItem(user, out var activeItem) ||
            activeItem != gunUid)
        {
            return;
        }

        EnsureActionGranted(ent, user);
    }

    private void OnRemovedFromContainer(Entity<WH40KWeaponModLaserSightComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        ClearGrantedAction(ent);
    }

    private void EnsureActionGranted(Entity<WH40KWeaponModLaserSightComponent> ent, EntityUid user)
    {
        if (ent.Comp.ToggleActionEntity == null ||
            !TryGetHostedGun(ent.Owner, out var gunUid) ||
            !_hands.TryGetActiveItem(user, out var activeItem) ||
            activeItem != gunUid)
        {
            return;
        }

        // If the action is already attached to this user, do not re-add it. AddAction would
        // RemoveAction + re-add, which dirties ActionsComponent on every hand-selected event
        // and makes the action flicker in the hotbar.
        if (_actions.GetAction(ent.Comp.ToggleActionEntity.Value) is { } existing &&
            existing.Comp.AttachedEntity == user)
        {
            _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.Active);
            return;
        }

        _actions.AddAction(user, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction, ent.Owner);
        _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.Active);
    }

    private void ClearGrantedAction(Entity<WH40KWeaponModLaserSightComponent> ent)
    {
        if (ent.Comp.ToggleActionEntity == null ||
            _actions.GetAction(ent.Comp.ToggleActionEntity.Value) is not { } action ||
            action.Comp.AttachedEntity is not { } attachedUser)
        {
            return;
        }

        _actions.RemoveProvidedAction(attachedUser, ent.Owner, action.Owner);
    }

    private bool TryGetHoldingUser(EntityUid item, out EntityUid user)
    {
        user = default;

        if (!TryComp(item, out TransformComponent? xform))
            return false;

        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid || !HasComp<HandsComponent>(parent))
            return false;

        user = parent;
        return true;
    }
}
