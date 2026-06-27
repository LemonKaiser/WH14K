using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Combat;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.KillTracking;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server._WH40K.MetaProgress;
using Content.Shared.GameTicking;
using Content.Shared.Station.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Strip.Components;
using Content.Shared._WH40K.Cinematic;
using Content.Shared._WH40K.GunGame;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem : GameRuleSystem<WH40KGunGameRuleComponent>
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private RespawnRuleSystem _respawn = default!;
    [Dependency] private RoundEndSystem _roundEnd = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private CombatAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WH40KMetaProgressSystem _metaProgress = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;
    [Dependency] private WH40KDamageProtectionSystem _damageProtection = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("wh40k.gungame");

        InitializeProtection();
        InitializePickups();
        InitializeKillFeed();
        InitializeStandings();

        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnRulePlayerSpawning);
        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged, before: new[] { typeof(KillTrackingSystem) });
    }

    protected override void Started(EntityUid uid, WH40KGunGameRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.WeaponSequence.Clear();
        component.PlayerLevel.Clear();
        component.PlayerKills.Clear();
        component.PlayerProfiles.Clear();
        component.ConsecutiveDeaths.Clear();
        component.Victor = null;
        component.NextTimerSyncAt = TimeSpan.Zero;
        component.LastTimerDurationSeconds = -1;
        component.LastTimerElapsedSeconds = -1;
        component.LastTimerStopped = false;
        component.PlacementRewardsGranted = false;
        ClearKillFeedState();
        ApplyMapStabilitySafeguards();

        var pool = component.WeaponPool.ToList();
        _random.Shuffle(pool);

        var take = Math.Clamp(component.WeaponCount - 1, 0, pool.Count);
        component.WeaponSequence = pool.Take(take).Select(p => new EntProtoId(p.Id)).ToList();
        component.WeaponSequence.Add(component.FinalWeapon);

        PushRoundTimer(component, force: true);
        _sawmill.Info($"Gun Game started with {component.WeaponSequence.Count} weapons in sequence");
    }

    protected override void Ended(EntityUid uid, WH40KGunGameRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        ClearKillFeedState();
        component.PlayerProfiles.Clear();
        ClearStandingsHud();
        ClearRoundTimerHud();

        var query = EntityQueryEnumerator<WH40KGunGamePlayerComponent>();
        while (query.MoveNext(out var playerUid, out var playerComp))
        {
            RemovePlayerProtection(playerUid, playerComp);
            RemCompDeferred<WH40KGunGamePlayerComponent>(playerUid);
        }
    }

    protected override void AppendRoundEndText(EntityUid uid, WH40KGunGameRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        var totalLevels = component.WeaponSequence.Count;

        if (component.Victor != null && _player.TryGetPlayerData(component.Victor.Value, out var data))
        {
            args.AddLine(Loc.GetString("wh40k-gun-game-winner", ("player", data.UserName)));
            args.AddLine("");
        }

        args.AddLine(Loc.GetString("wh40k-gun-game-scoreboard-level-header"));
        var byLevel = component.PlayerLevel
            .OrderByDescending(p => p.Value)
            .ThenByDescending(p => component.PlayerKills.GetValueOrDefault(p.Key))
            .ThenBy(p => p.Key.ToString(), System.StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < byLevel.Count; i++)
        {
            var (userId, level) = byLevel[i];
            if (_player.TryGetPlayerData(userId, out var playerData))
                args.AddLine(Loc.GetString("wh40k-gun-game-scoreboard-level-entry",
                    ("place", i + 1),
                    ("player", playerData.UserName),
                    ("level", GetDisplayedLevel(level, totalLevels)),
                    ("total", totalLevels)));
        }
    }

    private void OnRulePlayerSpawning(RulePlayerSpawningEvent ev)
    {
        if (!TryGetActiveRule(out var ruleEntity, out var rule))
            return;

        if (ev.PlayerPool.Count == 0)
            return;

        EntityUid station = EntityUid.Invalid;
        var stationQuery = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent>();
        while (stationQuery.MoveNext(out var uid, out _, out _))
        {
            station = uid;
            break;
        }

        if (station == EntityUid.Invalid)
            return;

        foreach (var player in ev.PlayerPool.ToList())
        {
            var profile = ev.Profiles.TryGetValue(player.UserId, out var p)
                ? p
                : HumanoidCharacterProfile.DefaultWithSpecies();

            SpawnGunGamePlayer(player, profile, station, ruleEntity, rule);
            GameTicker.PlayerJoinGame(player);
        }

        ev.PlayerPool.Clear();
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (TryGetActiveRule(out var ruleEntity, out var rule))
        {
            ev.Handled = SpawnGunGamePlayer(ev.Player, ev.Profile, ev.Station, ruleEntity, rule);
        }
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!TryGetActiveRule(out _, out _))
            return;

        EnsureComp<KillTrackerComponent>(ev.Mob);
        EnsureComp<WH40KGunGamePlayerComponent>(ev.Mob);

        if (!TryGetActiveRule(out _, out var rule))
            return;

        RememberPlayerProfile(ev.Player.UserId, ev.Profile, rule);
        PushStandings(rule);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical && args.NewMobState != MobState.Dead)
            return;

        if (!TryComp<WH40KGunGamePlayerComponent>(args.Target, out var playerComp))
            return;

        if (!TryGetActiveRule(out var ruleEntity, out var rule))
            return;

        RemovePlayerProtection(args.Target, playerComp);
        RemComp<WH40KGunGamePlayerComponent>(args.Target);

        NetUserId victimUserId;
        ActorComponent? actor = null;

        if (TryComp<ActorComponent>(args.Target, out var connectedActor))
        {
            actor = connectedActor;
            victimUserId = actor.PlayerSession.UserId;
        }
        else if (_mind.TryGetMind(args.Target, out _, out var mind) && mind.UserId is { } mindUserId)
        {
            victimUserId = mindUserId;
        }
        else
        {
            QueueDel(args.Target);
            return;
        }

        var killerUserId = ResolveLastHitKiller(args.Origin, args.Target);

        if (killerUserId is { } killer &&
            killer != victimUserId &&
            rule.PlayerLevel.ContainsKey(killer) &&
            rule.PlayerLevel.ContainsKey(victimUserId))
        {
            SendKillFeedEntry(killer, victimUserId);
            rule.ConsecutiveDeaths[killer] = 0;
            AdvanceLevel(killer, rule);
        }
        else
        {
            SendFallbackKillFeedEntry(victimUserId, killerUserId == victimUserId);
        }

        rule.ConsecutiveDeaths[victimUserId] = rule.ConsecutiveDeaths.GetValueOrDefault(victimUserId) + 1;
        if (rule.ConsecutiveDeaths[victimUserId] >= rule.ConsecutiveDeathThreshold)
        {
            rule.ConsecutiveDeaths[victimUserId] = 0;
            var current = rule.PlayerLevel.GetValueOrDefault(victimUserId);
            rule.PlayerLevel[victimUserId] = Math.Max(0, current - 1);
        }

        if (rule.RespawnDelay > TimeSpan.Zero && TryComp<RespawnTrackerComponent>(ruleEntity, out var tracker))
        {
            _respawn.AddToTracker(victimUserId, (ruleEntity, tracker));

            if (actor != null)
                _respawn.RespawnPlayer((args.Target, actor), (ruleEntity, tracker));
        }

        PushStandings(rule);
        QueueDel(args.Target);
    }

    private NetUserId? ResolveLastHitKiller(EntityUid? origin, EntityUid victim)
    {
        if (origin == null)
            return null;

        if (!_attackerResolver.TryResolveResponsibleEntity(origin.Value, out var responsible))
            return null;

        if (responsible == victim)
            return null;

        if (!TryComp<ActorComponent>(responsible, out var actor))
            return null;

        return actor.PlayerSession.UserId;
    }

    private void AdvanceLevel(NetUserId killerId, WH40KGunGameRuleComponent rule)
    {
        if (rule.Victor != null)
            return;

        rule.PlayerKills[killerId] = rule.PlayerKills.GetValueOrDefault(killerId) + 1;

        var currentLevel = rule.PlayerLevel.GetValueOrDefault(killerId);
        var newLevel = currentLevel + 1;
        var totalLevels = rule.WeaponSequence.Count;

        if (newLevel >= totalLevels)
        {
            rule.Victor = killerId;
            rule.PlayerLevel[killerId] = newLevel;
            GrantPlacementMetaProgressRewards(rule);
            _roundEnd.EndRound(rule.RestartDelay);
            return;
        }

        rule.PlayerLevel[killerId] = newLevel;

        if (!_player.TryGetSessionById(killerId, out var session))
            return;

        var mindUid = session.GetMind();
        if (mindUid == null || !TryComp<MindComponent>(mindUid, out var mindComp) || mindComp.OwnedEntity is not { } mob)
            return;

        EnsureComp<WH40KGunGamePlayerComponent>(mob);
        GiveWeaponToPlayer(mob, newLevel, rule);

        var weaponProto = Proto.Index(rule.WeaponSequence[newLevel]);
        _popup.PopupEntity(Loc.GetString("wh40k-gun-game-level-up",
            ("level", newLevel + 1),
            ("weapon", weaponProto.Name)), mob, mob);
    }

    private bool TryGetActiveRule(out EntityUid ruleEntity, out WH40KGunGameRuleComponent rule)
    {
        var query = EntityQueryEnumerator<WH40KGunGameRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var comp, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            ruleEntity = uid;
            rule = comp;
            return true;
        }

        ruleEntity = EntityUid.Invalid;
        rule = default!;
        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (TryGetActiveRule(out _, out var rule))
        {
            PushRoundTimer(rule);

            if (rule.Victor == null &&
                rule.RoundDuration > TimeSpan.Zero &&
                !_roundEnd.IsRoundEndRequested() &&
                GameTicker.RoundDuration() >= rule.RoundDuration)
            {
                rule.Victor = ResolveLeader(rule);
                GrantPlacementMetaProgressRewards(rule);
                _roundEnd.EndRound(rule.RestartDelay);
                return;
            }

            UpdatePickups();
        }
    }

    private NetUserId? ResolveLeader(WH40KGunGameRuleComponent rule)
    {
        return rule.PlayerLevel
            .OrderByDescending(entry => entry.Value)
            .ThenByDescending(entry => rule.PlayerKills.GetValueOrDefault(entry.Key))
            .ThenBy(entry => entry.Key.ToString(), System.StringComparer.Ordinal)
            .Select(entry => (NetUserId?) entry.Key)
            .FirstOrDefault();
    }

    private static int GetDisplayedLevel(int storedLevel, int totalLevels)
    {
        if (totalLevels <= 0)
            return 0;

        return Math.Clamp(storedLevel + 1, 1, totalLevels);
    }
}
