using System;
using System.Linq;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WH40K.WaveDefence;

public sealed class WH40KWaveDefenceMapRegistrySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
    }

    public bool ValidateLayout(
        MapId mapId,
        string defendingTeamId,
        int minimumRequiredAttackLanes,
        out List<string> errors)
    {
        errors = new List<string>();

        if (!TryGetPrimaryObjective(mapId, defendingTeamId, out _))
            errors.Add($"WaveDefence map {mapId} has no primary objective for team '{defendingTeamId}'.");

        if (GetSpawnPoints(mapId, WH40KWaveSpawnPointType.DefenderStart, defendingTeamId).Count == 0)
            errors.Add($"WaveDefence map {mapId} has no defender start markers for team '{defendingTeamId}'.");

        if (GetSpawnPoints(mapId, WH40KWaveSpawnPointType.DefenderReinforcement, defendingTeamId).Count == 0)
            errors.Add($"WaveDefence map {mapId} has no defender reinforcement markers for team '{defendingTeamId}'.");

        if (GetSpawnPoints(mapId, WH40KWaveSpawnPointType.Attacker).Count == 0)
            errors.Add($"WaveDefence map {mapId} has no attacker spawn markers.");

        return errors.Count == 0;
    }

    public bool TryGetPrimaryObjective(MapId mapId, string defendingTeamId, out EntityUid objectiveUid)
    {
        var query = EntityQueryEnumerator<WH40KWaveDefenceObjectiveComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var objective, out var xform))
        {
            if (xform.MapID != mapId ||
                objective.Destroyed ||
                !objective.IsPrimaryObjective ||
                !string.Equals(objective.TeamId, defendingTeamId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            objectiveUid = uid;
            return true;
        }

        objectiveUid = EntityUid.Invalid;
        return false;
    }

    public List<(EntityUid Uid, WH40KWaveSpawnPointComponent Spawn, TransformComponent Xform)> GetSpawnPoints(
        MapId mapId,
        WH40KWaveSpawnPointType spawnType,
        string? teamId = null,
        string? spawnId = null)
    {
        var result = new List<(EntityUid, WH40KWaveSpawnPointComponent, TransformComponent)>();
        var query = EntityQueryEnumerator<WH40KWaveSpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (xform.MapID != mapId || spawn.SpawnType != spawnType)
                continue;

            if (!string.IsNullOrWhiteSpace(teamId) &&
                !string.Equals(spawn.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(spawnId) &&
                !string.Equals(spawn.SpawnId, spawnId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add((uid, spawn, xform));
        }

        return result;
    }

    public HashSet<string> GetLaneIds(MapId mapId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var query = EntityQueryEnumerator<WH40KWaveLanePointComponent, TransformComponent>();
        while (query.MoveNext(out _, out var point, out var xform))
        {
            if (xform.MapID != mapId || !point.Enabled || string.IsNullOrWhiteSpace(point.LaneId))
                continue;

            result.Add(point.LaneId);
        }

        return result;
    }

    public List<EntityUid> GetLaneRoute(MapId mapId, string laneId)
    {
        return GetLaneRoute(mapId, laneId, null);
    }

    public List<EntityUid> GetLaneRoute(MapId mapId, string laneId, WH40KWaveSquadRole? role)
    {
        var points = GetLanePoints(mapId, laneId, role);
        return points.Select(point => point.Uid).ToList();
    }

    public List<(EntityUid Uid, WH40KWaveLanePointComponent Point, TransformComponent Xform)> GetLanePoints(
        MapId mapId,
        string laneId,
        WH40KWaveSquadRole? role = null,
        WH40KWaveLanePointType? pointType = null)
    {
        var points = new List<(EntityUid Uid, WH40KWaveLanePointComponent Point, TransformComponent Xform)>();
        var query = EntityQueryEnumerator<WH40KWaveLanePointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (xform.MapID != mapId ||
                !point.Enabled ||
                !string.Equals(point.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pointType != null && point.PointType != pointType.Value)
                continue;

            if (role != null &&
                point.AllowedRoles.Count > 0 &&
                !point.AllowedRoles.Contains(role.Value))
            {
                continue;
            }

            points.Add((uid, point, xform));
        }

        ApplyAutoOrders(points);

        if (pointType != null)
            points = points.Where(point => point.Point.PointType == pointType.Value).ToList();

        points.Sort(static (a, b) =>
        {
            var orderCompare = a.Point.Order.CompareTo(b.Point.Order);
            if (orderCompare != 0)
                return orderCompare;

            var idCompare = StringComparer.OrdinalIgnoreCase.Compare(a.Point.PointId, b.Point.PointId);
            if (idCompare != 0)
                return idCompare;

            return a.Uid.Id.CompareTo(b.Uid.Id);
        });
        return points;
    }

    private static void ApplyAutoOrders(List<(EntityUid Uid, WH40KWaveLanePointComponent Point, TransformComponent Xform)> points)
    {
        if (points.Count == 0)
            return;

        var autoPoints = points
            .Where(entry => entry.Point.AutoOrder)
            .OrderBy(entry => entry.Uid.Id)
            .ToList();

        if (autoPoints.Count == 0)
            return;

        var usedOrders = points
            .Where(entry => !entry.Point.AutoOrder && entry.Point.Order >= 0)
            .Select(entry => entry.Point.Order)
            .ToHashSet();

        var nextOrder = 0;
        foreach (var entry in autoPoints)
        {
            while (usedOrders.Contains(nextOrder))
            {
                nextOrder++;
            }

            if (entry.Point.Order != nextOrder)
                entry.Point.Order = nextOrder;

            usedOrders.Add(nextOrder);
            nextOrder++;
        }
    }

    public bool TryPickSpawnCoordinate(
        MapId mapId,
        WH40KWaveSpawnPointType spawnType,
        IRobustRandom random,
        out EntityCoordinates coordinates,
        string? teamId = null,
        string? spawnId = null)
    {
        var points = GetSpawnPoints(mapId, spawnType, teamId, spawnId);
        if (points.Count == 0)
        {
            coordinates = EntityCoordinates.Invalid;
            return false;
        }

        coordinates = random.Pick(points).Xform.Coordinates;
        return true;
    }

    public bool TryPickAttackerSpawnCoordinateForLane(
        MapId mapId,
        string laneId,
        IRobustRandom random,
        out EntityCoordinates coordinates,
        string? spawnId = null)
    {
        var points = GetSpawnPoints(mapId, WH40KWaveSpawnPointType.Attacker, spawnId: spawnId)
            .Where(point =>
                point.Spawn.LaneIds.Count == 0 ||
                point.Spawn.LaneIds.Any(id => string.Equals(id, laneId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (points.Count == 0)
            return TryPickSpawnCoordinate(mapId, WH40KWaveSpawnPointType.Attacker, random, out coordinates, spawnId: spawnId);

        coordinates = random.Pick(points).Xform.Coordinates;
        return true;
    }

    public bool LaneHasPointType(MapId mapId, string laneId, WH40KWaveLanePointType pointType, WH40KWaveSquadRole? role = null)
    {
        return GetLanePoints(mapId, laneId, role, pointType).Count > 0;
    }

    public bool HasImperiumBaseMarker(MapId mapId, string defendingTeamId)
    {
        var query = EntityQueryEnumerator<WH40KWaveImperiumBaseComponent, TransformComponent>();
        while (query.MoveNext(out _, out var marker, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            if (string.Equals(marker.TeamId, defendingTeamId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void ValidateLaneRouteVariant(
        MapId mapId,
        string laneId,
        WH40KWaveSquadRole? role,
        List<string> errors)
    {
        var points = GetLanePoints(mapId, laneId, role);
        if (points.Count == 0)
            return;

        var scope = role?.ToString() ?? "Default";
        var seenPointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateOrders = points
            .GroupBy(point => point.Point.Order)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(order => order)
            .ToList();

        foreach (var order in duplicateOrders)
        {
            errors.Add(
                $"WaveDefence lane '{laneId}' role '{scope}' on map {mapId} has duplicate route order {order}.");
        }

        foreach (var (uid, point, _) in points)
        {
            if (point.Order < 0)
            {
                errors.Add(
                    $"WaveDefence lane '{laneId}' role '{scope}' on map {mapId} uses negative order {point.Order} on {ToPrettyString(uid)}.");
            }

            if (string.IsNullOrWhiteSpace(point.PointId))
                continue;

            if (!seenPointIds.Add(point.PointId))
            {
                errors.Add(
                    $"WaveDefence lane '{laneId}' role '{scope}' on map {mapId} has duplicate pointId '{point.PointId}'.");
            }
        }
    }
}
