using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Module.Components;
using Content.Shared.Mech.Systems;
using Robust.Server.GameObjects;

namespace Content.Server.Mech.Systems;

/// <summary>
/// Handles atmospheric systems for mechs including air circulation, fans, and life support.
/// </summary>
public sealed class MechAtmosphereSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedMechSystem _mech = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private const float MinExternalPressure = 0.05f;
    private const float PressureTolerance = 0.1f;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MechComponent, MechTankToggleMessage>(OnTankToggleMessage);
        SubscribeLocalEvent<MechComponent, MechTankModeMessage>(OnTankModeMessage);
        SubscribeLocalEvent<MechComponent, MechTankPressureMessage>(OnTankPressureMessage);
        SubscribeLocalEvent<MechComponent, MechFanToggleMessage>(OnFanToggleMessage);
        SubscribeLocalEvent<MechComponent, MechFilterToggleMessage>(OnFilterToggleMessage);
        SubscribeLocalEvent<MechComponent, MechFanCompressorToggleMessage>(OnFanCompressorToggleMessage);

        SubscribeLocalEvent<MechPilotComponent, InhaleLocationEvent>(OnInhale, after: [typeof(InternalsSystem)]);
        SubscribeLocalEvent<MechPilotComponent, ExhaleLocationEvent>(OnExhale, after: [typeof(InternalsSystem)]);
        SubscribeLocalEvent<MechPilotComponent, AtmosExposedGetAirEvent>(OnExpose);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MechComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            var uiDirty = false;

            uiDirty |= UpdateFanModule((uid, component), frameTime);
            uiDirty |= UpdateCabinPressure((uid, component), frameTime);

            if (uiDirty && _ui.IsUiOpen(uid, MechUiKey.Key))
                _mech.UpdateMechUi(uid);
        }
    }

    #region Cabin & Airtight

    public bool TryGetGasModuleAir(Entity<MechComponent> ent, out GasMixture? air)
    {
        air = null;
        foreach (var moduleEnt in ent.Comp.ModuleContainer.ContainedEntities)
        {
            if (!HasComp<Shared.Mech.Module.Components.MechAirTankModuleComponent>(moduleEnt))
                continue;

            if (!TryComp<GasTankComponent>(moduleEnt, out var tank))
                continue;

            air = tank.Air;
            return true;
        }

        return false;
    }

    private bool UpdateCabinPressure(Entity<MechComponent> ent, float frameTime)
    {
        if (!ent.Comp.CanAirtight || !TryComp<MechCabinAirComponent>(ent.Owner, out var cabin))
            return false;

        if (!cabin.TankEnabled
            || cabin.TankMode != MechTankMode.Supply
            || !TryGetGasModuleAir(ent, out var tankAir)
            || tankAir == null)
            return false;

        return PumpTankToCabin(tankAir, cabin.Air, cabin.TargetPressure, cabin.TankSupplyRate, frameTime);
    }

    private bool PumpTankToCabin(GasMixture tankAir,
        GasMixture cabinAir,
        float targetPressure,
        float supplyRate,
        float frameTime)
    {
        if (frameTime <= 0f
            || supplyRate <= 0f
            || tankAir.TotalMoles <= 0f
            || tankAir.Volume <= 0f
            || tankAir.Temperature <= 0f)
            return false;

        var effectiveTargetPressure = MathF.Min(targetPressure, tankAir.Pressure);
        var pressureDelta = effectiveTargetPressure - cabinAir.Pressure;
        if (pressureDelta <= PressureTolerance)
            return false;

        var targetMoles = pressureDelta * cabinAir.Volume / (tankAir.Temperature * Atmospherics.R);
        var maxTransferMoles = tankAir.TotalMoles * Math.Clamp(supplyRate * frameTime / tankAir.Volume, 0f, 1f);
        var transferMoles = MathF.Min(targetMoles, maxTransferMoles);
        if (transferMoles <= Atmospherics.GasMinMoles)
            return false;

        var removed = tankAir.Remove(transferMoles);
        _atmosphere.Merge(cabinAir, removed);
        return true;
    }

    private void OnTankToggleMessage(Entity<MechComponent> ent, ref MechTankToggleMessage args)
    {
        if (!TryComp<MechCabinAirComponent>(ent.Owner, out var cabin))
            return;

        cabin.TankEnabled = args.Enabled;
        Dirty(ent.Owner, cabin);
        _mech.UpdateMechUi(ent.Owner);
    }

    private void OnTankModeMessage(Entity<MechComponent> ent, ref MechTankModeMessage args)
    {
        if (!TryComp<MechCabinAirComponent>(ent.Owner, out var cabin))
            return;

        cabin.TankMode = args.Mode;
        Dirty(ent.Owner, cabin);
        _mech.UpdateMechUi(ent.Owner);
    }

    private void OnTankPressureMessage(Entity<MechComponent> ent, ref MechTankPressureMessage args)
    {
        if (!TryComp<MechCabinAirComponent>(ent.Owner, out var cabin))
            return;

        cabin.TargetPressure = Math.Clamp(args.Pressure, 0f, cabin.MaxTargetPressure);
        Dirty(ent.Owner, cabin);
        _mech.UpdateMechUi(ent.Owner);
    }

    private void OnInhale(Entity<MechPilotComponent> ent, ref InhaleLocationEvent args)
    {
        if (!TryComp<MechComponent>(ent.Comp.Mech, out var mechComp))
            return;

        if (mechComp.CanAirtight && TryComp<MechCabinAirComponent>(ent.Comp.Mech, out var cabin))
        {
            if (TryGetFanBreath((ent.Comp.Mech, mechComp), args.Respirator.BreathVolume, out var fanBreath))
            {
                args.Gas = fanBreath;
                return;
            }

            if (TryGetSupplyTankBreath((ent.Comp.Mech, mechComp), cabin, args.Respirator.BreathVolume, out var tankBreath))
            {
                args.Gas = tankBreath;

                if (_ui.IsUiOpen(ent.Comp.Mech, MechUiKey.Key))
                    _mech.UpdateMechUi(ent.Comp.Mech);

                return;
            }

            args.Gas = cabin.Air;
            return;
        }

        args.Gas = _atmosphere.GetContainingMixture(ent.Comp.Mech, excite: true);
    }

    private bool TryGetFanBreath(Entity<MechComponent> ent, float breathVolume, out GasMixture? breath)
    {
        breath = null;

        var fanModule = GetFanModule(ent);
        if (fanModule is not { IsActive: true })
            return false;

        if (breathVolume <= 0f)
        {
            breath = new GasMixture();
            return true;
        }

        var external = _atmosphere.GetContainingMixture(ent.Owner, excite: true);
        if (external == null || external.Pressure <= MinExternalPressure)
        {
            breath = GasMixture.SpaceGas;
            return true;
        }

        breath = external.RemoveVolume(breathVolume);
        breath.Volume = breathVolume;
        FilterExternalSample(breath, external, fanModule);
        return true;
    }

    private bool TryGetSupplyTankBreath(Entity<MechComponent> ent,
        MechCabinAirComponent cabin,
        float breathVolume,
        out GasMixture? breath)
    {
        breath = null;

        // With the fan off, supply mode acts like a built-in mask fed directly from the tank.
        // Active fan breathing is handled by TryGetFanBreath so outside hazards can matter immediately.
        if (!cabin.TankEnabled
            || cabin.TankMode != MechTankMode.Supply
            || GetFanModule(ent) is { IsActive: true }
            || !TryGetGasModuleAir(ent, out var tankAir)
            || tankAir == null)
            return false;

        breath = RemoveTankBreathVolume(tankAir, cabin.TargetPressure, breathVolume);
        return breath.TotalMoles > Atmospherics.GasMinMoles;
    }

    private static GasMixture RemoveTankBreathVolume(GasMixture tankAir, float outputPressure, float breathVolume)
    {
        var breath = new GasMixture(breathVolume);
        if (breathVolume <= 0f
            || outputPressure <= 0f
            || tankAir.TotalMoles <= 0f
            || tankAir.Temperature <= 0f)
            return breath;

        var effectivePressure = MathF.Min(outputPressure, tankAir.Pressure);
        var molesNeeded = effectivePressure * breathVolume / (Atmospherics.R * tankAir.Temperature);
        if (molesNeeded <= Atmospherics.GasMinMoles)
            return breath;

        var removed = tankAir.Remove(MathF.Min(molesNeeded, tankAir.TotalMoles));
        removed.Volume = breathVolume;
        return removed;
    }

    private void OnExhale(Entity<MechPilotComponent> ent, ref ExhaleLocationEvent args)
    {
        if (!TryComp<MechComponent>(ent.Comp.Mech, out var mechComp))
            return;

        args.Gas = GetBreathMixture((ent.Comp.Mech, mechComp));
    }

    private void OnExpose(Entity<MechPilotComponent> ent, ref AtmosExposedGetAirEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<MechComponent>(ent.Comp.Mech, out var mechComp))
            return;

        args.Gas = GetBreathMixture((ent.Comp.Mech, mechComp), args.Excite);
        args.Handled = true;
    }

    private GasMixture? GetBreathMixture(Entity<MechComponent> ent, bool excite = true)
    {
        if (ent.Comp.CanAirtight && TryComp<MechCabinAirComponent>(ent.Owner, out var cabin))
        {
            if (GetFanModule(ent) is { IsActive: true })
                return _atmosphere.GetContainingMixture(ent.Owner, excite: excite) ?? GasMixture.SpaceGas;

            return cabin.Air;
        }

        return _atmosphere.GetContainingMixture(ent.Owner, excite: excite);
    }

    #endregion

    #region Fan

    private bool UpdateFanModule(Entity<MechComponent> ent, float frameTime)
    {
        var fanModule = GetFanModule(ent);
        if (fanModule == null || !fanModule.IsActive)
        {
            if (fanModule != null)
                return SetFanState(ent, fanModule, MechFanState.Off);

            return false;
        }

        var (tankComp, tankAir) = GetGasTank(ent.Comp);
        return ProcessFanOperation(ent, fanModule, tankComp, tankAir, frameTime);
    }

    private (GasTankComponent? tank, GasMixture? air) GetGasTank(MechComponent mechComp)
    {
        foreach (var ent in mechComp.ModuleContainer.ContainedEntities)
        {
            if (TryComp<Shared.Mech.Module.Components.MechAirTankModuleComponent>(ent, out _) && TryComp<GasTankComponent>(ent, out var tank))
                return (tank, tank.Air);
        }

        return (null, null);
    }

    private bool ProcessFanOperation(Entity<MechComponent> ent,
        MechFanModuleComponent fanModule,
        GasTankComponent? tankComp,
        GasMixture? tankAir,
        float frameTime)
    {
        if (!ent.Comp.CanAirtight || !TryComp<MechCabinAirComponent>(ent.Owner, out var cabin))
        {
            var changed = fanModule.IsActive;
            fanModule.IsActive = false;
            changed |= SetFanState(ent, fanModule, MechFanState.Off);
            return changed;
        }

        var external = _atmosphere.GetContainingMixture(ent.Owner);
        var canExchangeCabin = CanExchangeCabin(cabin.Air, external);
        var canScrubCabin = HasFilterableCabinGas(ent, fanModule);
        var canRefillTank = CanRefillTank(cabin, fanModule, tankComp, tankAir, external);

        if (!canExchangeCabin && !canScrubCabin && !canRefillTank)
        {
            return SetFanState(ent, fanModule, MechFanState.Idle);
        }

        var energyMultiplier = GetFanEnergyMultiplier(cabin, fanModule);
        if (!_mech.TryChangeEnergy(ent.AsNullable(), -fanModule.EnergyConsumption * energyMultiplier * frameTime))
        {
            var changed = fanModule.IsActive;
            fanModule.IsActive = false;
            changed |= SetFanState(ent, fanModule, MechFanState.Off);
            return changed;
        }

        var success = false;
        success |= ProcessCabinExternalExchange(cabin.Air, external, fanModule, frameTime);
        success |= ProcessCabinScrubbing(ent, fanModule, external, frameTime);
        success |= ProcessTankRefill(cabin, fanModule, tankComp, tankAir, external, frameTime);

        return SetFanState(ent, fanModule, success ? MechFanState.On : MechFanState.Idle) || success;
    }

    private float GetFanEnergyMultiplier(MechCabinAirComponent cabin, MechFanModuleComponent fanModule)
    {
        var multiplier = 1f;

        if (fanModule.FilterEnabled)
            multiplier *= MathF.Max(1f, fanModule.FilterEnergyMultiplier);

        if (fanModule.CompressorEnabled
            && cabin.TankEnabled
            && cabin.TankMode == MechTankMode.Refill)
            multiplier *= MathF.Max(1f, fanModule.CompressorEnergyMultiplier);

        return multiplier;
    }

    private bool CanExchangeCabin(GasMixture cabinAir, GasMixture? external)
    {
        if (external == null || external.Pressure <= MinExternalPressure)
            return cabinAir.Pressure > MinExternalPressure;

        return external.Pressure > MinExternalPressure
               || cabinAir.Pressure > external.Pressure + PressureTolerance;
    }

    private bool ProcessCabinExternalExchange(GasMixture cabinAir,
        GasMixture? external,
        MechFanModuleComponent fanModule,
        float frameTime)
    {
        var transferVolume = fanModule.GasProcessingRate * frameTime;
        if (transferVolume <= 0)
            return false;

        if (external == null || external.Pressure <= MinExternalPressure)
            return VentCabin(cabinAir, external, transferVolume);

        if (cabinAir.Pressure > external.Pressure + PressureTolerance)
            return VentCabin(cabinAir, external, transferVolume);

        var removed = external.RemoveVolume(transferVolume);
        if (removed.TotalMoles <= 0)
            return false;

        FilterExternalSample(removed, external, fanModule);
        if (removed.TotalMoles <= 0)
            return false;

        _atmosphere.Merge(cabinAir, removed);
        return true;
    }

    private bool VentCabin(GasMixture cabinAir, GasMixture? external, float transferVolume)
    {
        if (cabinAir.Pressure <= MinExternalPressure)
            return false;

        var removed = cabinAir.RemoveVolume(transferVolume);
        if (removed.TotalMoles <= 0)
            return false;

        if (external != null)
            _atmosphere.Merge(external, removed);

        return true;
    }

    private void FilterExternalSample(GasMixture sample, GasMixture external, MechFanModuleComponent fanModule)
    {
        if (fanModule is not { FilterEnabled: true, FilterGases.Count: > 0 })
            return;

        var filtered = new GasMixture(sample.Volume) { Temperature = sample.Temperature };
        _atmosphere.ScrubInto(sample, filtered, fanModule.FilterGases);

        // The filter rejects unsafe gases back to the outside intake instead of deleting them.
        _atmosphere.Merge(external, filtered);
    }

    private bool CanRefillTank(MechCabinAirComponent cabin,
        MechFanModuleComponent fanModule,
        GasTankComponent? tankComp,
        GasMixture? tankAir,
        GasMixture? external)
    {
        if (!cabin.TankEnabled
            || cabin.TankMode != MechTankMode.Refill
            || tankComp == null
            || tankAir == null
            || external == null
            || external.Pressure <= MinExternalPressure)
            return false;

        var targetPressure = GetTankRefillTargetPressure(fanModule, tankComp, external);
        if (tankAir.Pressure >= targetPressure - PressureTolerance)
            return false;

        return HasRefillableGas(external, fanModule);
    }

    private float GetTankRefillTargetPressure(MechFanModuleComponent fanModule, GasTankComponent tankComp, GasMixture external)
    {
        var target = fanModule.CompressorEnabled
            ? fanModule.CompressorTargetPressure
            : external.Pressure;

        return MathF.Min(target, Atmospherics.MaxOutputPressure);
    }

    private bool HasRefillableGas(GasMixture external, MechFanModuleComponent fanModule)
    {
        if (!fanModule.FilterEnabled || fanModule.FilterGases.Count == 0)
            return external.TotalMoles > Atmospherics.GasMinMoles;

        foreach (var (gas, moles) in external)
        {
            if (moles > Atmospherics.GasMinMoles && !fanModule.FilterGases.Contains(gas))
                return true;
        }

        return false;
    }

    private bool ProcessTankRefill(MechCabinAirComponent cabin,
        MechFanModuleComponent fanModule,
        GasTankComponent? tankComp,
        GasMixture? tankAir,
        GasMixture? external,
        float frameTime)
    {
        if (!CanRefillTank(cabin, fanModule, tankComp, tankAir, external)
            || tankComp == null
            || tankAir == null
            || external == null)
            return false;

        var transferVolume = fanModule.GasProcessingRate * frameTime;
        if (transferVolume <= 0)
            return false;

        var removed = external.RemoveVolume(transferVolume);
        if (removed.TotalMoles <= 0)
            return false;

        FilterExternalSample(removed, external, fanModule);
        if (removed.TotalMoles <= 0)
            return false;

        var targetPressure = GetTankRefillTargetPressure(fanModule, tankComp, external);
        var pressureDelta = targetPressure - tankAir.Pressure;
        var maxTransferMoles = pressureDelta * tankAir.Volume / (removed.Temperature * Atmospherics.R);
        if (maxTransferMoles <= Atmospherics.GasMinMoles)
        {
            _atmosphere.Merge(external, removed);
            return false;
        }

        if (removed.TotalMoles > maxTransferMoles)
        {
            var tankFill = removed.Remove(maxTransferMoles);
            _atmosphere.Merge(tankAir, tankFill);
            _atmosphere.Merge(external, removed);
            return tankFill.TotalMoles > 0;
        }

        _atmosphere.Merge(tankAir, removed);
        return true;
    }

    private bool HasFilterableCabinGas(Entity<MechComponent> ent, MechFanModuleComponent fanModule)
    {
        if (!ent.Comp.CanAirtight || !fanModule.FilterEnabled || fanModule.FilterGases.Count == 0)
            return false;

        if (!TryComp<MechCabinAirComponent>(ent.Owner, out var cabin))
            return false;

        foreach (var gas in fanModule.FilterGases)
        {
            if (cabin.Air.GetMoles(gas) > Atmospherics.GasMinMoles)
                return true;
        }

        return false;
    }

    private bool ProcessCabinScrubbing(Entity<MechComponent> ent,
        MechFanModuleComponent fanModule,
        GasMixture? external,
        float frameTime)
    {
        if (!HasFilterableCabinGas(ent, fanModule) || !TryComp<MechCabinAirComponent>(ent.Owner, out var cabin))
            return false;

        var transferVolume = fanModule.GasProcessingRate * frameTime;
        if (transferVolume <= 0)
            return false;

        var removed = cabin.Air.RemoveVolume(transferVolume);
        if (removed.TotalMoles <= 0)
            return false;

        var filtered = new GasMixture(removed.Volume) { Temperature = removed.Temperature };
        _atmosphere.ScrubInto(removed, filtered, fanModule.FilterGases);

        // Return the cleaned, breathable portion to the cabin. Scrubbed gases are vented or discarded into space.
        _atmosphere.Merge(cabin.Air, removed);
        if (filtered.TotalMoles <= 0)
            return false;

        if (external != null)
            _atmosphere.Merge(external, filtered);

        return true;
    }

    private bool SetFanState(Entity<MechComponent> ent, MechFanModuleComponent fanModule, MechFanState state)
    {
        if (fanModule.State == state)
            return false;

        fanModule.State = state;
        Dirty(ent);
        return true;
    }

    private void OnFanToggleMessage(Entity<MechComponent> ent, ref MechFanToggleMessage args)
    {
        var fanModule = GetFanModule(ent);
        if (fanModule == null)
            return;

        if (args.IsActive && !_mech.HasUsableEnergy(ent.AsNullable()))
        {
            fanModule.IsActive = false;
            SetFanState(ent, fanModule, MechFanState.Off);
            _mech.UpdateMechUi(ent.Owner);
            return;
        }

        fanModule.IsActive = args.IsActive;

        // Set the correct state based on the toggle.
        var newState = args.IsActive ? MechFanState.On : MechFanState.Off;
        if (fanModule.State != newState)
        {
            fanModule.State = newState;
            Dirty(ent);
        }

        _mech.UpdateMechUi(ent.Owner);
    }

    private void OnFilterToggleMessage(Entity<MechComponent> ent, ref MechFilterToggleMessage args)
    {
        var fanModule = GetFanModule(ent);
        if (fanModule == null)
            return;

        fanModule.FilterEnabled = args.Enabled;
        Dirty(ent);
        _mech.UpdateMechUi(ent.Owner);
    }

    private void OnFanCompressorToggleMessage(Entity<MechComponent> ent, ref MechFanCompressorToggleMessage args)
    {
        var fanModule = GetFanModule(ent);
        if (fanModule == null)
            return;

        fanModule.CompressorEnabled = args.Enabled;
        Dirty(ent);
        _mech.UpdateMechUi(ent.Owner);
    }

    private MechFanModuleComponent? GetFanModule(Entity<MechComponent> ent)
    {
        foreach (var entModule in ent.Comp.ModuleContainer.ContainedEntities)
        {
            if (TryComp<MechFanModuleComponent>(entModule, out var fanModule))
                return fanModule;
        }

        return null;
    }

    #endregion
}
