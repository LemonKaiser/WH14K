using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Light.Components;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Mech.Module.Components;
using Content.Shared.Mech.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.Mech.Systems;

/// <summary>
/// Handles logic for the mech interface.
/// </summary>
/// <remarks>
/// This system is responsible for updating the mech UI state and handling UI interactions.
/// It is not responsible for any mech logic on its own, it merely provides UI functionality.
/// </remarks>
public sealed partial class MechInterfaceSystem : EntitySystem
{
    [Dependency] private  ContainerSystem _container = default!;
    [Dependency] private  IGameTiming _gameTiming = default!;
    [Dependency] private  MechLockSystem _mechLock = default!;
    [Dependency] private  PowerCellSystem _powerCell = default!;
    [Dependency] private  SharedBatterySystem _battery = default!;
    [Dependency] private  UserInterfaceSystem _uiSystem = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MechComponent, UpdateMechUiEvent>(OnUpdateMechUi);

        Subs.BuiEvents<MechComponent>(MechUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>(OnMechUiOpened);
            });

        Subs.BuiEvents<MechComponent>(
            MechUiKey.Key,
            subs =>
            {
                subs.Event<MechEquipmentRemoveMessage>(HandleEquipmentRemove);
                subs.Event<MechModuleRemoveMessage>(HandleModuleRemove);

                subs.Event<MechDnaLockRegisterMessage>(HandleDnaLockRegister);
                subs.Event<MechDnaLockToggleMessage>(HandleDnaLockToggle);
                subs.Event<MechDnaLockResetMessage>(HandleDnaLockReset);
                subs.Event<MechCardLockRegisterMessage>(HandleCardLockRegister);
                subs.Event<MechCardLockToggleMessage>(HandleCardLockToggle);
                subs.Event<MechCardLockResetMessage>(HandleCardLockReset);

                subs.Event<MechEquipmentUiMessage>(HandleEquipmentUiMessageRelay);
                subs.Event<MechGrabberEjectMessage>(HandleEquipmentUiMessageRelay);
                subs.Event<MechSoundboardPlayMessage>(HandleEquipmentUiMessageRelay);
                subs.Event<MechGeneratorEjectFuelMessage>(HandleEquipmentUiMessageRelay);
                subs.Event<MechWeaponRechargeToggleMessage>(HandleWeaponRechargeToggle);
            });
    }

    private void OnMechUiOpened(Entity<MechComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnUpdateMechUi(Entity<MechComponent> ent, ref UpdateMechUiEvent args)
    {
        UpdateUi(ent);
    }

    private void RelayEquipmentUiMessage(MechEquipmentUiMessage msg)
    {
        var equipment = GetEntity(msg.Equipment);
        RaiseLocalEvent(equipment, new MechEquipmentUiMessageRelayEvent(msg));
    }

    private void HandleEquipmentUiMessageRelay(Entity<MechComponent> ent, ref MechEquipmentUiMessage args)
    {
        RelayEquipmentUiMessage(args);
    }

    private void HandleEquipmentUiMessageRelay(Entity<MechComponent> ent, ref MechGrabberEjectMessage args)
    {
        RelayEquipmentUiMessage(args);
    }

    private void HandleEquipmentUiMessageRelay(Entity<MechComponent> ent, ref MechSoundboardPlayMessage args)
    {
        RelayEquipmentUiMessage(args);
    }

    private void HandleEquipmentUiMessageRelay(Entity<MechComponent> ent, ref MechGeneratorEjectFuelMessage args)
    {
        RelayEquipmentUiMessage(args);
    }

    private void HandleEquipmentRemove(Entity<MechComponent> ent, ref MechEquipmentRemoveMessage args)
    {
        var equipment = GetEntity(args.Equipment);
        if (!ent.Comp.EquipmentContainer.Contains(equipment))
            return;

        _container.Remove(equipment, ent.Comp.EquipmentContainer);
        UpdateUi(ent);
    }

    private void HandleModuleRemove(Entity<MechComponent> ent, ref MechModuleRemoveMessage args)
    {
        var module = GetEntity(args.Module);
        if (!ent.Comp.ModuleContainer.Contains(module))
            return;

        _container.Remove(module, ent.Comp.ModuleContainer);
        UpdateUi(ent);
    }

    private void HandleWeaponRechargeToggle(Entity<MechComponent> ent, ref MechWeaponRechargeToggleMessage args)
    {
        var equipment = GetEntity(args.Equipment);
        if (!ent.Comp.EquipmentContainer.Contains(equipment)
            && (!TryComp<MechEquipmentComponent>(equipment, out var equipmentComp)
                || equipmentComp.EquipmentOwner != ent.Owner))
        {
            return;
        }

        if (!TryComp<MechEquipmentAutoRechargeComponent>(equipment, out var recharge))
            return;

        recharge.Enabled = args.Enabled;
        Dirty(equipment, recharge);

        TryComp<MechCabinAirComponent>(ent.Owner, out var cabin);
        var fanModule = GetFanModule(ent.Comp);
        _uiSystem.ServerSendUiMessage(ent.Owner,
            MechUiKey.Key,
            new MechWeaponRechargeStateMessage(
                args.Equipment,
                recharge.Enabled,
                CalculateEnergyDrainRate(ent, cabin, fanModule)));
    }

    private void HandleDnaLockRegister(Entity<MechComponent> ent, ref MechDnaLockRegisterMessage args)
    {
        if (!_mechLock.CheckAccessWithFeedback(ent.Owner, args.Actor))
            return;

        var ev = new MechDnaLockRegisterEvent { User = GetNetEntity(args.Actor) };
        RaiseLocalEvent(ent, ev);
    }

    private void HandleDnaLockToggle(Entity<MechComponent> ent, ref MechDnaLockToggleMessage args)
    {
        if (!_mechLock.CheckAccessWithFeedback(ent.Owner, args.Actor))
            return;

        var ev = new MechDnaLockToggleEvent { User = GetNetEntity(args.Actor) };
        RaiseLocalEvent(ent, ev);
    }

    private void HandleDnaLockReset(Entity<MechComponent> ent, ref MechDnaLockResetMessage args)
    {
        if (!_mechLock.CheckAccessWithFeedback(ent.Owner, args.Actor))
            return;

        var ev = new MechDnaLockResetEvent { User = GetNetEntity(args.Actor) };
        RaiseLocalEvent(ent, ev);
    }

    private void HandleCardLockRegister(Entity<MechComponent> ent, ref MechCardLockRegisterMessage args)
    {
        if (!_mechLock.CheckAccessWithFeedback(ent.Owner, args.Actor))
            return;

        var ev = new MechCardLockRegisterEvent { User = GetNetEntity(args.Actor) };
        RaiseLocalEvent(ent, ev);
    }

    private void HandleCardLockToggle(Entity<MechComponent> ent, ref MechCardLockToggleMessage args)
    {
        if (!_mechLock.CheckAccessWithFeedback(ent.Owner, args.Actor))
            return;

        var ev = new MechCardLockToggleEvent { User = GetNetEntity(args.Actor) };
        RaiseLocalEvent(ent, ev);
    }

    private void HandleCardLockReset(Entity<MechComponent> ent, ref MechCardLockResetMessage args)
    {
        if (!_mechLock.CheckAccessWithFeedback(ent.Owner, args.Actor))
            return;

        var ev = new MechCardLockResetEvent { User = GetNetEntity(args.Actor) };
        RaiseLocalEvent(ent, ev);
    }

    private void UpdateUi(Entity<MechComponent> ent)
    {
        if (!_uiSystem.IsUiOpen(ent.Owner, MechUiKey.Key))
            return;

        ent.Comp.LastUiUpdate = _gameTiming.CurTime;

        var equipment = new List<NetEntity>();
        foreach (var equipmentEnt in ent.Comp.EquipmentContainer.ContainedEntities)
        {
            equipment.Add(GetNetEntity(equipmentEnt));
        }

        var modules = new List<NetEntity>();
        foreach (var modulesEnt in ent.Comp.ModuleContainer.ContainedEntities)
        {
            modules.Add(GetNetEntity(modulesEnt));
        }

        var fanModule = GetFanModule(ent.Comp);

        var fanActive = fanModule?.IsActive ?? false;
        var fanState = fanModule?.State ?? MechFanState.Na;
        var filterEnabled = fanModule?.FilterEnabled ?? false;
        var compressorEnabled = fanModule?.CompressorEnabled ?? false;

        var hasFanModule = false;
        var hasGasModule = false;
        var moduleUsed = 0;
        foreach (var modulesEnt in ent.Comp.ModuleContainer.ContainedEntities)
        {
            if (HasComp<MechFanModuleComponent>(modulesEnt))
                hasFanModule = true;
            if (HasComp<MechAirTankModuleComponent>(modulesEnt))
                hasGasModule = true;
            if (TryComp<MechModuleComponent>(modulesEnt, out var m))
                moduleUsed += m.Size;
        }

        var cabinPressure = 0f;
        var cabinTemperature = 0f;
        var gasAmountLiters = 0f;
        var tankPressure = 0f;
        var tankEnabled = false;
        var tankMode = MechTankMode.Supply;
        var tankTargetPressure = 0f;
        var tankMaxTargetPressure = Atmospherics.OneAtmosphere;
        MechCabinAirComponent? cabin = null;
        if (TryComp<MechCabinAirComponent>(ent.Owner, out cabin))
        {
            cabinPressure = cabin.Air.Pressure;
            cabinTemperature = cabin.Air.Temperature;
            tankEnabled = cabin.TankEnabled;
            tankMode = cabin.TankMode;
            tankTargetPressure = cabin.TargetPressure;
            tankMaxTargetPressure = cabin.MaxTargetPressure;
        }

        GasMixture? tankAir = null;
        foreach (var modulesEnt in ent.Comp.ModuleContainer.ContainedEntities)
        {
            if (!HasComp<MechAirTankModuleComponent>(modulesEnt))
                continue;

            if (!TryComp<GasTankComponent>(modulesEnt, out var tank))
                continue;

            tankAir = tank.Air;
            break;
        }

        if (tankAir != null)
        {
            // Pressure straight from tank and amount in liters.
            tankPressure = tankAir.Pressure;
            var pressure = MathF.Max(tankAir.Pressure, 0f);
            if (pressure > 0)
                gasAmountLiters = tankAir.TotalMoles * Atmospherics.R * tankAir.Temperature / pressure;
        }

        // Compute energy from battery.
        var energy = 0f;
        var maxEnergy = 0f;
        if (_powerCell.TryGetBatteryFromSlot(ent.Owner, out var battery))
        {
            energy = _battery.GetCharge(battery.Value.AsNullable());
            maxEnergy = battery.Value.Comp.MaxCharge;
        }

        var state = new MechBoundUiState
        {
            Equipment = equipment,
            Modules = modules,
            IsAirtight = ent.Comp.Airtight,
            FanActive = fanActive,
            FanState = fanState,
            FilterEnabled = filterEnabled,
            CompressorEnabled = compressorEnabled,
            TankEnabled = tankEnabled,
            TankMode = tankMode,
            TankTargetPressure = tankTargetPressure,
            TankMaxTargetPressure = tankMaxTargetPressure,
            CabinPressureLevel = cabinPressure,
            CabinTemperature = cabinTemperature,
            GasAmountLiters = gasAmountLiters,
            TankPressure = tankPressure,
            HasFanModule = hasFanModule,
            HasGasModule = hasGasModule,
            ModuleSpaceMax = ent.Comp.MaxModuleAmount,
            ModuleSpaceUsed = moduleUsed,
            PilotPresent = ent.Comp.PilotSlot.ContainedEntity != null,
            Integrity = ent.Comp.Integrity.Float(),
            MaxIntegrity = ent.Comp.MaxIntegrity.Float(),
            Energy = energy,
            MaxEnergy = maxEnergy,
            EnergyDrainRate = CalculateEnergyDrainRate(ent, cabin, fanModule),
            CanAirtight = ent.Comp.CanAirtight,
            EquipmentUsed = ent.Comp.EquipmentContainer.ContainedEntities.Count,
            MaxEquipmentAmount = ent.Comp.MaxEquipmentAmount,
            IsBroken = ent.Comp.Broken,
        };

        if (TryComp<MechLockComponent>(ent.Owner, out var lockComp))
        {
            state.DnaLockRegistered = lockComp.DnaLockRegistered;
            state.DnaLockActive = lockComp.DnaLockActive;
            state.CardLockRegistered = lockComp.CardLockRegistered;
            state.CardLockActive = lockComp.CardLockActive;
            state.OwnerDna = lockComp.OwnerDna;
            state.OwnerJobTitle = lockComp.OwnerJobTitle;
            state.IsLocked = lockComp.IsLocked;
        }

        // Collect equipment and module UI states.
        CollectEquipmentUiStates(ent.Comp.EquipmentContainer.ContainedEntities, state.EquipmentUiStates);
        CollectEquipmentUiStates(ent.Comp.ModuleContainer.ContainedEntities, state.EquipmentUiStates);

        _uiSystem.SetUiState(ent.Owner, MechUiKey.Key, state);
    }

    private float CalculateEnergyDrainRate(Entity<MechComponent> ent,
        MechCabinAirComponent? cabin,
        MechFanModuleComponent? fanModule)
    {
        var drain = 0f;

        if (ent.Comp.PilotSlot.ContainedEntity != null)
            drain += MathF.Max(0f, ent.Comp.PilotPassiveEnergyPerSecond);

        if (TryComp<InputMoverComponent>(ent.Owner, out var mover)
            && mover.CanMove
            && mover.WishDir != Vector2.Zero)
        {
            drain += MathF.Max(0f, ent.Comp.MovementEnergyPerSecond);
        }

        if (TryComp<HandheldLightComponent>(ent.Owner, out var light) && light.Activated)
            drain += MathF.Max(0f, light.Wattage);

        if (fanModule is { IsActive: true })
            drain += fanModule.EnergyConsumption.Float() * GetFanEnergyMultiplier(cabin, fanModule);

        drain += CalculateAutoRechargeDrain(ent.Comp.EquipmentContainer.ContainedEntities);

        return drain;
    }

    private MechFanModuleComponent? GetFanModule(MechComponent mech)
    {
        foreach (var modulesEnt in mech.ModuleContainer.ContainedEntities)
        {
            if (TryComp<MechFanModuleComponent>(modulesEnt, out var fan))
                return fan;
        }

        return null;
    }

    private static float GetFanEnergyMultiplier(MechCabinAirComponent? cabin, MechFanModuleComponent fanModule)
    {
        var multiplier = 1f;

        if (fanModule.FilterEnabled)
            multiplier *= MathF.Max(1f, fanModule.FilterEnergyMultiplier);

        if (cabin != null
            && fanModule.CompressorEnabled
            && cabin.TankEnabled
            && cabin.TankMode == MechTankMode.Refill)
        {
            multiplier *= MathF.Max(1f, fanModule.CompressorEnergyMultiplier);
        }

        return multiplier;
    }

    private float CalculateAutoRechargeDrain(IEnumerable<EntityUid> equipmentEntities)
    {
        var drain = 0f;

        foreach (var equipment in equipmentEntities)
        {
            if (!TryComp<MechEquipmentAutoRechargeComponent>(equipment, out var autoRecharge)
                || !autoRecharge.Enabled
                || !TryComp<BatteryComponent>(equipment, out var equipmentBattery))
            {
                continue;
            }

            var equipmentCharge = _battery.GetCharge((equipment, equipmentBattery));
            if (equipmentCharge >= equipmentBattery.MaxCharge)
                continue;

            var rechargeUnit = 1f;
            if (TryComp<BatteryAmmoProviderComponent>(equipment, out var batteryAmmo))
                rechargeUnit = MathF.Max(1f, batteryAmmo.FireCost);

            drain += rechargeUnit
                     * MathF.Max(1f, autoRecharge.EnergyMultiplier)
                     / MathF.Max(1f, autoRecharge.SecondsPerCharge);
        }

        return drain;
    }

    private void CollectEquipmentUiStates(IEnumerable<EntityUid> entities,
        Dictionary<NetEntity, BoundUserInterfaceState> states)
    {
        foreach (var entity in entities)
        {
            var ev = new MechEquipmentUiStateReadyEvent();
            RaiseLocalEvent(entity, ev);
            if (ev.States.Count == 0)
                continue;

            foreach (var (netEntity, state) in ev.States)
            {
                states[netEntity] = state;
            }
        }
    }
}
