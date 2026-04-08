using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Popups;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Ghost;
using Content.Shared.Buckle.Components;
using Content.Shared._WH40K.Combat;
using Content.Shared._WH40K.GameMode;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Combat;

public sealed class WH40KTdmWarningBarrierSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamBattle = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextPopupAt = new();
    private readonly Dictionary<EntityUid, TimeSpan> _suppressMoveUntil = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KPreparationPhaseBarrierComponent, MapInitEvent>(OnBarrierMapInit);
        SubscribeLocalEvent<WH40KBattlePhaseChangedEvent>(OnPhaseChanged);
        SubscribeLocalEvent<WH40KTdmWarningBarrierComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<ActorComponent, MoveEvent>(OnActorMove);
        SubscribeLocalEvent<StrapComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<StrapComponent, MoveEvent>(OnStrapMove);
    }

    private void OnBarrierMapInit(Entity<WH40KPreparationPhaseBarrierComponent> ent, ref MapInitEvent args)
    {
        if (_teamBattle.GetCurrentPhase() <= WH40KBattlePhase.Preparation)
            return;

        QueueDel(ent.Owner);
    }

    private void OnPhaseChanged(WH40KBattlePhaseChangedEvent ev)
    {
        if (ev.NewPhase <= WH40KBattlePhase.Preparation)
            return;

        var query = EntityQueryEnumerator<WH40KPreparationPhaseBarrierComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
        }
    }

    private void OnStartCollide(Entity<WH40KTdmWarningBarrierComponent> ent, ref StartCollideEvent args)
    {
        if (!TryResolvePushTarget(args.OtherEntity, out var pushEntity, out var popupTargets) ||
            TerminatingOrDeleted(pushEntity))
        {
            return;
        }

        TryPushEntityBack(ent, pushEntity, collisionNormal: args.WorldNormal);

        if (TryComp<PhysicsComponent>(pushEntity, out var otherPhysics) &&
            otherPhysics.BodyType != BodyType.Static)
        {
            _physics.SetLinearVelocity(pushEntity, Vector2.Zero, body: otherPhysics);
        }

        foreach (var popupTarget in popupTargets)
        {
            TryPopupTarget(ent.Comp, popupTarget);
        }
    }

    private void OnActorMove(Entity<ActorComponent> ent, ref MoveEvent args)
    {
        if (ShouldIgnoreBarrier(ent.Owner))
            return;

        if (IsMoveSuppressed(ent.Owner))
            return;

        if (TryComp<BuckleComponent>(ent.Owner, out var buckle) && buckle.Buckled)
            return;

        if (!TryGetSeparatingBarrier(ent.Owner, args.OldPosition, args.NewPosition, out var barrier))
            return;

        if (!TryGetReturnDirectionFromMovement(args.OldPosition, args.NewPosition, out var preferredDirection))
            preferredDirection = null;

        if (!TryPushEntityBack(barrier, ent.Owner, preferredDirection))
            return;

        if (TryComp<PhysicsComponent>(ent.Owner, out var body) && body.BodyType != BodyType.Static)
            _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: body);

        if (TryComp<WH40KTdmWarningBarrierComponent>(barrier, out var warningBarrier))
            TryPopupTarget(warningBarrier, ent.Owner);
    }

    private void OnStrapMove(Entity<StrapComponent> ent, ref MoveEvent args)
    {
        if (IsMoveSuppressed(ent.Owner) || ent.Comp.BuckledEntities.Count == 0)
            return;

        if (!TryGetSeparatingBarrier(ent.Owner, args.OldPosition, args.NewPosition, out var barrier))
            return;

        if (!TryGetReturnDirectionFromMovement(args.OldPosition, args.NewPosition, out var preferredDirection))
            preferredDirection = null;

        if (!TryPushEntityBack(barrier, ent.Owner, preferredDirection))
            return;

        if (TryComp<PhysicsComponent>(ent.Owner, out var body) && body.BodyType != BodyType.Static)
            _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: body);

        foreach (var buckledEntity in ent.Comp.BuckledEntities)
        {
            if (!HasComp<ActorComponent>(buckledEntity) ||
                TerminatingOrDeleted(buckledEntity) ||
                ShouldIgnoreBarrier(buckledEntity))
                continue;

            if (TryComp<WH40KTdmWarningBarrierComponent>(barrier, out var warningBarrier))
                TryPopupTarget(warningBarrier, buckledEntity);
        }
    }

    private void OnStrapAttempt(Entity<StrapComponent> ent, ref StrapAttemptEvent args)
    {
        if (ShouldIgnoreBarrier(args.Buckle.Owner) ||
            !HasComp<ActorComponent>(args.Buckle.Owner) ||
            !TryGetSeparatingBarrier(args.Buckle.Owner, ent.Owner, out var barrier))
        {
            return;
        }

        args.Cancelled = true;
        if (TryComp<WH40KTdmWarningBarrierComponent>(barrier, out var warningBarrier))
            TryPopupTarget(warningBarrier, args.Buckle.Owner);
    }

    private string GetPopup(WH40KTdmWarningBarrierComponent component, EntityUid target)
    {
        if (_teamBattle.TryGetTeamIdFromEntity(target, out var teamId))
        {
            var teamKey = $"{component.PopupLocPrefix}-{teamId}";
            if (Loc.HasString(teamKey))
                return Loc.GetString(teamKey);
        }

        return Loc.GetString(component.GenericPopupLocKey);
    }

    private void TryPopupTarget(WH40KTdmWarningBarrierComponent component, EntityUid target)
    {
        if (_nextPopupAt.TryGetValue(target, out var nextPopup) &&
            _timing.CurTime < nextPopup)
        {
            return;
        }

        _nextPopupAt[target] = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(component.PopupCooldownSeconds, 0.1f));
        _popup.PopupEntity(GetPopup(component, target), target, target, PopupType.MediumCaution);
    }

    private bool TryResolvePushTarget(EntityUid otherEntity, out EntityUid pushEntity, out List<EntityUid> popupTargets)
    {
        popupTargets = new List<EntityUid>();
        pushEntity = EntityUid.Invalid;

        if (HasComp<ActorComponent>(otherEntity) && !ShouldIgnoreBarrier(otherEntity))
        {
            pushEntity = otherEntity;
            popupTargets.Add(otherEntity);
            return true;
        }

        if (!TryComp<StrapComponent>(otherEntity, out var strap))
            return false;

        foreach (var buckledEntity in strap.BuckledEntities)
        {
            if (!HasComp<ActorComponent>(buckledEntity) ||
                TerminatingOrDeleted(buckledEntity) ||
                ShouldIgnoreBarrier(buckledEntity))
                continue;

            popupTargets.Add(buckledEntity);
        }

        if (popupTargets.Count == 0)
            return false;

        pushEntity = otherEntity;
        return true;
    }

    private bool ShouldIgnoreBarrier(EntityUid uid)
    {
        return HasComp<GhostComponent>(uid);
    }

    private bool TryPushEntityBack(
        EntityUid barrier,
        EntityUid pushedEntity,
        Direction? preferredDirection = null,
        Vector2 collisionNormal = default)
    {
        var pushedXform = Transform(pushedEntity);
        var barrierXform = Transform(barrier);
        if (pushedXform.GridUid is not { } pushedGridUid ||
            barrierXform.GridUid != pushedGridUid ||
            !TryComp<MapGridComponent>(pushedGridUid, out var grid) ||
            !_transform.TryGetGridTilePosition((pushedEntity, pushedXform), out var pushedTile, grid) ||
            !_transform.TryGetGridTilePosition((barrier, barrierXform), out var barrierTile, grid))
        {
            return false;
        }

        foreach (var awayDirection in ResolvePushDirections(barrier, pushedEntity, barrierTile, pushedTile, preferredDirection, collisionNormal))
        {
            var targetTile = barrierTile + awayDirection.ToIntVec();
            var destinationTile = GetSafeDestinationTile(pushedGridUid, grid, barrierTile, pushedTile, targetTile, awayDirection);
            if (destinationTile == null || !_map.TryGetTileRef(pushedGridUid, grid, destinationTile.Value, out var tileRef))
                continue;

            var destination = _turf.GetTileCenter(tileRef);
            _suppressMoveUntil[pushedEntity] = _timing.CurTime + TimeSpan.FromSeconds(0.25);
            _transform.SetCoordinates(pushedEntity, pushedXform, destination, pushedXform.LocalRotation);
            return true;
        }

        return false;
    }

    private Vector2i? GetSafeDestinationTile(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i barrierTile,
        Vector2i currentTile,
        Vector2i targetTile,
        Direction awayDirection)
    {
        if (TryGetWalkableTile(gridUid, grid, targetTile, out var walkableTarget))
            return walkableTarget;

        var currentDelta = currentTile - barrierTile;
        if (currentDelta != Vector2i.Zero &&
            currentDelta.GetCardinalDir() == awayDirection &&
            TryGetWalkableTile(gridUid, grid, currentTile, out var walkableCurrent))
        {
            return walkableCurrent;
        }

        return null;
    }

    private bool TryGetWalkableTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile, out Vector2i walkableTile)
    {
        walkableTile = tile;
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) ||
            tileRef.Tile.IsEmpty ||
            _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
        {
            return false;
        }

        return true;
    }

    private IEnumerable<Direction> ResolvePushDirections(
        EntityUid barrier,
        EntityUid pushedEntity,
        Vector2i barrierTile,
        Vector2i pushedTile,
        Direction? preferredDirection,
        Vector2 collisionNormal)
    {
        var seen = new HashSet<Direction>();

        if (preferredDirection is { } direct && seen.Add(direct))
            yield return direct;

        if (TryComp<PhysicsComponent>(pushedEntity, out var body) &&
            body.LinearVelocity.LengthSquared() > 0.0001f)
        {
            var velocityDirection = GetCardinalDirection(-body.LinearVelocity);
            if (seen.Add(velocityDirection))
                yield return velocityDirection;
        }

        if (collisionNormal.LengthSquared() > 0.0001f)
        {
            var reverseNormal = GetCardinalDirection(-collisionNormal);
            if (seen.Add(reverseNormal))
                yield return reverseNormal;

            var normalDirection = GetCardinalDirection(collisionNormal);
            if (seen.Add(normalDirection))
                yield return normalDirection;
        }

        var tileDelta = pushedTile - barrierTile;
        if (tileDelta != Vector2i.Zero)
        {
            var tileDirection = tileDelta.GetCardinalDir();
            if (seen.Add(tileDirection))
                yield return tileDirection;
        }

        var barrierPosition = _transform.GetWorldPosition(barrier);
        var pushedPosition = _transform.GetWorldPosition(pushedEntity);
        var direction = pushedPosition - barrierPosition;
        if (direction.LengthSquared() > 0.0001f)
        {
            var positionDirection = GetCardinalDirection(direction);
            if (seen.Add(positionDirection))
                yield return positionDirection;
        }

        if (seen.Add(Direction.South))
            yield return Direction.South;
    }

    private bool IsMoveSuppressed(EntityUid uid)
    {
        if (!_suppressMoveUntil.TryGetValue(uid, out var until))
            return false;

        if (_timing.CurTime >= until)
        {
            _suppressMoveUntil.Remove(uid);
            return false;
        }

        return true;
    }

    private bool TryGetReturnDirectionFromMovement(EntityCoordinates oldPosition, EntityCoordinates newPosition, out Direction? direction)
    {
        var delta = oldPosition.Position - newPosition.Position;
        if (delta.LengthSquared() <= 0.0001f)
        {
            direction = null;
            return false;
        }

        direction = GetCardinalDirection(delta);
        return true;
    }

    private bool TryGetSeparatingBarrier(EntityUid buckle, EntityUid strap, out EntityUid barrier)
    {
        barrier = EntityUid.Invalid;

        var buckleXform = Transform(buckle);
        var strapXform = Transform(strap);
        if (buckleXform.GridUid is not { } buckleGrid ||
            strapXform.GridUid != buckleGrid ||
            !TryComp<MapGridComponent>(buckleGrid, out var grid) ||
            !_transform.TryGetGridTilePosition((buckle, buckleXform), out var buckleTile, grid) ||
            !_transform.TryGetGridTilePosition((strap, strapXform), out var strapTile, grid))
        {
            return false;
        }

        if (buckleTile == strapTile)
            return false;

            return TryGetSeparatingBarrier(buckle, buckleXform.Coordinates, strapXform.Coordinates, out barrier);
    }

    private bool TryGetSeparatingBarrier(EntityUid movingEntity, EntityCoordinates from, EntityCoordinates to, out EntityUid barrier)
    {
        barrier = EntityUid.Invalid;

        var movingXform = Transform(movingEntity);

        if (movingXform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        var fromMap = _transform.ToMapCoordinates(from);
        var toMap = _transform.ToMapCoordinates(to);
        if (fromMap.MapId != toMap.MapId || fromMap.MapId != movingXform.MapID)
        {
            return false;
        }

        var fromTile = _map.CoordinatesToTile(gridUid, grid, fromMap);
        var toTile = _map.CoordinatesToTile(gridUid, grid, toMap);

        if (fromTile == toTile)
            return false;

        var query = EntityQueryEnumerator<WH40KPreparationPhaseBarrierComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var barrierXform))
        {
            if (barrierXform.GridUid != gridUid ||
                !_transform.TryGetGridTilePosition((uid, barrierXform), out var barrierTile, grid))
            {
                continue;
            }

            if (HasBarrierBetweenTiles(fromTile, toTile, barrierTile))
            {
                barrier = uid;
                return true;
            }
        }

        return false;
    }

    private static bool HasBarrierBetweenTiles(Vector2i buckleTile, Vector2i strapTile, Vector2i barrierTile)
    {
        if (buckleTile.X == strapTile.X && barrierTile.X == buckleTile.X)
        {
            return barrierTile.Y >= Math.Min(buckleTile.Y, strapTile.Y) && barrierTile.Y <= Math.Max(buckleTile.Y, strapTile.Y);
        }

        if (buckleTile.Y == strapTile.Y && barrierTile.Y == buckleTile.Y)
        {
            return barrierTile.X >= Math.Min(buckleTile.X, strapTile.X) && barrierTile.X <= Math.Max(buckleTile.X, strapTile.X);
        }

        return false;
    }

    private static Direction GetCardinalDirection(Vector2 direction)
    {
        if (MathF.Abs(direction.X) >= MathF.Abs(direction.Y))
            return direction.X >= 0f ? Direction.East : Direction.West;

        return direction.Y >= 0f ? Direction.North : Direction.South;
    }
}
