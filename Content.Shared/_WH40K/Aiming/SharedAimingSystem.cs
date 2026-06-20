using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Toggleable;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Aiming;

public sealed partial class SharedAimingSystem : EntitySystem
{
    [Dependency] private  SharedActionsSystem _actions = default!;
    [Dependency] private  ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        SubscribeLocalEvent<AimingCameraComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AimingCameraComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<AimingCameraComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<AimingCameraComponent, ComponentHandleState>(OnHandleState);

        SubscribeLocalEvent<AimingUserComponent, ComponentGetState>(OnGetUserState);
        SubscribeLocalEvent<AimingUserComponent, ComponentHandleState>(OnHandleUserState);

        SubscribeLocalEvent<AimingCameraComponent, ToggleActionEvent>(OnToggleAction);
    }

    private void OnMapInit(EntityUid uid, AimingCameraComponent component, MapInitEvent args)
    {
        _actionContainer.EnsureAction(uid, ref component.ToggleActionEntity, component.ToggleAction);
        _actions.SetToggled(component.ToggleActionEntity, component.Enabled);
        Dirty(uid, component);
    }

    private void OnShutdown(EntityUid uid, AimingCameraComponent component, ComponentShutdown args)
    {
        if (component.ToggleActionEntity != null)
            _actions.RemoveAction(component.ToggleActionEntity);
    }

    private void OnGetState(EntityUid uid, AimingCameraComponent component, ref ComponentGetState args)
    {
        args.State = new AimingCameraComponentState(component.Enabled, component.MaxOffset);
    }

    private void OnHandleState(EntityUid uid, AimingCameraComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not AimingCameraComponentState state)
            return;

        component.Enabled = state.Enabled;
        component.MaxOffset = state.MaxOffset;
        _actions.SetToggled(component.ToggleActionEntity, component.Enabled);
    }

    private void OnGetUserState(EntityUid uid, AimingUserComponent component, ref ComponentGetState args)
    {
        args.State = new AimingUserComponentState(component.Enabled);
    }

    private void OnHandleUserState(EntityUid uid, AimingUserComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not AimingUserComponentState state)
            return;

        component.Enabled = state.Enabled;
    }

    private void OnToggleAction(EntityUid uid, AimingCameraComponent component, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        if (!_hands.TryGetActiveItem(args.Performer, out var activeItem) || activeItem != uid)
            return;

        var user = args.Performer;
        var userComp = EnsureComp<AimingUserComponent>(user);
        userComp.Enabled = !userComp.Enabled;
        Dirty(user, userComp);

        SetEnabled(uid, userComp.Enabled, component, user);
        args.Handled = true;
    }


    public void SetEnabled(EntityUid uid, bool enabled, AimingCameraComponent? component = null, EntityUid? user = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.Enabled == enabled)
            return;

        component.Enabled = enabled;
        Dirty(uid, component);

        _actions.SetToggled(component.ToggleActionEntity, component.Enabled);
        RaiseLocalEvent(uid, new AimingToggledEvent(component.Enabled, user));
    }
}
