using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Spawning;

public sealed partial class WH40KSpawnPointSystem : EntitySystem
{
    [Dependency] private  GameTicker _gameTicker = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  StationSystem _stationSystem = default!;
    [Dependency] private  StationSpawningSystem _stationSpawning = default!;
    private readonly Dictionary<(EntityUid Station, ProtoId<JobPrototype> Job), List<EntityCoordinates>> _spawnCache = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawningEvent>(
            OnPlayerSpawning,
            after: new[] { typeof(ContainerSpawnPointSystem) },
            before: new[] { typeof(SpawnPointSystem) });
        SubscribeLocalEvent<SpawnPointComponent, MapInitEvent>((_, _, _) => InvalidateSpawnCache());
        SubscribeLocalEvent<SpawnPointComponent, ComponentShutdown>((_, _, _) => InvalidateSpawnCache());
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => InvalidateSpawnCache());
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        if (!IsWh40KTeamBattleActive())
            return;

        if (args.Job == null || args.Station == null)
            return;

        var possiblePositions = GetSpawnPositions(args.Station.Value, args.Job.Value);

        if (possiblePositions.Count == 0)
            return;

        var spawnLoc = _random.Pick(possiblePositions);
        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            spawnLoc,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);
    }

    private bool IsWh40KTeamBattleActive()
    {
        var query = EntityQueryEnumerator<WH40KTeamBattleRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out _, out var gameRule))
        {
            if (_gameTicker.IsGameRuleActive(uid, gameRule))
                return true;
        }

        return false;
    }

    private List<EntityCoordinates> GetSpawnPositions(EntityUid station, ProtoId<JobPrototype> job)
    {
        var key = (station, job);
        if (_spawnCache.TryGetValue(key, out var cached))
            return cached;

        var result = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (spawnPoint.SpawnType != SpawnPointType.Job)
                continue;

            if (spawnPoint.Job != null && spawnPoint.Job != job)
                continue;

            if (_stationSystem.GetOwningStation(uid, xform) != station)
                continue;

            result.Add(xform.Coordinates);
        }

        _spawnCache[key] = result;
        return result;
    }

    private void InvalidateSpawnCache()
    {
        _spawnCache.Clear();
    }
}
