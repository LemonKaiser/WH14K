using System.Collections.Generic;
using System.Numerics;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Damage.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WH40K.WaveDefence;

/// <summary>
/// Resolves a practical attack approach around the wave defence objective.
/// This avoids steering directly into the center of a solid objective tile,
/// which would otherwise make A* fail before the NPC ever starts the siege.
/// </summary>
public sealed partial class WH40KWaveDefenceObjectiveNavigationSystem : EntitySystem
{
    [Dependency] private  PathfindingSystem _pathfinding = default!;
    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    private const int MaxApproachSearchRadius = 6;
    private static readonly float[] ForwardOffsets = [0.85f, 1.25f, 1.75f, 2.25f, 2.85f, 3.45f];
    private static readonly float[] LateralOffsets = [0f, 0.55f, -0.55f, 1.05f, -1.05f];
    private static readonly (float Backward, float Lateral)[] SwarmSlotOffsets =
    [
        (0f, 0f),
        (0.35f, 0.4f),
        (0.35f, -0.4f),
        (0.65f, 0.2f),
        (0.65f, -0.2f),
        (0.8f, 0f),
    ];
    private static readonly (float Backward, float Lateral)[] SwarmAttackSlotOffsets =
    [
        (0f, 0f),
        (0.1f, 0.22f),
        (0.1f, -0.22f),
        (0.2f, 0.12f),
        (0.2f, -0.12f),
    ];
    private const CollisionGroup BlockerRayMask = CollisionGroup.MobMask | CollisionGroup.InteractImpassable;
    private const float ObjectiveMeleeReachEpsilon = 0.02f;

    public bool TryResolveObjectiveAssaultTarget(
        EntityUid attacker,
        EntityCoordinates origin,
        EntityUid objective,
        out EntityCoordinates coordinates)
    {
        return TryResolveObjectiveAssaultTarget(attacker, origin, objective, out coordinates, out _);
    }

    public bool TryResolveObjectiveAssaultTarget(
        EntityUid attacker,
        EntityCoordinates origin,
        EntityUid objective,
        out EntityCoordinates coordinates,
        out EntityUid blocker)
    {
        blocker = EntityUid.Invalid;

        if (TryResolveObjectiveBlockerTarget(attacker, origin, objective, out coordinates, out blocker))
            return true;

        return TryResolveObjectiveApproach(origin, objective, out coordinates);
    }

    public bool TryResolveObjectiveApproach(
        EntityCoordinates origin,
        EntityUid objective,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        if (!TryGetObjectiveCoordinates(objective, out var objectiveCoordinates, out var objectiveMap))
            return false;

        return TryResolveApproachCoordinate(origin, objectiveCoordinates, objectiveMap.Position, out coordinates);
    }

    public bool TryResolveObjectiveMeleeTarget(
        EntityUid attacker,
        EntityCoordinates origin,
        EntityUid objective,
        float meleeRange,
        out EntityCoordinates coordinates,
        out EntityUid blocker)
    {
        blocker = EntityUid.Invalid;
        coordinates = EntityCoordinates.Invalid;

        if (TryResolveObjectiveBlockerTarget(attacker, origin, objective, out coordinates, out blocker))
            return true;

        if (!TryGetObjectiveCoordinates(objective, out var objectiveCoordinates, out var objectiveMap))
            return false;

        if (TryResolveCombatApproachCoordinate(origin, objectiveCoordinates, objectiveMap.Position, meleeRange, out coordinates))
            return true;

        return TryResolveObjectiveApproach(origin, objective, out coordinates);
    }

    public bool TryResolvePointApproach(
        EntityCoordinates origin,
        EntityCoordinates targetCoordinates,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        var targetMap = _transform.ToMapCoordinates(targetCoordinates);
        if (targetMap.MapId == MapId.Nullspace)
            return false;

        return TryResolveApproachCoordinate(origin, targetCoordinates, targetMap.Position, out coordinates);
    }

    public bool TryResolveSwarmSlotTarget(
        EntityUid attacker,
        EntityCoordinates origin,
        EntityCoordinates targetCoordinates,
        out EntityCoordinates coordinates)
    {
        coordinates = targetCoordinates;

        if (_pathfinding.GetPoly(targetCoordinates) == null)
            return false;

        var originMap = _transform.ToMapCoordinates(origin);
        var targetMap = _transform.ToMapCoordinates(targetCoordinates);
        if (originMap.MapId == MapId.Nullspace || originMap.MapId != targetMap.MapId)
            return true;

        var forward = targetMap.Position - originMap.Position;
        if (forward.LengthSquared() <= 0.001f)
            forward = Vector2.UnitX;
        else
            forward = Vector2.Normalize(forward);

        var perpendicular = new Vector2(-forward.Y, forward.X);
        var startIndex = Math.Abs(attacker.Id.GetHashCode()) % SwarmSlotOffsets.Length;

        for (var offsetIndex = 0; offsetIndex < SwarmSlotOffsets.Length; offsetIndex++)
        {
            var slot = SwarmSlotOffsets[(startIndex + offsetIndex) % SwarmSlotOffsets.Length];
            var candidatePosition = targetMap.Position - forward * slot.Backward + perpendicular * slot.Lateral;
            var candidate = _transform.ToCoordinates(
                targetCoordinates.EntityId,
                new MapCoordinates(candidatePosition, targetMap.MapId));

            if (_pathfinding.GetPoly(candidate) == null)
                continue;

            coordinates = candidate;
            return true;
        }

        return true;
    }

    public bool TryResolveSwarmAttackSlotTarget(
        EntityUid attacker,
        EntityCoordinates origin,
        EntityCoordinates targetCoordinates,
        EntityCoordinates attackReference,
        float meleeRange,
        out EntityCoordinates coordinates)
    {
        coordinates = targetCoordinates;

        if (_pathfinding.GetPoly(targetCoordinates) == null)
            return false;

        var originMap = _transform.ToMapCoordinates(origin);
        var targetMap = _transform.ToMapCoordinates(targetCoordinates);
        var attackMap = _transform.ToMapCoordinates(attackReference);
        if (originMap.MapId == MapId.Nullspace ||
            targetMap.MapId == MapId.Nullspace ||
            attackMap.MapId == MapId.Nullspace ||
            originMap.MapId != targetMap.MapId ||
            targetMap.MapId != attackMap.MapId)
        {
            return true;
        }

        var forward = targetMap.Position - originMap.Position;
        if (forward.LengthSquared() <= 0.001f)
            forward = targetMap.Position - attackMap.Position;

        if (forward.LengthSquared() <= 0.001f)
            forward = Vector2.UnitX;
        else
            forward = Vector2.Normalize(forward);

        var perpendicular = new Vector2(-forward.Y, forward.X);
        var startIndex = Math.Abs(attacker.Id.GetHashCode()) % SwarmAttackSlotOffsets.Length;

        for (var offsetIndex = 0; offsetIndex < SwarmAttackSlotOffsets.Length; offsetIndex++)
        {
            var slot = SwarmAttackSlotOffsets[(startIndex + offsetIndex) % SwarmAttackSlotOffsets.Length];
            var candidatePosition = targetMap.Position - forward * slot.Backward + perpendicular * slot.Lateral;
            var candidate = _transform.ToCoordinates(
                targetCoordinates.EntityId,
                new MapCoordinates(candidatePosition, targetMap.MapId));

            if (_pathfinding.GetPoly(candidate) == null)
                continue;

            var candidateMap = _transform.ToMapCoordinates(candidate);
            if (candidateMap.MapId == MapId.Nullspace)
                continue;

            if (Vector2.Distance(candidateMap.Position, attackMap.Position) > meleeRange + ObjectiveMeleeReachEpsilon)
                continue;

            coordinates = candidate;
            return true;
        }

        return true;
    }

    private bool TryResolveObjectiveBlockerTarget(
        EntityUid attacker,
        EntityCoordinates origin,
        EntityUid objective,
        out EntityCoordinates coordinates,
        out EntityUid blocker)
    {
        coordinates = EntityCoordinates.Invalid;
        blocker = EntityUid.Invalid;

        if (!TryGetObjectiveCoordinates(objective, out var objectiveCoordinates, out var objectiveMap))
            return false;

        var originMap = _transform.ToMapCoordinates(origin);
        if (originMap.MapId == MapId.Nullspace || originMap.MapId != objectiveMap.MapId)
            return false;

        var direction = objectiveMap.Position - originMap.Position;
        var length = direction.Length();
        if (length <= 0.05f)
            return false;

        var normalized = Vector2.Normalize(direction);
        var ray = new CollisionRay(originMap.Position, normalized, (int) BlockerRayMask);

        foreach (var hit in _physics.IntersectRayWithPredicate(
                     originMap.MapId,
                     ray,
                     length,
                     entity => entity == attacker || entity == objective || Deleted(entity),
                     false))
        {
            if (!IsSiegeBlockerCandidate(hit.HitEntity))
                continue;

            blocker = hit.HitEntity;

            if (TryResolvePointBeyondBlocker(
                    objectiveCoordinates,
                    objectiveMap.MapId,
                    objectiveMap.Position,
                    hit.HitPos,
                    normalized,
                    out coordinates))
            {
                return true;
            }

            if (TryResolveApproachCoordinate(
                    origin,
                    objectiveCoordinates,
                    objectiveMap.Position,
                    out coordinates,
                    preferredDirection: normalized,
                    minimumDirectionalDot: 0.15f))
            {
                return true;
            }

            break;
        }

        return false;
    }

    private bool TryGetObjectiveCoordinates(
        EntityUid objective,
        out EntityCoordinates objectiveCoordinates,
        out MapCoordinates objectiveMap)
    {
        objectiveCoordinates = EntityCoordinates.Invalid;
        objectiveMap = MapCoordinates.Nullspace;

        if (!TryComp(objective, out TransformComponent? objectiveXform) ||
            objectiveXform.MapID == MapId.Nullspace)
        {
            return false;
        }

        objectiveCoordinates = objectiveXform.Coordinates;
        objectiveMap = _transform.ToMapCoordinates(objectiveCoordinates);
        return objectiveMap.MapId != MapId.Nullspace;
    }

    private bool TryResolveApproachCoordinate(
        EntityCoordinates origin,
        EntityCoordinates objectiveCoordinates,
        Vector2 objectiveWorldPosition,
        out EntityCoordinates coordinates,
        Vector2? preferredDirection = null,
        float minimumDirectionalDot = float.NegativeInfinity)
    {
        coordinates = EntityCoordinates.Invalid;

        if (_pathfinding.GetPoly(objectiveCoordinates) != null)
        {
            coordinates = objectiveCoordinates;
            return true;
        }

        var found = false;
        var bestScore = float.MaxValue;
        foreach (var candidate in EnumerateObjectiveCandidates(objectiveCoordinates))
        {
            if (_pathfinding.GetPoly(candidate) == null)
                continue;

            var candidateMap = _transform.ToMapCoordinates(candidate);
            if (candidateMap.MapId == MapId.Nullspace)
                continue;

            if (preferredDirection is { } direction)
            {
                var offset = candidateMap.Position - objectiveWorldPosition;
                if (offset.LengthSquared() > 0.001f)
                {
                    var dot = Vector2.Dot(Vector2.Normalize(offset), direction);
                    if (dot < minimumDirectionalDot)
                        continue;
                }
            }

            var objectiveDistance = Vector2.Distance(candidateMap.Position, objectiveWorldPosition);
            var originDistance = origin.TryDistance(EntityManager, candidate, out var candidateDistance)
                ? candidateDistance
                : float.MaxValue;
            var score = objectiveDistance * 4f + originDistance;

            if (score >= bestScore)
                continue;

            bestScore = score;
            coordinates = candidate;
            found = true;
        }

        return found;
    }

    private bool TryResolveCombatApproachCoordinate(
        EntityCoordinates origin,
        EntityCoordinates objectiveCoordinates,
        Vector2 objectiveWorldPosition,
        float meleeRange,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        var found = false;
        var bestScore = float.MaxValue;
        var maxReach = MathF.Max(0.1f, meleeRange) + ObjectiveMeleeReachEpsilon;

        foreach (var candidate in EnumerateObjectiveCandidates(objectiveCoordinates))
        {
            if (_pathfinding.GetPoly(candidate) == null)
                continue;

            var candidateMap = _transform.ToMapCoordinates(candidate);
            if (candidateMap.MapId == MapId.Nullspace)
                continue;

            var objectiveDistance = Vector2.Distance(candidateMap.Position, objectiveWorldPosition);
            if (objectiveDistance > maxReach)
                continue;

            var originDistance = origin.TryDistance(EntityManager, candidate, out var candidateDistance)
                ? candidateDistance
                : float.MaxValue;
            var score = objectiveDistance * 6f + originDistance;

            if (score >= bestScore)
                continue;

            bestScore = score;
            coordinates = candidate;
            found = true;
        }

        return found;
    }

    private bool TryResolvePointBeyondBlocker(
        EntityCoordinates objectiveCoordinates,
        MapId objectiveMapId,
        Vector2 objectiveWorldPosition,
        Vector2 hitPosition,
        Vector2 direction,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var bestScore = float.MaxValue;
        var found = false;

        foreach (var forward in ForwardOffsets)
        {
            foreach (var lateral in LateralOffsets)
            {
                var candidatePosition = hitPosition + direction * forward + perpendicular * lateral;
                var candidate = _transform.ToCoordinates(
                    objectiveCoordinates.EntityId,
                    new MapCoordinates(candidatePosition, objectiveMapId));
                if (_pathfinding.GetPoly(candidate) == null)
                    continue;

                var candidateMap = _transform.ToMapCoordinates(candidate);
                if (candidateMap.MapId == MapId.Nullspace)
                    continue;

                var objectiveDistance = Vector2.Distance(candidateMap.Position, objectiveWorldPosition);
                var score = objectiveDistance + MathF.Abs(lateral) * 0.65f;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                coordinates = candidate;
                found = true;
            }
        }

        return found;
    }

    private IEnumerable<EntityCoordinates> EnumerateObjectiveCandidates(EntityCoordinates objectiveCoordinates)
    {
        for (var radius = 1; radius <= MaxApproachSearchRadius; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                        continue;

                    yield return new EntityCoordinates(
                        objectiveCoordinates.EntityId,
                        objectiveCoordinates.Position + new Vector2(dx, dy));
                }
            }
        }
    }

    private bool IsSiegeBlockerCandidate(EntityUid entity)
    {
        return TryComp<DamageableComponent>(entity, out _) ||
               TryComp<DoorComponent>(entity, out _);
    }
}
