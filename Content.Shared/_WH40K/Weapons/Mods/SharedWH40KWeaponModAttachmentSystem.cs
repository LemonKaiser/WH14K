using System;
using Content.Shared.Hands;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Shared._WH40K.Weapons.Mods;

public sealed partial class SharedWH40KWeaponModAttachmentSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModSuppressorComponent, GunRefreshModifiersEvent>(OnSuppressorRefreshModifiers);
        SubscribeLocalEvent<WH40KWeaponModSuppressorComponent, GunMuzzleFlashAttemptEvent>(OnSuppressorMuzzleFlash);
        SubscribeLocalEvent<WH40KWeaponModMuzzleBrakeComponent, GunRefreshModifiersEvent>(OnMuzzleBrakeRefreshModifiers);
        SubscribeLocalEvent<WH40KWeaponModBarrelComponent, GunRefreshModifiersEvent>(OnBarrelRefreshModifiers);
        SubscribeLocalEvent<WH40KWeaponModBarrelComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnBarrelRefreshMovementSpeed);
        SubscribeLocalEvent<WH40KWeaponModShortBarrelComponent, GunRefreshModifiersEvent>(OnShortBarrelRefreshModifiers);
        SubscribeLocalEvent<WH40KWeaponModShortBarrelComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnShortBarrelRefreshMovementSpeed);
    }

    private void OnSuppressorRefreshModifiers(Entity<WH40KWeaponModSuppressorComponent> ent, ref GunRefreshModifiersEvent args)
    {
        args.SoundGunshot = AdjustVolume(args.SoundGunshot, ent.Comp.VolumeOffset);
    }

    private void OnSuppressorMuzzleFlash(Entity<WH40KWeaponModSuppressorComponent> ent, ref GunMuzzleFlashAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnMuzzleBrakeRefreshModifiers(Entity<WH40KWeaponModMuzzleBrakeComponent> ent, ref GunRefreshModifiersEvent args)
    {
        args.SoundGunshot = AdjustVolume(args.SoundGunshot, ent.Comp.VolumeOffset);
        args.MinAngle = ClampAngle(args.MinAngle, ent.Comp.SpreadMultiplier, ent.Comp.MinAngleFloorDegrees);
        args.MaxAngle = ClampAngle(args.MaxAngle, ent.Comp.SpreadMultiplier, ent.Comp.MaxAngleFloorDegrees);
        args.AngleIncrease = ClampAngle(args.AngleIncrease, ent.Comp.SpreadMultiplier, ent.Comp.AngleIncreaseFloorDegrees);
        args.CameraRecoilScalar = MathF.Max(ent.Comp.CameraRecoilFloor, args.CameraRecoilScalar * ent.Comp.CameraRecoilMultiplier);

        if (args.MaxAngle.Theta < args.MinAngle.Theta)
            args.MaxAngle = args.MinAngle;
    }

    private void OnBarrelRefreshModifiers(Entity<WH40KWeaponModBarrelComponent> ent, ref GunRefreshModifiersEvent args)
    {
        // Long barrel: speed up projectile + tighten spread/recoil (wield-gated for movement only).
        args.ProjectileSpeed *= ent.Comp.ProjectileSpeedMultiplier;
        args.MinAngle = ClampAngle(args.MinAngle, ent.Comp.SpreadMultiplier, ent.Comp.MinAngleFloorDegrees);
        args.MaxAngle = ClampAngle(args.MaxAngle, ent.Comp.SpreadMultiplier, ent.Comp.MaxAngleFloorDegrees);
        args.AngleIncrease = ClampAngle(args.AngleIncrease, ent.Comp.SpreadMultiplier, ent.Comp.AngleIncreaseFloorDegrees);
        args.CameraRecoilScalar = MathF.Max(ent.Comp.CameraRecoilFloor, args.CameraRecoilScalar * ent.Comp.CameraRecoilMultiplier);

        if (args.MaxAngle.Theta < args.MinAngle.Theta)
            args.MaxAngle = args.MinAngle;
    }

    private void OnBarrelRefreshMovementSpeed(
        Entity<WH40KWeaponModBarrelComponent> ent,
        ref HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        // Long barrel mobility penalty applies only while wielded (it is a heavy precision barrel).
        if (!TryGetHostedGun(ent.Owner, out var gunUid) ||
            !TryComp(gunUid, out WieldableComponent? wieldable) ||
            !wieldable.Wielded)
        {
            return;
        }

        args.Args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier, MovementSpeedModifierLayer.Equipment);
    }

    private void OnShortBarrelRefreshModifiers(Entity<WH40KWeaponModShortBarrelComponent> ent, ref GunRefreshModifiersEvent args)
    {
        // Short/sawn-off barrel: slower projectile, wider spread, slightly more recoil.
        args.ProjectileSpeed *= ent.Comp.ProjectileSpeedMultiplier;
        args.MinAngle = new Angle(args.MinAngle.Theta * ent.Comp.SpreadMultiplier);
        args.MaxAngle = new Angle(args.MaxAngle.Theta * ent.Comp.SpreadMultiplier);
        args.AngleIncrease = new Angle(args.AngleIncrease.Theta * ent.Comp.SpreadMultiplier);
        args.CameraRecoilScalar *= ent.Comp.CameraRecoilMultiplier;

        if (args.MaxAngle.Theta < args.MinAngle.Theta)
            args.MaxAngle = args.MinAngle;
    }

    private void OnShortBarrelRefreshMovementSpeed(
        Entity<WH40KWeaponModShortBarrelComponent> ent,
        ref HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        // Short barrel mobility bonus applies only while wielded (it is a compact handling improvement).
        if (!TryGetHostedGun(ent.Owner, out var gunUid) ||
            !TryComp(gunUid, out WieldableComponent? wieldable) ||
            !wieldable.Wielded)
        {
            return;
        }

        args.Args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier, MovementSpeedModifierLayer.Equipment);
    }

    private bool TryGetHostedGun(EntityUid modUid, out EntityUid gunUid)
    {
        gunUid = default;

        if (!TryComp(modUid, out TransformComponent? xform))
            return false;

        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid || !TryComp(parent, out WH40KWeaponModHostComponent? _))
            return false;

        gunUid = parent;
        return true;
    }

    private static Angle ClampAngle(Angle angle, float multiplier, float floorDegrees)
    {
        var floor = Angle.FromDegrees(floorDegrees).Theta;
        return new Angle(Math.Max(angle.Theta * multiplier, floor));
    }

    private static SoundSpecifier? AdjustVolume(SoundSpecifier? sound, float volumeOffset)
    {
        if (sound == null)
            return null;

        return sound switch
        {
            SoundPathSpecifier path => new SoundPathSpecifier(path.Path, path.Params.AddVolume(volumeOffset)),
            SoundCollectionSpecifier collection when collection.Collection != null
                => new SoundCollectionSpecifier(collection.Collection, collection.Params.AddVolume(volumeOffset)),
            _ => sound,
        };
    }
}
