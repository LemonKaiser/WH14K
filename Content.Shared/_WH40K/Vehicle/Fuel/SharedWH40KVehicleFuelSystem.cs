using System;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using JetBrains.Annotations;
using Robust.Shared.GameStates;
using Robust.Shared.Containers;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Shared._WH40K.Vehicle.Fuel;

[UsedImplicitly]
public sealed partial class SharedWH40KVehicleFuelSystem : EntitySystem
{
    [Dependency] private  SharedActionsSystem _actions = default!;
    [Dependency] private  SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private  MovementSpeedModifierSystem _movement = default!;
    [Dependency] private  SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private  VehicleSystem _vehicle = default!;
    [Dependency] private  IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KVehicleFuelComponent, MapInitEvent>(OnFuelMapInit);
        SubscribeLocalEvent<WH40KVehicleFuelComponent, AfterAutoHandleStateEvent>(OnFuelHandleState);
        SubscribeLocalEvent<WH40KVehicleFuelComponent, VehicleCanRunEvent>(OnFuelCanRun);
        SubscribeLocalEvent<WH40KVehicleFuelComponent, ExaminedEvent>(OnFuelExamined);

        SubscribeLocalEvent<WH40KVehicleEngineComponent, MapInitEvent>(OnEngineMapInit);
        SubscribeLocalEvent<WH40KVehicleEngineComponent, AfterAutoHandleStateEvent>(OnEngineHandleState);
        SubscribeLocalEvent<WH40KVehicleEngineComponent, GetItemActionsEvent>(OnEngineGetActions);
        SubscribeLocalEvent<WH40KVehicleEngineComponent, ExaminedEvent>(OnEngineExamined);
        SubscribeLocalEvent<WH40KVehicleEngineComponent, EntInsertedIntoContainerMessage>(OnEngineKeyInserted, after: [typeof(VehicleSystem)]);
        SubscribeLocalEvent<WH40KVehicleEngineComponent, EntRemovedFromContainerMessage>(OnEngineKeyRemoved, after: [typeof(VehicleSystem)]);

        SubscribeLocalEvent<WH40KVehicleHandlingHealthComponent, MapInitEvent>(OnHandlingMapInit);
        SubscribeLocalEvent<WH40KVehicleHandlingHealthComponent, AfterAutoHandleStateEvent>(OnHandlingHandleState);
        SubscribeLocalEvent<WH40KVehicleHandlingHealthComponent, ExaminedEvent>(OnHandlingExamined);
        SubscribeLocalEvent<WH40KVehicleHandlingHealthComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<WH40KVehicleHandlingHealthComponent, RefreshFrictionModifiersEvent>(OnRefreshFriction);
    }

    private void OnFuelMapInit(Entity<WH40KVehicleFuelComponent> ent, ref MapInitEvent args)
    {
        SyncFuelSnapshot(ent.Owner, ent.Comp);
        RefreshVehicleRuntime(ent.Owner);
    }

    private void OnFuelHandleState(Entity<WH40KVehicleFuelComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshVehicleRuntime(ent.Owner);
    }

    private void OnEngineMapInit(Entity<WH40KVehicleEngineComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.NextFuelTickAt == TimeSpan.Zero)
            ent.Comp.NextFuelTickAt = _timing.CurTime + ent.Comp.FuelTickInterval;

        SetEngineState(ent.Owner, ent.Comp.State, ent.Comp, refreshRuntime: false);
        RefreshVehicleRuntime(ent.Owner);
    }

    private void OnEngineHandleState(Entity<WH40KVehicleEngineComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        SetEngineState(ent.Owner, ent.Comp.State, ent.Comp, refreshRuntime: true);
    }

    private void OnHandlingMapInit(Entity<WH40KVehicleHandlingHealthComponent> ent, ref MapInitEvent args)
    {
        RefreshVehicleRuntime(ent.Owner);
    }

    private void OnHandlingHandleState(Entity<WH40KVehicleHandlingHealthComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshVehicleRuntime(ent.Owner);
    }

    private void OnFuelCanRun(Entity<WH40KVehicleFuelComponent> ent, ref VehicleCanRunEvent args)
    {
        if (!args.CanRun)
            return;

        if (!TryComp(ent.Owner, out WH40KVehicleEngineComponent? engine) ||
            engine.State != WH40KVehicleEngineState.Running ||
            ent.Comp.FuelLevel <= 0.01f)
        {
            args = args with { CanRun = false };
            return;
        }

        if (TryComp(ent.Owner, out WH40KVehicleHandlingHealthComponent? handling) &&
            handling.ServiceState == WH40KVehicleServiceState.Disabled)
        {
            args = args with { CanRun = false };
        }
    }

    private void OnEngineGetActions(Entity<WH40KVehicleEngineComponent> ent, ref GetItemActionsEvent args)
    {
        if (string.IsNullOrWhiteSpace(ent.Comp.ToggleAction))
            return;

        args.AddAction(ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction, ent.Owner);
    }

    private void OnEngineKeyInserted(Entity<WH40KVehicleEngineComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!TryComp(ent.Owner, out GenericKeyedVehicleComponent? keyed) ||
            args.Container.ID != keyed.ContainerId)
        {
            return;
        }

        _ambient.SetAmbience(ent.Owner, ent.Comp.State == WH40KVehicleEngineState.Running);
    }

    private void OnEngineKeyRemoved(Entity<WH40KVehicleEngineComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!TryComp(ent.Owner, out GenericKeyedVehicleComponent? keyed) ||
            args.Container.ID != keyed.ContainerId)
        {
            return;
        }

        if (ent.Comp.State is WH40KVehicleEngineState.Running or WH40KVehicleEngineState.Starting)
            SetEngineState(ent.Owner, WH40KVehicleEngineState.Off, ent.Comp);
        else
            _ambient.SetAmbience(ent.Owner, false);
    }

    private void OnFuelExamined(Entity<WH40KVehicleFuelComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(WH40KVehicleFuelComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-vehicle-examine-fuel",
                ("percent", MathF.Round(GetFuelFraction(ent.Comp) * 100f)),
                ("current", MathF.Round(ent.Comp.FuelLevel)),
                ("capacity", MathF.Round(ent.Comp.FuelCapacity))));
            args.PushMarkup(Loc.GetString(
                "wh40k-vehicle-examine-runtime",
                ("remaining", FormatRuntime(ent.Comp.FuelLevel))));
        }
    }

    private void OnEngineExamined(Entity<WH40KVehicleEngineComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(WH40KVehicleEngineComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-vehicle-examine-engine",
                ("state", Loc.GetString(WH40KVehicleFuelLoc.GetEngineStateLocKey(ent.Comp.State)))));
        }
    }

    private void OnHandlingExamined(Entity<WH40KVehicleHandlingHealthComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(WH40KVehicleHandlingHealthComponent)))
        {
            args.PushMarkup(Loc.GetString(
                "wh40k-vehicle-examine-service",
                ("state", Loc.GetString(WH40KVehicleFuelLoc.GetServiceStateLocKey(ent.Comp.ServiceState))),
                ("integrity", MathF.Round(ent.Comp.ServiceRatio * 100f))));
        }
    }

    private void OnRefreshMovementSpeed(Entity<WH40KVehicleHandlingHealthComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var modifier = 1f;

        if (TryComp(ent.Owner, out WH40KVehicleFuelComponent? fuel))
        {
            var fraction = GetFuelFraction(fuel);
            if (fraction <= fuel.CriticalFuelThreshold)
                modifier *= ent.Comp.CriticalFuelSpeedModifier;
            else if (fraction <= fuel.LowFuelThreshold)
                modifier *= ent.Comp.LowFuelSpeedModifier;
        }

        modifier *= ent.Comp.ServiceState switch
        {
            WH40KVehicleServiceState.Worn => ent.Comp.WornSpeedModifier,
            WH40KVehicleServiceState.Critical => ent.Comp.CriticalSpeedModifier,
            WH40KVehicleServiceState.Disabled => 0.5f,
            _ => 1f
        };

        args.ModifySpeed(modifier);
    }

    private void OnRefreshFriction(Entity<WH40KVehicleHandlingHealthComponent> ent, ref RefreshFrictionModifiersEvent args)
    {
        var accelerationModifier = 1f;

        if (TryComp(ent.Owner, out WH40KVehicleFuelComponent? fuel))
        {
            var fraction = GetFuelFraction(fuel);
            if (fraction <= fuel.CriticalFuelThreshold)
                accelerationModifier *= ent.Comp.CriticalFuelAccelerationModifier;
            else if (fraction <= fuel.LowFuelThreshold)
                accelerationModifier *= ent.Comp.LowFuelAccelerationModifier;
        }

        accelerationModifier *= ent.Comp.ServiceState switch
        {
            WH40KVehicleServiceState.Worn => ent.Comp.WornAccelerationModifier,
            WH40KVehicleServiceState.Critical => ent.Comp.CriticalAccelerationModifier,
            WH40KVehicleServiceState.Disabled => 0.35f,
            _ => 1f
        };

        args.ModifyAcceleration(accelerationModifier);
    }

    public void RefreshVehicleRuntime(EntityUid uid)
    {
        if (TryComp(uid, out MovementSpeedModifierComponent? movement))
        {
            _movement.RefreshMovementSpeedModifiers(uid, movement);
            _movement.RefreshFrictionModifiers(uid, movement);
        }

        if (TryComp(uid, out VehicleComponent? vehicle))
            _vehicle.RefreshCanRun((uid, vehicle));
    }

    public void SyncFuelSnapshot(EntityUid uid, WH40KVehicleFuelComponent? fuel = null, SolutionManagerComponent? manager = null)
    {
        if (!Resolve(uid, ref fuel, false))
            return;

        if (!_solutions.TryGetSolution((uid, manager), fuel.FuelSolution, out _, out var solution))
            return;

        var amount = solution.GetTotalPrototypeQuantity(fuel.FuelReagent).Float();
        var capacity = (float) solution.MaxVolume;

        if (MathHelper.CloseTo(amount, fuel.FuelLevel) &&
            MathHelper.CloseTo(capacity, fuel.FuelCapacity))
        {
            return;
        }

        fuel.FuelLevel = amount;
        fuel.FuelCapacity = capacity;
        Dirty(uid, fuel);
        RefreshVehicleRuntime(uid);
    }

    public void SetEngineState(
        EntityUid uid,
        WH40KVehicleEngineState state,
        WH40KVehicleEngineComponent? engine = null,
        bool refreshRuntime = true)
    {
        if (!Resolve(uid, ref engine, false))
            return;

        var changed = engine.State != state;
        engine.State = state;

        if (state != WH40KVehicleEngineState.Starting)
            engine.StartingCompleteAt = TimeSpan.Zero;

        _ambient.SetAmbience(uid, state == WH40KVehicleEngineState.Running);

        if (engine.ToggleActionEntity != null)
            _actions.SetToggled(engine.ToggleActionEntity.Value, state is WH40KVehicleEngineState.Starting or WH40KVehicleEngineState.Running);

        if (changed)
            Dirty(uid, engine);

        if (refreshRuntime)
            RefreshVehicleRuntime(uid);
    }

    public float GetFuelFraction(WH40KVehicleFuelComponent fuel)
    {
        if (fuel.FuelCapacity <= 0f)
            return 0f;

        return Math.Clamp(fuel.FuelLevel / fuel.FuelCapacity, 0f, 1f);
    }

    public float GetFuelBurnPerSecond(WH40KVehicleFuelComponent fuel)
    {
        if (fuel.FullTankRuntime <= 0f)
            return 0f;

        return fuel.FuelCapacity / fuel.FullTankRuntime;
    }

    public string FormatRuntime(float seconds)
    {
        var clamped = Math.Max(0, (int) MathF.Round(seconds));
        var span = TimeSpan.FromSeconds(clamped);

        if (span.TotalHours >= 1)
            return $"{span.Hours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";

        return $"{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
