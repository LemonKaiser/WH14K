using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;
using Robust.Shared.Maths;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    public bool UpdateBaseConfiguration(
        Entity<GunComponent?> gun,
        SoundSpecifier? soundGunshot = null,
        float? fireRate = null,
        float? projectileSpeed = null,
        Angle? minAngle = null,
        Angle? maxAngle = null,
        SelectiveFire? availableModes = null,
        SelectiveFire? selectedMode = null)
    {
        if (!Resolve(gun, ref gun.Comp))
            return false;

        var comp = gun.Comp;
        var changed = false;

        if (soundGunshot != null)
        {
            comp.SoundGunshot = soundGunshot;
            changed = true;
        }

        if (fireRate is { } newFireRate && !MathHelper.CloseTo(comp.FireRate, newFireRate))
        {
            comp.FireRate = newFireRate;
            changed = true;
        }

        if (projectileSpeed is { } newProjectileSpeed && !MathHelper.CloseTo(comp.ProjectileSpeed, newProjectileSpeed))
        {
            comp.ProjectileSpeed = newProjectileSpeed;
            changed = true;
        }

        if (minAngle is { } newMinAngle && !comp.MinAngle.EqualsApprox(newMinAngle))
        {
            comp.MinAngle = newMinAngle;
            changed = true;
        }

        if (maxAngle is { } newMaxAngle && !comp.MaxAngle.EqualsApprox(newMaxAngle))
        {
            comp.MaxAngle = newMaxAngle;
            changed = true;
        }

        if (availableModes is { } newAvailableModes && comp.AvailableModes != newAvailableModes)
        {
            comp.AvailableModes = newAvailableModes;
            changed = true;
        }

        if (selectedMode is { } newSelectedMode)
        {
            if ((comp.AvailableModes & newSelectedMode) == 0x0)
                newSelectedMode = GetFallbackMode(comp.AvailableModes);

            if (comp.SelectedMode != newSelectedMode)
            {
                comp.SelectedMode = newSelectedMode;
                changed = true;
            }
        }
        else if ((comp.AvailableModes & comp.SelectedMode) == 0x0)
        {
            comp.SelectedMode = GetFallbackMode(comp.AvailableModes);
            changed = true;
        }

        if (comp.CurrentAngle < comp.MinAngle)
        {
            comp.CurrentAngle = comp.MinAngle;
            changed = true;
        }

        if (comp.CurrentAngle > comp.MaxAngle)
        {
            comp.CurrentAngle = comp.MaxAngle;
            changed = true;
        }

        if (!changed)
            return false;

        Dirty(gun, comp);
        RefreshModifiers((gun.Owner, comp));
        return true;
    }

    private static SelectiveFire GetFallbackMode(SelectiveFire availableModes)
    {
        if ((availableModes & SelectiveFire.SemiAuto) != 0x0)
            return SelectiveFire.SemiAuto;

        if ((availableModes & SelectiveFire.Burst) != 0x0)
            return SelectiveFire.Burst;

        if ((availableModes & SelectiveFire.FullAuto) != 0x0)
            return SelectiveFire.FullAuto;

        return SelectiveFire.SemiAuto;
    }
}
