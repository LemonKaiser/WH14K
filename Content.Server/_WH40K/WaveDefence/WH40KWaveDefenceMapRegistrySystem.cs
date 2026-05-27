using System;
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
}
