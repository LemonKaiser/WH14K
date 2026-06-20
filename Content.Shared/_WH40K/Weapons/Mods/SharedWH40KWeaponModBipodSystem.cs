using System;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Shared._WH40K.Weapons.Mods;

public abstract partial class SharedWH40KWeaponModBipodSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;

    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModBipodComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
        SubscribeLocalEvent<WH40KWeaponModBipodComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMovementSpeed);

        // React instantly when the wielder goes prone or stands up so the bipod bonus applies/removes
        // without waiting for the next GunRefreshModifiersEvent. DownedEvent/StoodEvent fire on the
        // USER entity (not the mod, which lives in a weapon ItemSlot held in hands — not an inventory
        // slot, so IInventoryRelayEvent won't relay it). Subscribe on HandsComponent and scan the
        // held weapon for an installed bipod.
        SubscribeLocalEvent<HandsComponent, DownedEvent>(OnUserDowned);
        SubscribeLocalEvent<HandsComponent, StoodEvent>(OnUserStood);
    }

    private void OnGunRefreshModifiers(Entity<WH40KWeaponModBipodComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!IsBipodActive(ent.Owner))
            return;

        args.MinAngle = ClampAngle(args.MinAngle, ent.Comp.SpreadMultiplier, ent.Comp.MinAngleFloorDegrees);
        args.MaxAngle = ClampAngle(args.MaxAngle, ent.Comp.SpreadMultiplier, ent.Comp.MaxAngleFloorDegrees);
        args.AngleIncrease = ClampAngle(args.AngleIncrease, ent.Comp.SpreadMultiplier, ent.Comp.AngleIncreaseFloorDegrees);
        args.CameraRecoilScalar = MathF.Max(0f, args.CameraRecoilScalar * ent.Comp.CameraRecoilMultiplier);

        if (args.MaxAngle.Theta < args.MinAngle.Theta)
            args.MaxAngle = args.MinAngle;
    }

    private void OnRefreshMovementSpeed(
        Entity<WH40KWeaponModBipodComponent> ent,
        ref HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (!IsBipodActive(ent.Owner))
            return;

        args.Args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier, MovementSpeedModifierLayer.Equipment);
    }

    /// <summary>
    ///     The bipod bonus is active only when the weapon is wielded AND the holding user is prone.
    /// </summary>
    private bool IsBipodActive(EntityUid modUid)
    {
        if (!TryGetHostedGun(modUid, out var gunUid) ||
            !TryComp(gunUid, out WieldableComponent? wieldable) ||
            !wieldable.Wielded)
        {
            return false;
        }

        if (!TryGetHoldingUser(gunUid, out var user))
            return false;

        return _standing.IsDown(user);
    }

    private void OnUserDowned(Entity<HandsComponent> ent, ref DownedEvent args)
    {
        OnUserStandingChanged(ent.Owner);
    }

    private void OnUserStood(Entity<HandsComponent> ent, ref StoodEvent args)
    {
        OnUserStandingChanged(ent.Owner);
    }

    /// <summary>
    ///     Called when a user goes prone or stands up. Finds any bipod-equipped weapon they are
    ///     wielding and refreshes its gun modifiers + movement speed, playing the deploy sound.
    /// </summary>
    private void OnUserStandingChanged(EntityUid user)
    {
        if (!_hands.TryGetActiveItem(user, out var activeItem) ||
            !TryComp<WH40KWeaponModHostComponent>(activeItem, out var host))
        {
            return;
        }

        var gunUid = activeItem.Value;
        if (!TryComp(gunUid, out WieldableComponent? wieldable) || !wieldable.Wielded)
            return;

        // Scan the weapon's mod slots for an installed bipod.
        EntityUid? bipodMod = null;
        WH40KWeaponModBipodComponent? bipod = null;
        foreach (var slot in host.ModSlots.Values)
        {
            if (slot.Item is not { } modUid || !TryComp(modUid, out WH40KWeaponModBipodComponent? b))
                continue;
            bipodMod = modUid;
            bipod = b;
            break;
        }

        if (bipod == null || bipodMod == null)
            return;

        _gun.RefreshModifiers(gunUid);
        _movementSpeed.RefreshMovementSpeedModifiers(user);
        _audio.PlayPredicted(bipod.DeploySound, gunUid, user);
    }

    private static Angle ClampAngle(Angle angle, float multiplier, float floorDegrees)
    {
        var floor = Angle.FromDegrees(floorDegrees).Theta;
        return new Angle(Math.Max(angle.Theta * multiplier, floor));
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

    protected bool TryGetHoldingUser(EntityUid item, out EntityUid user)
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
