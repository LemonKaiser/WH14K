using Content.Shared.PowerCell;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Mech.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Power.Components;

namespace Content.Shared.Mech.Equipment.Systems;

/// <summary>
/// Charges equipment batteries inside mechs using the mech's own power cell.
/// </summary>
public sealed partial class MechChargerSystem : EntitySystem
{
    [Dependency] private  SharedBatterySystem _battery = default!;
    [Dependency] private  PowerCellSystem _powerCell = default!;
    [Dependency] private  SharedMechSystem _mech = default!;

    private readonly Dictionary<EntityUid, float> _rechargeTimers = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MechEquipmentAutoRechargeComponent, MechEquipmentUiStateReadyEvent>(OnUiStateReady);
    }

    private void OnUiStateReady(Entity<MechEquipmentAutoRechargeComponent> ent, ref MechEquipmentUiStateReadyEvent args)
    {
        args.States[GetNetEntity(ent.Owner)] = new MechWeaponRechargeUiState
        {
            AutoRecharge = ent.Comp.Enabled,
        };
    }

    private float GetRechargeUnit(EntityUid equipment)
    {
        if (TryComp<BatteryAmmoProviderComponent>(equipment, out var batteryAmmo))
            return MathF.Max(1f, batteryAmmo.FireCost);

        return 1f;
    }

    /// <inheritdoc/>
    /// TODO: need a ChargerSystem refractor so it can charge batteries from another battery in the slot.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MechComponent>();
        while (query.MoveNext(out var mechUid, out var mech))
        {
            if (!_powerCell.TryGetBatteryFromSlot(mechUid, out var mechBattery))
                continue;

            var mechCharge = _battery.GetCharge(mechBattery.Value.AsNullable());
            if (mechCharge <= 0)
                continue;

            var uiDirty = false;

            // Charge all weapons in the container.
            foreach (var equipment in mech.EquipmentContainer.ContainedEntities)
            {
                if (mechCharge <= 0f)
                    break;

                if (!TryComp<MechEquipmentAutoRechargeComponent>(equipment, out var autoRecharge) || !autoRecharge.Enabled)
                {
                    _rechargeTimers.Remove(equipment);
                    continue;
                }

                if (!TryComp<BatteryComponent>(equipment, out var equipmentBattery))
                {
                    _rechargeTimers.Remove(equipment);
                    continue;
                }

                var equipmentCharge = _battery.GetCharge((equipment, equipmentBattery));
                if (equipmentCharge >= equipmentBattery.MaxCharge)
                {
                    _rechargeTimers.Remove(equipment);
                    continue;
                }

                var secondsPerCharge = MathF.Max(1f, autoRecharge.SecondsPerCharge);
                _rechargeTimers.TryGetValue(equipment, out var elapsed);
                elapsed += frameTime;

                if (elapsed < secondsPerCharge)
                {
                    _rechargeTimers[equipment] = elapsed;
                    continue;
                }

                var chargeNeeded = equipmentBattery.MaxCharge - equipmentCharge;
                var energyMultiplier = MathF.Max(1f, autoRecharge.EnergyMultiplier);
                var rechargeUnit = GetRechargeUnit(equipment);
                var transfer = MathF.Min(rechargeUnit, chargeNeeded);
                if (transfer <= 0f)
                    continue;

                var mechEnergyCost = transfer * energyMultiplier;
                if (!_mech.HasUsableEnergy((mechUid, mech), mechEnergyCost))
                {
                    _rechargeTimers[equipment] = secondsPerCharge;
                    continue;
                }

                if (!_battery.TryUseCharge(mechBattery.Value.AsNullable(), mechEnergyCost))
                {
                    _rechargeTimers[equipment] = secondsPerCharge;
                    continue;
                }

                _battery.SetCharge((equipment, equipmentBattery), equipmentCharge + transfer);
                mechCharge -= mechEnergyCost;
                elapsed -= secondsPerCharge;
                if (equipmentCharge + transfer >= equipmentBattery.MaxCharge)
                    _rechargeTimers.Remove(equipment);
                else
                    _rechargeTimers[equipment] = MathF.Min(elapsed, secondsPerCharge);

                uiDirty = true;
            }

            if (!uiDirty)
                continue;

            _mech.UpdateMechUi(mechUid);
            _mech.UpdateBatteryAlert(mechUid);
            _mech.RefreshEnergyDependentState(mechUid);
        }
    }
}
