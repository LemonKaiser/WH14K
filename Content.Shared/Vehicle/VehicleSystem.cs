using System.Diagnostics.CodeAnalysis;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Access.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Audio;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared.Vehicle;

/// <summary>
/// Handles logic relating to vehicles.
/// </summary>
public sealed partial class VehicleSystem : EntitySystem
{
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityWhitelistSystem _entityWhitelist = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<VehicleComponent> _vehicleQuery;
    private EntityQuery<VehicleOperatorComponent> _operatorQuery;
    private EntityQuery<AppearanceComponent> _appearanceQuery;
    private EntityQuery<InputMoverComponent> _inputMoverQuery;
    private EntityQuery<HandsComponent> _handsQuery;

    public override void Initialize()
    {
        _vehicleQuery = GetEntityQuery<VehicleComponent>();
        _operatorQuery = GetEntityQuery<VehicleOperatorComponent>();
        _appearanceQuery = GetEntityQuery<AppearanceComponent>();
        _inputMoverQuery = GetEntityQuery<InputMoverComponent>();
        _handsQuery = GetEntityQuery<HandsComponent>();

        InitializeOperator();
        InitializeKey();

        SubscribeLocalEvent<VehicleComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<VehicleComponent, GetItemActionsEvent>(OnVehicleGetActions);
        SubscribeLocalEvent<VehicleComponent, HonkActionEvent>(OnVehicleHonk);
        SubscribeLocalEvent<VehicleComponent, UpdateCanMoveEvent>(OnVehicleUpdateCanMove);
        SubscribeLocalEvent<VehicleComponent, ComponentShutdown>(OnVehicleShutdown);
        SubscribeLocalEvent<VehicleComponent, GetAdditionalAccessEvent>(OnVehicleGetAdditionalAccess);

        SubscribeLocalEvent<VehicleOperatorComponent, ComponentShutdown>(OnOperatorShutdown);
    }

    private void OnBeforeDamageChanged(Entity<VehicleComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!ent.Comp.TransferDamage || !args.Damage.AnyPositive() || ent.Comp.Operator is not { } operatorUid)
            return;

        var damage = DamageSpecifier.GetPositive(args.Damage);
        if (ent.Comp.TransferDamageModifier is { } modifierSet)
            damage = DamageSpecifier.ApplyModifierSet(damage, modifierSet);

        damage *= ent.Comp.TransferDamageMultiplier;

        _damageable.TryChangeDamage(operatorUid, damage, origin: args.Origin);
    }

    private void OnVehicleGetActions(Entity<VehicleComponent> ent, ref GetItemActionsEvent args)
    {
        if (ent.Comp.HornSound == null || ent.Comp.HornAction == null)
            return;

        args.AddAction(ref ent.Comp.HornActionEntity, ent.Comp.HornAction, ent.Owner);
    }

    private void OnVehicleHonk(Entity<VehicleComponent> ent, ref HonkActionEvent args)
    {
        if (args.Handled || ent.Comp.HornSound == null)
            return;

        _audio.PlayPredicted(ent.Comp.HornSound, ent.Owner, args.Performer);
        args.Handled = true;
    }

    private void OnVehicleUpdateCanMove(Entity<VehicleComponent> ent, ref UpdateCanMoveEvent args)
    {
        var ev = new VehicleCanRunEvent(ent);
        RaiseLocalEvent(ent, ref ev);
        if (!ev.CanRun)
            args.Cancel();
    }

    private void OnVehicleShutdown(Entity<VehicleComponent> ent, ref ComponentShutdown args)
    {
        TryRemoveOperator(ent);
    }

    private void OnVehicleGetAdditionalAccess(Entity<VehicleComponent> ent, ref GetAdditionalAccessEvent args)
    {
        if (ent.Comp.Operator is { } operatorUid)
            args.Entities.Add(operatorUid);
    }

    private void OnOperatorShutdown(Entity<VehicleOperatorComponent> ent, ref ComponentShutdown args)
    {
        TryRemoveOperator((ent.Owner, ent.Comp));
    }

    /// <summary>
    /// Set the operator for a given vehicle.
    /// </summary>
    public bool TrySetOperator(Entity<VehicleComponent> entity, EntityUid? uid, bool removeExisting = true)
    {
        if (entity.Comp.Operator == null && uid is null)
            return false;

        if (entity.Comp.Operator == uid)
            return true;

        if (uid is not null && _operatorQuery.TryComp(uid.Value, out var existingOperator))
        {
            if (existingOperator.Vehicle == entity.Owner)
                return true;

            if (!removeExisting)
                return false;
        }

        if (!removeExisting && entity.Comp.Operator is not null)
            return false;

        if (uid != null && !CanOperate(entity.AsNullable(), uid.Value))
            return false;

        var oldOperator = entity.Comp.Operator;

        if (oldOperator is { } currentOperator &&
            _operatorQuery.TryComp(currentOperator, out var currentOperatorComponent))
        {
            var exitEvent = new OnVehicleExitedEvent(entity, currentOperator);
            RaiseLocalEvent(currentOperator, ref exitEvent);

            _actions.RemoveProvidedActions(currentOperator, entity.Owner);
            currentOperatorComponent.Vehicle = null;
            RemCompDeferred<VehicleOperatorComponent>(currentOperator);
            RemCompDeferred<RelayInputMoverComponent>(currentOperator);
            RemCompDeferred<InteractionRelayComponent>(currentOperator);
        }

        entity.Comp.Operator = uid;

        if (uid != null)
        {
            var vehicleOperator = EnsureComp<VehicleOperatorComponent>(uid.Value);
            vehicleOperator.Vehicle = entity.Owner;
            Dirty(uid.Value, vehicleOperator);

            var interactionRelay = EnsureComp<InteractionRelayComponent>(uid.Value);
            _mover.SetRelay(uid.Value, entity.Owner);
            _interaction.SetRelay(uid.Value, entity.Owner, interactionRelay);
            GrantVehicleActions(entity, uid.Value);

            var enterEvent = new OnVehicleEnteredEvent(entity, uid.Value);
            RaiseLocalEvent(uid.Value, ref enterEvent);
        }
        else
        {
            RemCompDeferred<MovementRelayTargetComponent>(entity.Owner);
        }

        RefreshCanRun((entity.Owner, entity.Comp));

        var setEvent = new VehicleOperatorSetEvent(uid, oldOperator);
        RaiseLocalEvent(entity.Owner, ref setEvent);

        Dirty(entity.Owner, entity.Comp);
        return true;
    }

    [PublicAPI]
    public bool TryRemoveOperator(Entity<VehicleComponent> entity)
    {
        return TrySetOperator(entity, null, removeExisting: true);
    }

    [PublicAPI]
    public bool TryRemoveOperator(Entity<VehicleOperatorComponent?> operatorEntity)
    {
        if (!Resolve(operatorEntity.Owner, ref operatorEntity.Comp, false))
            return true;

        if (operatorEntity.Comp!.Vehicle is not { } vehicleUid ||
            !_vehicleQuery.TryComp(vehicleUid, out var vehicle))
        {
            return true;
        }

        return TrySetOperator((vehicleUid, vehicle), null, removeExisting: true);
    }

    [PublicAPI]
    public bool TryGetOperator(Entity<VehicleComponent?> entity, [NotNullWhen(true)] out Entity<VehicleOperatorComponent>? operatorEnt)
    {
        operatorEnt = null;
        if (!Resolve(entity.Owner, ref entity.Comp))
            return false;

        if (entity.Comp!.Operator is not { } operatorUid)
            return false;

        if (!_operatorQuery.TryComp(operatorUid, out var operatorComponent))
            return false;

        operatorEnt = (operatorUid, operatorComponent);
        return true;
    }

    public EntityUid? GetOperatorOrNull(Entity<VehicleComponent?> entity)
    {
        TryGetOperator(entity, out var operatorEnt);
        return operatorEnt?.Owner;
    }

    [PublicAPI]
    public bool HasOperator(Entity<VehicleComponent?> entity)
    {
        return TryGetOperator(entity, out _);
    }

    /// <summary>
    /// Checks if a given entity is capable of operating a vehicle.
    /// This checks only the user, not whether the vehicle can currently run.
    /// </summary>
    public bool CanOperate(Entity<VehicleComponent?> entity, EntityUid uid)
    {
        if (!Exists(uid))
            return false;

        if (!Resolve(entity.Owner, ref entity.Comp))
            return false;

        if (_entityWhitelist.IsWhitelistFail(entity.Comp!.OperatorWhitelist, uid))
            return false;

        if (entity.Comp.RequiresHands && (!_handsQuery.HasComp(uid) || !_actionBlocker.CanInteract(uid, entity.Owner)))
            return false;

        return _actionBlocker.CanConsciouslyPerformAction(uid);
    }

    /// <summary>
    /// Checks if the vehicle is capable of running and refreshes the cached movement state.
    /// </summary>
    public void RefreshCanRun(Entity<VehicleComponent?> entity)
    {
        if (TerminatingOrDeleted(entity.Owner))
            return;

        if (!Resolve(entity.Owner, ref entity.Comp))
            return;

        _actionBlocker.UpdateCanMove(entity.Owner);
        UpdateAppearance((entity.Owner, entity.Comp!));
    }

    private void UpdateAppearance(Entity<VehicleComponent> entity)
    {
        if (!_appearanceQuery.TryComp(entity.Owner, out var appearance))
            return;

        if (_inputMoverQuery.TryComp(entity.Owner, out var inputMover))
            _appearance.SetData(entity.Owner, VehicleVisuals.CanRun, inputMover.CanMove, appearance);

        _appearance.SetData(entity.Owner, VehicleVisuals.HasOperator, entity.Comp.Operator is not null, appearance);
    }

    private void GrantVehicleActions(Entity<VehicleComponent> entity, EntityUid operatorUid)
    {
        var ev = new GetItemActionsEvent(_actionContainer, operatorUid, entity.Owner);
        RaiseLocalEvent(entity.Owner, ev);

        if (ev.Actions.Count == 0)
            return;

        ActionsComponent? actions = null;
        var container = EnsureComp<ActionsContainerComponent>(entity.Owner);
        _actions.GrantActions((operatorUid, actions), ev.Actions, (entity.Owner, container));
    }
}
