using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.GameTicking.Rules;
using Content.Server.Mind;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Revolutionary.Components;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Research;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.WaveDefence;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Shared._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Notifications;
using Content.Shared._WH40K.Squads;
using Content.Shared._WH40K.StrategicPoints;
using Content.Shared._WH40K.WaveDefence;
using Content.Shared.EntityTable;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Gravity;
using Content.Shared.Mind.Components;
using Content.Shared._WH40K.GameMode;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Cargo.Components;
using Content.Shared.Station.Components;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Localization;
using Robust.Shared.Random;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.GameTicking.Rules;

public sealed partial class WH40KWaveDefenceRuleSystem : GameRuleSystem<WH40KWaveDefenceRuleComponent>
{
    private const string DefaultHumanoidAssaultRoot = "WH40KWaveDefenceSimpleHumanoidHostileCompound";
    private const string DefaultRangedAssaultRoot = "WH40KWaveDefenceSimpleRangedHostileCompound";
    private const string DefaultMeleeAssaultRoot = "WH40KWaveDefenceSimpleMeleeHostileCompound";

    [Dependency] private  AdminSystem _admin = default!;
    [Dependency] private  CargoSystem _cargo = default!;
    [Dependency] private  EntityTableSystem _entityTable = default!;
    [Dependency] private  IChatManager _chat = default!;
    [Dependency] private  MindSystem _mind = default!;
    [Dependency] private  MobStateSystem _mobState = default!;
    [Dependency] private  NpcFactionSystem _npcFaction = default!;
    [Dependency] private  IPlayerManager _players = default!;
    [Dependency] private  PlayTimeTrackingSystem _playTimeTracking = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  RoundEndSystem _roundEnd = default!;
    [Dependency] private  SharedRoleSystem _roles = default!;
    [Dependency] private  StationSpawningSystem _stationSpawning = default!;
    [Dependency] private  StationJobsSystem _stationJobs = default!;
    [Dependency] private  StationSystem _stations = default!;
    [Dependency] private  ShuttleSystem _shuttles = default!;
    [Dependency] private  WH40KTeamNpcFactionSystem _teamNpcFactions = default!;
    [Dependency] private  WH40KWaveDefenceAISystem _waveAi = default!;
    [Dependency] private  WH40KWaveDefenceMapRegistrySystem _registry = default!;
    [Dependency] private  IMapManager _mapManager = default!;
    [Dependency] private  SharedMapSystem _map = default!;

    private ISawmill _sawmill = default!;
    private EntityUid? _activeRuleUid;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.wave");

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnPlayerBeforeSpawn);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WH40KWaveDefenceObjectiveDestroyedEvent>(OnObjectiveDestroyed);
        SubscribeLocalEvent<WH40KValidatedKillRewardEvent>(OnValidatedKillReward);
        SubscribeLocalEvent<WH40KValidatedKillRewardRevokedEvent>(OnValidatedKillRewardRevoked);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    protected override void Started(
        EntityUid uid,
        WH40KWaveDefenceRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        _activeRuleUid = uid;

        if (!Proto.TryIndex(component.Config, out var config))
        {
            _sawmill.Error($"Missing WaveDefence config '{component.Config}'.");
            component.AuthoringValid = false;
            return;
        }

        component.Mode = config.Mode;
        component.DefendingTeamId = config.DefendingTeamId;
        component.AttackingTeamId = ResolveAttackingTeamId(config.AttackingTeamId, component.DefendingTeamId);
        component.PreparationDurationSeconds = Math.Max(5f, config.PreparationDurationSeconds);
        component.IntermissionDurationSeconds = Math.Max(5f, config.IntermissionDurationSeconds);
        component.MaxWaveDurationSeconds = Math.Max(30f, config.MaxWaveDurationSeconds);
        component.FinalWaveNumber = Math.Max(1, config.FinalWaveNumber);
        component.CountCritAsAlive = config.CountCritAsAlive;
        component.LateJoinQueuesDuringWave = config.LateJoinDuringWaveQueuesUntilPreparation;
        component.WaveProfiles = new List<ProtoId<WH40KWaveProfilePrototype>>(config.WaveProfiles);
        component.ActiveAttackers.Clear();
        component.PendingBatches.Clear();
        component.QueuedLateJoinJobs.Clear();
        component.QueuedRespawns.Clear();
        component.LastKnownJobIds.Clear();
        component.PlayerLastKnownTeam.Clear();
        component.TeamFrontPoints.Clear();
        component.TeamCommandPoints.Clear();
        component.TeamResearchPoints.Clear();
        component.TeamBaseLevels.Clear();
        component.CurrentWaveNumber = 0;
        component.Phase = WH40KWaveDefencePhase.Preparation;
        component.NextPhaseChange = Timing.CurTime + TimeSpan.FromSeconds(component.PreparationDurationSeconds);
        component.ActiveWaveEndsAt = TimeSpan.Zero;
        component.RoundStartTime = Timing.CurTime;
        component.NextLayoutRetryAt = Timing.CurTime;
        component.LayoutRetryCount = 0;
        component.LayoutReady = false;
        component.PreparationAnnounced = false;
        component.EndReason = null;
        component.PrimaryObjective = null;
        component.AuthoringValid = false;

        component.TeamStartingPoints = Math.Max(0, config.TeamStartingPoints);
        component.FrontPointsPerKill = Math.Max(1, config.FrontPointsPerKill);
        component.BaseLevelThresholds = config.BaseLevelThresholds.Count > 0
            ? config.BaseLevelThresholds
                .Where(threshold => threshold > 0)
                .Distinct()
                .OrderBy(threshold => threshold)
                .ToList()
            : new List<int> { 120, 300, 600, 1000, 1500, 2200, 3100, 4200 };
        component.EconomyPreparationMultiplier = Math.Max(1, config.EconomyPreparationMultiplier);
        component.EconomyAssaultMultiplier = Math.Max(1, config.EconomyAssaultMultiplier);
        component.EconomyApocalypseMultiplier = Math.Max(1, config.EconomyApocalypseMultiplier);
        component.ReinforcementCurveDurationMinSeconds = Math.Max(1f, config.ReinforcementCurveDurationMinSeconds);
        component.ReinforcementCurveDurationMaxSeconds = Math.Max(
            component.ReinforcementCurveDurationMinSeconds,
            config.ReinforcementCurveDurationMaxSeconds);
        component.ReinforcementCurveFallbackApocalypseSeconds = Math.Max(1f, config.ReinforcementCurveFallbackApocalypseSeconds);
        component.ReinforcementCurveBaseMultiplier = Math.Max(0.01f, config.ReinforcementCurveBaseMultiplier);
        component.ReinforcementCurveScale = Math.Max(0f, config.ReinforcementCurveScale);
        component.ReinforcementCurveExponent = Math.Max(0.01f, config.ReinforcementCurveExponent);
        EnsureTeamProgress(component);

        component.Station = _stations.GetStations().FirstOrDefault();
        if (component.Station == EntityUid.Invalid)
        {
            component.Station = null;
            component.AuthoringValid = false;
            _sawmill.Error("WaveDefence started without a valid station.");
            return;
        }

        if (!TryGetRuleMapId(component, out var mapId))
        {
            component.AuthoringValid = false;
            _sawmill.Error("WaveDefence could not resolve a valid map id from the active station.");
            return;
        }

        ApplyMapStabilitySafeguards(component, mapId);
        TryInitializeLayout(component, announceOnSuccess: true);
    }

    protected override void Ended(
        EntityUid uid,
        WH40KWaveDefenceRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        if (_activeRuleUid == uid)
            _activeRuleUid = null;
    }

    protected override void AppendRoundEndText(
        EntityUid uid,
        WH40KWaveDefenceRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        args.AddLine(Loc.GetString("wh40k-wave-defence-round-end-header"));
        args.AddLine(Loc.GetString(
            "wh40k-wave-defence-round-end-wave",
            ("wave", component.CurrentWaveNumber),
            ("final", component.FinalWaveNumber)));

        if (!string.IsNullOrWhiteSpace(component.EndReason))
            args.AddLine(component.EndReason);
    }

    protected override void ActiveTick(
        EntityUid uid,
        WH40KWaveDefenceRuleComponent component,
        GameRuleComponent gameRule,
        float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (!TryInitializeLayout(component, announceOnSuccess: true))
            return;

        PruneActiveAttackers(component);

        if (component.Phase is WH40KWaveDefencePhase.Victory or WH40KWaveDefencePhase.Defeat)
            return;

        if (ShouldDefeatForNoDefenders(component))
        {
            SetDefeat(component, Loc.GetString("wh40k-wave-defence-defeat-defenders"));
            return;
        }

        switch (component.Phase)
        {
            case WH40KWaveDefencePhase.Preparation:
                ProcessQueuedPreparationSpawns(component);
                if (!component.ManualWaveAdvanceOnly &&
                    Timing.CurTime >= component.NextPhaseChange)
                {
                    StartNextWave(component);
                }
                break;

            case WH40KWaveDefencePhase.WaveActive:
                ProcessPendingWaveBatches(component);
                if (component.ActiveWaveEndsAt > TimeSpan.Zero &&
                    Timing.CurTime >= component.ActiveWaveEndsAt)
                {
                    ExpireCurrentWave(component);
                    break;
                }

                if (component.PendingBatches.All(batch => batch.Spawned) && component.ActiveAttackers.Count == 0)
                    CompleteCurrentWave(component);
                break;

            case WH40KWaveDefencePhase.Intermission:
                ProcessQueuedPreparationSpawns(component);
                if (!component.ManualWaveAdvanceOnly &&
                    Timing.CurTime >= component.NextPhaseChange)
                {
                    StartNextWave(component);
                }
                break;
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _activeRuleUid = null;
    }

    private bool TryInitializeLayout(WH40KWaveDefenceRuleComponent component, bool announceOnSuccess)
    {
        if (component.LayoutReady && component.AuthoringValid && component.PrimaryObjective is { } objective && Exists(objective))
            return true;

        if (Timing.CurTime < component.NextLayoutRetryAt)
            return false;

        component.NextLayoutRetryAt = Timing.CurTime + TimeSpan.FromSeconds(0.5f);
        component.LayoutRetryCount++;

        if (!TryGetRuleMapId(component, out var mapId))
        {
            component.LastLayoutStatus = "WaveDefence layout retry could not resolve the active map id yet.";
            return false;
        }

        var valid = _registry.ValidateLayout(
            mapId,
            component.DefendingTeamId,
            out var errors);

        var hasObjective = _registry.TryGetPrimaryObjective(mapId, component.DefendingTeamId, out var resolvedObjective);

        if (!valid || !hasObjective)
        {
            component.AuthoringValid = false;
            component.LayoutReady = false;
            component.PrimaryObjective = hasObjective ? resolvedObjective : null;
            component.LastLayoutStatus =
                $"WaveDefence layout not ready on map {mapId}; attempt={component.LayoutRetryCount}, errors={errors.Count}, objectiveResolved={hasObjective}.";
            return false;
        }

        component.AuthoringValid = true;
        component.LayoutReady = true;
        component.PrimaryObjective = resolvedObjective;
        component.LastLayoutStatus =
            $"WaveDefence layout ready on map {mapId} after {component.LayoutRetryCount} attempt(s).";

        if (announceOnSuccess && !component.PreparationAnnounced)
        {
            component.PreparationAnnounced = true;
            component.NextPhaseChange = component.ManualWaveAdvanceOnly
                ? TimeSpan.Zero
                : Timing.CurTime + TimeSpan.FromSeconds(component.PreparationDurationSeconds);
            BroadcastWaveMessage(Loc.GetString("wh40k-wave-defence-preparation-announce"));
            BroadcastWaveNotification(
                "wh40k-wave-defence-preparation-announce",
                WH40KNotificationColors.Imperium,
                WH40KNotificationCategory.Event);
        }

        return true;
    }

    private void OnPlayerBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        TryInitializeLayout(rule, announceOnSuccess: true);

        if (rule.Station == null)
            return;

        var station = rule.Station.Value;
        var profile = ev.Profile;
        var jobId = ResolveSpawnJob(ev.Player, station, ev.JobId, profile);
        if (jobId == null)
            return;

        // Late-join during an active wave becomes an observer and is released on the next safe phase.
        if (ev.LateJoin &&
            rule.Phase == WH40KWaveDefencePhase.WaveActive &&
            rule.LateJoinQueuesDuringWave)
        {
            rule.QueuedLateJoinJobs[ev.Player.UserId] = jobId;
            GameTicker.SpawnObserver(ev.Player);
            ev.Handled = true;
            return;
        }

        var spawnType = ev.LateJoin
            ? WH40KWaveSpawnPointType.DefenderReinforcement
            : WH40KWaveSpawnPointType.DefenderStart;

        if (!TrySpawnDefenderForTicker(ev.Player, station, profile, jobId, ev.LateJoin, spawnType))
        {
            return;
        }

        ev.Handled = true;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        var member = EnsureComp<WH40KTeamMemberComponent>(ev.Mob);
        member.TeamId = rule.DefendingTeamId;

        var icon = EnsureComp<WH40KTeamBattleFactionIconComponent>(ev.Mob);
        if (!string.Equals(icon.TeamId, rule.DefendingTeamId, StringComparison.OrdinalIgnoreCase))
        {
            icon.TeamId = rule.DefendingTeamId;
            Dirty(ev.Mob, icon);
        }

        _teamNpcFactions.ApplyTeamFaction(ev.Mob, rule.DefendingTeamId);
        rule.LastKnownJobIds[ev.Player.UserId] = ev.JobId;
        rule.PlayerLastKnownTeam[ev.Player.UserId] = rule.DefendingTeamId;
        rule.QueuedLateJoinJobs.Remove(ev.Player.UserId);
        rule.QueuedRespawns.Remove(ev.Player.UserId);

        SendSpawnBriefing(ev.Player, rule);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || !TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!TryComp<ActorComponent>(args.Target, out var actor) ||
            !TryGetEntityTeamId(args.Target, out var teamId) ||
            !string.Equals(teamId, rule.DefendingTeamId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        rule.QueuedRespawns.Add(actor.PlayerSession.UserId);
    }

    private void OnObjectiveDestroyed(WH40KWaveDefenceObjectiveDestroyedEvent args)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!string.Equals(args.TeamId, rule.DefendingTeamId, StringComparison.OrdinalIgnoreCase))
            return;

        SetDefeat(rule, Loc.GetString("wh40k-wave-defence-defeat-objective"));
    }

    private void OnValidatedKillReward(WH40KValidatedKillRewardEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!TryResolveTeamId(rule, ev.KillerTeamId, out var killerTeamId) ||
            string.Equals(killerTeamId, ev.VictimTeamId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var reward = Math.Max(1, rule.FrontPointsPerKill) * GetKillRewardMultiplier(ev.Victim);
        TryAdjustTeamXp(killerTeamId, reward, out _, out _, out _, "kill");
        TryAdjustTeamInfluence(killerTeamId, reward, out _, out _, "kill");
    }

    private void OnValidatedKillRewardRevoked(WH40KValidatedKillRewardRevokedEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!TryResolveTeamId(rule, ev.KillerTeamId, out var killerTeamId) ||
            string.Equals(killerTeamId, ev.VictimTeamId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var reward = Math.Max(1, rule.FrontPointsPerKill) * GetKillRewardMultiplier(ev.Victim);
        TryAdjustTeamXp(killerTeamId, -reward, out _, out _, out _, "kill-revoked", allowDecrease: true);
        TryAdjustTeamInfluence(killerTeamId, -reward, out _, out _, "kill-revoked");
    }

    private bool TrySpawnDefenderForTicker(
        ICommonSession player,
        EntityUid station,
        HumanoidCharacterProfile profile,
        string jobId,
        bool lateJoin,
        WH40KWaveSpawnPointType spawnType)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        if (!TryGetRuleMapId(rule, out var mapId))
            return false;

        if (!_registry.TryPickSpawnCoordinate(
                mapId,
                spawnType,
                _random,
                out var coordinates,
                teamId: rule.DefendingTeamId))
        {
            return false;
        }

        var mob = SpawnPlayerMobFreshMind(player, profile, jobId, coordinates, station);
        if (mob == null)
            return false;

        GameTicker.PlayersJoinedRoundNormally++;
        var complete = new PlayerSpawnCompleteEvent(
            mob.Value,
            player,
            jobId,
            lateJoin,
            true,
            GameTicker.PlayersJoinedRoundNormally,
            station,
            profile);
        RaiseLocalEvent(mob.Value, complete, true);
        return true;
    }

    private EntityUid? SpawnPlayerMobFreshMind(
        ICommonSession player,
        HumanoidCharacterProfile profile,
        string jobId,
        EntityCoordinates coordinates,
        EntityUid station)
    {
        if (_mind.TryGetMind(player.UserId, out _, out _))
            _mind.WipeMind(player);

        var mob = _stationSpawning.SpawnPlayerMob(coordinates, jobId, profile, station);

        var newMind = _mind.CreateMind(player.UserId, profile.Name);
        _mind.SetUserId(newMind, player.UserId);
        _playTimeTracking.PlayerRolesChanged(player);
        _mind.TransferTo(newMind, mob);
        _roles.MindAddJobRole(newMind, silent: true, jobPrototype: jobId);
        _admin.UpdatePlayerList(player);
        _stationJobs.TryAssignJob(station, jobId, player.UserId);
        return mob;
    }

    private void ProcessQueuedPreparationSpawns(WH40KWaveDefenceRuleComponent rule)
    {
        if (rule.Station == null)
            return;

        foreach (var (userId, queuedJob) in rule.QueuedLateJoinJobs.ToArray())
        {
            if (!_players.TryGetSessionById(userId, out var session))
            {
                rule.QueuedLateJoinJobs.Remove(userId);
                continue;
            }

            if (!TryDirectSpawnQueuedDefender(session, rule.Station.Value, queuedJob))
                continue;

            rule.QueuedLateJoinJobs.Remove(userId);
        }

        foreach (var userId in rule.QueuedRespawns.ToArray())
        {
            if (!_players.TryGetSessionById(userId, out var session))
            {
                rule.QueuedRespawns.Remove(userId);
                continue;
            }

            if (session.AttachedEntity is { } attached &&
                TryGetEntityTeamId(attached, out var teamId) &&
                string.Equals(teamId, rule.DefendingTeamId, StringComparison.OrdinalIgnoreCase) &&
                (_mobState.IsAlive(attached) || (rule.CountCritAsAlive && _mobState.IsCritical(attached))))
            {
                rule.QueuedRespawns.Remove(userId);
                continue;
            }

            if (!rule.LastKnownJobIds.TryGetValue(userId, out var lastJob))
                lastJob = null;

            if (!TryDirectSpawnQueuedDefender(session, rule.Station.Value, lastJob))
                continue;

            rule.QueuedRespawns.Remove(userId);
        }
    }

    private bool TryDirectSpawnQueuedDefender(ICommonSession session, EntityUid station, string? requestedJob)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        var profile = GameTicker.GetPlayerProfile(session);
        var jobId = ResolveSpawnJob(session, station, requestedJob, profile);
        if (jobId == null)
            return false;

        if (!TryGetRuleMapId(rule, out var mapId))
            return false;

        if (!_registry.TryPickSpawnCoordinate(
                mapId,
                WH40KWaveSpawnPointType.DefenderReinforcement,
                _random,
                out var coordinates,
                teamId: rule.DefendingTeamId))
        {
            return false;
        }

        var mob = SpawnPlayerMobFreshMind(session, profile, jobId, coordinates, station);
        if (mob == null)
            return false;

        var complete = new PlayerSpawnCompleteEvent(
            mob.Value,
            session,
            jobId,
            true,
            true,
            GameTicker.PlayersJoinedRoundNormally,
            station,
            profile);
        RaiseLocalEvent(mob.Value, complete, true);
        return true;
    }

    private string? ResolveSpawnJob(
        ICommonSession player,
        EntityUid station,
        string? requestedJob,
        HumanoidCharacterProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(requestedJob))
            return requestedJob;

        var restricted = new HashSet<ProtoId<JobPrototype>>();
        return _stationJobs.PickBestAvailableJobWithPriority(station, profile.JobPriorities, true, restricted);
    }

    private void StartNextWave(WH40KWaveDefenceRuleComponent rule)
    {
        if (!rule.LayoutReady || !rule.AuthoringValid || rule.PrimaryObjective == null)
        {
            return;
        }

        var nextWaveNumber = rule.CurrentWaveNumber + 1;
        var profile = ResolveWaveProfile(rule, nextWaveNumber);
        if (profile == null)
        {
            if (rule.Mode == WH40KWaveDefenceMode.Fixed && nextWaveNumber > rule.FinalWaveNumber)
            {
                SetVictory(rule, Loc.GetString("wh40k-wave-defence-victory"));
            }
            else
            {
                SetDefeat(rule, Loc.GetString("wh40k-wave-defence-defeat-missing-profile"));
            }
            return;
        }

        rule.CurrentWaveNumber = nextWaveNumber;
        rule.Phase = WH40KWaveDefencePhase.WaveActive;
        rule.ActiveWaveEndsAt = Timing.CurTime + TimeSpan.FromSeconds(rule.MaxWaveDurationSeconds);
        rule.PendingBatches.Clear();
        foreach (var batch in profile.Batches)
        {
            rule.PendingBatches.Add(new WH40KWavePendingBatch
            {
                DueAt = Timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, batch.DelaySeconds)),
                Batch = batch,
                Spawned = false,
            });
        }

        BroadcastWaveMessage(Loc.GetString(
            "wh40k-wave-defence-wave-start",
            ("wave", rule.CurrentWaveNumber)));
        BroadcastWaveNotification(
            "wh40k-wave-defence-wave-start",
            WH40KNotificationColors.Event,
            WH40KNotificationCategory.Event,
            new Dictionary<string, string>
            {
                ["wave"] = rule.CurrentWaveNumber.ToString()
            });

        if (!string.IsNullOrWhiteSpace(profile.Announcement))
        {
            BroadcastWaveNotification(
                profile.Announcement,
                WH40KNotificationColors.Event,
                WH40KNotificationCategory.Event);
        }
    }

    private WH40KWaveProfilePrototype? ResolveWaveProfile(WH40KWaveDefenceRuleComponent rule, int waveNumber)
    {
        if (rule.WaveProfiles.Count == 0)
            return null;

        WH40KWaveProfilePrototype? fallback = null;
        foreach (var id in rule.WaveProfiles)
        {
            if (!Proto.TryIndex(id, out var profile))
                continue;

            if (profile.WaveNumber == waveNumber)
                return profile;

            if (fallback == null || profile.WaveNumber > fallback.WaveNumber)
                fallback = profile;
        }

        if (rule.Mode == WH40KWaveDefenceMode.Endless)
            return fallback;

        return null;
    }

    private void ProcessPendingWaveBatches(WH40KWaveDefenceRuleComponent rule)
    {
        if (rule.Station == null || rule.PrimaryObjective == null)
            return;

        foreach (var pending in rule.PendingBatches)
        {
            if (pending.Spawned || Timing.CurTime < pending.DueAt)
                continue;

            SpawnWaveBatch(rule, pending.Batch);
            pending.Spawned = true;
        }
    }

    private void SpawnWaveBatch(WH40KWaveDefenceRuleComponent rule, WH40KWaveBatchEntry batch)
    {
        if (rule.Station == null || rule.PrimaryObjective == null)
        {
            rule.LastBatchSummary = "Wave batch skipped: station or primary objective was missing.";
            return;
        }

        if (!TryGetRuleMapId(rule, out var mapId))
        {
            rule.LastBatchSummary = "Wave batch skipped: active map id could not be resolved.";
            return;
        }

        var candidateSpawns = ResolveAttackerSpawnPoints(mapId, batch.SpawnId);
        if (candidateSpawns.Count == 0)
        {
            rule.LastBatchSummary =
                $"Wave batch '{batch.EntityTable}' failed: attacker spawn '{batch.SpawnId ?? "<any>"}' could not resolve a spawn marker.";
            return;
        }

        var requestedCount = Math.Max(1, batch.Count);
        var spawnedCount = 0;
        var spawnedPrototypes = new List<string>(requestedCount);
        var usedSpawns = new List<string>(requestedCount);

        for (var i = 0; i < requestedCount; i++)
        {
            var protoId = _entityTable
                .GetSpawns(Proto.Index(batch.EntityTable), new Random(_random.Next()))
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(protoId))
                continue;

            var selectedSpawn = PickWeightedSpawnPoint(candidateSpawns);
            var spawnCoordinates = selectedSpawn.Xform.Coordinates;

            var spawned = Spawn(protoId, spawnCoordinates);
            rule.ActiveAttackers.Add(spawned);
            spawnedCount++;
            spawnedPrototypes.Add(protoId);

            var member = EnsureComp<WH40KTeamMemberComponent>(spawned);
            member.TeamId = rule.AttackingTeamId;

            var icon = EnsureComp<WH40KTeamBattleFactionIconComponent>(spawned);
            if (!string.Equals(icon.TeamId, rule.AttackingTeamId, StringComparison.OrdinalIgnoreCase))
            {
                icon.TeamId = rule.AttackingTeamId;
                Dirty(spawned, icon);
            }

            _teamNpcFactions.ApplyTeamFaction(spawned, rule.AttackingTeamId);

            if (!string.IsNullOrWhiteSpace(batch.NpcFactionId))
            {
                _npcFaction.ClearFactions(spawned);
                _npcFaction.AddFaction(spawned, batch.NpcFactionId);
            }

            var attacker = EnsureComp<WH40KWaveDefenceAttackerComponent>(spawned);
            attacker.Objective = rule.PrimaryObjective;
            attacker.Role = batch.SquadRole;
            attacker.AiProfile = batch.AiProfile;
            attacker.VisionRadius = Math.Max(6f, batch.VisionRadius);
            attacker.AggroVisionRadius = Math.Max(attacker.VisionRadius, batch.AggroVisionRadius);
            attacker.PlayerMemorySeconds = Math.Max(1f, batch.PlayerMemorySeconds);
            attacker.RootTaskOverride = ResolveAssaultRootOverride(spawned, batch.RootTaskOverride, batch.AiProfile);
            attacker.DebugState = $"spawn:{DescribeAttackerSpawn(selectedSpawn.Spawn)}";
            usedSpawns.Add(DescribeAttackerSpawn(selectedSpawn.Spawn));
            _waveAi.ConfigureAttacker(spawned, attacker);
        }

        rule.LastBatchSummary =
            $"Wave {rule.CurrentWaveNumber}: spawned {spawnedCount}/{requestedCount} attackers from '{batch.EntityTable}' on map {mapId}; protos=[{string.Join(", ", spawnedPrototypes)}]; spawns=[{string.Join(", ", usedSpawns)}].";

        if (spawnedCount == 0)
        {
            return;
        }
    }

    private List<(EntityUid Uid, WH40KWaveSpawnPointComponent Spawn, TransformComponent Xform)> ResolveAttackerSpawnPoints(
        MapId mapId,
        string? spawnId)
    {
        return _registry.GetSpawnPoints(mapId, WH40KWaveSpawnPointType.Attacker, spawnId: spawnId);
    }

    private (EntityUid Uid, WH40KWaveSpawnPointComponent Spawn, TransformComponent Xform) PickWeightedSpawnPoint(
        IReadOnlyList<(EntityUid Uid, WH40KWaveSpawnPointComponent Spawn, TransformComponent Xform)> spawnPoints)
    {
        if (spawnPoints.Count == 0)
            throw new ArgumentException("At least one spawn point is required.", nameof(spawnPoints));

        var totalWeight = 0;
        foreach (var point in spawnPoints)
        {
            totalWeight += Math.Max(1, point.Spawn.Priority);
        }

        if (totalWeight <= 0)
            return _random.Pick(spawnPoints);

        var roll = _random.Next(totalWeight);
        foreach (var point in spawnPoints)
        {
            roll -= Math.Max(1, point.Spawn.Priority);
            if (roll < 0)
                return point;
        }

        return spawnPoints[^1];
    }

    private static string DescribeAttackerSpawn(WH40KWaveSpawnPointComponent spawn)
    {
        var spawnId = string.IsNullOrWhiteSpace(spawn.SpawnId) ? "<any>" : spawn.SpawnId;
        return spawnId;
    }

    private string? ResolveAssaultRootOverride(EntityUid uid, string? explicitOverride, WH40KWaveAiProfile aiProfile)
    {
        if (!string.IsNullOrWhiteSpace(explicitOverride))
            return explicitOverride;

        var profileRoot = aiProfile switch
        {
            WH40KWaveAiProfile.SimpleSwarm => "WH40KWaveDefenceAIProfileSimpleSwarm",
            WH40KWaveAiProfile.AdvancedHumanoidConcept => "WH40KWaveDefenceAIProfileAdvancedHumanoidConcept",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(profileRoot))
            return profileRoot;

        if (!TryComp<HTNComponent>(uid, out var htn))
            return null;

        return htn.RootTask.Task switch
        {
            "SimpleHumanoidHostileCompound" => DefaultHumanoidAssaultRoot,
            "SimpleRangedHostileCompound" => DefaultRangedAssaultRoot,
            "SimpleHostileCompound" => DefaultMeleeAssaultRoot,
            "MeleeCombatCompound" => DefaultMeleeAssaultRoot,
            "XenoCompound" => DefaultMeleeAssaultRoot,
            _ => null
        };
    }

    private static string ResolveAttackingTeamId(string? explicitTeamId, string defendingTeamId)
    {
        if (!string.IsNullOrWhiteSpace(explicitTeamId))
            return explicitTeamId.Trim();

        if (string.Equals(defendingTeamId, "Imperium", StringComparison.OrdinalIgnoreCase))
            return "Heretics";

        if (string.Equals(defendingTeamId, "Heretics", StringComparison.OrdinalIgnoreCase))
            return "Imperium";

        if (string.Equals(defendingTeamId, "Tau", StringComparison.OrdinalIgnoreCase))
            return "Imperium";

        return "Heretics";
    }

    private void CompleteCurrentWave(WH40KWaveDefenceRuleComponent rule)
    {
        rule.ActiveWaveEndsAt = TimeSpan.Zero;
        BroadcastWaveMessage(Loc.GetString(
            "wh40k-wave-defence-wave-clear",
            ("wave", rule.CurrentWaveNumber)));
        BroadcastWaveNotification(
            "wh40k-wave-defence-wave-clear",
            WH40KNotificationColors.Success,
            WH40KNotificationCategory.Event,
            new Dictionary<string, string>
            {
                ["wave"] = rule.CurrentWaveNumber.ToString()
            });

        if (rule.Mode == WH40KWaveDefenceMode.Fixed && rule.CurrentWaveNumber >= rule.FinalWaveNumber)
        {
            SetVictory(rule, Loc.GetString("wh40k-wave-defence-victory"));
            return;
        }

        rule.Phase = WH40KWaveDefencePhase.Intermission;
        rule.NextPhaseChange = rule.ManualWaveAdvanceOnly
            ? TimeSpan.Zero
            : Timing.CurTime + TimeSpan.FromSeconds(rule.IntermissionDurationSeconds);
        BroadcastWaveNotification(
            "wh40k-wave-defence-phase-intermission",
            WH40KNotificationColors.Imperium,
            WH40KNotificationCategory.Info);
    }

    private void ExpireCurrentWave(WH40KWaveDefenceRuleComponent rule)
    {
        foreach (var attacker in rule.ActiveAttackers.ToArray())
        {
            if (Exists(attacker))
                QueueDel(attacker);
        }

        rule.ActiveAttackers.Clear();
        foreach (var batch in rule.PendingBatches)
        {
            batch.Spawned = true;
        }

        rule.LastBatchSummary =
            $"Wave {rule.CurrentWaveNumber} expired after {rule.MaxWaveDurationSeconds:0.#} seconds and was counted as completed.";
        CompleteCurrentWave(rule);
    }

    private void SetVictory(WH40KWaveDefenceRuleComponent rule, string reason)
    {
        if (rule.Phase is WH40KWaveDefencePhase.Victory or WH40KWaveDefencePhase.Defeat)
            return;

        rule.ActiveWaveEndsAt = TimeSpan.Zero;
        rule.Phase = WH40KWaveDefencePhase.Victory;
        rule.EndReason = reason;
        BroadcastWaveMessage(reason);
        BroadcastWaveNotification(
            "wh40k-wave-defence-victory",
            WH40KNotificationColors.Success,
            WH40KNotificationCategory.Critical);
        _roundEnd.EndRound();
    }

    private void SetDefeat(WH40KWaveDefenceRuleComponent rule, string reason)
    {
        if (rule.Phase is WH40KWaveDefencePhase.Victory or WH40KWaveDefencePhase.Defeat)
            return;

        rule.ActiveWaveEndsAt = TimeSpan.Zero;
        rule.Phase = WH40KWaveDefencePhase.Defeat;
        rule.EndReason = reason;
        BroadcastWaveMessage(reason);
        BroadcastWaveNotification(
            string.Equals(reason, Loc.GetString("wh40k-wave-defence-defeat-objective"), StringComparison.Ordinal)
                ? "wh40k-wave-defence-defeat-objective"
                : "wh40k-wave-defence-defeat-defenders",
            WH40KNotificationColors.Warning,
            WH40KNotificationCategory.Critical);
        _roundEnd.EndRound();
    }

    private bool ShouldDefeatForNoDefenders(WH40KWaveDefenceRuleComponent rule)
    {
        if (rule.CurrentWaveNumber == 0 && rule.Phase == WH40KWaveDefencePhase.Preparation)
            return false;

        return CountAliveDefenders(rule) <= 0;
    }

    private void PruneActiveAttackers(WH40KWaveDefenceRuleComponent rule)
    {
        foreach (var attacker in rule.ActiveAttackers.ToArray())
        {
            if (!Exists(attacker) || TerminatingOrDeleted(attacker))
            {
                rule.ActiveAttackers.Remove(attacker);
                continue;
            }

            if (_mobState.IsDead(attacker))
                rule.ActiveAttackers.Remove(attacker);
        }
    }

    public void BroadcastWaveMessage(string message)
    {
        foreach (var session in _players.Sessions)
        {
            _chat.DispatchServerMessage(session, message);
        }
    }

    private void BroadcastWaveNotification(
        string locKey,
        Color accentColor,
        WH40KNotificationCategory category,
        Dictionary<string, string>? locArgs = null,
        bool resolveArgValues = false)
    {
        RaiseNetworkEvent(new WH40KLocalizedNotificationEvent
        {
            LocKey = locKey,
            LocArgs = locArgs,
            ResolveArgValues = resolveArgValues,
            AccentColor = accentColor,
            Category = category,
            Size = WH40KNotificationSize.Wide,
        });
    }

    private void SendSpawnBriefing(ICommonSession session, WH40KWaveDefenceRuleComponent rule)
    {
        var phaseLocKey = rule.Phase switch
        {
            WH40KWaveDefencePhase.Preparation => "wh40k-wave-defence-phase-preparation",
            WH40KWaveDefencePhase.WaveActive => "wh40k-wave-defence-phase-wave-active",
            WH40KWaveDefencePhase.Intermission => "wh40k-wave-defence-phase-intermission",
            WH40KWaveDefencePhase.Victory => "wh40k-wave-defence-victory",
            WH40KWaveDefencePhase.Defeat => "wh40k-wave-defence-defeat-defenders",
            _ => "wh40k-wave-defence-phase-preparation"
        };

        RaiseNetworkEvent(new WH40KLocalizedNotificationEvent
        {
            LocKey = "wh40k-wave-defence-spawn-briefing",
            LocArgs = new Dictionary<string, string>
            {
                ["phase"] = phaseLocKey,
                ["wave"] = rule.CurrentWaveNumber.ToString(),
                ["final"] = rule.FinalWaveNumber.ToString(),
            },
            ResolveArgValues = true,
            AccentColor = WH40KNotificationColors.Imperium,
            Category = WH40KNotificationCategory.Objective,
            Icon = WH40KNotificationIcon.Aquila,
            Size = WH40KNotificationSize.Wide,
            DurationSeconds = 10f,
        }, session);
    }

    private bool TryGetRuleMapId(WH40KWaveDefenceRuleComponent rule, out MapId mapId)
    {
        if (rule.PrimaryObjective is { } objective && Exists(objective))
        {
            mapId = Transform(objective).MapID;
            if (mapId != MapId.Nullspace)
                return true;
        }

        if (rule.Station is { } station)
        {
            if (TryComp<StationDataComponent>(station, out var stationData))
            {
                foreach (var grid in stationData.Grids)
                {
                    if (!Exists(grid))
                        continue;

                    mapId = Transform(grid).MapID;
                    if (mapId != MapId.Nullspace)
                        return true;
                }
            }

            mapId = Transform(station).MapID;
            if (mapId != MapId.Nullspace)
                return true;
        }

        mapId = MapId.Nullspace;
        return false;
    }

    public bool TryGetActiveRule(
        out EntityUid uid,
        out WH40KWaveDefenceRuleComponent component,
        out GameRuleComponent gameRule)
    {
        if (_activeRuleUid is { } active &&
            TryComp<WH40KWaveDefenceRuleComponent>(active, out var activeComponent) &&
            TryComp<GameRuleComponent>(active, out var activeGameRule) &&
            GameTicker.IsGameRuleActive(active, activeGameRule))
        {
            uid = active;
            component = activeComponent;
            gameRule = activeGameRule;
            return true;
        }

        var query = EntityQueryEnumerator<WH40KWaveDefenceRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var nextUid, out var nextComponent, out var nextGameRule))
        {
            if (!GameTicker.IsGameRuleActive(nextUid, nextGameRule))
                continue;

            uid = nextUid;
            component = nextComponent;
            gameRule = nextGameRule;
            _activeRuleUid = nextUid;
            return true;
        }

        uid = EntityUid.Invalid;
        component = default!;
        gameRule = default!;
        return false;
    }

    public bool TryGetActiveRule(out EntityUid uid, out WH40KWaveDefenceRuleComponent component)
    {
        return TryGetActiveRule(out uid, out component, out _);
    }

    public bool TryGetEntityTeamId(EntityUid entity, out string teamId)
    {
        if (TryComp<WH40KTeamMemberComponent>(entity, out var member) &&
            !string.IsNullOrWhiteSpace(member.TeamId))
        {
            teamId = member.TeamId;
            return true;
        }

        if (TryComp<WH40KWaveDefenceObjectiveComponent>(entity, out var objective) &&
            !string.IsNullOrWhiteSpace(objective.TeamId))
        {
            teamId = objective.TeamId;
            return true;
        }

        teamId = string.Empty;
        return false;
    }

    public bool TryGetRememberedTeam(NetUserId userId, out string teamId)
    {
        teamId = string.Empty;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        if (!rule.PlayerLastKnownTeam.TryGetValue(userId, out var rememberedTeam) ||
            string.IsNullOrWhiteSpace(rememberedTeam))
        {
            return false;
        }

        teamId = rememberedTeam;
        return true;
    }

    public bool TryGetTeamIdForUser(NetUserId userId, out string teamId)
    {
        if (TryGetRememberedTeam(userId, out teamId))
            return true;

        teamId = string.Empty;
        if (!_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity is not { Valid: true } attached)
        {
            return false;
        }

        if (!TryGetEntityTeamId(attached, out var rawTeamId))
            return false;

        return TryResolveTeamId(rawTeamId, out teamId);
    }

    public IReadOnlyList<string> GetTeamIds()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return Array.Empty<string>();

        return GetConfiguredTeamIds(rule).ToArray();
    }

    public bool TryResolveTeamId(string teamId, out string resolvedTeamId)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
        {
            resolvedTeamId = string.Empty;
            return false;
        }

        return TryResolveTeamId(rule, teamId, out resolvedTeamId);
    }

    public bool TryGetTeamDisplayName(string teamId, out string teamName)
    {
        teamName = string.Empty;
        if (!TryResolveTeamId(teamId, out var resolvedTeamId))
            return false;

        if (string.Equals(resolvedTeamId, "Imperium", StringComparison.OrdinalIgnoreCase))
        {
            teamName = "wh40k-team-imperium";
            return true;
        }

        if (string.Equals(resolvedTeamId, "Heretics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resolvedTeamId, "Chaos", StringComparison.OrdinalIgnoreCase))
        {
            teamName = "wh40k-team-heretics";
            return true;
        }

        return false;
    }

    public bool TryGetTeamColor(string teamId, out Color teamColor)
    {
        if (!TryResolveTeamId(teamId, out var resolvedTeamId))
        {
            teamColor = Color.White;
            return false;
        }

        teamColor = WH40KNotificationColors.ForTeam(resolvedTeamId);
        return true;
    }

    public bool TryGetTeamDepartments(string teamId, out IReadOnlyList<ProtoId<DepartmentPrototype>> departments)
    {
        departments = Array.Empty<ProtoId<DepartmentPrototype>>();
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        var candidate = new ProtoId<DepartmentPrototype>(teamId);
        if (!Proto.HasIndex<DepartmentPrototype>(candidate))
            return false;

        departments = new[] { candidate };
        return true;
    }

    public WH40KBattlePhase GetCurrentPhase()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return WH40KBattlePhase.Preparation;

        return GetBattlePhase(rule);
    }

    public int GetCurrentEconomyMultiplier()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return 1;

        return GetEconomyMultiplier(rule, GetBattlePhase(rule));
    }

    public int GetRoundElapsedSeconds()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return 0;

        var elapsed = (Timing.CurTime - rule.RoundStartTime).TotalSeconds;
        if (elapsed <= 0)
            return 0;

        return (int) Math.Floor(elapsed);
    }

    public bool TryGetTeamProgress(string teamId, out int level, out int frontPoints, out int? pointsToNextLevel)
    {
        level = 1;
        frontPoints = 0;
        pointsToNextLevel = null;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId))
            return false;

        frontPoints = rule.TeamFrontPoints.GetValueOrDefault(resolvedTeamId, 0);
        level = rule.TeamBaseLevels.GetValueOrDefault(resolvedTeamId, CalculateTeamLevel(frontPoints, rule.BaseLevelThresholds));
        pointsToNextLevel = GetPointsToNextLevel(frontPoints, rule.BaseLevelThresholds);
        return true;
    }

    public bool TryAdjustTeamXp(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int teamXp,
        out int level,
        string? source = null,
        bool allowDecrease = false)
    {
        resolvedTeamId = string.Empty;
        teamXp = 0;
        level = 1;

        if (string.IsNullOrWhiteSpace(teamId) || delta == 0 ||
            !TryGetActiveRule(out _, out var rule, out _))
        {
            return false;
        }

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out resolvedTeamId))
            return false;

        var oldLevel = rule.TeamBaseLevels.GetValueOrDefault(resolvedTeamId, 1);
        var adjusted = WH40KTeamProgressionMath.AdjustTeamXp(
            rule.TeamFrontPoints.GetValueOrDefault(resolvedTeamId, 0),
            oldLevel,
            rule.BaseLevelThresholds,
            delta,
            allowDecrease);

        teamXp = adjusted.TeamXp;
        level = adjusted.Level;
        rule.TeamFrontPoints[resolvedTeamId] = teamXp;
        rule.TeamBaseLevels[resolvedTeamId] = level;
        return true;
    }

    public bool TryAdjustTeamFrontPoints(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int frontPoints,
        out int level,
        string? source = null)
    {
        resolvedTeamId = string.Empty;
        frontPoints = 0;
        level = 1;

        if (string.IsNullOrWhiteSpace(teamId) ||
            !TryGetActiveRule(out _, out var rule, out _))
        {
            return false;
        }

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out resolvedTeamId))
            return false;

        var currentPoints = rule.TeamFrontPoints.GetValueOrDefault(resolvedTeamId, 0);
        frontPoints = Math.Max(0, currentPoints + delta);
        rule.TeamFrontPoints[resolvedTeamId] = frontPoints;
        level = CalculateTeamLevel(frontPoints, rule.BaseLevelThresholds);
        rule.TeamBaseLevels[resolvedTeamId] = level;
        return true;
    }

    public bool TryGetTeamCommandPoints(string teamId, out int points)
    {
        points = 0;
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId))
            return false;

        points = rule.TeamCommandPoints.GetValueOrDefault(resolvedTeamId, 0);
        return true;
    }

    public bool TryAdjustTeamCommandPoints(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int commandPoints,
        string? source = null)
    {
        resolvedTeamId = string.Empty;
        commandPoints = 0;

        if (string.IsNullOrWhiteSpace(teamId) ||
            !TryGetActiveRule(out _, out var rule, out _))
        {
            return false;
        }

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out resolvedTeamId))
            return false;

        var current = rule.TeamCommandPoints.GetValueOrDefault(resolvedTeamId, 0);
        commandPoints = Math.Max(0, current + delta);
        rule.TeamCommandPoints[resolvedTeamId] = commandPoints;
        return true;
    }

    public bool TryGetTeamInfluencePoints(string teamId, out int points)
    {
        return TryGetTeamCommandPoints(teamId, out points);
    }

    public bool TrySpendTeamInfluence(string teamId, int amount, out int remaining, string? source = null)
    {
        remaining = 0;
        if (amount <= 0 || !TryGetTeamInfluencePoints(teamId, out var current) || current < amount)
            return false;

        if (!TryAdjustTeamCommandPoints(teamId, -amount, out _, out remaining, source ?? "influence-spend"))
            return false;

        return true;
    }

    public bool TryAdjustTeamInfluence(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int influence,
        string? source = null)
    {
        return TryAdjustTeamCommandPoints(teamId, delta, out resolvedTeamId, out influence, source ?? "influence-adjust");
    }

    public bool TryGetTeamResearchPoints(string teamId, out int points)
    {
        points = 0;
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId))
            return false;

        points = rule.TeamResearchPoints.GetValueOrDefault(resolvedTeamId, 0);
        return true;
    }

    public bool TrySpendTeamResearchPoints(string teamId, int amount, out int remaining, string? source = null)
    {
        remaining = 0;
        if (string.IsNullOrWhiteSpace(teamId) || amount <= 0 ||
            !TryGetActiveRule(out _, out var rule, out _))
        {
            return false;
        }

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId))
            return false;

        var current = rule.TeamResearchPoints.GetValueOrDefault(resolvedTeamId, 0);
        if (current < amount)
            return false;

        remaining = current - amount;
        rule.TeamResearchPoints[resolvedTeamId] = remaining;
        RaiseLocalEvent(new WH40KTeamResearchBalanceChangedEvent(
            resolvedTeamId,
            remaining,
            -amount,
            source ?? "research-spend"));
        return true;
    }

    public bool TryAdjustTeamResearchPoints(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int researchPoints,
        string? source = null)
    {
        resolvedTeamId = string.Empty;
        researchPoints = 0;

        if (string.IsNullOrWhiteSpace(teamId) || delta == 0 ||
            !TryGetActiveRule(out _, out var rule, out _))
        {
            return false;
        }

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out resolvedTeamId))
            return false;

        var current = rule.TeamResearchPoints.GetValueOrDefault(resolvedTeamId, 0);
        researchPoints = Math.Max(0, current + delta);
        rule.TeamResearchPoints[resolvedTeamId] = researchPoints;
        RaiseLocalEvent(new WH40KTeamResearchBalanceChangedEvent(
            resolvedTeamId,
            researchPoints,
            researchPoints - current,
            source ?? "research-adjust"));
        return true;
    }

    public bool TryGetTeamEconomySnapshot(EntityUid? sourceUid, string teamId, out WH40KTeamEconomySnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId))
            return false;

        var teamXp = rule.TeamFrontPoints.GetValueOrDefault(resolvedTeamId, 0);
        var influence = rule.TeamCommandPoints.GetValueOrDefault(resolvedTeamId, 0);
        var research = rule.TeamResearchPoints.GetValueOrDefault(resolvedTeamId, 0);
        var level = rule.TeamBaseLevels.GetValueOrDefault(resolvedTeamId, 1);
        var pointsToNextLevel = GetPointsToNextLevel(teamXp, rule.BaseLevelThresholds);
        TryGetTeamFunds(sourceUid, resolvedTeamId, out var funds);

        snapshot = new WH40KTeamEconomySnapshot(
            resolvedTeamId,
            teamXp,
            influence,
            research,
            funds,
            level,
            pointsToNextLevel);
        return true;
    }

    public bool TryGetBaseLevelThresholds(out IReadOnlyList<int> thresholds)
    {
        thresholds = Array.Empty<int>();
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        thresholds = rule.BaseLevelThresholds;
        return true;
    }

    public bool TryGetTeamAliveSnapshot(string teamId, out int aliveCount, out int totalCount)
    {
        aliveCount = 0;
        totalCount = 0;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId))
            return false;

        CountTeamBodies(rule, resolvedTeamId, out aliveCount, out totalCount);
        return totalCount > 0 || string.Equals(resolvedTeamId, rule.DefendingTeamId, StringComparison.OrdinalIgnoreCase);
    }

    public string BuildStatusText()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return "Active WH40K WaveDefence rule not found.";

        var objective = rule.PrimaryObjective is { } obj && Exists(obj)
            ? ToPrettyString(obj)
            : "none";

        var mapDescription = TryGetRuleMapId(rule, out var mapId)
            ? mapId.ToString()
            : "unresolved";

        return
            $"Phase: {rule.Phase}\n" +
            $"Wave: {rule.CurrentWaveNumber}/{rule.FinalWaveNumber}\n" +
            $"Manual wave advance: {rule.ManualWaveAdvanceOnly}\n" +
            $"Active wave ends at: {(rule.ActiveWaveEndsAt > TimeSpan.Zero ? rule.ActiveWaveEndsAt.ToString() : "none")}\n" +
            $"Map: {mapDescription}\n" +
            $"Objective: {objective}\n" +
            $"Layout ready: {rule.LayoutReady}\n" +
            $"Layout retries: {rule.LayoutRetryCount}\n" +
            $"Alive defender bodies: {CountAliveDefenders(rule)}\n" +
            $"Active defender sessions: {CountActiveDefenderParticipants(rule)}\n" +
            $"Remembered defenders: {CountRememberedDefenders(rule)}\n" +
            $"Active attackers: {rule.ActiveAttackers.Count}\n" +
            $"Queued late joins: {rule.QueuedLateJoinJobs.Count}\n" +
            $"Queued respawns: {rule.QueuedRespawns.Count}\n" +
            $"Authoring valid: {rule.AuthoringValid}\n" +
            $"Map safeguards: {rule.MapStabilitySummary}\n" +
            $"Layout status: {rule.LastLayoutStatus}\n" +
            $"Last batch: {rule.LastBatchSummary}";
    }

    private void ApplyMapStabilitySafeguards(WH40KWaveDefenceRuleComponent rule, MapId mapId)
    {
        var stationGrids = new HashSet<EntityUid>();

        if (rule.Station is { } station &&
            TryComp<StationDataComponent>(station, out var stationData))
        {
            foreach (var grid in stationData.Grids)
            {
                if (Exists(grid))
                    stationGrids.Add(grid);
            }
        }

        if (stationGrids.Count == 0)
        {
            foreach (var grid in _mapManager.GetAllGrids(mapId))
            {
                stationGrids.Add(grid.Owner);
            }
        }

        foreach (var gridUid in stationGrids)
        {
            _shuttles.Disable(gridUid);
            EnsureInherentGravity(gridUid, raiseGravityChangedEvent: true);
        }

        if (_map.MapExists(mapId))
        {
            var mapUid = _map.GetMapOrInvalid(mapId);
            if (mapUid != EntityUid.Invalid)
                EnsureInherentGravity(mapUid, raiseGravityChangedEvent: false);
        }

        rule.MapStabilitySummary = $"gravity enforced on {stationGrids.Count} grid(s) for map {mapId}";
    }

    private void EnsureInherentGravity(EntityUid uid, bool raiseGravityChangedEvent)
    {
        var gravity = EnsureComp<GravityComponent>(uid);
        var wasEnabled = gravity.Enabled;

        if (gravity.Enabled && gravity.Inherent)
            return;

        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(uid, gravity);

        if (raiseGravityChangedEvent && !wasEnabled)
        {
            var ev = new GravityChangedEvent(uid, true);
            RaiseLocalEvent(uid, ref ev, true);
        }
    }

    public int CountAliveDefenders(WH40KWaveDefenceRuleComponent rule)
    {
        CountTeamBodies(rule, rule.DefendingTeamId, out var aliveCount, out _);
        return aliveCount;
    }

    private int CountActiveDefenderParticipants(WH40KWaveDefenceRuleComponent rule)
    {
        var total = 0;
        foreach (var session in _players.Sessions)
        {
            if (IsSessionActivelyDefending(session, rule))
                total++;
        }

        return total;
    }

    private int CountRememberedDefenders(WH40KWaveDefenceRuleComponent rule)
    {
        return rule.PlayerLastKnownTeam.Values.Count(teamId =>
            string.Equals(teamId, rule.DefendingTeamId, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSessionActivelyDefending(ICommonSession session, WH40KWaveDefenceRuleComponent rule)
    {
        if (session.AttachedEntity is not { Valid: true } attached)
            return false;

        if (!TryGetEntityTeamId(attached, out var teamId) ||
            !string.Equals(teamId, rule.DefendingTeamId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _mobState.IsAlive(attached) || (rule.CountCritAsAlive && _mobState.IsCritical(attached));
    }

    private void CountTeamBodies(
        WH40KWaveDefenceRuleComponent rule,
        string teamId,
        out int aliveCount,
        out int totalCount)
    {
        aliveCount = 0;
        totalCount = 0;

        var query = EntityQueryEnumerator<WH40KTeamMemberComponent, MindContainerComponent>();
        while (query.MoveNext(out var uid, out var member, out var mindContainer))
        {
            if (!string.Equals(member.TeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
                !mindContainer.HasMind)
            {
                continue;
            }

            totalCount++;
            if (_mobState.IsAlive(uid) || (rule.CountCritAsAlive && _mobState.IsCritical(uid)))
                aliveCount++;
        }
    }

    private static IReadOnlyCollection<string> GetConfiguredTeamIds(WH40KWaveDefenceRuleComponent rule)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(rule.DefendingTeamId))
            ids.Add(rule.DefendingTeamId);

        if (!string.IsNullOrWhiteSpace(rule.AttackingTeamId))
            ids.Add(rule.AttackingTeamId);

        return ids;
    }

    private void EnsureTeamProgress(WH40KWaveDefenceRuleComponent rule)
    {
        var startingPoints = Math.Max(0, rule.TeamStartingPoints);

        foreach (var teamId in GetConfiguredTeamIds(rule))
        {
            rule.TeamFrontPoints.TryAdd(teamId, startingPoints);
            rule.TeamCommandPoints.TryAdd(teamId, startingPoints);
            rule.TeamResearchPoints.TryAdd(teamId, 0);
            rule.TeamBaseLevels.TryAdd(teamId, CalculateTeamLevel(startingPoints, rule.BaseLevelThresholds));
        }
    }

    private static bool TryResolveTeamId(WH40KWaveDefenceRuleComponent rule, string teamId, out string resolvedTeamId)
    {
        resolvedTeamId = string.Empty;
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        foreach (var candidate in GetConfiguredTeamIds(rule))
        {
            if (!string.Equals(candidate, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            resolvedTeamId = candidate;
            return true;
        }

        return false;
    }

    private static WH40KBattlePhase GetBattlePhase(WH40KWaveDefenceRuleComponent rule)
    {
        return rule.Phase switch
        {
            WH40KWaveDefencePhase.WaveActive => WH40KBattlePhase.Assault,
            WH40KWaveDefencePhase.Victory or WH40KWaveDefencePhase.Defeat => WH40KBattlePhase.Apocalypse,
            _ => WH40KBattlePhase.Preparation
        };
    }

    private static int GetEconomyMultiplier(WH40KWaveDefenceRuleComponent rule, WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Assault => Math.Max(1, rule.EconomyAssaultMultiplier),
            WH40KBattlePhase.Apocalypse => Math.Max(1, rule.EconomyApocalypseMultiplier),
            _ => Math.Max(1, rule.EconomyPreparationMultiplier)
        };
    }

    private static int CalculateTeamLevel(int points, IReadOnlyList<int> thresholds)
    {
        return WH40KTeamProgressionMath.CalculateLevel(points, thresholds);
    }

    private static int? GetPointsToNextLevel(int points, IReadOnlyList<int> thresholds)
    {
        foreach (var threshold in thresholds)
        {
            if (points < threshold)
                return threshold - Math.Max(0, points);
        }

        return null;
    }

    private bool TryGetTeamFunds(EntityUid? sourceUid, string teamId, out int funds)
    {
        funds = 0;

        if (!TryResolveCargoAccountForTeam(teamId, out var account) ||
            !TryGetTeamBank(sourceUid, out var bank))
        {
            return false;
        }

        funds = Math.Max(0, _cargo.GetBalanceFromAccount(bank, account));
        return true;
    }

    private bool TryGetTeamBank(EntityUid? sourceUid, out Entity<StationBankAccountComponent?> bank)
    {
        bank = default;

        if (sourceUid is { } source &&
            _stations.GetOwningStation(source) is { } stationUid &&
            TryComp<StationBankAccountComponent>(stationUid, out var sourceBank))
        {
            bank = (stationUid, sourceBank);
            return true;
        }

        var query = EntityQueryEnumerator<StationBankAccountComponent>();
        while (query.MoveNext(out var uid, out var fallbackBank))
        {
            bank = (uid, fallbackBank);
            return true;
        }

        return false;
    }

    private static bool TryResolveCargoAccountForTeam(string teamId, out ProtoId<CargoAccountPrototype> account)
    {
        if (string.Equals(teamId, "Imperium", StringComparison.OrdinalIgnoreCase))
        {
            account = "WH40KImperium";
            return true;
        }

        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(teamId, "Chaos", StringComparison.OrdinalIgnoreCase))
        {
            account = "WH40KHeretics";
            return true;
        }

        account = default;
        return false;
    }

    private int GetKillRewardMultiplier(EntityUid victim)
    {
        if (!Exists(victim))
            return 1;

        if (HasComp<CommandStaffComponent>(victim) ||
            HasComp<WH40KSquadLeaderComponent>(victim))
        {
            return 3;
        }

        if (HasComp<WH40KStrategicPointUpgradeSkillComponent>(victim))
            return 2;

        return 1;
    }

    public bool ForceNextWave()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        if (!TryInitializeLayout(rule, announceOnSuccess: true))
        {
            return false;
        }

        if (rule.Phase == WH40KWaveDefencePhase.WaveActive)
        {
            foreach (var attacker in rule.ActiveAttackers.ToArray())
            {
                if (Exists(attacker))
                    QueueDel(attacker);
            }

            rule.ActiveAttackers.Clear();
            foreach (var batch in rule.PendingBatches)
            {
                batch.Spawned = true;
            }

            CompleteCurrentWave(rule);
            return true;
        }

        if (rule.Phase is WH40KWaveDefencePhase.Preparation or WH40KWaveDefencePhase.Intermission)
        {
            StartNextWave(rule);
            return true;
        }

        return false;
    }

    public bool ForceVictory()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        SetVictory(rule, Loc.GetString("wh40k-wave-defence-victory"));
        return true;
    }

    public bool ForceDefeat(string reason)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        SetDefeat(rule, reason);
        return true;
    }
}
