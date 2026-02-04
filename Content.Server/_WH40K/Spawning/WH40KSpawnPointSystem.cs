using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Spawning;

public sealed class WH40KSpawnPointSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawningEvent>(
            OnPlayerSpawning,
            after: new[] { typeof(ContainerSpawnPointSystem) },
            before: new[] { typeof(SpawnPointSystem) });
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        if (!IsWh40KTeamBattleActive())
            return;

        if (args.Job == null || args.Station == null)
            return;

        var possiblePositions = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (spawnPoint.SpawnType != SpawnPointType.Job)
                continue;

            if (spawnPoint.Job != null && spawnPoint.Job != args.Job)
                continue;

            if (_stationSystem.GetOwningStation(uid, xform) != args.Station)
                continue;

            possiblePositions.Add(xform.Coordinates);
        }

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
}
