using System;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Shared._WH40K.Vehicle.Fuel;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Power;
using Content.Shared.Popups;
using Content.Shared.Repairable;
using Content.Shared.UserInterface;
using Content.Shared.Vehicle.Components;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Vehicle.Fuel;

public sealed partial class WH40KVehicleFuelSystem : EntitySystem
{
    [Dependency] private  SharedContainerSystem _containers = default!;
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  PopupSystem _popup = default!;
    [Dependency] private  SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private  SharedWH40KVehicleFuelSystem _sharedFuel = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  UserInterfaceSystem _ui = default!;
    [Dependency] private  EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KVehicleEngineComponent, WH40KToggleVehicleEngineActionEvent>(OnToggleEngineAction);

        SubscribeLocalEvent<WH40KVehicleFuelComponent, SolutionChangedEvent>(OnVehicleFuelSolutionChanged);

        SubscribeLocalEvent<WH40KVehicleHandlingHealthComponent, ComponentStartup>(OnHandlingStartup);
        SubscribeLocalEvent<WH40KVehicleHandlingHealthComponent, DamageDealtEvent>(OnDamageDealt);
        SubscribeLocalEvent<WH40KVehicleHandlingHealthComponent, RepairedEvent>(OnRepaired);

        SubscribeLocalEvent<WH40KVehicleFuelTerminalComponent, MapInitEvent>(OnTerminalMapInit);
        SubscribeLocalEvent<WH40KVehicleFuelTerminalComponent, PowerChangedEvent>(OnTerminalPowerChanged);
        SubscribeLocalEvent<WH40KVehicleFuelTerminalComponent, ExaminedEvent>(OnTerminalExamined);

        Subs.BuiEvents<WH40KVehicleFuelTerminalComponent>(WH40KVehicleFuelTerminalUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnTerminalUiOpened);
            subs.Event<WH40KVehicleFuelTerminalToggleAutoIntakeMessage>(OnToggleAutoIntake);
            subs.Event<WH40KVehicleFuelTerminalToggleAutoRefuelMessage>(OnToggleAutoRefuel);
        });
    }

    private void OnVehicleFuelSolutionChanged(Entity<WH40KVehicleFuelComponent> ent, ref SolutionChangedEvent args)
    {
        if (args.Solution.Comp.Id != ent.Comp.FuelSolution)
            return;

        _sharedFuel.SyncFuelSnapshot(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var engineQuery = EntityQueryEnumerator<WH40KVehicleEngineComponent, WH40KVehicleFuelComponent>();
        while (engineQuery.MoveNext(out var uid, out var engine, out var fuel))
        {
            if (engine.State == WH40KVehicleEngineState.Starting &&
                engine.StartingCompleteAt != TimeSpan.Zero &&
                now >= engine.StartingCompleteAt)
            {
                CompleteEngineStart(uid, engine, fuel);
            }

            if (engine.State != WH40KVehicleEngineState.Running)
                continue;

            while (now >= engine.NextFuelTickAt)
            {
                engine.NextFuelTickAt += engine.FuelTickInterval;
                if (!ConsumeFuelTick(uid, fuel, engine))
                    break;
            }
        }

        var terminalQuery = EntityQueryEnumerator<WH40KVehicleFuelTerminalComponent>();
        while (terminalQuery.MoveNext(out var uid, out var terminal))
        {
            var powered = IsTerminalPowered(uid);

            if (powered)
            {
                while (now >= terminal.NextTransferAt)
                {
                    terminal.NextTransferAt += terminal.TransferInterval;
                    if (ProcessTerminalTick(uid, terminal))
                        terminal.NextUiRefresh = TimeSpan.Zero;
                }
            }
            else
            {
                terminal.NextTransferAt = now + terminal.TransferInterval;
            }

            if (!_ui.IsUiOpen(uid, WH40KVehicleFuelTerminalUiKey.Key))
                continue;

            if (terminal.NextUiRefresh > now)
                continue;

            terminal.NextUiRefresh = now + terminal.UiRefreshInterval;
            UpdateTerminalUi(uid, terminal);
        }
    }

    private void OnHandlingStartup(Entity<WH40KVehicleHandlingHealthComponent> ent, ref ComponentStartup args)
    {
        UpdateServiceState(ent.Owner, ent.Comp);
    }

    private void OnDamageDealt(Entity<WH40KVehicleHandlingHealthComponent> ent, ref DamageDealtEvent args)
    {
        UpdateServiceState(ent.Owner, ent.Comp);
    }

    private void OnRepaired(Entity<WH40KVehicleHandlingHealthComponent> ent, ref RepairedEvent args)
    {
        UpdateServiceState(ent.Owner, ent.Comp);
    }

    private void OnToggleEngineAction(Entity<WH40KVehicleEngineComponent> ent, ref WH40KToggleVehicleEngineActionEvent args)
    {
        if (args.Handled || !TryComp(ent.Owner, out WH40KVehicleFuelComponent? fuel))
            return;

        args.Handled = true;

        if (ent.Comp.State is WH40KVehicleEngineState.Running or WH40KVehicleEngineState.Starting)
        {
            _sharedFuel.SetEngineState(ent.Owner, WH40KVehicleEngineState.Off, ent.Comp);
            ShowEnginePopup(ent.Owner, args.Performer, "wh40k-vehicle-engine-popup-off");
            return;
        }

        if (!CanStartVehicle(ent.Owner, fuel, out var failureKey))
        {
            ShowEnginePopup(ent.Owner, args.Performer, failureKey, PopupType.MediumCaution);
            return;
        }

        ent.Comp.StartingCompleteAt = _timing.CurTime + ent.Comp.StartingDelay;
        ent.Comp.NextFuelTickAt = _timing.CurTime + ent.Comp.FuelTickInterval;
        _sharedFuel.SetEngineState(ent.Owner, WH40KVehicleEngineState.Starting, ent.Comp);
        ShowEnginePopup(ent.Owner, args.Performer, "wh40k-vehicle-engine-popup-starting");
    }

    private void OnTerminalMapInit(Entity<WH40KVehicleFuelTerminalComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTransferAt = _timing.CurTime + ent.Comp.TransferInterval;
        ent.Comp.NextUiRefresh = TimeSpan.Zero;
    }

    private void OnTerminalPowerChanged(Entity<WH40KVehicleFuelTerminalComponent> ent, ref PowerChangedEvent args)
    {
        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        if (_ui.IsUiOpen(ent.Owner, WH40KVehicleFuelTerminalUiKey.Key))
            UpdateTerminalUi(ent.Owner, ent.Comp);
    }

    private void OnTerminalExamined(Entity<WH40KVehicleFuelTerminalComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.BufferSolution, out _, out var solution))
            return;

        var amount = solution.GetTotalPrototypeQuantity(ent.Comp.FuelReagent).Float();
        var capacity = (float) solution.MaxVolume;

        using (args.PushGroup(nameof(WH40KVehicleFuelTerminalComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-vehicle-terminal-examine-buffer",
                ("current", MathF.Round(amount)),
                ("capacity", MathF.Round(capacity))));
            args.PushMarkup(Loc.GetString(
                "wh40k-vehicle-terminal-examine-modes",
                ("intake", Loc.GetString(ent.Comp.AutoIntakeEnabled
                    ? "wh40k-vehicle-toggle-on"
                    : "wh40k-vehicle-toggle-off")),
                ("refuel", Loc.GetString(ent.Comp.AutoRefuelEnabled
                    ? "wh40k-vehicle-toggle-on"
                    : "wh40k-vehicle-toggle-off"))));
        }
    }

    private void OnTerminalUiOpened(Entity<WH40KVehicleFuelTerminalComponent> ent, ref BoundUIOpenedEvent args)
    {
        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateTerminalUi(ent.Owner, ent.Comp);
    }

    private void OnToggleAutoIntake(Entity<WH40KVehicleFuelTerminalComponent> ent, ref WH40KVehicleFuelTerminalToggleAutoIntakeMessage args)
    {
        ent.Comp.AutoIntakeEnabled = args.Enabled;
        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateTerminalUi(ent.Owner, ent.Comp);
    }

    private void OnToggleAutoRefuel(Entity<WH40KVehicleFuelTerminalComponent> ent, ref WH40KVehicleFuelTerminalToggleAutoRefuelMessage args)
    {
        ent.Comp.AutoRefuelEnabled = args.Enabled;
        ent.Comp.NextUiRefresh = TimeSpan.Zero;
        UpdateTerminalUi(ent.Owner, ent.Comp);
    }

    private bool CanStartVehicle(EntityUid uid, WH40KVehicleFuelComponent fuel, out string failureKey)
    {
        failureKey = string.Empty;

        if (!HasIgnitionKey(uid))
        {
            failureKey = "wh40k-vehicle-engine-popup-no-key";
            return false;
        }

        if (fuel.FuelLevel <= 0.01f)
        {
            failureKey = "wh40k-vehicle-engine-popup-no-fuel";
            return false;
        }

        if (TryComp(uid, out WH40KVehicleHandlingHealthComponent? handling) &&
            handling.ServiceState == WH40KVehicleServiceState.Disabled)
        {
            failureKey = "wh40k-vehicle-engine-popup-disabled";
            return false;
        }

        return true;
    }

    private bool HasIgnitionKey(EntityUid uid)
    {
        if (!TryComp(uid, out GenericKeyedVehicleComponent? keyed))
            return true;

        if (!_containers.TryGetContainer(uid, keyed.ContainerId, out var container))
            return false;

        foreach (var contained in container.ContainedEntities)
        {
            if (_whitelist.IsWhitelistPass(keyed.KeyWhitelist, contained))
                return true;
        }

        return false;
    }

    private void CompleteEngineStart(EntityUid uid, WH40KVehicleEngineComponent engine, WH40KVehicleFuelComponent fuel)
    {
        if (TryComp(uid, out WH40KVehicleHandlingHealthComponent? handling) &&
            handling.ServiceState == WH40KVehicleServiceState.Disabled)
        {
            _sharedFuel.SetEngineState(uid, WH40KVehicleEngineState.Disabled, engine);
            ShowEnginePopupToOperator(uid, "wh40k-vehicle-engine-popup-disabled-while-running", PopupType.MediumCaution);
            return;
        }

        if (fuel.FuelLevel <= 0.01f)
        {
            _sharedFuel.SetEngineState(uid, WH40KVehicleEngineState.Stalled, engine);
            ShowEnginePopupToOperator(uid, "wh40k-vehicle-engine-popup-stalled-no-fuel", PopupType.MediumCaution);
            return;
        }

        _sharedFuel.SetEngineState(uid, WH40KVehicleEngineState.Running, engine);
        ShowEnginePopupToOperator(uid, "wh40k-vehicle-engine-popup-running");
    }

    private bool ConsumeFuelTick(EntityUid uid, WH40KVehicleFuelComponent fuel, WH40KVehicleEngineComponent engine)
    {
        if (!_solutions.TryGetSolution(uid, fuel.FuelSolution, out var solutionEnt, out _))
        {
            _sharedFuel.SetEngineState(uid, WH40KVehicleEngineState.Stalled, engine);
            ShowEnginePopupToOperator(uid, "wh40k-vehicle-engine-popup-stalled-no-fuel", PopupType.MediumCaution);
            return false;
        }

        var burnPerSecond = _sharedFuel.GetFuelBurnPerSecond(fuel);
        var toBurn = FixedPoint2.New(burnPerSecond * (float) engine.FuelTickInterval.TotalSeconds);
        _solutions.RemoveReagent(solutionEnt.Value, fuel.FuelReagent, toBurn);
        _sharedFuel.SyncFuelSnapshot(uid, fuel);

        if (fuel.FuelLevel > 0.01f)
            return true;

        _sharedFuel.SetEngineState(uid, WH40KVehicleEngineState.Stalled, engine);
        ShowEnginePopupToOperator(uid, "wh40k-vehicle-engine-popup-stalled-no-fuel", PopupType.MediumCaution);
        return false;
    }

    private void UpdateServiceState(EntityUid uid, WH40KVehicleHandlingHealthComponent handling, DamageableComponent? damageable = null)
    {
#pragma warning disable CS0618
        var damage = damageable != null
            ? _damageable.GetTotalDamage((uid, damageable)).Float()
            : _damageable.GetTotalDamage(uid).Float();
#pragma warning restore CS0618
        var ratio = handling.MaxDamage <= 0f
            ? 1f
            : Math.Clamp(1f - damage / handling.MaxDamage, 0f, 1f);

        var nextState = damage >= handling.DisabledDamage
            ? WH40KVehicleServiceState.Disabled
            : damage >= handling.CriticalDamage
                ? WH40KVehicleServiceState.Critical
                : damage >= handling.WornDamage
                    ? WH40KVehicleServiceState.Worn
                    : WH40KVehicleServiceState.Nominal;

        var changed = handling.ServiceState != nextState || !MathHelper.CloseTo(handling.ServiceRatio, ratio);
        handling.ServiceState = nextState;
        handling.ServiceRatio = ratio;

        if (changed)
            Dirty(uid, handling);

        if (TryComp(uid, out WH40KVehicleEngineComponent? engine))
        {
            if (nextState == WH40KVehicleServiceState.Disabled &&
                engine.State != WH40KVehicleEngineState.Disabled)
            {
                var wasActive = engine.State is WH40KVehicleEngineState.Running or WH40KVehicleEngineState.Starting;
                _sharedFuel.SetEngineState(uid, WH40KVehicleEngineState.Disabled, engine);
                if (wasActive)
                    ShowEnginePopupToOperator(uid, "wh40k-vehicle-engine-popup-disabled-while-running", PopupType.MediumCaution);
            }
            else if (nextState != WH40KVehicleServiceState.Disabled &&
                     engine.State == WH40KVehicleEngineState.Disabled)
            {
                _sharedFuel.SetEngineState(uid, WH40KVehicleEngineState.Off, engine);
            }
        }

        _sharedFuel.RefreshVehicleRuntime(uid);
    }

    private void ShowEnginePopup(EntityUid vehicle, EntityUid recipient, string locKey, PopupType type = PopupType.Medium)
    {
        _popup.PopupEntity(Loc.GetString(locKey), vehicle, recipient, type);
    }

    private void ShowEnginePopupToOperator(EntityUid vehicle, string locKey, PopupType type = PopupType.Medium)
    {
        if (!TryComp(vehicle, out VehicleComponent? vehicleComponent) ||
            vehicleComponent.Operator is not { } operatorUid)
        {
            return;
        }

        ShowEnginePopup(vehicle, operatorUid, locKey, type);
    }

    private bool ProcessTerminalTick(EntityUid uid, WH40KVehicleFuelTerminalComponent terminal)
    {
        var changed = false;

        if (terminal.AutoIntakeEnabled &&
            TryFindFuelSource(uid, terminal, out var sourceUid, out _, out _) &&
            sourceUid is { } source)
        {
            changed |= TransferFuelIntoTerminal(uid, terminal, source);
        }

        if (terminal.AutoRefuelEnabled &&
            TryFindVehicle(uid, terminal, out var vehicleUid, out _, out _, out _, out _) &&
            vehicleUid is { } vehicle)
        {
            changed |= TransferFuelIntoVehicle(uid, terminal, vehicle);
        }

        return changed;
    }

    private bool TransferFuelIntoTerminal(EntityUid terminalUid, WH40KVehicleFuelTerminalComponent terminal, EntityUid sourceUid)
    {
        if (!_solutions.TryGetDrainableSolution(sourceUid, out var sourceSoln, out var sourceSolution) ||
            !_solutions.TryGetSolution(terminalUid, terminal.BufferSolution, out var bufferSoln, out var bufferSolution))
        {
            return false;
        }

        var availableSource = sourceSolution.GetTotalPrototypeQuantity(terminal.FuelReagent);
        var availableBuffer = bufferSolution.AvailableVolume;
        var requested = FixedPoint2.New(terminal.IntakeRatePerSecond * (float) terminal.TransferInterval.TotalSeconds);
        var transfer = FixedPoint2.Min(requested, FixedPoint2.Min(availableSource, availableBuffer));

        if (transfer <= FixedPoint2.Zero)
            return false;

        if (!_solutions.TryAddReagent(bufferSoln.Value, terminal.FuelReagent, transfer, out var accepted) ||
            accepted <= FixedPoint2.Zero)
        {
            return false;
        }

        _solutions.RemoveReagent(sourceSoln.Value, terminal.FuelReagent, accepted);
        return true;
    }

    private bool TransferFuelIntoVehicle(EntityUid terminalUid, WH40KVehicleFuelTerminalComponent terminal, EntityUid vehicleUid)
    {
        if (!_solutions.TryGetSolution(terminalUid, terminal.BufferSolution, out var bufferSoln, out var bufferSolution) ||
            !TryComp(vehicleUid, out WH40KVehicleFuelComponent? fuel) ||
            !_solutions.TryGetRefillableSolution(vehicleUid, out var vehicleSoln, out var vehicleSolution))
        {
            return false;
        }

        var availableBuffer = bufferSolution.GetTotalPrototypeQuantity(terminal.FuelReagent);
        var availableVehicle = vehicleSolution.AvailableVolume;
        var requested = FixedPoint2.New(terminal.RefuelRatePerSecond * (float) terminal.TransferInterval.TotalSeconds);
        var transfer = FixedPoint2.Min(requested, FixedPoint2.Min(availableBuffer, availableVehicle));

        if (transfer <= FixedPoint2.Zero)
            return false;

        if (!_solutions.TryAddReagent(vehicleSoln.Value, terminal.FuelReagent, transfer, out var accepted) ||
            accepted <= FixedPoint2.Zero)
        {
            return false;
        }

        _solutions.RemoveReagent(bufferSoln.Value, terminal.FuelReagent, accepted);
        _sharedFuel.SyncFuelSnapshot(vehicleUid, fuel);
        return true;
    }

    private bool TryFindFuelSource(
        EntityUid uid,
        WH40KVehicleFuelTerminalComponent terminal,
        out EntityUid? sourceUid,
        out float amount,
        out float capacity)
    {
        sourceUid = null;
        amount = 0f;
        capacity = 0f;

        foreach (var candidate in _lookup.GetEntitiesInRange(uid, terminal.ScanRange, LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (candidate == uid || HasComp<WH40KVehicleFuelComponent>(candidate))
                continue;

            if (!_solutions.TryGetDrainableSolution(candidate, out _, out var solution))
                continue;

            var qty = solution.GetTotalPrototypeQuantity(terminal.FuelReagent).Float();
            if (qty <= 0.01f)
                continue;

            sourceUid = candidate;
            amount = qty;
            capacity = (float) solution.MaxVolume;
            return true;
        }

        return false;
    }

    private bool TryFindVehicle(
        EntityUid uid,
        WH40KVehicleFuelTerminalComponent terminal,
        out EntityUid? vehicleUid,
        out float fuelAmount,
        out float fuelCapacity,
        out WH40KVehicleEngineState engineState,
        out WH40KVehicleServiceState serviceState)
    {
        vehicleUid = null;
        fuelAmount = 0f;
        fuelCapacity = 0f;
        engineState = WH40KVehicleEngineState.Off;
        serviceState = WH40KVehicleServiceState.Nominal;

        foreach (var candidate in _lookup.GetEntitiesInRange(uid, terminal.ScanRange, LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (!TryComp(candidate, out WH40KVehicleFuelComponent? fuel) ||
                !_solutions.TryGetSolution(candidate, fuel.FuelSolution, out _, out var solution))
            {
                continue;
            }

            if (solution.AvailableVolume <= FixedPoint2.Zero)
                continue;

            vehicleUid = candidate;
            fuelAmount = fuel.FuelLevel;
            fuelCapacity = fuel.FuelCapacity;
            engineState = TryComp(candidate, out WH40KVehicleEngineComponent? engine)
                ? engine.State
                : WH40KVehicleEngineState.Off;
            serviceState = TryComp(candidate, out WH40KVehicleHandlingHealthComponent? handling)
                ? handling.ServiceState
                : WH40KVehicleServiceState.Nominal;
            return true;
        }

        return false;
    }

    private void UpdateTerminalUi(EntityUid uid, WH40KVehicleFuelTerminalComponent terminal)
    {
        if (!_solutions.TryGetSolution(uid, terminal.BufferSolution, out _, out var bufferSolution))
            return;

        var powered = IsTerminalPowered(uid);
        var bufferAmount = bufferSolution.GetTotalPrototypeQuantity(terminal.FuelReagent).Float();
        var bufferCapacity = (float) bufferSolution.MaxVolume;

        var sourceName = string.Empty;
        var sourceAmount = 0f;
        var sourceCapacity = 0f;
        if (TryFindFuelSource(uid, terminal, out var sourceUid, out sourceAmount, out sourceCapacity) &&
            sourceUid != null)
        {
            sourceName = Name(sourceUid.Value);
        }

        var vehicleName = string.Empty;
        var vehicleFuel = 0f;
        var vehicleCapacity = 0f;
        var engineState = WH40KVehicleEngineState.Off;
        var serviceState = WH40KVehicleServiceState.Nominal;
        var serviceRatio = 0f;

        if (TryFindVehicle(uid, terminal, out var vehicleUid, out vehicleFuel, out vehicleCapacity, out engineState, out serviceState) &&
            vehicleUid != null)
        {
            vehicleName = Name(vehicleUid.Value);
            if (TryComp(vehicleUid.Value, out WH40KVehicleHandlingHealthComponent? handling))
                serviceRatio = handling.ServiceRatio;
        }

        var state = new WH40KVehicleFuelTerminalBuiState(
            terminal.Account,
            powered,
            terminal.AutoIntakeEnabled,
            terminal.AutoRefuelEnabled,
            bufferAmount,
            bufferCapacity,
            sourceName,
            sourceAmount,
            sourceCapacity,
            vehicleName,
            vehicleFuel,
            vehicleCapacity,
            engineState,
            serviceState,
            serviceRatio);

        _ui.SetUiState(uid, WH40KVehicleFuelTerminalUiKey.Key, state);
    }

    private bool IsTerminalPowered(EntityUid uid)
    {
        return !TryComp(uid, out ApcPowerReceiverComponent? power) || power.Powered;
    }
}
