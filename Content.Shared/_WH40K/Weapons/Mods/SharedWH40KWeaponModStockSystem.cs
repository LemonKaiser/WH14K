using System;
using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Weapons.Mods;

public abstract partial class SharedWH40KWeaponModStockSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedWH40KWeaponModSystem _weaponMods = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModStockComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
        SubscribeLocalEvent<WH40KWeaponModStockComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMovementSpeed);

        SubscribeLocalEvent<WH40KWeaponModFoldingStockComponent, MapInitEvent>(OnFoldingStockMapInit);
        SubscribeLocalEvent<WH40KWeaponModFoldingStockComponent, ComponentShutdown>(OnFoldingStockShutdown);
        SubscribeLocalEvent<WH40KWeaponModFoldingStockComponent, WH40KToggleWeaponStockActionEvent>(OnToggleStockAction);
        SubscribeLocalEvent<WH40KWeaponModFoldingStockComponent, AfterAutoHandleStateEvent>(OnFoldingStockAutoHandleState);
    }

    /// <summary>
    /// Server-replicated Folded state arrived on the client (e.g. after PVS re-entry when the
    /// weapon is dropped on the floor). The toggle handler is gated behind
    /// <c>IsFirstTimePredicted</c> to avoid prediction-replay oscillation, so the client never
    /// re-applies the folded overlay during replays. Without this handler the client would keep
    /// its last known (or default <c>Folded=false</c>) state, and the host appearance would be
    /// rebuilt with the stale <c>OverlayState="base"</c> once the weapon leaves and re-enters
    /// PVS — making a folded stock visually unfold ~0.5s after being dropped.
    /// </summary>
    private void OnFoldingStockAutoHandleState(Entity<WH40KWeaponModFoldingStockComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_net.IsServer)
            return;

        // Re-derive the mod's OverlayState from the freshly-replicated Folded field, then rebuild
        // the host's appearance so the overlay dict uses the correct "folded"/"base" state.
        // Use ForceRebuildHostOverlayClient (not RebuildHostOverlayClient) because this fires
        // DURING state application, where SetData no-ops (CheckIfApplyingState). The force variant
        // defers the rebuild to the next frame via Timer.Spawn(0) so SetData is no longer a no-op.
        UpdateOverlayState(ent);

        if (TryGetHostedGun(ent.Owner, out var gunUid, out var host))
            _weaponMods.ForceRebuildHostOverlayClient((gunUid, host));
    }

    private void OnGunRefreshModifiers(Entity<WH40KWeaponModStockComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!IsStockActive(ent.Owner) ||
            !TryComp(args.Gun, out WieldableComponent? wieldable) ||
            !wieldable.Wielded)
        {
            return;
        }

        args.MinAngle = ClampAngle(args.MinAngle, ent.Comp.SpreadMultiplier, ent.Comp.MinAngleFloorDegrees);
        args.MaxAngle = ClampAngle(args.MaxAngle, ent.Comp.SpreadMultiplier, ent.Comp.MaxAngleFloorDegrees);
        args.AngleIncrease = ClampAngle(args.AngleIncrease, ent.Comp.SpreadMultiplier, ent.Comp.AngleIncreaseFloorDegrees);
        args.CameraRecoilScalar = MathF.Max(ent.Comp.CameraRecoilFloor, args.CameraRecoilScalar * ent.Comp.CameraRecoilMultiplier);

        if (args.MaxAngle.Theta < args.MinAngle.Theta)
            args.MaxAngle = args.MinAngle;
    }

    private void OnRefreshMovementSpeed(
        Entity<WH40KWeaponModStockComponent> ent,
        ref HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (!IsStockActive(ent.Owner) ||
            !TryGetHostedGun(ent.Owner, out var gunUid) ||
            !TryComp(gunUid, out WieldableComponent? wieldable) ||
            !wieldable.Wielded)
        {
            return;
        }

        args.Args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier, MovementSpeedModifierLayer.Equipment);
    }

    private void OnFoldingStockMapInit(Entity<WH40KWeaponModFoldingStockComponent> ent, ref MapInitEvent args)
    {
        _actionContainer.EnsureAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction);
        _actions.SetToggled(ent.Comp.ToggleActionEntity, !ent.Comp.Folded);
        UpdateOverlayState(ent);
    }

    private void OnFoldingStockShutdown(Entity<WH40KWeaponModFoldingStockComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ToggleActionEntity != null)
            _actions.RemoveAction(ent.Comp.ToggleActionEntity);
    }

    private void OnToggleStockAction(Entity<WH40KWeaponModFoldingStockComponent> ent, ref WH40KToggleWeaponStockActionEvent args)
    {
        if (args.Handled ||
            !_timing.IsFirstTimePredicted ||
            !TryGetHostedGun(ent.Owner, out var gunUid, out var host) ||
            !_hands.TryGetActiveItem(args.Performer, out var activeItem) ||
            activeItem != gunUid)
        {
            return;
        }

        ent.Comp.Folded = !ent.Comp.Folded;
        _actions.SetToggled(ent.Comp.ToggleActionEntity, !ent.Comp.Folded);
        UpdateOverlayState(ent);
        _audio.PlayPredicted(ent.Comp.ToggleSound, gunUid, args.Performer);
        _weaponMods.RefreshHost(gunUid, host);
        args.Handled = true;
    }

    private void UpdateOverlayState(Entity<WH40KWeaponModFoldingStockComponent> ent)
    {
        if (!TryComp(ent.Owner, out WH40KWeaponModComponent? mod))
            return;

        mod.OverlayState = ent.Comp.Folded
            ? ent.Comp.FoldedOverlayState
            : ent.Comp.UnfoldedOverlayState;

        // OverlayState is networked on WH40KWeaponModComponent; replicate it so the client's
        // UpdateAppearance rebuilds the overlay dict with the correct (folded/unfolded) state
        // instead of falling back to "base". Also Dirty the FoldingStock component itself now
        // that Folded is networked, so the server-replicated folded state survives PVS re-entry.
        if (_net.IsServer)
        {
            Dirty(ent.Owner, mod);
            Dirty(ent.Owner, ent.Comp);
        }
    }

    private static Angle ClampAngle(Angle angle, float multiplier, float floorDegrees)
    {
        var floor = Angle.FromDegrees(floorDegrees).Theta;
        return new Angle(Math.Max(angle.Theta * multiplier, floor));
    }

    private bool IsStockActive(EntityUid stockUid)
    {
        return !TryComp(stockUid, out WH40KWeaponModFoldingStockComponent? folding) || !folding.Folded;
    }

    protected bool TryGetHostedGun(EntityUid modUid, out EntityUid gunUid, out WH40KWeaponModHostComponent host)
    {
        gunUid = default;
        host = default!;

        if (!TryComp(modUid, out TransformComponent? xform))
            return false;

        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid || !TryComp(parent, out WH40KWeaponModHostComponent? resolvedHost))
            return false;

        gunUid = parent;
        host = resolvedHost;
        return true;
    }

    protected bool TryGetHostedGun(EntityUid modUid, out EntityUid gunUid)
    {
        return TryGetHostedGun(modUid, out gunUid, out _);
    }
}
