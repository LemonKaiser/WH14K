using System.Linq;
using System.Numerics;
using Content.Server.Destructible;
using Content.Server.Examine;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared._WH40K.Combat;
using Content.Shared.Climbing;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Turrets;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using ClimbingComponent = Content.Shared.Climbing.Components.ClimbingComponent;
using Robust.Shared.Random;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    private void ApplySeek(Span<float> interest, Vector2 direction, float weight)
    {
        if (weight == 0f || direction == Vector2.Zero)
            return;

        var directionAngle = (float)direction.ToAngle().Theta;

        for (var i = 0; i < InterestDirections; i++)
        {
            var angle = i * InterestRadians;
            var dot = MathF.Cos(directionAngle - angle);
            dot = (dot + 1f) * 0.5f;
            interest[i] = Math.Clamp(interest[i] + dot * weight, 0f, 1f);
        }
    }

    #region Seek

    /// <summary>
    /// Takes into account agent-specific context that may allow it to bypass a node which is not FreeSpace.
    /// </summary>
    private bool IsFreeSpace(
        EntityUid uid,
        NPCSteeringComponent steering,
        PathPoly node)
    {
        if (node.Data.IsFreeSpace)
        {
            return true;
        }
        // Handle the case where the node is a climb, we can climb, and we are climbing.
        else if ((node.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0 &&
            (steering.Flags & PathFlags.Climbing) != 0x0 &&
            TryComp<ClimbingComponent>(uid, out var climbing) &&
            climbing.IsClimbing)
        {
            return true;
        }

        var ents = _entSetPool.Get();
        _lookup.GetLocalEntitiesIntersecting(
            node.GraphUid,
            node.Box.Enlarged(MathF.Max(steering.Radius, 0.04f)),
            ents,
            flags: LookupFlags.Static);
        var result = true;

        if (ents.Count > 0)
        {
            var fixtures = _fixturesQuery.GetComponent(uid);
            var physics = _physicsQuery.GetComponent(uid);

            foreach (var intersecting in ents)
            {
                if (HasComp<PathfindingIgnoredComponent>(intersecting))
                    continue;

                if (!_physics.IsCurrentlyHardCollidable((uid, fixtures, physics), intersecting))
                {
                    continue;
                }

                result = false;
                break;
            }
        }

        _entSetPool.Return(ents);
        return result;
    }

    /// <summary>
    /// Attempts to head to the target destination, either via the next pathfinding node or the final target.
    /// </summary>
    private bool TrySeek(
        EntityUid uid,
        InputMoverComponent mover,
        NPCSteeringComponent steering,
        PhysicsComponent body,
        TransformComponent xform,
        Angle offsetRot,
        float moveSpeed,
        Span<float> interest,
        float frameTime,
        ref bool forceSteer)
    {
        var ourCoordinates = xform.Coordinates;
        var destinationCoordinates = steering.Coordinates;
        var inLos = true;

        // Check if we're in LOS if that's required.
        // TODO: Need something uhh better not sure on the interaction between these.
        if (!steering.ForceMove && steering.ArriveOnLineOfSight)
        {
            // TODO: use vision range
            inLos = _interaction.InRangeUnobstructed(uid, steering.Coordinates, 10f);

            if (inLos)
            {
                steering.LineOfSightTimer += frameTime;

                if (steering.LineOfSightTimer >= steering.LineOfSightTimeRequired)
                {
                    steering.Status = SteeringStatus.InRange;
                    ResetStuck(steering, ourCoordinates);
                    return true;
                }
            }
            else
            {
                steering.LineOfSightTimer = 0f;
            }
        }
        else
        {
            steering.LineOfSightTimer = 0f;
            steering.ForceMove = false;
        }

        // We've arrived, nothing else matters.
        if (xform.Coordinates.TryDistance(EntityManager, destinationCoordinates, out var targetDistance) &&
            inLos &&
            targetDistance <= steering.Range &&
            IsDirectPathClear(uid, ourCoordinates, destinationCoordinates, steering.Radius, body))
        {
            steering.Status = SteeringStatus.InRange;
            ResetStuck(steering, ourCoordinates);
            return true;
        }

        // Grab the target position, either the next path node or our end goal..
        var targetCoordinates = GetTargetCoordinates(uid, steering, ourCoordinates);

        if (!targetCoordinates.IsValid(EntityManager))
        {
            steering.Status = SteeringStatus.NoPath;
            return false;
        }

        var needsPath = false;

        // If the next node is invalid then get new ones
        if (!targetCoordinates.IsValid(EntityManager))
        {
            if (steering.CurrentPath.TryPeek(out var poly) &&
                (poly.Data.Flags & PathfindingBreadcrumbFlag.Invalid) != 0x0)
            {
                steering.CurrentPath.Dequeue();
                // Try to get the next node temporarily.
                targetCoordinates = GetTargetCoordinates(uid, steering, ourCoordinates);
                needsPath = true;
                ResetStuck(steering, ourCoordinates);
            }
        }

        // Check if mapids match.
        var targetMap = _transform.ToMapCoordinates(targetCoordinates);
        var ourMap = _transform.ToMapCoordinates(ourCoordinates);

        if (targetMap.MapId != ourMap.MapId)
        {
            steering.Status = SteeringStatus.NoPath;
            return false;
        }

        var direction = targetMap.Position - ourMap.Position;

        if (TryRepathBlockedLiveSegment(uid, steering, xform, targetCoordinates, targetDistance))
            return true;

        // Need to be pretty close if it's just a node to make sure LOS for door bashes or the likes.
        bool arrived;

        if (targetCoordinates.Equals(steering.Coordinates))
        {
            // What's our tolerance for arrival.
            // If it's a pathfinding node it might be different to the destination.
            arrived = direction.Length() <= steering.Range;
        }
        // If next node is a free tile then get within its bounds.
        // This is to avoid popping it too early
        else if (steering.CurrentPath.TryPeek(out var node) && IsFreeSpace(uid, steering, node))
        {
            arrived = node.Box.Contains(ourCoordinates.Position);
        }
        // Try getting into blocked range I guess?
        // TODO: Consider melee range or the likes.
        else
        {
            arrived = direction.Length() <= SharedInteractionSystem.InteractionRange - 0.05f;
        }

        // Are we in range
        if (arrived)
        {
            // Node needs some kind of special handling like access or smashing.
            if (steering.CurrentPath.TryPeek(out var node) && !IsFreeSpace(uid, steering, node))
            {
                // Ignore stuck while handling obstacles.
                ResetStuck(steering, ourCoordinates);
                needsPath |= ShouldRepathAroundObstacle(steering);
                SteeringObstacleStatus status;

                // Breaking behaviours and the likes.
                lock (_obstacles)
                {
                    status = TryHandleFlags(uid, steering, node);
                }

                // TODO: Need to handle re-pathing in case the target moves around.
                switch (status)
                {
                    case SteeringObstacleStatus.Completed:
                        steering.DoAfterId = null;
                        break;
                    case SteeringObstacleStatus.Failed:
                        steering.DoAfterId = null;
                        MarkPathBlockedForRepath(steering, node);
                        CheckPath(uid, steering, xform, true, targetDistance);
                        return true;
                    case SteeringObstacleStatus.Continuing:
                        CheckPath(uid, steering, xform, needsPath, targetDistance);
                        return true;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // Distance should already be handled above.
            // It was just a node, not the target, so grab the next destination (either the target or next node).
            if (steering.CurrentPath.Count > 0)
            {
                forceSteer = true;
                steering.CurrentPath.Dequeue();

                // Alright just adjust slightly and grab the next node so we don't stop moving for a tick.
                // TODO: If it's the last node just grab the target instead.
                targetCoordinates = GetTargetCoordinates(uid, steering, ourCoordinates);

                if (!targetCoordinates.IsValid(EntityManager))
                {
                    SetDirection(uid, mover, steering, Vector2.Zero);
                    steering.Status = SteeringStatus.NoPath;
                    return false;
                }

                targetMap = _transform.ToMapCoordinates(targetCoordinates);

                // Can't make it again.
                if (ourMap.MapId != targetMap.MapId)
                {
                    SetDirection(uid, mover, steering, Vector2.Zero);
                    steering.Status = SteeringStatus.NoPath;
                    return false;
                }

                // Gonna resume now business as usual
                direction = targetMap.Position - ourMap.Position;
                ResetStuck(steering, ourCoordinates);
            }
            else
            {
                needsPath = true;
            }
        }
        // Stuck detection
        // Check if we have moved further than the movespeed * stuck time.
        else if (AntiStuck &&
                 ourCoordinates.TryDistance(EntityManager, steering.LastStuckCoordinates, out var stuckDistance) &&
                 stuckDistance < NPCSteeringComponent.StuckDistance)
        {
            var stuckTime = _timing.CurTime - steering.LastStuckTime;
            // Either 1 second or how long it takes to move the stuck distance + buffer if we're REALLY slow.
            var maxStuckTime = Math.Max(1, NPCSteeringComponent.StuckDistance / moveSpeed * 1.2f);

            if (stuckTime.TotalSeconds > maxStuckTime)
            {
                // TODO: Blacklist nodes (pathfinder factor wehn)
                // TODO: This should be a warning but
                // A) NPCs get stuck on non-anchored static bodies still (e.g. closets)
                // B) NPCs still try to move in locked containers (e.g. cow, hamster)
                // and I don't want to spam grafana even harder than it gets spammed rn.
                Log.Debug($"NPC {ToPrettyString(uid)} found stuck at {ourCoordinates}");
                needsPath = true;

                if (stuckTime.TotalSeconds > maxStuckTime * 3)
                {
                    steering.Status = SteeringStatus.NoPath;
                    return false;
                }
            }
        }
        else
        {
            ResetStuck(steering, ourCoordinates);
        }

        // If not in LOS and no path then get a new one fam.
        if ((!inLos && steering.ArriveOnLineOfSight && steering.CurrentPath.Count == 0) ||
            (!steering.ArriveOnLineOfSight && steering.CurrentPath.Count == 0))
        {
            needsPath = true;
        }

        if (TryStartFallbackRepath(uid, steering, ourMap))
        {
            CheckPath(uid, steering, xform, true, targetDistance);
            return true;
        }

        // TODO: Probably need partial planning support i.e. patch from the last node to where the target moved to.
        CheckPath(uid, steering, xform, needsPath, targetDistance);

        // If we don't have a path yet then do nothing; this is to avoid stutter-stepping if it turns out there's no path
        // available but we assume there was.
        if (steering is { Pathfind: true, CurrentPath.Count: 0 })
        {
            if (TryApplyPendingPathDirectMove(uid, steering, xform, ourMap, direction, offsetRot, interest))
                return true;

            LogSteeringStall(
                uid,
                steering,
                xform,
                "waiting-for-path",
                $"first path is still pending and local direct step is blocked; targetDistance={targetDistance:0.00}");

            return true;
        }

        if (moveSpeed == 0f || direction == Vector2.Zero)
        {
            LogSteeringStall(
                uid,
                steering,
                xform,
                "zero-speed-or-direction",
                $"speed={moveSpeed:0.00} direction={direction}");
            steering.Status = SteeringStatus.NoPath;
            return false;
        }

        var input = direction.Normalized();
        var tickMovement = moveSpeed * frameTime;

        // We have the input in world terms but need to convert it back to what movercontroller is doing.
        input = offsetRot.RotateVec(input);
        var norm = input.Normalized();
        var weight = MapValue(direction.Length(), tickMovement * 0.5f, tickMovement * 0.75f);

        ApplySeek(interest, norm, weight);

        // Prefer our current direction
        if (weight > 0f && body.LinearVelocity.LengthSquared() > 0f)
        {
            const float sameDirectionWeight = 0.1f;
            norm = body.LinearVelocity.Normalized();

            ApplySeek(interest, norm, sameDirectionWeight);
        }

        return true;
    }

    private bool TryApplyPendingPathDirectMove(
        EntityUid uid,
        NPCSteeringComponent steering,
        TransformComponent xform,
        MapCoordinates ourMap,
        Vector2 direction,
        Angle offsetRot,
        Span<float> interest)
    {
        if (direction.LengthSquared() <= 0.01f)
            return false;

        var normal = Vector2.Normalize(direction);
        var probeDistance = MathF.Min(
            direction.Length(),
            MathF.Max(0.5f, steering.PendingPathDirectMoveProbe));

        var mask = GetMovementCollisionMask(uid);
        if (mask != 0)
        {
            var probeEnd = new MapCoordinates(ourMap.Position + normal * probeDistance, ourMap.MapId);
            var probeRadius = Math.Clamp(steering.Radius + 0.05f, 0.1f, 0.6f);

            if (!IsCorridorClear(uid, ourMap, probeEnd, mask, probeRadius))
                return false;
        }

        steering.PendingPathDirectMoveTicks++;
        if (steering.PendingPathDirectMoveTicks == 1)
        {
            LogSteeringStall(
                uid,
                steering,
                xform,
                "path-pending-direct-step",
                $"path is pending with no queued polys; using clear local step probe={probeDistance:0.00}");
        }

        ApplySeek(interest, offsetRot.RotateVec(normal), 0.75f);
        return true;
    }

    private bool ShouldRepathAroundObstacle(NPCSteeringComponent component)
    {
        if (component.ObstacleRepathInterval <= 0f ||
            component.Pathfind ||
            _doAfter.GetStatus(component.DoAfterId) == DoAfterStatus.Running)
        {
            return false;
        }

        var now = _timing.CurTime;
        if (component.LastObstacleRepathTime != TimeSpan.Zero &&
            (now - component.LastObstacleRepathTime).TotalSeconds < component.ObstacleRepathInterval)
        {
            return false;
        }

        component.LastObstacleRepathTime = now;
        return true;
    }

    private bool TryRepathBlockedLiveSegment(
        EntityUid uid,
        NPCSteeringComponent component,
        TransformComponent xform,
        EntityCoordinates targetCoordinates,
        float targetDistance)
    {
        if (component.Pathfind ||
            component.LivePathCheckInterval <= 0f ||
            _doAfter.GetStatus(component.DoAfterId) == DoAfterStatus.Running)
        {
            return false;
        }

        var now = _timing.CurTime;
        if (component.LastLivePathCheckTime != TimeSpan.Zero &&
            (now - component.LastLivePathCheckTime).TotalSeconds < component.LivePathCheckInterval)
        {
            return false;
        }

        component.LastLivePathCheckTime = now;

        PathPoly? liveNode = null;
        if (component.CurrentPath.TryPeek(out var nextNode))
        {
            if (!nextNode.Data.IsFreeSpace)
                return false;

            liveNode = nextNode;
        }

        if (IsDirectPathClear(uid, xform.Coordinates, targetCoordinates, component.Radius))
            return false;

        MarkPathBlockedForRepath(component, liveNode);
        CheckPath(uid, component, xform, true, targetDistance);
        return true;
    }

    private void MarkPathBlockedForRepath(NPCSteeringComponent component, PathPoly? blockedPoly)
    {
        if (blockedPoly is { } poly && poly.IsValid())
            component.AvoidedPathPoly = poly;

        component.PathfindToken?.Cancel();
        component.PathfindToken = null;
        component.CurrentPath.Clear();
        component.LastFallbackRepathTime = _timing.CurTime;
    }

    private bool TryStartFallbackRepath(EntityUid uid, NPCSteeringComponent component, MapCoordinates ourCoordinates)
    {
        if (component.FallbackNoProgressTime <= 0f ||
            component.CurrentPath.Count == 0 ||
            component.Pathfind ||
            _doAfter.GetStatus(component.DoAfterId) == DoAfterStatus.Running)
        {
            return false;
        }

        if (!TryGetRemainingRouteDistance(component, ourCoordinates, out var routeDistance))
            return false;

        var now = _timing.CurTime;
        if (component.LastRouteProgressTime == TimeSpan.Zero ||
            !float.IsFinite(component.LastRouteProgressDistance) ||
            component.LastRouteProgressDistance - routeDistance >= component.FallbackNoProgressDistance ||
            routeDistance > component.LastRouteProgressDistance + component.FallbackNoProgressDistance)
        {
            component.LastRouteProgressDistance = routeDistance;
            component.LastRouteProgressTime = now;
            component.AvoidedPathPoly = null;
            return false;
        }

        if ((now - component.LastRouteProgressTime).TotalSeconds < component.FallbackNoProgressTime ||
            component.LastFallbackRepathTime != TimeSpan.Zero &&
            (now - component.LastFallbackRepathTime).TotalSeconds < component.FallbackRepathCooldown)
        {
            return false;
        }

        if (component.CurrentPath.TryPeek(out var avoided) && avoided.IsValid())
        {
            component.AvoidedPathPoly = avoided;
        }

        MarkPathBlockedForRepath(component, component.AvoidedPathPoly);
        component.LastFallbackRepathTime = now;
        component.LastRouteProgressTime = now;
        component.LastRouteProgressDistance = routeDistance;
        Log.Debug($"NPC {ToPrettyString(uid)} fallback re-path at {ourCoordinates}");
        return true;
    }

    private bool TryGetRemainingRouteDistance(NPCSteeringComponent component, MapCoordinates ourCoordinates, out float distance)
    {
        distance = 0f;
        var last = ourCoordinates;

        if (!component.Coordinates.IsValid(EntityManager))
            return false;

        foreach (var node in component.CurrentPath)
        {
            if (!node.IsValid() ||
                !node.Coordinates.IsValid(EntityManager))
                return false;

            var nodeCoordinates = _transform.ToMapCoordinates(node.Coordinates);
            if (nodeCoordinates.MapId != ourCoordinates.MapId)
                return false;

            distance += Vector2.Distance(last.Position, nodeCoordinates.Position);
            last = nodeCoordinates;
        }

        var targetCoordinates = _transform.ToMapCoordinates(component.Coordinates);
        if (targetCoordinates.MapId != ourCoordinates.MapId)
            return false;

        distance += Vector2.Distance(last.Position, targetCoordinates.Position);
        return true;
    }

    private void ResetRouteProgress(NPCSteeringComponent component, MapCoordinates ourCoordinates)
    {
        if (!TryGetRemainingRouteDistance(component, ourCoordinates, out var routeDistance))
        {
            component.LastRouteProgressDistance = float.PositiveInfinity;
            component.LastRouteProgressTime = TimeSpan.Zero;
            return;
        }

        component.LastRouteProgressDistance = routeDistance;
        component.LastRouteProgressTime = _timing.CurTime;
    }

    private void ResetStuck(NPCSteeringComponent component, EntityCoordinates ourCoordinates)
    {
        component.LastStuckCoordinates = ourCoordinates;
        component.LastStuckTime = _timing.CurTime;
    }

    private void CheckPath(EntityUid uid, NPCSteeringComponent steering, TransformComponent xform, bool needsPath, float targetDistance)
    {
        if (!_pathfinding)
        {
            steering.CurrentPath.Clear();
            steering.PathfindToken?.Cancel();
            steering.PathfindToken = null;
            return;
        }

        if (!needsPath && steering.CurrentPath.Count > 0)
        {
            needsPath = PathNeedsRefresh(steering);

            // If the target has sufficiently moved.
            var lastNode = GetCoordinates(steering.CurrentPath.Last());

            if (lastNode.TryDistance(EntityManager, steering.Coordinates, out var lastDistance) &&
                lastDistance > steering.RepathRange)
            {
                needsPath = true;
            }
        }

        // Request the new path.
        if (needsPath)
        {
            RequestPath(uid, steering, xform, targetDistance);
        }
    }

    private bool PathNeedsRefresh(NPCSteeringComponent component)
    {
        foreach (var poly in component.CurrentPath)
        {
            if (!poly.IsValid())
                return true;
        }

        if (component.AvoidedPathPoly is { } avoided && !avoided.IsValid())
        {
            component.AvoidedPathPoly = null;
        }

        return false;
    }

    /// <summary>
    /// We may be pathfinding and moving at the same time in which case early nodes may be out of date.
    /// </summary>
    public void PrunePath(
        EntityUid uid,
        MapCoordinates mapCoordinates,
        Vector2 direction,
        List<PathPoly> nodes,
        NPCSteeringComponent? steering = null)
    {
        if (nodes.Count <= 1)
            return;

        // Work out if we're inside any nodes, then use the next one as the starting point.
        var index = 0;
        var found = false;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var matrix = _transform.GetWorldMatrix(node.GraphUid);

            // Always want to prune the poly itself so we point to the next poly and don't backtrack.
            if (matrix.TransformBox(node.Box).Contains(mapCoordinates.Position))
            {
                index = i + 1;
                found = true;
                break;
            }
        }

        if (found)
        {
            nodes.RemoveRange(0, index);
            _pathfindingSystem.Simplify(nodes);
            ShortcutPath(uid, mapCoordinates, nodes, steering);
            return;
        }

        // Otherwise, take the node after the nearest node.

        // TODO: Really need layer support
        CollisionGroup mask = 0;

        if (TryComp<PhysicsComponent>(uid, out var physics))
        {
            mask = (CollisionGroup)physics.CollisionMask;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            if (!node.Data.IsFreeSpace)
                break;

            var nodeMap = _transform.ToMapCoordinates(node.Coordinates);

            // If any nodes are 'behind us' relative to the target we'll prune them.
            // This isn't perfect but should fix most cases of stutter stepping.
            if (nodeMap.MapId == mapCoordinates.MapId &&
                Vector2.Dot(direction, nodeMap.Position - mapCoordinates.Position) < 0f)
            {
                nodes.RemoveAt(i);
                continue;
            }

            break;
        }

        _pathfindingSystem.Simplify(nodes);
        ShortcutPath(uid, mapCoordinates, nodes, steering);
    }

    private void ShortcutPath(
        EntityUid uid,
        MapCoordinates mapCoordinates,
        List<PathPoly> nodes,
        NPCSteeringComponent? steering)
    {
        if (steering is not { EnablePathShortcutting: true } ||
            nodes.Count <= 1 ||
            !_physicsQuery.TryGetComponent(uid, out var physics))
        {
            return;
        }

        var mask = GetMovementCollisionMask(uid, physics);

        var lookahead = Math.Clamp(steering.PathShortcutLookahead, 1, nodes.Count - 1);
        var probeRadius = Math.Clamp(steering.Radius + 0.05f, 0.1f, 0.6f);

        for (var i = lookahead; i >= 1; i--)
        {
            var node = nodes[i];
            if (!node.Data.IsFreeSpace)
                continue;

            if (!IsShortcutClear(uid, mapCoordinates, node, mask, probeRadius))
                continue;

            nodes.RemoveRange(0, i);
            return;
        }
    }

    private bool IsShortcutClear(
        EntityUid uid,
        MapCoordinates start,
        PathPoly endNode,
        int collisionMask,
        float probeRadius)
    {
        return IsCorridorClear(
            uid,
            start,
            _transform.ToMapCoordinates(endNode.Coordinates),
            collisionMask,
            probeRadius);
    }

    private bool IsDirectPathClear(
        EntityUid uid,
        EntityCoordinates startCoordinates,
        EntityCoordinates endCoordinates,
        float radius,
        PhysicsComponent? physics = null)
    {
        var mask = GetMovementCollisionMask(uid, physics);

        if (mask == 0)
        {
            return true;
        }

        var ignoredTarget = endCoordinates.Position.Equals(Vector2.Zero)
            ? endCoordinates.EntityId
            : EntityUid.Invalid;

        return IsCorridorClear(
            uid,
            _transform.ToMapCoordinates(startCoordinates),
            _transform.ToMapCoordinates(endCoordinates),
            mask,
            Math.Clamp(radius + 0.05f, 0.1f, 0.6f),
            ignoredTarget);
    }

    private int GetMovementCollisionMask(EntityUid uid, PhysicsComponent? physics = null)
    {
        var (_, mask) = _physics.GetHardCollision(uid);

        if (mask == 0 && Resolve(uid, ref physics, false))
        {
            mask = physics.CollisionMask;
        }

        return mask;
    }

    private bool IsCorridorClear(
        EntityUid uid,
        MapCoordinates start,
        MapCoordinates end,
        int collisionMask,
        float probeRadius,
        EntityUid ignoredTarget = default)
    {
        if (start.MapId != end.MapId)
            return false;

        var offset = end.Position - start.Position;
        var length = offset.Length();
        if (length <= 0.01f)
            return true;

        var filter = new QueryFilter
        {
            MaskBits = collisionMask,
            Flags = QueryFlags.Dynamic | QueryFlags.Static,
            IsIgnored = entity => entity == uid ||
                                entity == ignoredTarget ||
                                HasComp<PathfindingIgnoredComponent>(entity) ||
                                IsDoorBecomingPassable(entity) ||
                                Deleted(entity),
        };

        var shape = new PhysShapeCircle(probeRadius);
        var result = _rayCast.CastShape(
            start.MapId,
            shape,
            new Transform(start.Position, Angle.Zero),
            offset,
            filter,
            RayCastSystem.RayCastClosestCallback);

        return !result.Hit;
    }

    private bool IsDoorBecomingPassable(EntityUid uid)
    {
        return _doorQuery.TryGetComponent(uid, out var door) &&
               door.State is DoorState.Open or DoorState.Opening;
    }

    /// <summary>
    /// Get the coordinates we should be heading towards.
    /// </summary>
    private EntityCoordinates GetTargetCoordinates(
        EntityUid uid,
        NPCSteeringComponent steering,
        EntityCoordinates ourCoordinates)
    {
        // Depending on what's going on we may return the target or a pathfind node.

        // Even if we're at the last node may not be able to head to target in case we get stuck on a corner or the likes.
        if (_pathfinding && steering.CurrentPath.Count >= 1 && steering.CurrentPath.TryPeek(out var nextTarget))
        {
            var coordinates = GetCoordinates(nextTarget);
            if (TryGetPathOffsetTarget(uid, steering, ourCoordinates, nextTarget, coordinates, out var offsetCoordinates))
                return offsetCoordinates;

            return coordinates;
        }

        return steering.Coordinates;
    }

    private bool TryGetPathOffsetTarget(
        EntityUid uid,
        NPCSteeringComponent steering,
        EntityCoordinates ourCoordinates,
        PathPoly node,
        EntityCoordinates targetCoordinates,
        out EntityCoordinates offsetCoordinates)
    {
        offsetCoordinates = EntityCoordinates.Invalid;

        if (!steering.EnablePathOffsets ||
            steering.PathOffsetMax <= 0f ||
            !node.Data.IsFreeSpace ||
            !targetCoordinates.IsValid(EntityManager) ||
            ourCoordinates.EntityId != node.GraphUid ||
            targetCoordinates.EntityId != node.GraphUid)
        {
            return false;
        }

        var direction = targetCoordinates.Position - ourCoordinates.Position;
        if (direction.LengthSquared() <= 0.01f)
            return false;

        var lateral = new Vector2(-direction.Y, direction.X).Normalized();
        var desiredOffset = GetPathOffsetMagnitude(uid, steering, node);
        if (desiredOffset <= 0.01f)
            return false;

        var safeMargin = Math.Clamp(steering.Radius + steering.PathOffsetSafetyPadding, 0.05f, 0.49f);
        var safeBox = node.Box.Enlarged(-safeMargin);
        if (!safeBox.IsValid())
            return false;

        var sign = GetPathOffsetSign(uid, node);
        var mask = GetMovementCollisionMask(uid);
        var probeRadius = Math.Clamp(steering.Radius + steering.PathOffsetSafetyPadding, 0.1f, 0.6f);

        for (var i = 0; i < 6; i++)
        {
            var amount = i switch
            {
                0 => desiredOffset * sign,
                1 => desiredOffset * -sign,
                2 => desiredOffset * 0.5f * sign,
                3 => desiredOffset * 0.5f * -sign,
                4 => desiredOffset * 0.25f * sign,
                _ => desiredOffset * 0.25f * -sign,
            };

            if (MathF.Abs(amount) < 0.02f)
                continue;

            var candidate = targetCoordinates.Position + lateral * amount;
            if (!safeBox.Contains(candidate))
                continue;

            var candidateCoordinates = new EntityCoordinates(node.GraphUid, candidate);
            if (mask != 0 &&
                !IsCorridorClear(
                    uid,
                    _transform.ToMapCoordinates(ourCoordinates),
                    _transform.ToMapCoordinates(candidateCoordinates),
                    mask,
                    probeRadius))
            {
                continue;
            }

            offsetCoordinates = candidateCoordinates;
            return true;
        }

        return false;
    }

    private float GetPathOffsetMagnitude(EntityUid uid, NPCSteeringComponent steering, PathPoly node)
    {
        var min = MathF.Min(steering.PathOffsetMin, steering.PathOffsetMax);
        var max = MathF.Max(steering.PathOffsetMin, steering.PathOffsetMax);
        var hash = MixPathOffsetHash(uid, node);
        var t = ((hash >> 8) & 0xFF) / 255f;
        return min + (max - min) * t;
    }

    private int GetPathOffsetSign(EntityUid uid, PathPoly node)
    {
        return (MixPathOffsetHash(uid, node) & 0x1) == 0 ? 1 : -1;
    }

    private static uint MixPathOffsetHash(EntityUid uid, PathPoly node)
    {
        var hash = (uint) uid.GetHashCode();
        hash ^= (uint) node.TileIndex * 0x9E3779B9u;
        hash ^= (uint) node.ChunkOrigin.GetHashCode() * 0x85EBCA6Bu;
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        hash *= 0x846CA68Bu;
        hash ^= hash >> 16;
        return hash;
    }

    /// <summary>
    /// Gets the fraction this value is between min and max
    /// </summary>
    /// <returns></returns>
    private float MapValue(float value, float minValue, float maxValue)
    {
        if (maxValue > minValue)
        {
            var mapped = (value - minValue) / (maxValue - minValue);
            return Math.Clamp(mapped, 0f, 1f);
        }

        return value >= minValue ? 1f : 0f;
    }

    #endregion

    #region Static Avoidance

    /// <summary>
    /// Tries to avoid static blockers such as walls.
    /// </summary>
    private void CollisionAvoidance(
        EntityUid uid,
        Angle offsetRot,
        Vector2 worldPos,
        float agentRadius,
        int layer,
        int mask,
        TransformComponent xform,
        Span<float> danger)
    {
        var objectRadius = 0.25f;
        var detectionRadius = MathF.Max(0.35f, agentRadius + objectRadius);
        var ignoreClimbedSurface = TryComp<ClimbingComponent>(uid, out var climbing) &&
                                   climbing.IsClimbing;
        var ents = _entSetPool.Get();
        _lookup.GetEntitiesInRange(uid, detectionRadius, ents, LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Approximate);

        foreach (var ent in ents)
        {
            if (ignoreClimbedSurface && _climbableQuery.HasComponent(ent))
                continue;

            if (HasComp<PathfindingIgnoredComponent>(ent))
                continue;

            // TODO: If we can access the door or smth.
            if (!_physicsQuery.TryGetComponent(ent, out var otherBody) ||
                !otherBody.Hard ||
                !otherBody.CanCollide ||
                otherBody.BodyType == BodyType.KinematicController ||
                (mask & otherBody.CollisionLayer) == 0x0 &&
                (layer & otherBody.CollisionMask) == 0x0)
            {
                continue;
            }

            var xformB = _xformQuery.GetComponent(ent);

            if (!_physics.TryGetNearest(uid, ent,
                    out var pointA, out var pointB, out var distance,
                    xform, xformB))
            {
                continue;
            }

            if (distance > detectionRadius)
                continue;

            var weight = 1f;
            var obstacleDirection = pointB - pointA;

            // Inside each other so just use worldPos
            if (distance == 0f)
            {
                obstacleDirection = _transform.GetWorldPosition(xformB) - worldPos;
            }
            else
            {
                weight = (detectionRadius - distance) / detectionRadius;
            }

            if (obstacleDirection == Vector2.Zero)
                continue;

            obstacleDirection = offsetRot.RotateVec(obstacleDirection);
            var norm = obstacleDirection.Normalized();

            for (var i = 0; i < InterestDirections; i++)
            {
                var dot = Vector2.Dot(norm, Directions[i]);
                danger[i] = MathF.Max(dot * weight, danger[i]);
            }
        }

        _entSetPool.Return(ents);
    }

    #endregion

    #region Combat Danger Avoidance

    private void IncomingProjectileAvoidance(
        EntityUid uid,
        Angle offsetRot,
        Vector2 worldPos,
        float agentRadius,
        TransformComponent xform,
        Span<float> danger)
    {
        const float detectionRadius = 6f;
        const float maxTimeToImpact = 1.1f;
        const float baseLanePadding = 0.45f;

        var ents = _entSetPool.Get();
        _lookup.GetEntitiesInRange(uid, detectionRadius, ents, LookupFlags.Dynamic | LookupFlags.Approximate);

        foreach (var ent in ents)
        {
            if (!_projectileQuery.TryGetComponent(ent, out var projectile) ||
                projectile.ProjectileSpent ||
                projectile.Shooter == uid ||
                projectile.Shooter is { } shooter && IsFriendly(uid, shooter) ||
                !_physicsQuery.TryGetComponent(ent, out var projectileBody) ||
                !_xformQuery.TryGetComponent(ent, out var projectileXform) ||
                projectileXform.MapID != xform.MapID)
            {
                continue;
            }

            var velocity = projectileBody.LinearVelocity;
            if (velocity.LengthSquared() <= 0.01f)
                velocity = projectile.Angle.ToWorldVec();

            if (velocity.LengthSquared() <= 0.01f)
                continue;

            var speed = velocity.Length();
            var direction = velocity / speed;
            var projectilePos = _transform.GetWorldPosition(projectileXform);
            var toNpc = worldPos - projectilePos;
            var along = Vector2.Dot(toNpc, direction);

            if (along <= 0f)
                continue;

            var timeToClosest = along / speed;
            if (timeToClosest > maxTimeToImpact)
                continue;

            var closestOffset = toNpc - direction * along;
            var laneRadius = agentRadius + baseLanePadding;
            var lateralDistance = closestOffset.Length();
            if (lateralDistance > laneRadius)
                continue;

            var closestPoint = projectilePos + direction * along;
            var dangerDirection = closestPoint - worldPos;
            var urgency = 1f - Math.Clamp(timeToClosest / maxTimeToImpact, 0f, 1f);
            var laneWeight = 1f - Math.Clamp(lateralDistance / laneRadius, 0f, 1f);
            var weight = Math.Clamp(0.35f + urgency * 0.45f + laneWeight * 0.45f, 0f, 1f);

            if (dangerDirection.LengthSquared() > 0.001f)
                ApplyDanger(danger, offsetRot.RotateVec(dangerDirection), weight);

            // Head-on shots have little lateral offset, so block forward/back movement and let side slots win.
            ApplyDanger(danger, offsetRot.RotateVec(direction), weight * 0.55f);
            ApplyDanger(danger, offsetRot.RotateVec(-direction), weight * 0.35f);
        }

        _entSetPool.Return(ents);
    }

    private void TurretThreatAvoidance(
        EntityUid uid,
        Angle offsetRot,
        Vector2 worldPos,
        float agentRadius,
        TransformComponent xform,
        Span<float> danger)
    {
        var origin = _transform.GetMapCoordinates(uid, xform: xform);
        const float maxDetectionRadius = 16f;

        foreach (var turret in _lookup.GetEntitiesInRange<WH40KTurretProfileComponent>(origin, maxDetectionRadius))
        {
            var turretUid = turret.Owner;
            if (!IsActiveHostileTurret(uid, turretUid) ||
                !_xformQuery.TryGetComponent(turretUid, out var turretXform) ||
                turretXform.MapID != xform.MapID)
            {
                continue;
            }

            var turretPos = _transform.GetWorldPosition(turretXform);
            var toNpc = worldPos - turretPos;
            var distance = toNpc.Length();
            if (distance <= 0.01f)
                continue;

            var range = MathF.Min(maxDetectionRadius, MathF.Max(0.1f, turret.Comp.FireRange ?? turret.Comp.DetectionRange));
            if (distance > range)
                continue;

            var firingDirection = _transform.GetWorldRotation(turretXform).ToWorldVec();
            if (firingDirection.LengthSquared() <= 0.01f)
                continue;

            firingDirection = Vector2.Normalize(firingDirection);
            var along = Vector2.Dot(toNpc, firingDirection);
            if (along <= 0f || along > range)
                continue;

            var closestPoint = turretPos + firingDirection * along;
            var lateral = Vector2.Distance(worldPos, closestPoint);
            var laneRadius = agentRadius + 0.75f;
            if (lateral > laneRadius)
                continue;

            if (!_interaction.InRangeUnobstructed(turretUid, uid, distance + 0.1f, CollisionGroup.Opaque))
                continue;

            var laneWeight = 1f - Math.Clamp(lateral / laneRadius, 0f, 1f);
            var rangeWeight = 1f - Math.Clamp(along / range, 0f, 1f);
            var weight = Math.Clamp(0.3f + laneWeight * 0.45f + rangeWeight * 0.25f, 0f, 0.95f);
            var dangerDirection = closestPoint - worldPos;

            if (dangerDirection.LengthSquared() > 0.001f)
                ApplyDanger(danger, offsetRot.RotateVec(dangerDirection), weight);

            ApplyDanger(danger, offsetRot.RotateVec(firingDirection), weight * 0.45f);
            ApplyDanger(danger, offsetRot.RotateVec(-firingDirection), weight * 0.35f);
        }
    }

    private bool IsActiveHostileTurret(EntityUid uid, EntityUid turret)
    {
        if (!_turretProfileQuery.HasComponent(turret) ||
            !_factionQuery.TryGetComponent(uid, out var ourFaction) ||
            !_factionQuery.TryGetComponent(turret, out var turretFaction) ||
            !IsFactionHostileTo(uid, ourFaction, turretFaction) ||
            _npcFaction.IsEntityFriendly((uid, ourFaction), (turret, turretFaction)))
        {
            return false;
        }

        if (_deployableTurretQuery.TryGetComponent(turret, out var deployable) &&
            (!deployable.Enabled || deployable.CurrentState == DeployableTurretState.Broken))
        {
            return false;
        }

        return !_destructibleQuery.TryGetComponent(turret, out var destructible) ||
               !destructible.IsBroken;
    }

    private bool IsFactionHostileTo(
        EntityUid uid,
        NpcFactionMemberComponent ourFaction,
        NpcFactionMemberComponent otherFaction)
    {
        foreach (var faction in otherFaction.Factions)
        {
            if (_npcFaction.IsFactionHostile(faction, (uid, ourFaction)))
                return true;
        }

        return false;
    }

    private bool IsFriendly(EntityUid uid, EntityUid other)
    {
        return _factionQuery.TryGetComponent(uid, out var ourFaction) &&
               _factionQuery.TryGetComponent(other, out var otherFaction) &&
               _npcFaction.IsEntityFriendly((uid, ourFaction), (other, otherFaction));
    }

    private static void ApplyDanger(Span<float> danger, Vector2 direction, float weight)
    {
        if (weight <= 0f || direction.LengthSquared() <= 0.001f)
            return;

        var norm = Vector2.Normalize(direction);
        for (var i = 0; i < InterestDirections; i++)
        {
            var dot = Vector2.Dot(norm, Directions[i]);
            if (dot <= 0f)
                continue;

            danger[i] = MathF.Max(danger[i], dot * weight);
        }
    }

    #endregion

    #region Dynamic Avoidance

    /// <summary>
    /// Tries to avoid mobs of the same faction.
    /// </summary>
    private void Separation(
        EntityUid uid,
        Angle offsetRot,
        Vector2 worldPos,
        float agentRadius,
        int layer,
        int mask,
        PhysicsComponent body,
        TransformComponent xform,
        Span<float> danger)
    {
        var objectRadius = 0.25f;
        var detectionRadius = MathF.Max(0.35f, agentRadius + objectRadius);
        var collective = TryGetCollectiveGroup(uid, out var group);
        if (collective)
            detectionRadius = MathF.Max(detectionRadius, group.SeparationRadius);

        _factionQuery.TryGetComponent(uid, out var ourFaction);
        var ents = _entSetPool.Get();
        _lookup.GetEntitiesInRange(uid, detectionRadius, ents, LookupFlags.Dynamic | LookupFlags.Approximate);

        foreach (var ent in ents)
        {
            if (ent == uid)
                continue;

            if (HasComp<PathfindingIgnoredComponent>(ent))
                continue;

            var sameCollectiveGroup = collective && IsInCollectiveGroup(ent, group.GroupId);

            // TODO: If we can access the door or smth.
            if (!_physicsQuery.TryGetComponent(ent, out var otherBody) ||
                !otherBody.Hard ||
                !otherBody.CanCollide ||
                (mask & otherBody.CollisionLayer) == 0x0 &&
                (layer & otherBody.CollisionMask) == 0x0)
            {
                continue;
            }

            if (!sameCollectiveGroup &&
                (!_factionQuery.TryGetComponent(ent, out var otherFaction) ||
                 !_npcFaction.IsEntityFriendly((uid, ourFaction), (ent, otherFaction))))
            {
                continue;
            }

            var xformB = _xformQuery.GetComponent(ent);

            if (!_physics.TryGetNearest(uid, ent, out var pointA, out var pointB, out var distance, xform, xformB))
            {
                continue;
            }

            if (distance > detectionRadius)
                continue;

            var weight = 1f;
            var obstacleDirection = pointB - pointA;

            // Inside each other so just use worldPos
            if (distance == 0f)
            {
                obstacleDirection = _transform.GetWorldPosition(xformB) - worldPos;

                // Welp
                if (obstacleDirection == Vector2.Zero)
                {
                    obstacleDirection = _random.NextAngle().ToVec();
                }
            }
            else
            {
                weight = (detectionRadius - distance) / detectionRadius;
            }

            obstacleDirection = offsetRot.RotateVec(obstacleDirection);
            var norm = obstacleDirection.Normalized();
            weight *= sameCollectiveGroup ? group.SeparationWeight : 0.25f;

            for (var i = 0; i < InterestDirections; i++)
            {
                var dot = Vector2.Dot(norm, Directions[i]);
                danger[i] = MathF.Max(dot * weight, danger[i]);
            }
        }

        _entSetPool.Return(ents);
    }

    #endregion

    // TODO: Alignment

    // TODO: Cohesion
    private void Blend(NPCSteeringComponent steering, float frameTime, Span<float> interest, Span<float> danger)
    {
        /*
         * Future sloth notes:
         * Pathfinder cleanup:
            - Cleanup whatever the fuck is happening in pathfinder
            - Use Flee for melee behavior / actions and get the seek direction from that rather than bulldozing
            - Must always have a path
            - Path should return the full version + the snipped version
            - Pathfinder needs to do diagonals
            - Next node is either <current node + 1> or <nearest node + 1> (on the full path)
            - If greater than <1.5m distance> repath
         */

        // IDK why I didn't do this sooner but blending is a lot better than lastdir for fixing stuttering.
        const float BlendWeight = 10f;
        var blendValue = Math.Min(1f, frameTime * BlendWeight);

        for (var i = 0; i < InterestDirections; i++)
        {
            var currentInterest = interest[i];
            var lastInterest = steering.Interest[i];
            var interestDiff = (currentInterest - lastInterest) * blendValue;
            steering.Interest[i] = lastInterest + interestDiff;

            var currentDanger = danger[i];
            var lastDanger = steering.Danger[i];
            var dangerDiff = (currentDanger - lastDanger) * blendValue;
            steering.Danger[i] = lastDanger + dangerDiff;
        }
    }
}
