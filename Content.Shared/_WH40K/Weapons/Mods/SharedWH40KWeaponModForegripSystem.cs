using System;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Shared._WH40K.Weapons.Mods;

public abstract partial class SharedWH40KWeaponModForegripSystem : EntitySystem
{
    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        base.Initialize();

        SubscribeLocalEvent<WH40KWeaponModForegripComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    private void OnGunRefreshModifiers(Entity<WH40KWeaponModForegripComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!TryGetHostedGun(ent.Owner, out var gunUid) ||
            !TryComp(gunUid, out WieldableComponent? wieldable) ||
            !wieldable.Wielded)
        {
            return;
        }

        args.MinAngle = ClampAngle(args.MinAngle, ent.Comp.SpreadMultiplier, ent.Comp.MinAngleFloorDegrees);
        args.MaxAngle = ClampAngle(args.MaxAngle, ent.Comp.SpreadMultiplier, ent.Comp.MaxAngleFloorDegrees);
        args.AngleIncrease = ClampAngle(args.AngleIncrease, ent.Comp.SpreadMultiplier, ent.Comp.AngleIncreaseFloorDegrees);

        if (args.MaxAngle.Theta < args.MinAngle.Theta)
            args.MaxAngle = args.MinAngle;
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
}
