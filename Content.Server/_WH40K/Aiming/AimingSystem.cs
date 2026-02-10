using System;
using Robust.Shared;
using Robust.Shared.Configuration;
using Content.Server.Movement.Systems;
using Content.Shared.Camera;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared._WH40K.Aiming;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Server._WH40K.Aiming;

public sealed class AimingSystem : EntitySystem
{
    [Dependency] private readonly ContentEyeSystem _eye = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedAimingSystem _shared = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AimingCameraComponent, AimingToggledEvent>(OnAimingToggled);
        SubscribeLocalEvent<AimingCameraComponent, GotEquippedHandEvent>(OnEquipped);
        SubscribeLocalEvent<AimingCameraComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<AimingCameraComponent, HandSelectedEvent>(OnHandSelected);
        SubscribeLocalEvent<AimingCameraComponent, HandDeselectedEvent>(OnHandDeselected);
        SubscribeLocalEvent<AimingCameraComponent, ItemWieldedEvent>(OnItemWielded);
        SubscribeLocalEvent<AimingCameraComponent, ItemUnwieldedEvent>(OnItemUnwielded);

        SubscribeLocalEvent<AimingCameraComponent, HeldRelayedEvent<GetEyePvsScaleRelayedEvent>>(OnGetEyePvsScale);
    }

    private void OnAimingToggled(EntityUid uid, AimingCameraComponent component, AimingToggledEvent args)
    {
        if (args.User != null)
            _eye.UpdatePvsScale(args.User.Value);
    }

    private void OnEquipped(EntityUid uid, AimingCameraComponent component, GotEquippedHandEvent args)
    {
        SyncToUserState(uid, component, args.User);
        _eye.UpdatePvsScale(args.User);
    }

    private void OnUnequipped(EntityUid uid, AimingCameraComponent component, GotUnequippedHandEvent args)
    {
        _eye.UpdatePvsScale(args.User);
    }

    private void OnHandSelected(EntityUid uid, AimingCameraComponent component, HandSelectedEvent args)
    {
        SyncToUserState(uid, component, args.User);
        _eye.UpdatePvsScale(args.User);
    }

    private void OnHandDeselected(EntityUid uid, AimingCameraComponent component, HandDeselectedEvent args)
    {
        _eye.UpdatePvsScale(args.User);
    }

    private void OnItemWielded(EntityUid uid, AimingCameraComponent component, ref ItemWieldedEvent args)
    {
        SyncToUserState(uid, component, args.User);
        _eye.UpdatePvsScale(args.User);
    }

    private void OnItemUnwielded(EntityUid uid, AimingCameraComponent component, ItemUnwieldedEvent args)
    {
        _eye.UpdatePvsScale(args.User);
    }

    private void SyncToUserState(EntityUid uid, AimingCameraComponent component, EntityUid user)
    {
        var userComp = EnsureComp<AimingUserComponent>(user);
        _shared.SetEnabled(uid, userComp.Enabled, component, user);
    }

    private void OnGetEyePvsScale(EntityUid uid, AimingCameraComponent component, ref HeldRelayedEvent<GetEyePvsScaleRelayedEvent> args)
    {
        if (component.RequireWield && !IsWieldedOrMultiHanded(uid))
            return;

        if (!TryGetHoldingUser(uid, out var user, out var hands) || _hands.GetActiveItem((user, hands)) != uid)
            return;

        var userComp = EnsureComp<AimingUserComponent>(user);
        if (!userComp.Enabled)
            return;

        var baseRange = MathF.Max(_cfg.GetCVar(CVars.NetMaxUpdateRange), _cfg.GetCVar(CVars.NetPvsPriorityRange)) / 2f;
        var reserve = MathF.Max(4f, component.MaxOffset * 0.45f);
        var requiredIncrease = (component.MaxOffset + reserve) / MathF.Max(baseRange, 1f);
        var minScale = component.MaxOffset * 0.1f;
        args.Args.Scale += MathF.Max(component.PvsIncrease, MathF.Max(minScale, requiredIncrease + 0.1f));
    }

    private bool TryGetHoldingUser(EntityUid item, out EntityUid user, out HandsComponent hands)
    {
        user = default;
        hands = default!;

        if (!TryComp(item, out TransformComponent? xform))
            return false;

        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid)
            return false;

        if (!TryComp(parent, out HandsComponent? handsComp) || handsComp == null)
            return false;

        user = parent;
        hands = handsComp;
        return true;
    }

    private bool IsWieldedOrMultiHanded(EntityUid uid)
    {
        if (TryComp(uid, out WieldableComponent? wieldable) && wieldable.Wielded)
            return true;

        return HasComp<MultiHandedItemComponent>(uid);
    }
}
