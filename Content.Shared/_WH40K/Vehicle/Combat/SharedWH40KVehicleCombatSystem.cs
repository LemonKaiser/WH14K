using System;
using Content.Shared._WH40K.Vehicle.Fuel;
using Content.Shared._WH40K.Vehicle.Movement;
using Content.Shared.Examine;
using Content.Shared.Vehicle.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._WH40K.Vehicle.Combat;

public sealed partial class SharedWH40KVehicleCombatSystem : EntitySystem
{
    [Dependency] private  SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KVehicleMountedGunComponent, AttemptShootEvent>(OnMountedAttemptShoot);
        SubscribeLocalEvent<WH40KVehicleMountedGunComponent, GetShootingEntityEvent>(OnMountedGetShootingEntity);
        SubscribeLocalEvent<WH40KVehicleMountedGunComponent, ExaminedEvent>(OnMountedExamined);
        SubscribeLocalEvent<WH40KVehicleRamComponent, ExaminedEvent>(OnRamExamined);
    }

    private void OnMountedAttemptShoot(Entity<WH40KVehicleMountedGunComponent> ent, ref AttemptShootEvent args)
    {
        if (!TryComp(ent.Owner, out VehicleComponent? vehicle) ||
            vehicle.Operator == null)
        {
            args.Cancelled = true;
            return;
        }

        if (args.User != ent.Owner && args.User != vehicle.Operator.Value)
        {
            args.Cancelled = true;
            return;
        }

        if (ent.Comp.RequiresRunningEngine &&
            TryComp(ent.Owner, out WH40KVehicleEngineComponent? engine) &&
            engine.State != WH40KVehicleEngineState.Running)
        {
            args.Cancelled = true;
            return;
        }

        if (ent.Comp.BlockWhenDisabled &&
            TryComp(ent.Owner, out WH40KVehicleHandlingHealthComponent? handling) &&
            handling.ServiceState == WH40KVehicleServiceState.Disabled)
        {
            args.Cancelled = true;
        }
    }

    private void OnMountedGetShootingEntity(Entity<WH40KVehicleMountedGunComponent> ent, ref GetShootingEntityEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(ent.Owner, out VehicleComponent? vehicle) ||
            vehicle.Operator == null)
        {
            return;
        }

        args.ShootingEntity = vehicle.Operator.Value;
        args.Handled = true;
    }

    private void OnMountedExamined(Entity<WH40KVehicleMountedGunComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !ent.Comp.ShowExamineText || !TryComp(ent.Owner, out GunComponent? gun))
            return;

        var ammoCount = _gun.GetAmmoCount(ent.Owner);
        var ammoCapacity = _gun.GetAmmoCapacity(ent.Owner);

        using (args.PushGroup(nameof(WH40KVehicleMountedGunComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-vehicle-mounted-gun-examine",
                ("current", ammoCount),
                ("capacity", ammoCapacity)));
            args.PushMarkup(Loc.GetString("wh40k-vehicle-mounted-gun-controls"));
        }
    }

    private void OnRamExamined(Entity<WH40KVehicleRamComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        TryComp(ent.Owner, out WH40KVehicleCarMovementComponent? movement);
        var minimumImpactSpeed = ent.Comp.GetMinimumImpactSpeed(movement);
        var percent = (int) MathF.Round(Math.Clamp(ent.Comp.MinimumImpactSpeedRatio, 0f, 1f) * 100f);

        using (args.PushGroup(nameof(WH40KVehicleRamComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-vehicle-ram-examine",
                ("percent", percent),
                ("speed", minimumImpactSpeed)));
        }
    }
}
