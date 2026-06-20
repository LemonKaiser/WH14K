using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared._WH40K.Weapons.Mods;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Weapons.Mods;

public sealed partial class WH40KWeaponModGrenadeLauncherSystem : SharedWH40KWeaponModGrenadeLauncherSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModGrenadeLauncherComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnParentChanged(Entity<WH40KWeaponModGrenadeLauncherComponent> ent, ref EntParentChangedMessage args)
    {
        if (args.OldParent is { } previousParent &&
            previousParent != EntityUid.Invalid &&
            TryComp(previousParent, out WH40KWeaponModHostComponent? oldHost))
        {
            RestoreHostedMode(previousParent, oldHost);
        }

        ClearGrantedAction(ent);
    }

    protected override void EnsureActionGranted(Entity<WH40KWeaponModGrenadeLauncherComponent> ent, EntityUid user)
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
        // (and OnHostedContextChanged fires on every shot/reload/hand-switch) and makes the
        // action flicker in the hotbar.
        if (_actions.GetAction(ent.Comp.ToggleActionEntity.Value) is { } existing &&
            existing.Comp.AttachedEntity == user)
        {
            _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.Active);
            return;
        }

        _actions.AddAction(user, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction, ent.Owner);
        _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.Active);
    }

    protected override void ClearGrantedAction(Entity<WH40KWeaponModGrenadeLauncherComponent> ent)
    {
        if (ent.Comp.ToggleActionEntity == null ||
            _actions.GetAction(ent.Comp.ToggleActionEntity.Value) is not { } action ||
            action.Comp.AttachedEntity is not { } attachedUser)
        {
            return;
        }

        _actions.RemoveProvidedAction(attachedUser, ent.Owner, action.Owner);
    }
}
