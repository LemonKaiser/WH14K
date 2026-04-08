using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Server._WH40K.Combat;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.GameTicking.Rules.Prototypes;
using Content.Server._WH40K.LateJoin;
using Content.Server._WH40K.Store.Components;
using Content.Shared._WH40K.Chat;
using Content.Shared._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Interface;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.Influence;
using Content.Shared._WH40K.RoundEvents;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.GameTicking.Rules;
using Content.Server.KillTracking;
using Content.Server.Mind;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Server.Store.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Administration;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Polymorph;
using Content.Shared.Players;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Content.Shared.Weather;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.GameTicking.Rules;

public sealed class WH40KTeamBattleRuleSystem : GameRuleSystem<Components.WH40KTeamBattleRuleComponent>
{
    private static readonly bool AnnounceTeamOnSpawn = true;
    private static readonly bool AnnounceWinner = true;
    private static readonly bool CountCriticalAsAlive = true;
    private const float WH40KSprintDrain = 5f;
    private const float WH40KWalkRecovery = 2f;
    private const float WH40KIdleRecovery = 4f;
    private const float WH40KSprintMinRemaining = 25f;
    private const float WH40KFatigueShakeReserve = 20f;
    [Dependency] private readonly AdminSystem _admin = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly KillTrackingSystem _killTracking = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly PlayTimeTrackingSystem _playTimeTracking = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly SharedRoofSystem _roof = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private readonly WH40KFactionSystem _wh40kFactions = default!;
    [Dependency] private readonly WH40KTeamNpcFactionSystem _teamNpcFactions = default!;
    [Dependency] private readonly SharedWeatherSystem _weather = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly CableSystem _cables = default!;
    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly Diagnostics.WH40KNetDiagAttributionSystem _attribution = default!;

    private ISawmill _sawmill = default!;
    private float _checkInterval;
    private bool _requireAllTeamsPresent;
    private float _roundTimeLimitSeconds;
    private bool _economyTelemetryTrace;
    private float _economyTelemetrySnapshotIntervalSeconds;
    private int _economyTelemetryBurstCommandDelta;
    private TimeSpan _lastEconomyTelemetrySnapshotAt = TimeSpan.Zero;
    private TimeSpan _nextEconomyTelemetrySnapshotAt = TimeSpan.Zero;
    private readonly Dictionary<string, int> _economySnapshotFrontPoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _economySnapshotCommandPoints = new(StringComparer.OrdinalIgnoreCase);
    private EntityUid? _activeRuleUid;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.teamrule");

        Subs.CVar(_config, CCVars.WH40KTeamCheckInterval, v =>
        {
            _checkInterval = v;
            ApplyConfigToActiveRules();
        }, true);

        Subs.CVar(_config, CCVars.WH40KRequireAllTeamsPresent, v =>
        {
            _requireAllTeamsPresent = v;
            ApplyConfigToActiveRules();
        }, true);

        Subs.CVar(_config, CCVars.WH40KRoundTimeLimitSeconds, v =>
        {
            _roundTimeLimitSeconds = Math.Max(0f, v);
            ApplyConfigToActiveRules();
        }, true);

        Subs.CVar(_config, CCVars.WH40KEconomyTelemetryTrace, v =>
        {
            _economyTelemetryTrace = v;
            _sawmill.Info($"WH40K economy telemetry trace logging {(v ? "enabled" : "disabled")}.");
        }, true);

        Subs.CVar(_config, CCVars.WH40KEconomyTelemetrySnapshotIntervalSeconds, v =>
        {
            _economyTelemetrySnapshotIntervalSeconds = Math.Max(30f, v);
        }, true);

        Subs.CVar(_config, CCVars.WH40KEconomyTelemetryBurstCommandDelta, v =>
        {
            _economyTelemetryBurstCommandDelta = Math.Max(1, v);
        }, true);

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnPlayerBeforeSpawn);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<KillReportedEvent>(OnKillReported);
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WH40KTeamBattleFactionIconComponent, PolymorphedEvent>(OnFactionIconPolymorphed);
    }

    protected override void Started(EntityUid uid, Components.WH40KTeamBattleRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        _activeRuleUid = uid;

        BuildDepartmentMap(component);
        component.CheckInterval = _checkInterval;
        component.RequireAllTeamsPresent = _requireAllTeamsPresent;
        ApplyOptionalRoundTimeLimitOverride(component);
        component.NextCheck = Timing.CurTime + TimeSpan.FromSeconds(component.CheckInterval);
        component.RoundStartTime = Timing.CurTime;
        component.RoundEnding = false;
        component.WinnerTeamId = null;
        component.Draw = false;
        component.TimeLimitReached = false;
        component.PlayerKills.Clear();
        component.NextFriendlyFireAhelpTime.Clear();
        component.CurrentPhase = WH40KBattlePhase.Preparation;
        component.NextPhaseChange = Timing.CurTime + TimeSpan.FromSeconds(component.PreparationDurationSeconds);
        component.TeamFrontPoints.Clear();
        component.TeamCommandPoints.Clear();
        component.TeamBaseLevels.Clear();
        component.TeamLevelBuffs.Clear();
        component.PlayerLastKnownTeam.Clear();
        component.WeatherSuppressedForRound = false;
        component.NextWeatherStart = null;
        component.ActiveWeatherEnd = null;
        component.ActiveWeather = null;
        component.PendingWeather = null;
        component.LastWeatherWarningForStart = null;
        component.RoundEventsSuppressedForRound = false;
        component.ActiveRoundEvent = WH40KRoundEventType.None;
        component.PendingRoundEvent = null;
        component.NextRoundEventStart = null;
        component.ActiveRoundEventEnd = null;
        component.LastRoundEventWarningForStart = null;
        component.NextOrbitalWaveAt = TimeSpan.Zero;
        component.PendingOrbitalStrikes.Clear();
        ApplyExternalConfigProfile(component);
        NormalizeEconomyRuntimeConfig(component);
        NormalizeWeatherDangerProfile(component);
        component.LevelBuffPool = SanitizeLevelBuffPool(component.LevelBuffPool);
        EnsureTeamArrays(component);
        EnsureTeamProgress(component);
        ResetEconomyTelemetryState(component);
        InitializeWeatherState(component);
        InitializeRoundEventState(component);
        ApplyMapStabilitySafeguards();

        if (component.Teams.Count < 2)
            _sawmill.Warning($"WH40K team rule '{ToPrettyString(uid)}' has fewer than 2 teams configured.");

        RaiseNetworkEvent(new WH40KTeamColorsAssignedEvent(BuildTeamColorDefinitions(component)));
        _teamNpcFactions.RefreshAllTeamFactions();
        ApplyWh40KStaminaProfileToAllTeamMembers();
        _wh40kFactions.BroadcastFactionsToAll();
    }

    protected override void Ended(EntityUid uid, Components.WH40KTeamBattleRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        EndRoundEvent(component, Timing.CurTime, forceCleanup: true);
        component.PendingOrbitalStrikes.Clear();
        ClearAllTeamLevelBuffComponents();

        if (_gameTicker.DefaultMap != MapId.Nullspace)
            _weather.TrySetWeather(_gameTicker.DefaultMap, null, out _);

        if (_activeRuleUid == uid)
            _activeRuleUid = null;

        ResetEconomyTelemetryState(null);
        _wh40kFactions.BroadcastFactionsToAll();
    }

    protected override void ActiveTick(EntityUid uid, Components.WH40KTeamBattleRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.RoundEnding)
            return;

        using (_attribution.EnterScope("game_ticking.wh40k_team_battle_rule.phase"))
            UpdatePhase(uid, component);

        using (_attribution.EnterScope("game_ticking.wh40k_team_battle_rule.events"))
            UpdateRoundEvents(component);

        using (_attribution.EnterScope("game_ticking.wh40k_team_battle_rule.weather"))
            UpdateWeather(component);

        using (_attribution.EnterScope("game_ticking.wh40k_team_battle_rule.eco_telemetry"))
            UpdateEconomyTelemetrySnapshots(component);

        if (component.RoundTimeLimitSeconds > 0f)
        {
            var elapsed = (Timing.CurTime - component.RoundStartTime).TotalSeconds;
            if (elapsed >= component.RoundTimeLimitSeconds)
            {
                TriggerTimeLimitDraw(component);
                return;
            }
        }

        if (Timing.CurTime < component.NextCheck)
            return;

        component.NextCheck = Timing.CurTime + TimeSpan.FromSeconds(component.CheckInterval);
        if (AllowsTeamVictory(component) && !IsEarlyVictoryLocked(component))
            CheckForVictory(uid, component, gameRule);
    }

    protected override void AppendRoundEndText(EntityUid uid, Components.WH40KTeamBattleRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        if (component.Teams.Count == 0)
            return;

        ComputeTeamCounts(component, out var total, out var alive);

        args.AddLine(Loc.GetString("wh40k-team-round-end-header"));
        args.AddLine(Loc.GetString("wh40k-team-round-end-summary"));
        args.AddLine(Loc.GetString("wh40k-team-round-end-summary-2"));
        args.AddLine("");

        if (component.TimeLimitReached)
            args.AddLine(Loc.GetString("wh40k-team-round-end-time-limit"));

        var showDraw = component.Draw;
        var winnerTeamId = component.WinnerTeamId;

        // If the round ended without a winner/time-limit, treat it as a draw (e.g., admin endround).
        if (!showDraw && winnerTeamId == null && !component.TimeLimitReached)
            showDraw = true;

        if (showDraw)
        {
            args.AddLine(Loc.GetString("wh40k-team-round-end-draw"));
        }
        else if (winnerTeamId != null)
        {
            var team = component.Teams.FirstOrDefault(t => t.Id == winnerTeamId);
            if (!string.IsNullOrEmpty(team?.Id))
                args.AddLine(Loc.GetString("wh40k-team-round-end-winner", ("team", Loc.GetString(team!.Name))));
        }

        args.AddLine("");

        for (var i = 0; i < component.Teams.Count; i++)
        {
            var team = component.Teams[i];
            args.AddLine(Loc.GetString("wh40k-team-round-end-team-line",
                ("team", Loc.GetString(team.Name)),
                ("alive", alive[i]),
                ("total", total[i]),
                ("deaths", GetTeamDeaths(component, i)),
                ("kills", GetTeamKills(component, i))));
        }

        args.AddLine("");

        AppendTopKillers(component, ref args);
    }

    private void OnPlayerBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (!TryGetActiveRule(out _, out _, out _))
            return;

        if (!ev.LateJoin || string.IsNullOrEmpty(ev.JobId))
            return;

        var player = ev.Player;
        var profile = ev.Profile;
        var station = ev.Station;
        var jobId = ev.JobId!;

        var allowed = new IsRoleAllowedEvent(player, new List<ProtoId<JobPrototype>> { jobId }, null);
        RaiseLocalEvent(ref allowed);
        if (allowed.Cancelled)
            return;

        var data = player.ContentData();
        if (data == null)
            return;

        var newMind = _mind.CreateMind(data.UserId, profile.Name);
        _mind.SetUserId(newMind, data.UserId);

        _playTimeTracking.PlayerRolesChanged(player);

        var mobMaybe = _stationSpawning.SpawnPlayerCharacterOnStation(station, jobId, profile);
        if (mobMaybe == null)
            return;

        var mob = mobMaybe.Value;
        _mind.TransferTo(newMind, mob);
        _roles.MindAddJobRole(newMind, silent: true, jobPrototype: jobId);
        _admin.UpdatePlayerList(player);

        _stationJobs.TryAssignJob(station, jobId, player.UserId);

        _gameTicker.PlayersJoinedRoundNormally++;
        var complete = new PlayerSpawnCompleteEvent(
            mob,
            player,
            jobId,
            ev.LateJoin,
            true,
            _gameTicker.PlayersJoinedRoundNormally,
            station,
            profile);
        RaiseLocalEvent(mob, complete, true);

        ev.Handled = true;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!_mind.TryGetMind(ev.Mob, out var mindId, out _))
            return;

        var killTracker = EnsureComp<KillTrackerComponent>(ev.Mob);
        _killTracking.SetKillState(ev.Mob, MobState.Dead, killTracker);

        if (!TryGetTeamIndex(mindId, rule, out var teamIndex))
        {
            _sawmill.Debug($"No team match for {ev.Player?.Name} ({ToPrettyString(ev.Mob)}). Job not in configured departments.");
            RemCompDeferred<WH40KTeamBattleFactionIconComponent>(ev.Mob);
            if (ev.Player is { } unknownPlayer)
            {
                RaiseNetworkEvent(new WH40KTeamColorsAssignedEvent(BuildTeamColorDefinitions(rule)), unknownPlayer);
                RaiseNetworkEvent(new WH40KTeamThemeAssignedEvent(null), unknownPlayer);
            }
            return;
        }

        var team = rule.Teams[teamIndex];
        var member = EnsureComp<WH40KTeamMemberComponent>(ev.Mob);
        member.TeamId = team.Id;
        var factionIcon = EnsureComp<WH40KTeamBattleFactionIconComponent>(ev.Mob);
        if (!string.Equals(factionIcon.TeamId, team.Id, StringComparison.OrdinalIgnoreCase))
        {
            factionIcon.TeamId = team.Id;
            Dirty(ev.Mob, factionIcon);
        }
        _teamNpcFactions.ApplyTeamFaction(ev.Mob, team.Id);
        if (ev.Player is { } teamPlayer)
        {
            rule.PlayerLastKnownTeam[teamPlayer.UserId] = team.Id;
            RaiseNetworkEvent(new WH40KTeamColorsAssignedEvent(BuildTeamColorDefinitions(rule)), teamPlayer);
            RaiseNetworkEvent(new WH40KTeamThemeAssignedEvent(team.Id), teamPlayer);
        }

        if (rule.TeamLevelBuffs.TryGetValue(team.Id, out var buffType) &&
            buffType != WH40KLevelBuffType.None)
        {
            ApplyTeamLevelBuffToEntity(ev.Mob, rule, buffType);
        }

        ApplyWh40KStaminaProfile(ev.Mob);

        if (!AnnounceTeamOnSpawn)
            return;

        if (ev.Player != null)
        {
            RaiseNetworkEvent(new WH40KLocalizedChatEvent
            {
                LocKey = "wh40k-team-service-message",
                LocArgs = new Dictionary<string, string> { ["team"] = team.Name },
                ResolveArgValues = true,
            }, ev.Player);

            var perTeamKey = $"wh40k-team-service-message-{team.Id}";
            if (Loc.HasString(perTeamKey))
            {
                RaiseNetworkEvent(new WH40KLocalizedChatEvent
                {
                    LocKey = perTeamKey,
                }, ev.Player);
            }
        }
    }

    private void OnFactionIconPolymorphed(Entity<WH40KTeamBattleFactionIconComponent> ent, ref PolymorphedEvent args)
    {
        using var scope = _attribution.EnterScope("game_ticking.wh40k_team_battle_rule.polymorph");
        var sourceTeamId = ent.Comp.TeamId;
        if (string.IsNullOrWhiteSpace(sourceTeamId))
            return;

        var resolvedTeamId = sourceTeamId;
        if (TryGetActiveRule(out _, out var rule, out _) &&
            TryResolveTeamId(rule, sourceTeamId, out var canonicalTeamId))
        {
            resolvedTeamId = canonicalTeamId;
        }

        var targetIcon = EnsureComp<WH40KTeamBattleFactionIconComponent>(args.NewEntity);
        if (!string.Equals(targetIcon.TeamId, resolvedTeamId, StringComparison.OrdinalIgnoreCase))
        {
            targetIcon.TeamId = resolvedTeamId;
            Dirty(args.NewEntity, targetIcon);
        }

        _teamNpcFactions.ApplyTeamFaction(args.NewEntity, resolvedTeamId);
    }

    private static List<WH40KTeamColorDefinition> BuildTeamColorDefinitions(Components.WH40KTeamBattleRuleComponent rule)
    {
        var colors = new List<WH40KTeamColorDefinition>(rule.Teams.Count);
        foreach (var team in rule.Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Id))
                continue;

            colors.Add(new WH40KTeamColorDefinition(team.Id, team.Color.ToHexNoAlpha()));
        }

        return colors;
    }

    private bool TryGetActiveRule(out EntityUid uid, out Components.WH40KTeamBattleRuleComponent component, out GameRuleComponent gameRule)
    {
        if (_activeRuleUid is { } cachedUid &&
            cachedUid.IsValid() &&
            TryComp(cachedUid, out Components.WH40KTeamBattleRuleComponent? cachedRule) &&
            TryComp(cachedUid, out GameRuleComponent? cachedGameRule) &&
            GameTicker.IsGameRuleActive(cachedUid, cachedGameRule))
        {
            uid = cachedUid;
            component = cachedRule;
            gameRule = cachedGameRule;
            return true;
        }

        var query = EntityQueryEnumerator<Components.WH40KTeamBattleRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var foundUid, out var foundComponent, out var foundGameRule))
        {
            if (GameTicker.IsGameRuleActive(foundUid, foundGameRule))
            {
                _activeRuleUid = foundUid;
                uid = foundUid;
                component = foundComponent;
                gameRule = foundGameRule;
                return true;
            }
        }

        _activeRuleUid = null;
        uid = default;
        component = default!;
        gameRule = default!;
        return false;
    }

    public bool AreObjectivesEnabled()
    {
        if (TryGetActiveRule(out _, out var activeRule, out _))
            return activeRule.ObjectivesEnabled;

        // Fallback for early map-init stages where the rule may be added but not yet active.
        var query = EntityQueryEnumerator<Components.WH40KTeamBattleRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var fallbackUid, out var fallbackRule, out var fallbackGameRule))
        {
            if (!GameTicker.IsGameRuleAdded(fallbackUid, fallbackGameRule))
                continue;

            return fallbackRule.ObjectivesEnabled;
        }

        return false;
    }

    public bool TryGetTeamIdFromEntity(EntityUid entity, out string teamId)
    {
        teamId = string.Empty;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        if (!TryGetTeamIndexFromEntity(entity, rule, out var teamIndex))
            return false;

        if (teamIndex < 0 || teamIndex >= rule.Teams.Count)
            return false;

        var team = rule.Teams[teamIndex];
        if (string.IsNullOrEmpty(team.Id))
            return false;

        teamId = team.Id;
        return true;
    }

    public bool TryGetTeamDisplayName(string teamId, out string teamName)
    {
        teamName = string.Empty;

        if (string.IsNullOrEmpty(teamId))
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        var team = rule.Teams.FirstOrDefault(t => t.Id == teamId);
        if (string.IsNullOrEmpty(team?.Id))
            return false;

        // Return the FTL key so callers (especially BUI state builders)
        // can forward it to the client for culture-aware resolution.
        teamName = team!.Name;
        return true;
    }

    public bool TryGetTeamColor(string teamId, out Color teamColor)
    {
        teamColor = Color.White;

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        var team = rule.Teams.FirstOrDefault(t => t.Id == teamId);
        if (string.IsNullOrEmpty(team?.Id))
            return false;

        teamColor = team!.Color;
        return true;
    }

    public bool TryGetTeamDepartments(string teamId, out IReadOnlyList<ProtoId<DepartmentPrototype>> departments)
    {
        departments = Array.Empty<ProtoId<DepartmentPrototype>>();

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        var team = rule.Teams.FirstOrDefault(t => t.Id == teamId);
        if (string.IsNullOrEmpty(team?.Id))
            return false;

        departments = team!.Departments;
        return true;
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

    private void ApplyWh40KStaminaProfile(EntityUid mob)
    {
        if (!TryComp<StaminaComponent>(mob, out var stamina))
            return;

        stamina.SprintMinRemaining = WH40KSprintMinRemaining;
        stamina.SprintDrain = WH40KSprintDrain;
        stamina.WalkRecovery = WH40KWalkRecovery;
        stamina.IdleRecovery = WH40KIdleRecovery;

        // Start fatigue "shaking" only near the low-stamina end of the bar.
        stamina.AnimationThreshold = Math.Clamp(
            stamina.CritThreshold - WH40KFatigueShakeReserve,
            0f,
            MathF.Max(0f, stamina.CritThreshold - 1f));

        Dirty(mob, stamina);
    }

    private void ApplyWh40KStaminaProfileToAllTeamMembers()
    {
        using var scope = _attribution.EnterScope("game_ticking.wh40k_team_battle_rule.stamina_profile");
        var query = EntityQueryEnumerator<WH40KTeamMemberComponent>();
        var hits = 0;
        while (query.MoveNext(out var uid, out _))
        {
            ApplyWh40KStaminaProfile(uid);
            hits++;
        }

        if (hits > 0)
            _sawmill.Debug($"Applied WH40K stamina profile to {hits} team members.");
    }

    public bool TryGetTeamIdForUser(NetUserId userId, out string teamId)
    {
        teamId = string.Empty;

        if (TryGetRememberedTeam(userId, out teamId))
            return true;

        if (!_players.TryGetSessionById(userId, out var session) ||
            session.AttachedEntity is not { Valid: true } attached)
        {
            return false;
        }

        return TryGetTeamIdFromEntity(attached, out teamId);
    }

    public bool TryGetRoundOutcome(out string? winnerTeamId, out bool draw, out bool timeLimitReached)
    {
        if (TryGetActiveRule(out _, out var rule, out _))
        {
            winnerTeamId = rule.WinnerTeamId;
            draw = rule.Draw;
            timeLimitReached = rule.TimeLimitReached;
            return true;
        }

        var query = EntityQueryEnumerator<Components.WH40KTeamBattleRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var fallbackRule, out var gameRule))
        {
            if (!GameTicker.IsGameRuleAdded(uid, gameRule))
                continue;

            winnerTeamId = fallbackRule.WinnerTeamId;
            draw = fallbackRule.Draw;
            timeLimitReached = fallbackRule.TimeLimitReached;
            return true;
        }

        winnerTeamId = null;
        draw = false;
        timeLimitReached = false;
        return false;
    }

    public void HandleObjectiveDestroyed(string destroyedTeamId)
    {
        if (string.IsNullOrEmpty(destroyedTeamId))
            return;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!AllowsObjectiveVictory(rule))
            return;

        if (IsEarlyVictoryLocked(rule))
            return;

        if (rule.RoundEnding)
            return;

        var otherTeams = new List<WH40KTeamDefinition>();
        foreach (var t in rule.Teams)
        {
            if (t.Id != destroyedTeamId)
                otherTeams.Add(t);
        }

        if (otherTeams.Count == 1)
        {
            var winner = otherTeams[0];
            rule.WinnerTeamId = winner.Id;
            rule.Draw = false;
            rule.RoundEnding = true;
            rule.TimeLimitReached = false;

            if (AnnounceWinner)
                RaiseNetworkEvent(new WH40KLocalizedChatEvent
                {
                    LocKey = "wh40k-team-winner-announce",
                    LocArgs = new Dictionary<string, string> { ["team"] = winner.Name },
                    ResolveArgValues = true,
                });

            _roundEnd.EndRound();
            return;
        }

        rule.WinnerTeamId = null;
        rule.Draw = true;
        rule.RoundEnding = true;
        rule.TimeLimitReached = false;

        if (AnnounceWinner)
            RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-team-draw-announce" });

        _roundEnd.EndRound();
    }

    private void BuildDepartmentMap(Components.WH40KTeamBattleRuleComponent component)
    {
        component.DepartmentToTeam.Clear();

        for (var i = 0; i < component.Teams.Count; i++)
        {
            var team = component.Teams[i];
            foreach (var dept in team.Departments)
            {
                if (component.DepartmentToTeam.ContainsKey(dept))
                {
                    _sawmill.Warning($"Department '{dept}' is configured for multiple teams. Using first match.");
                    continue;
                }

                component.DepartmentToTeam[dept] = i;
            }
        }
    }

    private void CheckForVictory(EntityUid uid, Components.WH40KTeamBattleRuleComponent component, GameRuleComponent gameRule)
    {
        ComputeTeamCounts(component, out var total, out var alive);

        var totalAssigned = total.Sum();
        if (totalAssigned == 0)
            return;

        if (component.RequireAllTeamsPresent && total.Any(t => t == 0))
            return;

        var aliveTeams = alive
            .Select((count, index) => (count, index))
            .Where(x => x.count > 0)
            .ToList();

        if (aliveTeams.Count == 1)
        {
            var winnerIndex = aliveTeams[0].index;
            var winner = component.Teams[winnerIndex];
            component.WinnerTeamId = winner.Id;
            component.Draw = false;
            component.RoundEnding = true;

            if (AnnounceWinner)
                RaiseNetworkEvent(new WH40KLocalizedChatEvent
                {
                    LocKey = "wh40k-team-winner-announce",
                    LocArgs = new Dictionary<string, string> { ["team"] = winner.Name },
                    ResolveArgValues = true,
                });

            _roundEnd.EndRound();
            return;
        }

        if (aliveTeams.Count == 0)
        {
            component.WinnerTeamId = null;
            component.Draw = true;
            component.RoundEnding = true;

            if (AnnounceWinner)
                RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-team-draw-announce" });

            _roundEnd.EndRound();
        }
    }

    private void OnKillReported(ref KillReportedEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        var victimTeamIndex = -1;
        if (TryComp<WH40KTeamMemberComponent>(ev.Entity, out var teamMember) &&
            TryGetTeamIndexById(teamMember.TeamId, rule, out var victimTeam))
        {
            EnsureTeamArrays(rule);
            rule.TeamDeaths[victimTeam]++;
            victimTeamIndex = victimTeam;
        }
        else if (_mind.TryGetMind(ev.Entity, out var victimMindId, out _))
        {
            if (TryGetTeamIndex(victimMindId, rule, out var resolvedVictimTeamIndex))
            {
                EnsureTeamArrays(rule);
                rule.TeamDeaths[resolvedVictimTeamIndex]++;
                victimTeamIndex = resolvedVictimTeamIndex;
            }
        }

        if (ev.Suicide)
            return;

        if (ev.Primary is KillPlayerSource killer)
        {
            IncrementPlayerStat(rule.PlayerKills, killer.PlayerId);
            if (TryGetTeamIndex(killer.PlayerId, rule, out var teamIndex))
            {
                EnsureTeamArrays(rule);
                rule.TeamKills[teamIndex]++;

                if (teamIndex != victimTeamIndex &&
                    teamIndex >= 0 &&
                    teamIndex < rule.Teams.Count)
                {
                    var teamId = rule.Teams[teamIndex].Id;
                    var reward = Math.Max(1, rule.FrontPointsPerKill);
                    AddTeamFrontPointsUnscaled(teamId, reward, "kill");
                }
            }
        }

    }

    private void OnDamageChanged(EntityUid uid, DamageableComponent component, DamageChangedEvent args)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        TryRaiseHealingDoneEvent(uid, args, rule);

        if (!_config.GetCVar(CCVars.WH40KFriendlyFireAhelpEnabled))
            return;

        if (!args.DamageIncreased || args.Origin == null)
            return;

        var minDamage = _config.GetCVar(CCVars.WH40KFriendlyFireAhelpMinDamage);
        if (minDamage > 0f && args.DamageDelta != null)
        {
            var totalDamage = 0f;
            foreach (var value in args.DamageDelta.DamageDict.Values)
            {
                if (value <= 0)
                    continue;

                totalDamage += value.Float();
            }

            if (totalDamage < minDamage)
                return;
        }

        ActorComponent? attackerActor = null;
        if (!_attackerResolver.TryResolveAttacker(args.Origin.Value, out var attacker, out var resolvedActor))
            attacker = args.Origin.Value;
        else
            attackerActor = resolvedActor;

        if (attacker == uid)
            return;

        if (HasComp<WH40KFriendlyFireAllowedComponent>(attacker))
            return;

        if (!TryGetTeamIndexFromEntity(uid, rule, out var victimTeam))
            return;

        if (!TryGetTeamIndexFromEntity(attacker, rule, out var attackerTeam))
            return;

        if (victimTeam != attackerTeam)
            return;

        if (attackerActor == null)
            return;

        var attackerId = attackerActor.PlayerSession.UserId;
        var cooldownSeconds = _config.GetCVar(CCVars.WH40KFriendlyFireAhelpCooldownSeconds);
        if (cooldownSeconds > 0f)
        {
            if (rule.NextFriendlyFireAhelpTime.TryGetValue(attackerId, out var nextAllowed) &&
                Timing.CurTime < nextAllowed)
                return;

            rule.NextFriendlyFireAhelpTime[attackerId] = Timing.CurTime + TimeSpan.FromSeconds(cooldownSeconds);
        }

        var text = Loc.GetString("wh40k-ahelp-friendly-fire-warning");
        RaiseNetworkEvent(
            new SharedBwoinkSystem.BwoinkTextMessage(attackerId, SharedBwoinkSystem.SystemUserId, text),
            attackerActor.PlayerSession.Channel);
    }

    private void TryRaiseHealingDoneEvent(EntityUid targetUid, DamageChangedEvent args, Components.WH40KTeamBattleRuleComponent rule)
    {
        if (args.DamageIncreased || args.DamageDelta == null || args.Origin == null)
            return;

        ActorComponent? sourceActor = null;
        if (!_attackerResolver.TryResolveAttacker(args.Origin.Value, out var sourceEntity, out var resolvedActor))
            sourceEntity = args.Origin.Value;
        else
            sourceActor = resolvedActor;

        if (sourceActor == null || sourceEntity == targetUid)
            return;

        if (!TryGetTeamIndexFromEntity(sourceEntity, rule, out var sourceTeam) ||
            !TryGetTeamIndexFromEntity(targetUid, rule, out var targetTeam) ||
            sourceTeam != targetTeam)
        {
            return;
        }

        if (sourceTeam < 0 || sourceTeam >= rule.Teams.Count)
            return;

        if (!_players.TryGetSessionByEntity(targetUid, out var targetSession))
            return;

        var sourceUserId = sourceActor.PlayerSession.UserId;
        if (targetSession.UserId == sourceUserId)
            return;

        var healed = 0.0;
        foreach (var value in args.DamageDelta.DamageDict.Values)
        {
            if (value < 0)
                healed += -value.Double();
        }

        var healedInt = (int) Math.Floor(healed);
        if (healedInt <= 0)
            return;

        var teamId = rule.Teams[sourceTeam].Id;
        RaiseLocalEvent(new WH40KTeamBattleHealingDoneEvent(
            sourceUserId,
            targetSession.UserId,
            teamId,
            healedInt));
    }

    private void ApplyConfigToActiveRules()
    {
        var query = EntityQueryEnumerator<Components.WH40KTeamBattleRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var rule, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            rule.CheckInterval = _checkInterval;
            rule.RequireAllTeamsPresent = _requireAllTeamsPresent;
            ApplyOptionalRoundTimeLimitOverride(rule);
            rule.NextCheck = Timing.CurTime + TimeSpan.FromSeconds(rule.CheckInterval);
        }
    }

    private void ApplyOptionalRoundTimeLimitOverride(Components.WH40KTeamBattleRuleComponent rule)
    {
        if (_roundTimeLimitSeconds <= 0f)
            return;

        rule.RoundTimeLimitSeconds = _roundTimeLimitSeconds;
    }

    private void ResetEconomyTelemetryState(Components.WH40KTeamBattleRuleComponent? component)
    {
        _lastEconomyTelemetrySnapshotAt = TimeSpan.Zero;
        _nextEconomyTelemetrySnapshotAt = TimeSpan.Zero;
        _economySnapshotFrontPoints.Clear();
        _economySnapshotCommandPoints.Clear();

        if (component == null)
            return;

        foreach (var team in component.Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Id))
                continue;

            var front = component.TeamFrontPoints.GetValueOrDefault(team.Id, 0);
            var command = component.TeamCommandPoints.GetValueOrDefault(team.Id, 0);
            _economySnapshotFrontPoints[team.Id] = front;
            _economySnapshotCommandPoints[team.Id] = command;
        }

        _lastEconomyTelemetrySnapshotAt = Timing.CurTime;
        var interval = Math.Max(30f, _economyTelemetrySnapshotIntervalSeconds);
        _nextEconomyTelemetrySnapshotAt = Timing.CurTime + TimeSpan.FromSeconds(interval);
    }

    private void UpdateEconomyTelemetrySnapshots(Components.WH40KTeamBattleRuleComponent component)
    {
        if (!_economyTelemetryTrace)
            return;

        var now = Timing.CurTime;
        if (now < _nextEconomyTelemetrySnapshotAt)
            return;

        var elapsedSeconds = Math.Max(0, (int) (now - component.RoundStartTime).TotalSeconds);
        var windowSeconds = _lastEconomyTelemetrySnapshotAt == TimeSpan.Zero
            ? Math.Max(1f, _economyTelemetrySnapshotIntervalSeconds)
            : Math.Max(1f, (float) (now - _lastEconomyTelemetrySnapshotAt).TotalSeconds);

        foreach (var team in component.Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Id))
                continue;

            var teamId = team.Id;
            var front = component.TeamFrontPoints.GetValueOrDefault(teamId, 0);
            var command = component.TeamCommandPoints.GetValueOrDefault(teamId, 0);
            var level = component.TeamBaseLevels.GetValueOrDefault(teamId, 1);
            var previousFront = _economySnapshotFrontPoints.GetValueOrDefault(teamId, front);
            var previousCommand = _economySnapshotCommandPoints.GetValueOrDefault(teamId, command);
            var deltaFront = front - previousFront;
            var deltaCommand = command - previousCommand;
            var frontPerMinute = deltaFront * 60f / windowSeconds;
            var commandPerMinute = deltaCommand * 60f / windowSeconds;

            _sawmill.Info(
                $"[eco][snapshot] t={FormatClockShort(elapsedSeconds)} phase={component.CurrentPhase} team={teamId} " +
                $"lvl={level} fp={front} cp={command} dFp={deltaFront} dCp={deltaCommand} " +
                $"fpPerMin={frontPerMinute:F1} cpPerMin={commandPerMinute:F1}");

            _economySnapshotFrontPoints[teamId] = front;
            _economySnapshotCommandPoints[teamId] = command;
        }

        _lastEconomyTelemetrySnapshotAt = now;
        var interval = Math.Max(30f, _economyTelemetrySnapshotIntervalSeconds);
        _nextEconomyTelemetrySnapshotAt = now + TimeSpan.FromSeconds(interval);
    }

    private void TraceEconomyDelta(
        Components.WH40KTeamBattleRuleComponent component,
        string teamId,
        string source,
        int frontDelta,
        int commandDelta)
    {
        if (!_economyTelemetryTrace)
            return;

        var front = component.TeamFrontPoints.GetValueOrDefault(teamId, 0);
        var command = component.TeamCommandPoints.GetValueOrDefault(teamId, 0);
        var level = component.TeamBaseLevels.GetValueOrDefault(teamId, 1);
        var elapsedSeconds = Math.Max(0, (int) (Timing.CurTime - component.RoundStartTime).TotalSeconds);
        var burst = Math.Abs(commandDelta) >= Math.Max(1, _economyTelemetryBurstCommandDelta);
        var burstMarker = burst ? " burst=true" : string.Empty;

        _sawmill.Info(
            $"[eco] t={FormatClockShort(elapsedSeconds)} phase={component.CurrentPhase} team={teamId} " +
            $"source={source} dFp={frontDelta} dCp={commandDelta} fp={front} cp={command} lvl={level}{burstMarker}");
    }

    private static string FormatClockShort(int totalSeconds)
    {
        var safeSeconds = Math.Max(0, totalSeconds);
        var minutes = safeSeconds / 60;
        var seconds = safeSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private void ApplyExternalConfigProfile(Components.WH40KTeamBattleRuleComponent component)
    {
        if (component.ConfigProfile is not { } profileId)
            return;

        if (!_proto.TryIndex(profileId, out WH40KTeamBattleConfigPrototype? profile))
        {
            _sawmill.Warning($"WH40K mode config profile '{profileId}' was not found. Using inline/default values.");
            return;
        }

        var points = profile.Points;
        var weather = profile.Weather;
        var eventsCfg = profile.Events;
        var logistics = profile.Logistics;
        var blackFront = profile.BlackFront;
        var orbital = profile.Orbital;
        var economy = profile.Economy;
        var levelBuff = profile.LevelBuff;
        var weatherDangerProfile = component.WeatherDangerProfile;

        if (profile.PointsProfile is { } pointsProfileId)
        {
            if (_proto.TryIndex(pointsProfileId, out WH40KTeamBattlePointsProfilePrototype? pointsProfile))
                points = pointsProfile.Config;
            else
                _sawmill.Warning($"WH40K points profile '{pointsProfileId}' (config '{profileId}') was not found. Using inline section.");
        }

        if (profile.WeatherProfile is { } weatherProfileId)
        {
            if (_proto.TryIndex(weatherProfileId, out WH40KTeamBattleWeatherProfilePrototype? weatherProfile))
                weather = weatherProfile.Config;
            else
                _sawmill.Warning($"WH40K weather profile '{weatherProfileId}' (config '{profileId}') was not found. Using inline section.");
        }

        if (profile.EventsProfile is { } eventsProfileId)
        {
            if (_proto.TryIndex(eventsProfileId, out WH40KTeamBattleRoundEventsProfilePrototype? eventsProfile))
                eventsCfg = eventsProfile.Config;
            else
                _sawmill.Warning($"WH40K events profile '{eventsProfileId}' (config '{profileId}') was not found. Using inline section.");
        }

        if (profile.LogisticsProfile is { } logisticsProfileId)
        {
            if (_proto.TryIndex(logisticsProfileId, out WH40KTeamBattleLogisticsProfilePrototype? logisticsProfile))
                logistics = logisticsProfile.Config;
            else
                _sawmill.Warning($"WH40K logistics profile '{logisticsProfileId}' (config '{profileId}') was not found. Using inline section.");
        }

        if (profile.BlackFrontProfile is { } blackFrontProfileId)
        {
            if (_proto.TryIndex(blackFrontProfileId, out WH40KTeamBattleBlackFrontProfilePrototype? blackFrontProfile))
                blackFront = blackFrontProfile.Config;
            else
                _sawmill.Warning($"WH40K black-front profile '{blackFrontProfileId}' (config '{profileId}') was not found. Using inline section.");
        }

        if (profile.OrbitalProfile is { } orbitalProfileId)
        {
            if (_proto.TryIndex(orbitalProfileId, out WH40KTeamBattleOrbitalProfilePrototype? orbitalProfile))
                orbital = orbitalProfile.Config;
            else
                _sawmill.Warning($"WH40K orbital profile '{orbitalProfileId}' (config '{profileId}') was not found. Using inline section.");
        }

        if (profile.EconomyProfile is { } economyProfileId)
        {
            if (_proto.TryIndex(economyProfileId, out WH40KTeamBattleEconomyProfilePrototype? economyProfile))
                economy = economyProfile.Config;
            else
                _sawmill.Warning($"WH40K economy profile '{economyProfileId}' (config '{profileId}') was not found. Using inline section.");
        }

        if (profile.LevelBuffProfile is { } levelBuffProfileId)
        {
            if (_proto.TryIndex(levelBuffProfileId, out WH40KTeamBattleLevelBuffProfilePrototype? levelBuffProfile))
                levelBuff = levelBuffProfile.Config;
            else
                _sawmill.Warning($"WH40K level-buff profile '{levelBuffProfileId}' (config '{profileId}') was not found. Using inline section.");
        }

        if (profile.WeatherDangerProfile is { } weatherDangerProfileId)
        {
            if (_proto.HasIndex<WH40KWeatherDangerProfilePrototype>(weatherDangerProfileId))
                weatherDangerProfile = weatherDangerProfileId;
            else
                _sawmill.Warning($"WH40K weather-danger profile '{weatherDangerProfileId}' (config '{profileId}') was not found. Using component/default profile.");
        }

        component.TeamStartingPoints = points.TeamStartingPoints;
        component.FrontPointsPerKill = points.FrontPointsPerKill;
        component.BaseLevelThresholds = new List<int>(points.BaseLevelThresholds);
        component.LevelBuffConstructionDoAfterMultiplier = points.LevelBuffConstructionDoAfterMultiplier;
        component.LevelBuffMedicalDoAfterMultiplier = points.LevelBuffMedicalDoAfterMultiplier;
        component.EconomyPreparationMultiplier = Math.Max(1, economy.PreparationMultiplier);
        component.EconomyAssaultMultiplier = Math.Max(1, economy.AssaultMultiplier);
        component.EconomyApocalypseMultiplier = Math.Max(1, economy.ApocalypseMultiplier);
        component.ReinforcementCurveDurationMinSeconds = Math.Max(1f, economy.ReinforcementCurveDurationMinSeconds);
        component.ReinforcementCurveDurationMaxSeconds = Math.Max(
            component.ReinforcementCurveDurationMinSeconds,
            economy.ReinforcementCurveDurationMaxSeconds);
        component.ReinforcementCurveFallbackApocalypseSeconds = Math.Max(0f, economy.ReinforcementCurveFallbackApocalypseSeconds);
        component.ReinforcementCurveBaseMultiplier = Math.Max(0f, economy.ReinforcementCurveBaseMultiplier);
        component.ReinforcementCurveScale = Math.Max(0f, economy.ReinforcementCurveScale);
        component.ReinforcementCurveExponent = Math.Clamp(economy.ReinforcementCurveExponent, 0f, 10f);
        component.LevelBuffPool = SanitizeLevelBuffPool(levelBuff.Pool);

        component.WeatherMinStartDelaySeconds = weather.MinStartDelaySeconds;
        component.WeatherFirstStartJitterSeconds = weather.FirstStartJitterSeconds;
        component.WeatherNoRoundChance = weather.NoRoundChance;
        component.WeatherMinDurationSeconds = weather.MinDurationSeconds;
        component.WeatherMaxDurationSeconds = weather.MaxDurationSeconds;
        component.WeatherGapMinSeconds = weather.GapMinSeconds;
        component.WeatherGapMaxSeconds = weather.GapMaxSeconds;
        component.WeatherRepeatChance = weather.RepeatChance;
        component.WeatherWarningLeadSeconds = weather.WarningLeadSeconds;
        component.WeatherPool = new List<EntProtoId>(weather.Pool);
        component.WeatherDangerProfile = weatherDangerProfile;

        component.RoundEventsEnabled = eventsCfg.Enabled;
        component.RoundEventMinStartDelaySeconds = eventsCfg.MinStartDelaySeconds;
        component.RoundEventFirstStartJitterSeconds = eventsCfg.FirstStartJitterSeconds;
        component.RoundEventNoRoundChance = eventsCfg.NoRoundChance;
        component.RoundEventMinDurationSeconds = eventsCfg.MinDurationSeconds;
        component.RoundEventMaxDurationSeconds = eventsCfg.MaxDurationSeconds;
        component.RoundEventGapMinSeconds = eventsCfg.GapMinSeconds;
        component.RoundEventGapMaxSeconds = eventsCfg.GapMaxSeconds;
        component.RoundEventRepeatChance = eventsCfg.RepeatChance;
        component.RoundEventWarningLeadSeconds = eventsCfg.WarningLeadSeconds;
        component.RoundEventPool = new List<WH40KRoundEventType>(eventsCfg.Pool);

        component.LogisticsAmmoPriceMultiplier = logistics.AmmoPriceMultiplier;
        component.LogisticsAmmoCategories = logistics.AmmoCategories.Count > 0
            ? new List<ProtoId<StoreCategoryPrototype>>(logistics.AmmoCategories)
            : BuildDefaultLogisticsAmmoCategories();
        component.LogisticsCooldownMultiplier = logistics.CooldownMultiplier;
        component.LogisticsConstructionDoAfterMultiplier = logistics.ConstructionDoAfterMultiplier;
        component.LogisticsMedicalDoAfterMultiplier = logistics.MedicalDoAfterMultiplier;

        component.BlackFrontInfluenceMultiplier = blackFront.InfluenceMultiplier;
        component.BlackFrontWeatherId = blackFront.WeatherId;

        component.OrbitalBombardmentDurationSeconds = orbital.BombardmentDurationSeconds;
        component.OrbitalWaveIntervalSeconds = orbital.WaveIntervalSeconds;
        component.OrbitalStrikesPerWaveMin = orbital.StrikesPerWaveMin;
        component.OrbitalStrikesPerWaveMax = orbital.StrikesPerWaveMax;
        component.OrbitalStrikeDelaySeconds = orbital.StrikeDelaySeconds;
        component.OrbitalTargetScatterRadius = orbital.TargetScatterRadius;
        component.OrbitalExplosionIntensity = orbital.ExplosionIntensity;
        component.OrbitalExplosionSlope = orbital.ExplosionSlope;
        component.OrbitalExplosionMaxTileIntensity = orbital.ExplosionMaxTileIntensity;
        component.OrbitalMarkerPrototype = orbital.MarkerPrototype;
    }

    private static List<WH40KTeamBattleLevelBuffPoolEntry> SanitizeLevelBuffPool(
        IReadOnlyCollection<WH40KTeamBattleLevelBuffPoolEntry> source)
    {
        var sanitized = new List<WH40KTeamBattleLevelBuffPoolEntry>(source.Count);
        foreach (var entry in source)
        {
            if (entry.BuffType == WH40KLevelBuffType.None || entry.Weight <= 0)
                continue;

            sanitized.Add(new WH40KTeamBattleLevelBuffPoolEntry
            {
                BuffType = entry.BuffType,
                Weight = entry.Weight
            });
        }

        if (sanitized.Count > 0)
            return sanitized;

        return BuildDefaultLevelBuffPool();
    }

    private static void NormalizeEconomyRuntimeConfig(Components.WH40KTeamBattleRuleComponent component)
    {
        component.EconomyPreparationMultiplier = Math.Max(1, component.EconomyPreparationMultiplier);
        component.EconomyAssaultMultiplier = Math.Max(1, component.EconomyAssaultMultiplier);
        component.EconomyApocalypseMultiplier = Math.Max(1, component.EconomyApocalypseMultiplier);
        component.ReinforcementCurveDurationMinSeconds = Math.Max(1f, component.ReinforcementCurveDurationMinSeconds);
        component.ReinforcementCurveDurationMaxSeconds = Math.Max(
            component.ReinforcementCurveDurationMinSeconds,
            component.ReinforcementCurveDurationMaxSeconds);
        component.ReinforcementCurveFallbackApocalypseSeconds = Math.Max(0f, component.ReinforcementCurveFallbackApocalypseSeconds);
        component.ReinforcementCurveBaseMultiplier = Math.Max(0f, component.ReinforcementCurveBaseMultiplier);
        component.ReinforcementCurveScale = Math.Max(0f, component.ReinforcementCurveScale);
        component.ReinforcementCurveExponent = Math.Clamp(component.ReinforcementCurveExponent, 0f, 10f);
    }

    private void NormalizeWeatherDangerProfile(Components.WH40KTeamBattleRuleComponent component)
    {
        if (_proto.HasIndex<WH40KWeatherDangerProfilePrototype>(component.WeatherDangerProfile))
            return;

        _sawmill.Warning(
            $"WH40K weather-danger profile '{component.WeatherDangerProfile}' was not found. " +
            "Using WH40KWeatherDangerProfileDefault.");
        component.WeatherDangerProfile = "WH40KWeatherDangerProfileDefault";
    }

    private static List<WH40KTeamBattleLevelBuffPoolEntry> BuildDefaultLevelBuffPool()
    {
        return new List<WH40KTeamBattleLevelBuffPoolEntry>
        {
            new() { BuffType = WH40KLevelBuffType.Pulling, Weight = 1 },
            new() { BuffType = WH40KLevelBuffType.Medical, Weight = 1 },
            new() { BuffType = WH40KLevelBuffType.Construction, Weight = 1 }
        };
    }

    private void ComputeTeamCounts(Components.WH40KTeamBattleRuleComponent component, out int[] total, out int[] alive)
    {
        total = new int[component.Teams.Count];
        alive = new int[component.Teams.Count];

        var memberQuery = EntityQueryEnumerator<WH40KTeamMemberComponent>();
        while (memberQuery.MoveNext(out var entity, out var member))
        {
            if (!TryGetTeamIndexById(member.TeamId, component, out var teamIndex))
                continue;

            total[teamIndex]++;

            if (IsConsideredAlive(entity, component))
                alive[teamIndex]++;
        }
    }

    private bool IsConsideredAlive(EntityUid entity, Components.WH40KTeamBattleRuleComponent component)
    {
        if (_mobState.IsAlive(entity))
            return true;

        return CountCriticalAsAlive && _mobState.IsCritical(entity);
    }

    private bool TryGetTeamIndex(EntityUid mindId, Components.WH40KTeamBattleRuleComponent component, out int teamIndex)
    {
        teamIndex = -1;

        if (!_jobs.MindTryGetJobId(mindId, out var jobId) || jobId == null)
            return false;

        if (component.DepartmentToTeam.Count == 0)
            BuildDepartmentMap(component);

        if (_jobs.TryGetPrimaryDepartment(jobId.Value, out var primaryDept))
        {
            var primaryId = new ProtoId<DepartmentPrototype>(primaryDept.ID);
            if (component.DepartmentToTeam.TryGetValue(primaryId, out teamIndex))
                return true;
        }

        if (_jobs.TryGetAllDepartments(jobId.Value, out var departments))
        {
            foreach (var dept in departments)
            {
                var deptId = new ProtoId<DepartmentPrototype>(dept.ID);
                if (component.DepartmentToTeam.TryGetValue(deptId, out teamIndex))
                    return true;
            }
        }

        return false;
    }

    private bool TryGetTeamIndexFromEntity(EntityUid entity, Components.WH40KTeamBattleRuleComponent component, out int teamIndex)
    {
        teamIndex = -1;

        // Admin ghosts should not inherit team restrictions while ghosting.
        if (TryComp<GhostComponent>(entity, out var ghost) && ghost.CanGhostInteract)
            return false;

        if (TryComp<WH40KTeamMemberComponent>(entity, out var member) &&
            TryGetTeamIndexById(member.TeamId, component, out teamIndex))
            return true;

        if (_mind.TryGetMind(entity, out var mindId, out _))
            return TryGetTeamIndex(mindId, component, out teamIndex);

        return false;
    }

    private bool TryGetTeamIndexById(string teamId, Components.WH40KTeamBattleRuleComponent component, out int teamIndex)
    {
        teamIndex = -1;

        for (var i = 0; i < component.Teams.Count; i++)
        {
            if (component.Teams[i].Id != teamId)
                continue;

            teamIndex = i;
            return true;
        }

        return false;
    }

    private void TriggerTimeLimitDraw(Components.WH40KTeamBattleRuleComponent component)
    {
        if (component.RoundEnding)
            return;

        component.WinnerTeamId = null;
        component.Draw = true;
        component.RoundEnding = true;
        component.TimeLimitReached = true;

        if (AnnounceWinner)
            RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-team-time-limit-announce" });

        _roundEnd.EndRound();
    }

    private bool AllowsTeamVictory(Components.WH40KTeamBattleRuleComponent component)
    {
        return component.VictoryCondition == Components.WH40KVictoryCondition.Teams ||
               component.VictoryCondition == Components.WH40KVictoryCondition.Either;
    }

    private bool AllowsObjectiveVictory(Components.WH40KTeamBattleRuleComponent component)
    {
        return component.VictoryCondition == Components.WH40KVictoryCondition.Objectives ||
               component.VictoryCondition == Components.WH40KVictoryCondition.Either;
    }

    private void EnsureTeamArrays(Components.WH40KTeamBattleRuleComponent component)
    {
        if (component.TeamKills.Length != component.Teams.Count)
            component.TeamKills = new int[component.Teams.Count];

        if (component.TeamDeaths.Length != component.Teams.Count)
            component.TeamDeaths = new int[component.Teams.Count];
    }

    private int GetTeamKills(Components.WH40KTeamBattleRuleComponent component, int index)
    {
        if (index < 0 || index >= component.TeamKills.Length)
            return 0;
        return component.TeamKills[index];
    }

    private int GetTeamDeaths(Components.WH40KTeamBattleRuleComponent component, int index)
    {
        if (index < 0 || index >= component.TeamDeaths.Length)
            return 0;
        return component.TeamDeaths[index];
    }

    private bool TryGetTeamIndex(NetUserId userId, Components.WH40KTeamBattleRuleComponent component, out int teamIndex)
    {
        teamIndex = -1;

        if (!_mind.TryGetMind(userId, out var mindId, out _))
            return false;

        return TryGetTeamIndex(mindId!.Value, component, out teamIndex);
    }

    private void IncrementPlayerStat(Dictionary<NetUserId, int> stats, NetUserId playerId)
    {
        if (stats.TryGetValue(playerId, out var value))
            stats[playerId] = value + 1;
        else
            stats[playerId] = 1;
    }

    private void AppendTopKillers(Components.WH40KTeamBattleRuleComponent component, ref RoundEndTextAppendEvent args)
    {
        var top = component.PlayerKills
            .Where(x => x.Value > 0)
            .OrderByDescending(x => x.Value)
            .Take(3)
            .ToList();

        if (top.Count == 0)
            return;

        args.AddLine(Loc.GetString("wh40k-team-round-end-top-header"));

        var place = 1;
        foreach (var (playerId, kills) in top)
        {
            var name = _players.TryGetPlayerData(playerId, out var data)
                ? data.UserName
                : playerId.ToString();

            args.AddLine(Loc.GetString("wh40k-team-round-end-top-entry",
                ("place", place),
                ("name", name),
                ("kills", kills)));
            place++;
        }

        args.AddLine("");
    }

    public WH40KBattlePhase GetCurrentPhase()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return WH40KBattlePhase.Preparation;

        return rule.CurrentPhase;
    }

    public bool TrySetCurrentPhase(WH40KBattlePhase phase)
    {
        if (!TryGetActiveRule(out var uid, out var rule, out _))
            return false;

        ApplyPhase(uid, rule, phase, Timing.CurTime, announce: true);
        return true;
    }

    public IReadOnlyList<string> GetTeamIds()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return Array.Empty<string>();

        var ids = new List<string>(rule.Teams.Count);
        foreach (var team in rule.Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Id))
                continue;

            ids.Add(team.Id);
        }

        return ids;
    }

    public bool IsEarlyVictoryLocked()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        return IsEarlyVictoryLocked(rule);
    }

    public int GetCurrentEconomyMultiplier()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return 1;

        return GetEconomyMultiplier(rule, GetCurrentPhase());
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

    public int GetDynamicReinforcementCost(int baseCost)
    {
        var baseValue = Math.Max(1, baseCost);
        if (!TryGetActiveRule(out _, out var rule, out _))
            return baseValue;

        var fallbackDuration = Math.Max(
            1f,
            rule.PreparationDurationSeconds + rule.AssaultDurationSeconds + rule.ReinforcementCurveFallbackApocalypseSeconds);
        var duration = rule.RoundTimeLimitSeconds > 0f
            ? rule.RoundTimeLimitSeconds
            : fallbackDuration;

        duration = Math.Clamp(duration, rule.ReinforcementCurveDurationMinSeconds, rule.ReinforcementCurveDurationMaxSeconds);
        var elapsed = (float) Math.Max(0.0, (Timing.CurTime - rule.RoundStartTime).TotalSeconds);
        var normalized = Math.Clamp(elapsed / duration, 0f, 1f);

        var curve = rule.ReinforcementCurveExponent <= 0f
            ? 1f
            : MathF.Pow(normalized, rule.ReinforcementCurveExponent);
        var multiplier = rule.ReinforcementCurveBaseMultiplier + rule.ReinforcementCurveScale * curve;
        return Math.Max(1, (int) MathF.Round(baseValue * Math.Max(0.01f, multiplier)));
    }

    public float GetStoreCooldownMultiplier()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return 1f;

        if (rule.ActiveRoundEvent != WH40KRoundEventType.LogisticsSurge)
            return 1f;

        return Math.Clamp(rule.LogisticsCooldownMultiplier, 0.1f, 10f);
    }

    public int GetInfluenceRewardMultiplier()
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return 1;

        if (rule.ActiveRoundEvent != WH40KRoundEventType.BlackFront)
            return 1;

        return Math.Max(1, rule.BlackFrontInfluenceMultiplier);
    }

    public bool TryGetTeamProgress(string teamId, out int level, out int frontPoints, out int? pointsToNextLevel)
    {
        level = 1;
        frontPoints = 0;
        pointsToNextLevel = null;

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId) ||
            !rule.TeamFrontPoints.TryGetValue(resolvedTeamId, out frontPoints))
        {
            return false;
        }

        if (!rule.TeamBaseLevels.TryGetValue(resolvedTeamId, out level))
            level = CalculateTeamLevel(frontPoints, rule.BaseLevelThresholds);

        pointsToNextLevel = GetPointsToNextLevel(frontPoints, rule.BaseLevelThresholds);
        return true;
    }

    public bool TryGetTeamProgressForEntity(EntityUid entity, out string teamId, out int level, out int frontPoints, out int? pointsToNextLevel)
    {
        teamId = string.Empty;
        level = 1;
        frontPoints = 0;
        pointsToNextLevel = null;

        if (!TryGetTeamIdFromEntity(entity, out teamId))
            return false;

        return TryGetTeamProgress(teamId, out level, out frontPoints, out pointsToNextLevel);
    }

    public bool AddTeamFrontPoints(string teamId, int baseAmount, string? source = null)
    {
        return AddTeamFrontPointsInternal(teamId, baseAmount, applyEconomyMultiplier: true, source: source);
    }

    public bool AddTeamFrontPointsUnscaled(string teamId, int amount, string? source = null)
    {
        return AddTeamFrontPointsInternal(teamId, amount, applyEconomyMultiplier: false, source: source);
    }

    private bool AddTeamFrontPointsInternal(
        string teamId,
        int baseAmount,
        bool applyEconomyMultiplier,
        string? source = null)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (baseAmount <= 0)
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);

        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId) ||
            !rule.TeamFrontPoints.ContainsKey(resolvedTeamId))
            return false;

        var oldLevel = rule.TeamBaseLevels.GetValueOrDefault(resolvedTeamId, 1);
        var gained = applyEconomyMultiplier
            ? Math.Max(1, baseAmount * GetEconomyMultiplier(rule, rule.CurrentPhase))
            : Math.Max(1, baseAmount);

        var points = rule.TeamFrontPoints[resolvedTeamId] + gained;

        rule.TeamFrontPoints[resolvedTeamId] = points;
        rule.TeamCommandPoints[resolvedTeamId] = rule.TeamCommandPoints.GetValueOrDefault(resolvedTeamId, 0) + gained;
        TraceEconomyDelta(
            rule,
            resolvedTeamId,
            source ?? (applyEconomyMultiplier ? "front-gain" : "front-gain-unscaled"),
            gained,
            gained);

        var newLevel = CalculateTeamLevel(points, rule.BaseLevelThresholds);
        rule.TeamBaseLevels[resolvedTeamId] = newLevel;

        if (newLevel > oldLevel)
        {
            RollTeamLevelBuff(rule, resolvedTeamId);

            if (TryGetTeamDisplayName(resolvedTeamId, out var teamName))
            {
                RaiseNetworkEvent(new WH40KLocalizedChatEvent
                {
                    LocKey = "wh40k-team-level-up-announce",
                    LocArgs = new Dictionary<string, string>
                    {
                        ["team"] = teamName,
                        ["level"] = newLevel.ToString()
                    },
                    ResolveArgValues = true
                });

                var activeBuff = rule.TeamLevelBuffs.GetValueOrDefault(resolvedTeamId, WH40KLevelBuffType.None);
                RaiseNetworkEvent(new WH40KLocalizedChatEvent
                {
                    LocKey = "wh40k-team-level-buff-announce",
                    LocArgs = new Dictionary<string, string>
                    {
                        ["team"] = teamName,
                        ["buff"] = GetLevelBuffNameKey(activeBuff),
                        ["effect"] = GetLevelBuffEffectKey(activeBuff)
                    },
                    ResolveArgValues = true
                });
            }
        }

        return true;
    }

    public bool TryGetTeamCommandPoints(string teamId, out int points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId))
            return false;

        return rule.TeamCommandPoints.TryGetValue(resolvedTeamId, out points);
    }

    public bool TrySpendTeamCommandPoints(string teamId, int amount, out int remaining, string? source = null)
    {
        remaining = 0;

        if (string.IsNullOrWhiteSpace(teamId) || amount <= 0)
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out var resolvedTeamId) ||
            !rule.TeamCommandPoints.TryGetValue(resolvedTeamId, out var current))
        {
            return false;
        }

        if (current < amount)
            return false;

        current -= amount;
        rule.TeamCommandPoints[resolvedTeamId] = current;
        remaining = current;
        TraceEconomyDelta(
            rule,
            resolvedTeamId,
            source ?? "command-spend",
            0,
            -amount);
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

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out resolvedTeamId))
            return false;

        var current = rule.TeamCommandPoints.GetValueOrDefault(resolvedTeamId, 0);
        commandPoints = Math.Max(0, current + delta);
        rule.TeamCommandPoints[resolvedTeamId] = commandPoints;
        TraceEconomyDelta(
            rule,
            resolvedTeamId,
            source ?? "command-adjust",
            0,
            commandPoints - current);
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

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out resolvedTeamId))
            return false;

        var currentPoints = rule.TeamFrontPoints.GetValueOrDefault(resolvedTeamId, 0);
        frontPoints = Math.Max(0, currentPoints + delta);
        rule.TeamFrontPoints[resolvedTeamId] = frontPoints;
        TraceEconomyDelta(
            rule,
            resolvedTeamId,
            source ?? "front-adjust",
            frontPoints - currentPoints,
            0);

        level = CalculateTeamLevel(frontPoints, rule.BaseLevelThresholds);
        rule.TeamBaseLevels[resolvedTeamId] = level;

        if (level <= 1)
        {
            rule.TeamLevelBuffs[resolvedTeamId] = WH40KLevelBuffType.None;
            ApplyTeamLevelBuffToTeam(rule, resolvedTeamId, WH40KLevelBuffType.None);
        }
        else if (!rule.TeamLevelBuffs.TryGetValue(resolvedTeamId, out var buff) ||
                 buff == WH40KLevelBuffType.None)
        {
            RollTeamLevelBuff(rule, resolvedTeamId);
        }

        return true;
    }

    public bool TrySetTeamBaseLevel(
        string teamId,
        int requestedLevel,
        out string resolvedTeamId,
        out int level,
        out int frontPoints)
    {
        resolvedTeamId = string.Empty;
        level = 1;
        frontPoints = 0;

        if (requestedLevel <= 0)
            return false;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return false;

        EnsureTeamProgress(rule);
        if (!TryResolveTeamId(rule, teamId, out resolvedTeamId))
            return false;

        var maxLevel = Math.Max(1, rule.BaseLevelThresholds.Count + 1);
        level = Math.Clamp(requestedLevel, 1, maxLevel);
        var targetPoints = GetMinimumPointsForLevel(level, rule.BaseLevelThresholds);
        var currentPoints = rule.TeamFrontPoints.GetValueOrDefault(resolvedTeamId, 0);

        if (!TryAdjustTeamFrontPoints(resolvedTeamId, targetPoints - currentPoints, out _, out frontPoints, out level))
            return false;

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

    private static bool TryResolveTeamId(
        Components.WH40KTeamBattleRuleComponent component,
        string teamId,
        out string resolvedTeamId)
    {
        resolvedTeamId = string.Empty;

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        foreach (var team in component.Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Id))
                continue;

            if (!string.Equals(team.Id, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            resolvedTeamId = team.Id;
            return true;
        }

        return false;
    }

    private void EnsureTeamProgress(Components.WH40KTeamBattleRuleComponent component)
    {
        var startingPoints = Math.Max(0, component.TeamStartingPoints);

        foreach (var team in component.Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Id))
                continue;

            component.TeamFrontPoints.TryAdd(team.Id, startingPoints);
            component.TeamCommandPoints.TryAdd(team.Id, startingPoints);
            component.TeamBaseLevels.TryAdd(team.Id, 1);
            component.TeamLevelBuffs.TryAdd(team.Id, WH40KLevelBuffType.None);
        }
    }

    private int GetMinimumPointsForLevel(int level, IReadOnlyList<int> thresholds)
    {
        if (level <= 1 || thresholds.Count == 0)
            return 0;

        var ordered = thresholds.OrderBy(t => t).ToArray();
        var clampedIndex = Math.Clamp(level - 2, 0, ordered.Length - 1);
        return Math.Max(0, ordered[clampedIndex]);
    }

    private int CalculateTeamLevel(int points, IReadOnlyList<int> thresholds)
    {
        var level = 1;
        foreach (var threshold in thresholds.OrderBy(t => t))
        {
            if (points < threshold)
                break;

            level++;
        }

        return level;
    }

    private int? GetPointsToNextLevel(int points, IReadOnlyList<int> thresholds)
    {
        foreach (var threshold in thresholds.OrderBy(t => t))
        {
            if (points < threshold)
                return threshold - points;
        }

        return null;
    }

    private static int GetEconomyMultiplier(Components.WH40KTeamBattleRuleComponent component, WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => Math.Max(1, component.EconomyPreparationMultiplier),
            WH40KBattlePhase.Assault => Math.Max(1, component.EconomyAssaultMultiplier),
            WH40KBattlePhase.Apocalypse => Math.Max(1, component.EconomyApocalypseMultiplier),
            _ => 1
        };
    }

    private bool IsEarlyVictoryLocked(Components.WH40KTeamBattleRuleComponent component)
    {
        if (component.EarlyVictoryLockSeconds <= 0f)
            return false;

        var elapsed = (Timing.CurTime - component.RoundStartTime).TotalSeconds;
        return elapsed < component.EarlyVictoryLockSeconds;
    }

    private void UpdatePhase(EntityUid uid, Components.WH40KTeamBattleRuleComponent component)
    {
        if (component.CurrentPhase == WH40KBattlePhase.Apocalypse)
            return;

        var now = Timing.CurTime;
        if (now < component.NextPhaseChange)
            return;

        switch (component.CurrentPhase)
        {
            case WH40KBattlePhase.Preparation:
                ApplyPhase(uid, component, WH40KBattlePhase.Assault, now, announce: true);
                break;

            case WH40KBattlePhase.Assault:
                ApplyPhase(uid, component, WH40KBattlePhase.Apocalypse, now, announce: true);
                break;
        }
    }

    private void ApplyPhase(
        EntityUid uid,
        Components.WH40KTeamBattleRuleComponent component,
        WH40KBattlePhase phase,
        TimeSpan now,
        bool announce)
    {
        var previous = component.CurrentPhase;
        component.CurrentPhase = phase;

        switch (phase)
        {
            case WH40KBattlePhase.Preparation:
                component.NextPhaseChange = now + TimeSpan.FromSeconds(Math.Max(1f, component.PreparationDurationSeconds));
                break;
            case WH40KBattlePhase.Assault:
                component.NextPhaseChange = now + TimeSpan.FromSeconds(Math.Max(1f, component.AssaultDurationSeconds));
                break;
            default:
                component.NextPhaseChange = TimeSpan.MaxValue;
                break;
        }

        if (previous == phase)
            return;

        RaiseLocalEvent(new WH40KBattlePhaseChangedEvent(uid, previous, phase));

        if (announce)
            AnnouncePhaseChange(phase);
    }

    private void AnnouncePhaseChange(WH40KBattlePhase phase)
    {
        var key = phase switch
        {
            WH40KBattlePhase.Preparation => "wh40k-phase-preparation-announce",
            WH40KBattlePhase.Assault => "wh40k-phase-assault-announce",
            WH40KBattlePhase.Apocalypse => "wh40k-phase-apocalypse-announce",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(key) && Loc.HasString(key))
        {
            RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = key });
            return;
        }

        _chat.DispatchServerAnnouncement($"WH40K phase changed: {phase}.");
    }

    private void InitializeWeatherState(Components.WH40KTeamBattleRuleComponent component)
    {
        component.ActiveWeather = null;
        component.ActiveWeatherEnd = null;
        component.NextWeatherStart = null;
        component.PendingWeather = null;
        component.LastWeatherWarningForStart = null;

        if (component.WeatherPool.Count == 0 || _gameTicker.DefaultMap == MapId.Nullspace)
        {
            component.WeatherSuppressedForRound = true;
            return;
        }

        component.WeatherSuppressedForRound = _random.Prob(Math.Clamp(component.WeatherNoRoundChance, 0f, 1f));
        if (component.WeatherSuppressedForRound)
            return;

        var jitter = Math.Max(0f, component.WeatherFirstStartJitterSeconds);
        var extraDelay = jitter > 0f ? _random.NextFloat(0f, jitter) : 0f;
        var delay = Math.Max(0f, component.WeatherMinStartDelaySeconds) + extraDelay;

        component.NextWeatherStart = component.RoundStartTime + TimeSpan.FromSeconds(delay);
        component.PendingWeather = PickWeather(component);
    }

    private void UpdateWeather(Components.WH40KTeamBattleRuleComponent component)
    {
        if (component.ActiveRoundEvent == WH40KRoundEventType.BlackFront)
            return;

        if (component.WeatherSuppressedForRound || _gameTicker.DefaultMap == MapId.Nullspace)
            return;

        var now = Timing.CurTime;

        if (component.ActiveWeatherEnd is { } activeEnd)
        {
            if (now < activeEnd + SharedWeatherSystem.ShutdownTime)
                return;

            component.ActiveWeather = null;
            component.ActiveWeatherEnd = null;
            ScheduleNextWeather(component, now);
            return;
        }

        if (component.NextWeatherStart is { } warningStart)
            TryAnnounceWeatherWarning(component, warningStart, now);

        if (component.NextWeatherStart is not { } nextStart || now < nextStart)
            return;

        StartWeatherEvent(component, now);
    }

    private void ScheduleNextWeather(Components.WH40KTeamBattleRuleComponent component, TimeSpan now)
    {
        if (!_random.Prob(Math.Clamp(component.WeatherRepeatChance, 0f, 1f)))
        {
            component.WeatherSuppressedForRound = true;
            component.NextWeatherStart = null;
            component.PendingWeather = null;
            component.LastWeatherWarningForStart = null;
            return;
        }

        var minGap = Math.Max(0f, component.WeatherGapMinSeconds);
        var maxGap = Math.Max(minGap, component.WeatherGapMaxSeconds);
        var gap = _random.NextFloat(minGap, maxGap);

        component.NextWeatherStart = now + TimeSpan.FromSeconds(gap);
        component.PendingWeather = PickWeather(component);
        component.LastWeatherWarningForStart = null;
    }

    private void StartWeatherEvent(Components.WH40KTeamBattleRuleComponent component, TimeSpan now)
    {
        var weatherId = component.PendingWeather is { } pending &&
                        _weather.IsWeatherPrototype(pending)
            ? pending
            : PickWeather(component);

        if (weatherId == null)
        {
            component.WeatherSuppressedForRound = true;
            component.NextWeatherStart = null;
            component.PendingWeather = null;
            component.LastWeatherWarningForStart = null;
            return;
        }

        var minDuration = Math.Max(1f, component.WeatherMinDurationSeconds);
        var maxDuration = Math.Max(minDuration, component.WeatherMaxDurationSeconds);
        var duration = _random.NextFloat(minDuration, maxDuration);

        var endTime = now + TimeSpan.FromSeconds(duration);
        if (!_weather.TrySetWeather(_gameTicker.DefaultMap, weatherId.Value, out _, endTime - now))
        {
            _sawmill.Warning($"Failed to start WH40K weather '{weatherId.Value}' on map {_gameTicker.DefaultMap}.");
            component.ActiveWeather = null;
            component.ActiveWeatherEnd = null;
            component.PendingWeather = weatherId.Value;
            component.LastWeatherWarningForStart = null;
            ScheduleNextWeather(component, now);
            return;
        }

        component.ActiveWeather = weatherId.Value;
        component.ActiveWeatherEnd = endTime;
        component.NextWeatherStart = null;
        component.PendingWeather = null;
        component.LastWeatherWarningForStart = null;

        var weatherKey = weatherId.ToString() ?? "Unknown";
        RaiseNetworkEvent(new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-weather-start-announce",
            LocArgs = new Dictionary<string, string>
            {
                ["weather"] = GetWeatherDisplayNameKey(weatherKey),
                ["danger"] = GetWeatherDangerKey(GetWeatherDanger(component, weatherKey)),
                ["summary"] = GetWeatherSummaryKey(weatherKey),
                ["protection"] = GetWeatherProtectionAdviceKey(weatherKey)
            },
            ResolveArgValues = true
        });
    }

    private EntProtoId? PickWeather(Components.WH40KTeamBattleRuleComponent component)
    {
        var available = component.WeatherPool
            .Where(_weather.IsWeatherPrototype)
            .ToArray();

        if (available.Length == 0)
            return null;

        return available[_random.Next(available.Length)];
    }

    private void TryAnnounceWeatherWarning(
        Components.WH40KTeamBattleRuleComponent component,
        TimeSpan nextStart,
        TimeSpan now)
    {
        if (component.LastWeatherWarningForStart == nextStart)
            return;

        var leadSeconds = Math.Max(1f, component.WeatherWarningLeadSeconds);
        var warningTime = nextStart - TimeSpan.FromSeconds(leadSeconds);
        if (now < warningTime)
            return;

        component.LastWeatherWarningForStart = nextStart;
        var weatherId = component.PendingWeather?.ToString() ?? "Unknown";
        var danger = GetWeatherDanger(component, weatherId);
        var warningSeconds = Math.Max(1, (int) Math.Ceiling((nextStart - now).TotalSeconds));

        RaiseNetworkEvent(new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-weather-warning-announce",
            LocArgs = new Dictionary<string, string>
            {
                ["seconds"] = warningSeconds.ToString(),
                ["weather"] = GetWeatherDisplayNameKey(weatherId),
                ["danger"] = GetWeatherDangerKey(danger),
                ["summary"] = GetWeatherSummaryKey(weatherId),
                ["protection"] = GetWeatherProtectionAdviceKey(weatherId)
            },
            ResolveArgValues = true
        });
    }

    private void InitializeRoundEventState(Components.WH40KTeamBattleRuleComponent component)
    {
        component.ActiveRoundEvent = WH40KRoundEventType.None;
        component.ActiveRoundEventEnd = null;
        component.PendingRoundEvent = null;
        component.NextRoundEventStart = null;
        component.LastRoundEventWarningForStart = null;
        component.NextOrbitalWaveAt = TimeSpan.Zero;
        component.PendingOrbitalStrikes.Clear();

        if (!component.RoundEventsEnabled || component.RoundEventPool.Count == 0)
        {
            component.RoundEventsSuppressedForRound = true;
            return;
        }

        component.RoundEventsSuppressedForRound = _random.Prob(Math.Clamp(component.RoundEventNoRoundChance, 0f, 1f));
        if (component.RoundEventsSuppressedForRound)
            return;

        var jitter = Math.Max(0f, component.RoundEventFirstStartJitterSeconds);
        var extraDelay = jitter > 0f ? _random.NextFloat(0f, jitter) : 0f;
        var delay = Math.Max(0f, component.RoundEventMinStartDelaySeconds) + extraDelay;

        component.NextRoundEventStart = component.RoundStartTime + TimeSpan.FromSeconds(delay);
        component.PendingRoundEvent = PickRoundEvent(component);
        if (component.PendingRoundEvent == null)
            component.RoundEventsSuppressedForRound = true;
    }

    private void UpdateRoundEvents(Components.WH40KTeamBattleRuleComponent component)
    {
        var now = Timing.CurTime;
        UpdatePendingOrbitalStrikes(component, now);

        if (!component.RoundEventsEnabled || component.RoundEventsSuppressedForRound)
            return;

        if (component.ActiveRoundEvent != WH40KRoundEventType.None)
        {
            if (component.ActiveRoundEvent == WH40KRoundEventType.OrbitalBombardment &&
                now >= component.NextOrbitalWaveAt)
            {
                SpawnOrbitalWave(component, now);
                component.NextOrbitalWaveAt = now + TimeSpan.FromSeconds(Math.Max(1f, component.OrbitalWaveIntervalSeconds));
            }

            if (component.ActiveRoundEventEnd is { } end && now >= end)
                EndRoundEvent(component, now);

            return;
        }

        if (component.NextRoundEventStart is { } warningStart)
            TryAnnounceRoundEventWarning(component, warningStart, now);

        if (component.NextRoundEventStart is not { } nextStart || now < nextStart)
            return;

        StartRoundEvent(component, now);
    }

    private void ScheduleNextRoundEvent(Components.WH40KTeamBattleRuleComponent component, TimeSpan now)
    {
        if (!_random.Prob(Math.Clamp(component.RoundEventRepeatChance, 0f, 1f)))
        {
            component.RoundEventsSuppressedForRound = true;
            component.NextRoundEventStart = null;
            component.PendingRoundEvent = null;
            component.LastRoundEventWarningForStart = null;
            return;
        }

        var minGap = Math.Max(1f, component.RoundEventGapMinSeconds);
        var maxGap = Math.Max(minGap, component.RoundEventGapMaxSeconds);
        var gap = _random.NextFloat(minGap, maxGap);

        component.NextRoundEventStart = now + TimeSpan.FromSeconds(gap);
        component.PendingRoundEvent = PickRoundEvent(component);
        component.LastRoundEventWarningForStart = null;
        if (component.PendingRoundEvent == null)
            component.RoundEventsSuppressedForRound = true;
    }

    private WH40KRoundEventType? PickRoundEvent(Components.WH40KTeamBattleRuleComponent component)
    {
        var available = component.RoundEventPool
            .Where(e => e != WH40KRoundEventType.None)
            .ToArray();

        if (available.Length == 0)
            return null;

        return available[_random.Next(available.Length)];
    }

    private void StartRoundEvent(Components.WH40KTeamBattleRuleComponent component, TimeSpan now)
    {
        var eventType = component.PendingRoundEvent ?? PickRoundEvent(component);
        if (eventType == null || eventType == WH40KRoundEventType.None)
        {
            component.RoundEventsSuppressedForRound = true;
            component.PendingRoundEvent = null;
            component.NextRoundEventStart = null;
            return;
        }

        component.ActiveRoundEvent = eventType.Value;
        component.PendingRoundEvent = null;
        component.NextRoundEventStart = null;
        component.LastRoundEventWarningForStart = null;

        var minDuration = Math.Max(1f, component.RoundEventMinDurationSeconds);
        var maxDuration = Math.Max(minDuration, component.RoundEventMaxDurationSeconds);
        var durationSeconds = _random.NextFloat(minDuration, maxDuration);

        switch (component.ActiveRoundEvent)
        {
            case WH40KRoundEventType.LogisticsSurge:
                ApplyAmmoDiscountToWh40KStores(component, true);
                RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-round-event-logistics-start" });
                break;

            case WH40KRoundEventType.OrbitalBombardment:
                durationSeconds = Math.Max(10f, component.OrbitalBombardmentDurationSeconds);
                component.NextOrbitalWaveAt = now;
                RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-round-event-orbital-start" });
                break;

            case WH40KRoundEventType.BlackFront:
                StartBlackFrontWeather(component, now, TimeSpan.FromSeconds(durationSeconds));
                RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-round-event-blackfront-start" });
                break;
        }

        component.ActiveRoundEventEnd = now + TimeSpan.FromSeconds(durationSeconds);
    }

    private void EndRoundEvent(
        Components.WH40KTeamBattleRuleComponent component,
        TimeSpan now,
        bool forceCleanup = false)
    {
        var finishedEvent = component.ActiveRoundEvent;
        if (finishedEvent == WH40KRoundEventType.None && !forceCleanup)
            return;

        switch (finishedEvent)
        {
            case WH40KRoundEventType.LogisticsSurge:
                ApplyAmmoDiscountToWh40KStores(component, false);
                if (!forceCleanup)
                    RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-round-event-logistics-end" });
                break;

            case WH40KRoundEventType.OrbitalBombardment:
                if (!forceCleanup)
                    RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-round-event-orbital-end" });
                break;

            case WH40KRoundEventType.BlackFront:
                StopBlackFrontWeather(component, now);
                if (!forceCleanup)
                    RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = "wh40k-round-event-blackfront-end" });
                break;
        }

        component.ActiveRoundEvent = WH40KRoundEventType.None;
        component.ActiveRoundEventEnd = null;
        component.NextOrbitalWaveAt = TimeSpan.Zero;

        if (forceCleanup)
        {
            component.RoundEventsSuppressedForRound = true;
            component.PendingRoundEvent = null;
            component.NextRoundEventStart = null;
            component.LastRoundEventWarningForStart = null;
            return;
        }

        ScheduleNextRoundEvent(component, now);
    }

    private void TryAnnounceRoundEventWarning(
        Components.WH40KTeamBattleRuleComponent component,
        TimeSpan nextStart,
        TimeSpan now)
    {
        if (component.LastRoundEventWarningForStart == nextStart)
            return;

        var leadSeconds = Math.Max(1f, component.RoundEventWarningLeadSeconds);
        var warningTime = nextStart - TimeSpan.FromSeconds(leadSeconds);
        if (now < warningTime)
            return;

        component.LastRoundEventWarningForStart = nextStart;
        if (component.PendingRoundEvent is not { } pending)
            return;

        RaiseNetworkEvent(new WH40KLocalizedChatEvent
        {
            LocKey = "wh40k-round-event-warning",
            LocArgs = new Dictionary<string, string>
            {
                ["seconds"] = ((int) leadSeconds).ToString(),
                ["event"] = GetRoundEventNameKey(pending)
            },
            ResolveArgValues = true
        });
    }

    private void SpawnOrbitalWave(Components.WH40KTeamBattleRuleComponent component, TimeSpan now)
    {
        var minStrikes = Math.Max(1, component.OrbitalStrikesPerWaveMin);
        var maxStrikes = Math.Max(minStrikes, component.OrbitalStrikesPerWaveMax);
        var strikes = _random.Next(minStrikes, maxStrikes + 1);
        var delay = Math.Max(0.2f, component.OrbitalStrikeDelaySeconds);
        var detonateAt = now + TimeSpan.FromSeconds(delay);

        for (var i = 0; i < strikes; i++)
        {
            if (!TryPickOrbitalTarget(component, out var target))
                break;

            if (_proto.HasIndex(component.OrbitalMarkerPrototype))
                Spawn(component.OrbitalMarkerPrototype, target);

            component.PendingOrbitalStrikes.Add(new WH40KPendingOrbitalStrike(target, detonateAt));
        }
    }

    private bool TryPickOrbitalTarget(Components.WH40KTeamBattleRuleComponent component, out MapCoordinates target)
    {
        target = default;
        var mapId = _gameTicker.DefaultMap;
        if (mapId == MapId.Nullspace)
            return false;

        var candidates = new List<MapCoordinates>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        var points = EntityQueryEnumerator<WH40KInfluencePointComponent, TransformComponent>();
        while (points.MoveNext(out _, out _, out var pointXform))
        {
            if (pointXform.MapID != mapId)
                continue;

            var worldPos = _transform.GetWorldPosition(pointXform, xformQuery);
            candidates.Add(new MapCoordinates(worldPos, pointXform.MapID));
        }

        if (candidates.Count == 0)
        {
            var members = EntityQueryEnumerator<WH40KTeamMemberComponent, TransformComponent>();
            while (members.MoveNext(out var memberUid, out _, out var memberXform))
            {
                if (memberXform.MapID != mapId || !_mobState.IsAlive(memberUid))
                    continue;

                var worldPos = _transform.GetWorldPosition(memberXform, xformQuery);
                candidates.Add(new MapCoordinates(worldPos, memberXform.MapID));
            }
        }

        if (candidates.Count == 0)
            return false;

        var scatter = Math.Max(0f, component.OrbitalTargetScatterRadius);
        const int maxAttempts = 32;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var center = candidates[_random.Next(candidates.Count)];
            var offset = scatter > 0f ? _random.NextVector2(0f, scatter) : System.Numerics.Vector2.Zero;
            var candidate = new MapCoordinates(center.Position + offset, center.MapId);
            if (!IsOrbitalTargetAllowed(candidate))
                continue;

            target = candidate;
            return true;
        }

        return false;
    }

    private bool IsOrbitalTargetAllowed(MapCoordinates candidate)
    {
        if (candidate.MapId == MapId.Nullspace)
            return false;

        if (!_mapManager.TryFindGridAt(candidate, out var gridUid, out var grid))
            return false;

        var tileIndices = _map.WorldToTile(gridUid, grid, candidate.Position);
        if (!_map.TryGetTileRef(gridUid, grid, tileIndices, out var tileRef))
            return false;

        if (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
            return false;

        return !IsRoovedTile(gridUid, grid, tileIndices);
    }

    private bool IsRoovedTile(EntityUid gridUid, MapGridComponent grid, Vector2i tileIndices)
    {
        if (HasComp<ImplicitRoofComponent>(gridUid))
            return true;

        if (!TryComp<RoofComponent>(gridUid, out var roofComp))
            return false;

        return _roof.IsRooved((gridUid, grid, roofComp), tileIndices);
    }

    private void UpdatePendingOrbitalStrikes(Components.WH40KTeamBattleRuleComponent component, TimeSpan now)
    {
        if (component.PendingOrbitalStrikes.Count == 0)
            return;

        for (var i = component.PendingOrbitalStrikes.Count - 1; i >= 0; i--)
        {
            var strike = component.PendingOrbitalStrikes[i];
            if (now < strike.DetonateAt)
                continue;

            _explosion.QueueExplosion(
                strike.Target,
                ExplosionSystem.DefaultExplosionPrototypeId,
                Math.Max(1f, component.OrbitalExplosionIntensity),
                Math.Max(0.1f, component.OrbitalExplosionSlope),
                Math.Max(0.5f, component.OrbitalExplosionMaxTileIntensity),
                null,
                canCreateVacuum: false,
                addLog: false);

            component.PendingOrbitalStrikes.RemoveAt(i);
        }
    }

    private void RollTeamLevelBuff(Components.WH40KTeamBattleRuleComponent component, string teamId)
    {
        var selected = PickRandomLevelBuffType(component);
        component.TeamLevelBuffs[teamId] = selected;
        ApplyTeamLevelBuffToTeam(component, teamId, selected);
    }

    private void ApplyTeamLevelBuffToTeam(
        Components.WH40KTeamBattleRuleComponent component,
        string teamId,
        WH40KLevelBuffType buffType)
    {
        using var scope = _attribution.EnterScope("game_ticking.wh40k_team_battle_rule.level_buff");
        var query = EntityQueryEnumerator<WH40KTeamMemberComponent>();
        var hits = 0;
        while (query.MoveNext(out var uid, out var member))
        {
            if (!string.Equals(member.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            ApplyTeamLevelBuffToEntity(uid, component, buffType);
            hits++;
        }

        if (hits > 0)
            _sawmill.Info($"Applied {buffType} to team {teamId} (hits={hits})");
    }

    private void ApplyTeamLevelBuffToEntity(
        EntityUid uid,
        Components.WH40KTeamBattleRuleComponent component,
        WH40KLevelBuffType buffType)
    {
        if (buffType == WH40KLevelBuffType.None)
        {
            if (HasComp<WH40KRoundEventBuffComponent>(uid))
            {
                RemComp<WH40KRoundEventBuffComponent>(uid);
                _movement.RefreshMovementSpeedModifiers(uid);
            }

            return;
        }

        var buff = EnsureComp<WH40KRoundEventBuffComponent>(uid);
        buff.IgnorePullSlowdown = buffType == WH40KLevelBuffType.Pulling;
        buff.MedicalDelayMultiplier = buffType == WH40KLevelBuffType.Medical
            ? Math.Clamp(component.LevelBuffMedicalDoAfterMultiplier, 0.1f, 5f)
            : 1f;
        buff.ConstructionDelayMultiplier = buffType == WH40KLevelBuffType.Construction
            ? Math.Clamp(component.LevelBuffConstructionDoAfterMultiplier, 0.1f, 5f)
            : 1f;

        Dirty(uid, buff);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private WH40KLevelBuffType PickRandomLevelBuffType(Components.WH40KTeamBattleRuleComponent component)
    {
        var pool = component.LevelBuffPool.Count > 0
            ? component.LevelBuffPool
            : BuildDefaultLevelBuffPool();

        var totalWeight = 0;
        foreach (var entry in pool)
        {
            if (entry.BuffType == WH40KLevelBuffType.None || entry.Weight <= 0)
                continue;

            totalWeight += entry.Weight;
        }

        if (totalWeight <= 0)
            return WH40KLevelBuffType.Pulling;

        var roll = _random.Next(totalWeight);
        var running = 0;
        foreach (var entry in pool)
        {
            if (entry.BuffType == WH40KLevelBuffType.None || entry.Weight <= 0)
                continue;

            running += entry.Weight;
            if (roll < running)
                return entry.BuffType;
        }

        return WH40KLevelBuffType.Pulling;
    }

    private void ClearAllTeamLevelBuffComponents()
    {
        using var scope = _attribution.EnterScope("game_ticking.wh40k_team_battle_rule.clear_buffs");
        var query = EntityQueryEnumerator<WH40KRoundEventBuffComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemComp<WH40KRoundEventBuffComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }
    }

    private void ApplyAmmoDiscountToWh40KStores(
        Components.WH40KTeamBattleRuleComponent component,
        bool enabled)
    {
        const string sourceId = "wh40k-logistics-ammo-discount";
        var priceMultiplier = Math.Clamp(component.LogisticsAmmoPriceMultiplier, 0.1f, 1f);
        var ammoCategories = component.LogisticsAmmoCategories.Count > 0
            ? component.LogisticsAmmoCategories
            : BuildDefaultLogisticsAmmoCategories();
        var ammoCategorySet = new HashSet<ProtoId<StoreCategoryPrototype>>(ammoCategories);

        var query = EntityQueryEnumerator<StoreComponent, WH40KStoreTeamComponent>();
        while (query.MoveNext(out var storeUid, out var store, out _))
        {
            var changed = false;
            foreach (var listing in store.FullListingsCatalog)
            {
                var hadModifier = listing.CostModifiersBySourceId.ContainsKey(sourceId);
                listing.RemoveCostModifier(sourceId);
                if (hadModifier)
                    changed = true;

                if (!enabled || priceMultiplier >= 0.999f || !IsAmmoListing(listing, ammoCategorySet))
                    continue;

                var modifier = BuildPriceModifier(listing.OriginalCost, priceMultiplier);
                if (modifier.Count == 0)
                    continue;

                listing.AddCostModifier(sourceId, modifier);
                changed = true;
            }

            if (!changed)
                continue;

            _store.UpdateUserInterface(store.AccountOwner, storeUid, store);
        }
    }

    private static bool IsAmmoListing(
        ListingData listing,
        HashSet<ProtoId<StoreCategoryPrototype>> ammoCategorySet)
    {
        if (ammoCategorySet.Count == 0)
            return false;

        foreach (var category in listing.Categories)
        {
            if (ammoCategorySet.Contains(category))
                return true;
        }

        return false;
    }

    private static List<ProtoId<StoreCategoryPrototype>> BuildDefaultLogisticsAmmoCategories()
    {
        return new List<ProtoId<StoreCategoryPrototype>>
        {
            "VoxAmmo",
            "AltarAmmo"
        };
    }

    private static Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> BuildPriceModifier(
        IReadOnlyDictionary<ProtoId<CurrencyPrototype>, FixedPoint2> originalCost,
        float multiplier)
    {
        var result = new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>();
        foreach (var (currency, amount) in originalCost)
        {
            var adjusted = amount * multiplier;
            var delta = adjusted - amount;
            if (delta == FixedPoint2.Zero)
                continue;

            result[currency] = delta;
        }

        return result;
    }

    private void StartBlackFrontWeather(
        Components.WH40KTeamBattleRuleComponent component,
        TimeSpan now,
        TimeSpan duration)
    {
        if (_gameTicker.DefaultMap == MapId.Nullspace ||
            !_weather.IsWeatherPrototype(component.BlackFrontWeatherId))
        {
            return;
        }

        var end = now + duration;
        if (!_weather.TrySetWeather(_gameTicker.DefaultMap, component.BlackFrontWeatherId, out _, end - now))
            _sawmill.Warning($"Failed to start WH40K black front weather '{component.BlackFrontWeatherId}' on map {_gameTicker.DefaultMap}.");
    }

    private void StopBlackFrontWeather(Components.WH40KTeamBattleRuleComponent component, TimeSpan now)
    {
        if (_gameTicker.DefaultMap != MapId.Nullspace)
            _weather.TrySetWeather(_gameTicker.DefaultMap, null, out _);

        if (!component.WeatherSuppressedForRound &&
            component.NextWeatherStart is { } nextStart &&
            nextStart <= now)
        {
            var minGap = Math.Max(30f, component.WeatherGapMinSeconds * 0.5f);
            var maxGap = Math.Max(minGap, component.WeatherGapMaxSeconds);
            var gap = _random.NextFloat(minGap, maxGap);
            component.NextWeatherStart = now + TimeSpan.FromSeconds(gap);
            component.PendingWeather ??= PickWeather(component);
            component.LastWeatherWarningForStart = null;
        }
    }

    private static string GetRoundEventNameKey(WH40KRoundEventType type)
    {
        return type switch
        {
            WH40KRoundEventType.LogisticsSurge => "wh40k-round-event-name-logistics",
            WH40KRoundEventType.OrbitalBombardment => "wh40k-round-event-name-orbital",
            WH40KRoundEventType.BlackFront => "wh40k-round-event-name-blackfront",
            _ => "wh40k-round-event-name-unknown"
        };
    }

    private static string GetLevelBuffNameKey(WH40KLevelBuffType buffType)
    {
        return buffType switch
        {
            WH40KLevelBuffType.Pulling => "wh40k-team-level-buff-name-pulling",
            WH40KLevelBuffType.Medical => "wh40k-team-level-buff-name-medical",
            WH40KLevelBuffType.Construction => "wh40k-team-level-buff-name-construction",
            _ => "wh40k-team-level-buff-name-none"
        };
    }

    private static string GetLevelBuffEffectKey(WH40KLevelBuffType buffType)
    {
        return buffType switch
        {
            WH40KLevelBuffType.Pulling => "wh40k-team-level-buff-effect-pulling",
            WH40KLevelBuffType.Medical => "wh40k-team-level-buff-effect-medical",
            WH40KLevelBuffType.Construction => "wh40k-team-level-buff-effect-construction",
            _ => "wh40k-team-level-buff-effect-none"
        };
    }

    private int GetWeatherDanger(Components.WH40KTeamBattleRuleComponent component, string weatherId)
    {
        if (!_proto.TryIndex(component.WeatherDangerProfile, out WH40KWeatherDangerProfilePrototype? profile))
            return 3;

        foreach (var entry in profile.WeatherDanger)
        {
            if (!string.Equals(entry.WeatherId.ToString(), weatherId, StringComparison.OrdinalIgnoreCase))
                continue;

            return Math.Clamp(entry.Danger, 1, 4);
        }

        return Math.Clamp(profile.DefaultDanger, 1, 4);
    }

    private static string GetWeatherDangerKey(int danger)
    {
        return danger switch
        {
            1 => "wh40k-weather-danger-low",
            2 => "wh40k-weather-danger-medium",
            3 => "wh40k-weather-danger-high",
            _ => "wh40k-weather-danger-extreme"
        };
    }

    private string GetWeatherDisplayName(string weatherId)
    {
        return GetWeatherLocString("wh40k-weather-name", weatherId, "wh40k-weather-name-unknown");
    }

    private string GetWeatherSummary(string weatherId)
    {
        return GetWeatherLocString("wh40k-weather-summary", weatherId, "wh40k-weather-summary-unknown");
    }

    private string GetWeatherProtectionAdvice(string weatherId)
    {
        if (TryGetWeatherLocString("wh40k-weather-protection", weatherId) is { } overrideText)
            return overrideText;

        if (!_weather.TryGetWeatherPrototype(weatherId, out var weather) ||
            weather.Effects is not { } effects)
        {
            return Loc.GetString("wh40k-weather-protection-generic");
        }

        if (effects.ProtectedByGasMask && effects.ProtectedByHardsuit)
            return Loc.GetString("wh40k-weather-protection-gasmask-or-hardsuit");

        if (effects.ProtectedByHardsuit)
            return Loc.GetString("wh40k-weather-protection-hardsuit");

        if (effects.ProtectedByGasMask)
            return Loc.GetString("wh40k-weather-protection-gasmask");

        if (effects.Emp != null)
            return Loc.GetString("wh40k-weather-protection-emp");

        if (effects.StructureDamage != null)
            return Loc.GetString("wh40k-weather-protection-structures");

        if (effects.Wind != null || effects.Slowdown != null || effects.HazardSpawn != null)
            return Loc.GetString("wh40k-weather-protection-cover");

        return Loc.GetString("wh40k-weather-protection-generic");
    }

    private string GetWeatherLocString(string prefix, string weatherId, string fallbackKey)
    {
        return TryGetWeatherLocString(prefix, weatherId) ?? Loc.GetString(fallbackKey);
    }

    private string? TryGetWeatherLocString(string prefix, string weatherId)
    {
        var key = $"{prefix}-{weatherId}";
        return Loc.TryGetString(key, out var localized) && !string.IsNullOrWhiteSpace(localized)
            ? localized
            : null;
    }

    private string GetWeatherLocKey(string prefix, string weatherId, string fallbackKey)
    {
        var key = $"{prefix}-{weatherId}";
        return Loc.HasString(key) ? key : fallbackKey;
    }

    private string GetWeatherDisplayNameKey(string weatherId)
    {
        return GetWeatherLocKey("wh40k-weather-name", weatherId, "wh40k-weather-name-unknown");
    }

    private string GetWeatherSummaryKey(string weatherId)
    {
        return GetWeatherLocKey("wh40k-weather-summary", weatherId, "wh40k-weather-summary-unknown");
    }

    private string GetWeatherProtectionAdviceKey(string weatherId)
    {
        var overrideKey = $"wh40k-weather-protection-{weatherId}";
        if (Loc.HasString(overrideKey))
            return overrideKey;

        if (!_weather.TryGetWeatherPrototype(weatherId, out var weather) ||
            weather.Effects is not { } effects)
            return "wh40k-weather-protection-generic";

        if (effects.ProtectedByGasMask && effects.ProtectedByHardsuit)
            return "wh40k-weather-protection-gasmask-or-hardsuit";

        if (effects.ProtectedByHardsuit)
            return "wh40k-weather-protection-hardsuit";

        if (effects.ProtectedByGasMask)
            return "wh40k-weather-protection-gasmask";

        if (effects.Emp != null)
            return "wh40k-weather-protection-emp";

        if (effects.StructureDamage != null)
            return "wh40k-weather-protection-structures";

        if (effects.Wind != null || effects.Slowdown != null || effects.HazardSpawn != null)
            return "wh40k-weather-protection-cover";

        return "wh40k-weather-protection-generic";
    }

    private void ApplyMapStabilitySafeguards()
    {
        var mapId = _gameTicker.DefaultMap;
        if (mapId == MapId.Nullspace)
        {
            _sawmill.Warning("WH40K map stability safeguards skipped: default map is nullspace.");
            return;
        }

        var stationGrids = new HashSet<EntityUid>();
        foreach (var grid in _mapManager.GetAllGrids(mapId))
        {
            if (HasComp<BecomesStationComponent>(grid.Owner))
                stationGrids.Add(grid.Owner);
        }

        // Fallback for maps without explicit station markers.
        if (stationGrids.Count == 0)
        {
            foreach (var grid in _mapManager.GetAllGrids(mapId))
            {
                stationGrids.Add(grid.Owner);
            }
        }

        foreach (var gridUid in stationGrids)
        {
            _shuttle.Disable(gridUid);
            EnsureInherentGravity(gridUid, raiseGravityChangedEvent: true);
        }

        if (_map.TryGetMap(mapId, out var mapUid))
            EnsureInherentGravity(mapUid.Value, raiseGravityChangedEvent: false);

        var atmosRebuilt = RebuildGridAtmosphereForMap(mapId);
        var protectedCables = ProtectApcExtensionCablesFromCutting(mapId);

        _sawmill.Info(
            $"Applied WH40K map stability safeguards: anchored grids={stationGrids.Count}, fixgridatmos grids={atmosRebuilt}, protected APC extension cables={protectedCables}, map={mapId}.");
    }

    private int RebuildGridAtmosphereForMap(MapId mapId)
    {
        // DISABLED: Rebuilding atmosphere at round start causes a massive dirty spike
        // (19,000+ entities) which floods the network send buffer and forces a lag spike
        // or a socket connection exception ("would block - send buffer full").
        // Normally, SS14 map loading handles atmos appropriately without needing a full rebuild.
        return 0;
    }

    private int ProtectApcExtensionCablesFromCutting(MapId mapId)
    {
        var changed = 0;
        var query = EntityQueryEnumerator<CableComponent, ExtensionCableProviderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var cable, out _, out var xform))
        {
            if (xform.MapID != mapId || cable.CuttingQuality == null)
                continue;

            _cables.SetCanBeCut(uid, false, cable);
            changed++;
        }

        return changed;
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

}
