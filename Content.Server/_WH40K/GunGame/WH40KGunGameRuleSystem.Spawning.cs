using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.KillTracking;
using Content.Server.Spawners.Components;
using Content.Shared._WH40K.GunGame;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    [Dependency] private TransformSystem _transform = default!;

    private static readonly ProtoId<JobPrototype> FallbackJob = "GunGamePlayer";
    private static readonly HashSet<ProtoId<SpeciesPrototype>> AllowedGunGameSpecies =
    [
        "Human",
        "Felinid",
        "Dwarf"
    ];

    private bool SpawnGunGamePlayer(ICommonSession player, HumanoidCharacterProfile originalProfile, EntityUid station, EntityUid ruleEntity, WH40KGunGameRuleComponent rule)
    {
        var profile = SanitizeProfileForGunGame(player, originalProfile);
        var (mindId, mind) = _mind.GetOrCreateMind(player.UserId);
        mind.CharacterName = profile.Name;
        Dirty(mindId, mind);

        var coordinates = FindFarthestSpawnPoint(station);
        if (coordinates == EntityCoordinates.Invalid)
            return false;

        var mob = _stationSpawning.SpawnPlayerMob(coordinates, FallbackJob, profile, station);

        _mind.TransferTo(mindId, mob, mind: mind);

        EnsureComp<KillTrackerComponent>(mob);
        var playerComp = EnsureComp<WH40KGunGamePlayerComponent>(mob);

        var level = rule.PlayerLevel.GetValueOrDefault(player.UserId);

        EquipRandomClothing(mob, rule);
        GiveWeaponToPlayer(mob, level, rule);
        GiveBackupKnife(mob, rule);
        ApplyPlayerProtection(mob, playerComp);

        rule.PlayerLevel.TryAdd(player.UserId, 0);
        rule.PlayerKills.TryAdd(player.UserId, 0);
        rule.ConsecutiveDeaths.TryAdd(player.UserId, 0);
        RememberPlayerProfile(player.UserId, profile, rule);
        PushStandings(rule);

        if (TryComp<RespawnTrackerComponent>(ruleEntity, out var tracker))
        {
            _respawn.AddToTracker(player.UserId, (ruleEntity, tracker));
        }

        return true;
    }

    private HumanoidCharacterProfile SanitizeProfileForGunGame(ICommonSession player, HumanoidCharacterProfile profile)
    {
        if (AllowedGunGameSpecies.Contains(profile.Species))
            return profile;

        var fallback = HumanoidCharacterProfile.DefaultWithSpecies();
        _sawmill.Warning(
            "Resetting Gun Game profile for {Player} ({UserId}) from unsupported species '{Species}' to default '{FallbackSpecies}'.",
            player.Name,
            player.UserId,
            profile.Species,
            fallback.Species);
        return fallback;
    }

    private EntityCoordinates FindFarthestSpawnPoint(EntityUid station)
    {
        var stationMaps = new HashSet<MapId>();
        var stationGrids = new HashSet<EntityUid>();

        if (TryComp<StationDataComponent>(station, out var stationData))
        {
            foreach (var grid in stationData.Grids)
            {
                if (!TryComp(grid, out TransformComponent? gridXform) ||
                    gridXform.MapID == MapId.Nullspace)
                {
                    continue;
                }

                stationGrids.Add(grid);
                stationMaps.Add(gridXform.MapID);
            }
        }

        if (stationMaps.Count == 0)
        {
            var stationMap = Transform(station).MapID;
            if (stationMap != MapId.Nullspace)
                stationMaps.Add(stationMap);
        }

        var spawnPoints = new List<(EntityUid Uid, EntityCoordinates Coords, int Priority)>();
        var spawnQuery = EntityQueryEnumerator<WH40KGunGameSpawnPointComponent, TransformComponent>();
        while (spawnQuery.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (!IsOnGunGameStation(xform, stationMaps, stationGrids))
                continue;

            spawnPoints.Add((uid, xform.Coordinates, spawnPoint.Priority));
        }

        if (spawnPoints.Count == 0)
        {
            var lateJoinQuery = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (lateJoinQuery.MoveNext(out var uid, out var sp, out var xform))
            {
                if (!IsOnGunGameStation(xform, stationMaps, stationGrids))
                    continue;

                if (sp.SpawnType == SpawnPointType.LateJoin || sp.SpawnType == SpawnPointType.Job)
                    spawnPoints.Add((uid, xform.Coordinates, 0));
            }
        }

        if (spawnPoints.Count == 0)
        {
            foreach (var grid in stationGrids)
            {
                if (TryComp(grid, out TransformComponent? gridXform))
                    return gridXform.Coordinates;
            }

            _sawmill.Error($"Gun Game could not find any valid spawn points or station grids for station {ToPrettyString(station)}.");
            return EntityCoordinates.Invalid;
        }

        var enemies = new List<EntityUid>();
        var enemyQuery = EntityQueryEnumerator<WH40KGunGamePlayerComponent, MobStateComponent, TransformComponent>();
        while (enemyQuery.MoveNext(out var enemyUid, out _, out var mobState, out var xform))
        {
            if (mobState.CurrentState != MobState.Alive)
                continue;

            if (!IsOnGunGameStation(xform, stationMaps, stationGrids))
                continue;

            enemies.Add(enemyUid);
        }

        if (enemies.Count == 0)
            return _random.Pick(spawnPoints).Coords;

        var bestPoint = spawnPoints[0];
        var bestMinDistance = -1f;
        var bestPriority = int.MinValue;

        foreach (var point in spawnPoints)
        {
            var pointPos = _transform.GetMapCoordinates(point.Uid, Transform(point.Uid));
            var minDistance = float.MaxValue;

            foreach (var enemy in enemies)
            {
                var enemyPos = _transform.GetMapCoordinates(enemy, Transform(enemy));
                var distance = (pointPos.Position - enemyPos.Position).LengthSquared();
                if (distance < minDistance)
                    minDistance = distance;
            }

            if (minDistance > bestMinDistance ||
                minDistance.Equals(bestMinDistance) && point.Priority > bestPriority)
            {
                bestMinDistance = minDistance;
                bestPriority = point.Priority;
                bestPoint = point;
            }
        }

        return bestPoint.Coords;
    }

    private static bool IsOnGunGameStation(TransformComponent xform, HashSet<MapId> stationMaps, HashSet<EntityUid> stationGrids)
    {
        if (xform.MapID == MapId.Nullspace || !stationMaps.Contains(xform.MapID))
            return false;

        return xform.GridUid == null || stationGrids.Count == 0 || stationGrids.Contains(xform.GridUid.Value);
    }

    private void GiveWeaponToPlayer(EntityUid mob, int level, WH40KGunGameRuleComponent rule)
    {
        if (level < 0 || level >= rule.WeaponSequence.Count)
            return;

        var playerComp = EnsureComp<WH40KGunGamePlayerComponent>(mob);

        if (playerComp.CurrentWeapon is { } oldWeapon && !TerminatingOrDeleted(oldWeapon))
            Del(oldWeapon);

        var weaponProto = rule.WeaponSequence[level];
        var weapon = Spawn(weaponProto, _transform.GetMapCoordinates(mob));

        EnsureComp<WH40KGunGameLockedComponent>(weapon);
        playerComp.CurrentWeapon = weapon;

        _hands.TryPickupAnyHand(mob, weapon);
    }

    private void GiveBackupKnife(EntityUid mob, WH40KGunGameRuleComponent rule)
    {
        var knife = Spawn(rule.BackupWeapon, _transform.GetMapCoordinates(mob));
        EnsureComp<WH40KGunGameLockedComponent>(knife);
        _hands.TryPickupAnyHand(mob, knife);
    }
}
