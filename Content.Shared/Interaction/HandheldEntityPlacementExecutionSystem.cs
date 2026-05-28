using System.Collections.Generic;
using System.Linq;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared.Interaction;

/// <summary>
/// Server execution path for in-hand placement requests.
/// Client-side preview / mode lifecycle is handled by <see cref="Content.Client.Interaction.Systems.HandheldEntityPlacementSystem"/>.
/// </summary>
public sealed partial class HandheldEntityPlacementExecutionSystem : EntitySystem
{
    [Dependency] private  SharedDoAfterSystem _doAfter = default!;
    [Dependency] private  SharedHandsSystem _hands = default!;
    [Dependency] private  SharedInteractionSystem _interaction = default!;
    [Dependency] private  INetManager _net = default!;

    private readonly Dictionary<(EntityUid User, EntityUid Item), DoAfterId> _activePlacementDoAfters = new();

    public override void Initialize()
    {
        if (!_net.IsServer)
            return;

        SubscribeNetworkEvent<RequestHandheldEntityPlacementEvent>(OnPlacementRequest);
        SubscribeNetworkEvent<RequestCancelHandheldEntityPlacementEvent>(OnPlacementCancelRequest);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, HandheldEntityPlacementDoAfterEvent>(OnPlacementDoAfter);
        SubscribeLocalEvent<HandheldEntityPlacementComponent, ComponentShutdown>(OnPlacementItemShutdown);
    }

    private void OnPlacementRequest(RequestHandheldEntityPlacementEvent ev, EntitySessionEventArgs args)
    {
        if (!_net.IsServer || args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        var item = GetEntity(ev.Item);
        if (!Exists(item) || !_hands.IsHolding(user, item))
            return;

        if (!TryComp(item, out HandheldEntityPlacementComponent? placement))
            return;

        var requestedCoords = GetCoordinates(ev.Coordinates);
        if (!_interaction.InRangeUnobstructed(user, requestedCoords, placement.Range, popup: true))
            return;

        var direction = NormalizeDirection(ev.Direction, placement.CanRotate);
        var placementAttempt = new HandheldEntityPlacementAttemptEvent(user, requestedCoords, direction);
        RaiseLocalEvent(item, placementAttempt);

        if (placementAttempt.Cancelled || placementAttempt.DeployDelay <= TimeSpan.Zero)
            return;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            placementAttempt.DeployDelay,
            new HandheldEntityPlacementDoAfterEvent(
                GetNetCoordinates(placementAttempt.Coordinates),
                NormalizeDirection(placementAttempt.Direction, placement.CanRotate)),
            item,
            item,
            used: item)
        {
            BreakOnMove = placementAttempt.BreakOnMove,
            BreakOnDamage = placementAttempt.BreakOnDamage,
            BreakOnHandChange = placementAttempt.BreakOnHandChange,
            NeedHand = placementAttempt.NeedHand,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs, out var doAfterId))
            _activePlacementDoAfters[(user, item)] = doAfterId.Value;
    }

    private void OnPlacementDoAfter(Entity<HandheldEntityPlacementComponent> ent, ref HandheldEntityPlacementDoAfterEvent args)
    {
        if (!_net.IsServer)
            return;

        TryCleanupTrackedDoAfter(args.User, ent.Owner, args.DoAfter.Id);

        if (args.Cancelled || args.Handled)
            return;

        if (!_hands.IsHolding(args.User, ent.Owner))
            return;

        var requestedCoords = GetCoordinates(args.Coordinates);
        if (!_interaction.InRangeUnobstructed(args.User, requestedCoords, ent.Comp.Range))
            return;

        var direction = NormalizeDirection(args.Direction, ent.Comp.CanRotate);
        var completed = new HandheldEntityPlacementCompleteEvent(args.User, requestedCoords, direction);
        RaiseLocalEvent(ent.Owner, completed);

        args.Handled = completed.Handled;
    }

    private void OnPlacementCancelRequest(RequestCancelHandheldEntityPlacementEvent ev, EntitySessionEventArgs args)
    {
        if (!_net.IsServer || args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        var item = GetEntity(ev.Item);
        if (!item.IsValid())
            return;

        if (!_activePlacementDoAfters.Remove((user, item), out var activeDoAfter))
            return;

        _doAfter.Cancel(activeDoAfter);
    }

    private void TryCleanupTrackedDoAfter(EntityUid user, EntityUid item, DoAfterId finishedDoAfterId)
    {
        var key = (user, item);
        if (!_activePlacementDoAfters.TryGetValue(key, out var trackedDoAfter))
            return;

        if (trackedDoAfter != finishedDoAfterId)
            return;

        _activePlacementDoAfters.Remove(key);
    }

    private void OnPlacementItemShutdown(Entity<HandheldEntityPlacementComponent> ent, ref ComponentShutdown args)
    {
        if (!_net.IsServer || _activePlacementDoAfters.Count == 0)
            return;

        foreach (var (key, doAfterId) in _activePlacementDoAfters.Where(pair => pair.Key.Item == ent.Owner).ToArray())
        {
            _activePlacementDoAfters.Remove(key);

            if (_doAfter.IsRunning(doAfterId))
                _doAfter.Cancel(doAfterId);
        }
    }

    private static Direction NormalizeDirection(Direction direction, bool canRotate)
    {
        if (!canRotate || direction == Direction.Invalid)
            return Direction.North;

        return direction.ToAngle().GetCardinalDir();
    }
}
