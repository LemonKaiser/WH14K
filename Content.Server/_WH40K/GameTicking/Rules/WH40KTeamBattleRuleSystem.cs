using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration.Systems;
using Content.Server._WH40K.Combat;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.LateJoin;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.GameTicking.Rules;
using Content.Server.KillTracking;
using Content.Server.Mind;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.RoundEnd;
using Content.Server.Station.Systems;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.GameTicking.Rules;

public sealed class WH40KTeamBattleRuleSystem : GameRuleSystem<Components.WH40KTeamBattleRuleComponent>
{
    private static readonly bool AnnounceTeamOnSpawn = true;
    private static readonly bool AnnounceWinner = true;
    private static readonly bool CountCriticalAsAlive = true;
    [Dependency] private readonly AdminSystem _admin = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly PlayTimeTrackingSystem _playTimeTracking = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly WH40KAttackerResolverSystem _attackerResolver = default!;
    [Dependency] private readonly WH40KFactionSystem _wh40kFactions = default!;

    private ISawmill _sawmill = default!;
    private float _checkInterval;
    private bool _requireAllTeamsPresent;
    private float _roundTimeLimitSeconds;
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
            _roundTimeLimitSeconds = v;
            ApplyConfigToActiveRules();
        }, true);

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnPlayerBeforeSpawn);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<KillReportedEvent>(OnKillReported);
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
    }

    protected override void Started(EntityUid uid, Components.WH40KTeamBattleRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        _activeRuleUid = uid;

        BuildDepartmentMap(component);
        component.CheckInterval = _checkInterval;
        component.RequireAllTeamsPresent = _requireAllTeamsPresent;
        component.RoundTimeLimitSeconds = _roundTimeLimitSeconds;
        component.NextCheck = Timing.CurTime + TimeSpan.FromSeconds(component.CheckInterval);
        component.RoundStartTime = Timing.CurTime;
        component.RoundEnding = false;
        component.WinnerTeamId = null;
        component.Draw = false;
        component.TimeLimitReached = false;
        component.PlayerKills.Clear();
        component.NextFriendlyFireAhelpTime.Clear();
        EnsureTeamArrays(component);

        if (component.Teams.Count < 2)
            _sawmill.Warning($"WH40K team rule '{ToPrettyString(uid)}' has fewer than 2 teams configured.");

        _wh40kFactions.BroadcastFactionsToAll();
    }

    protected override void Ended(EntityUid uid, Components.WH40KTeamBattleRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        if (_activeRuleUid == uid)
            _activeRuleUid = null;
        _wh40kFactions.BroadcastFactionsToAll();
    }

    protected override void ActiveTick(EntityUid uid, Components.WH40KTeamBattleRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.RoundEnding)
            return;

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
        if (AllowsTeamVictory(component))
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

        EnsureComp<KillTrackerComponent>(ev.Mob);

        if (!TryGetTeamIndex(mindId, rule, out var teamIndex))
        {
            _sawmill.Debug($"No team match for {ev.Player?.Name} ({ToPrettyString(ev.Mob)}). Job not in configured departments.");
            return;
        }

        var team = rule.Teams[teamIndex];
        var member = EnsureComp<WH40KTeamMemberComponent>(ev.Mob);
        member.TeamId = team.Id;

        if (!AnnounceTeamOnSpawn)
            return;

        if (ev.Player != null)
        {
            _chat.DispatchServerMessage(ev.Player, Loc.GetString("wh40k-team-service-message", ("team", Loc.GetString(team.Name))));

            var perTeamKey = $"wh40k-team-service-message-{team.Id}";
            if (Loc.HasString(perTeamKey))
                _chat.DispatchServerMessage(ev.Player, Loc.GetString(perTeamKey));
        }
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

        teamName = Loc.GetString(team!.Name);
        return true;
    }

    public void HandleObjectiveDestroyed(string destroyedTeamId)
    {
        if (string.IsNullOrEmpty(destroyedTeamId))
            return;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!AllowsObjectiveVictory(rule))
            return;

        if (rule.RoundEnding)
            return;

        var otherTeams = rule.Teams.Where(t => t.Id != destroyedTeamId).ToList();

        if (otherTeams.Count == 1)
        {
            var winner = otherTeams[0];
            rule.WinnerTeamId = winner.Id;
            rule.Draw = false;
            rule.RoundEnding = true;
            rule.TimeLimitReached = false;

            if (AnnounceWinner)
                _chat.DispatchServerAnnouncement(Loc.GetString("wh40k-team-winner-announce", ("team", Loc.GetString(winner.Name))));

            _roundEnd.EndRound();
            return;
        }

        rule.WinnerTeamId = null;
        rule.Draw = true;
        rule.RoundEnding = true;
        rule.TimeLimitReached = false;

        if (AnnounceWinner)
            _chat.DispatchServerAnnouncement(Loc.GetString("wh40k-team-draw-announce"));

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
                _chat.DispatchServerAnnouncement(Loc.GetString("wh40k-team-winner-announce", ("team", Loc.GetString(winner.Name))));

            _roundEnd.EndRound();
            return;
        }

        if (aliveTeams.Count == 0)
        {
            component.WinnerTeamId = null;
            component.Draw = true;
            component.RoundEnding = true;

            if (AnnounceWinner)
                _chat.DispatchServerAnnouncement(Loc.GetString("wh40k-team-draw-announce"));

            _roundEnd.EndRound();
        }
    }

    private void OnKillReported(ref KillReportedEvent ev)
    {
        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (TryComp<WH40KTeamMemberComponent>(ev.Entity, out var teamMember) &&
            TryGetTeamIndexById(teamMember.TeamId, rule, out var victimTeam))
        {
            EnsureTeamArrays(rule);
            rule.TeamDeaths[victimTeam]++;
        }
        else if (_mind.TryGetMind(ev.Entity, out var victimMindId, out _))
        {
            if (TryGetTeamIndex(victimMindId, rule, out var victimTeamIndex))
            {
                EnsureTeamArrays(rule);
                rule.TeamDeaths[victimTeamIndex]++;
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
            }
        }

    }

    private void OnDamageChanged(EntityUid uid, DamageableComponent component, DamageChangedEvent args)
    {
        if (!_config.GetCVar(CCVars.WH40KFriendlyFireAhelpEnabled))
            return;

        if (!args.DamageIncreased || args.Origin == null)
            return;

        if (!TryGetActiveRule(out _, out var rule, out _))
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

    private void OnBeforeDamageChanged(EntityUid uid, DamageableComponent component, ref BeforeDamageChangedEvent args)
    {
        if (!_config.GetCVar(CCVars.WH40KFriendlyFireDisabled))
            return;

        if (args.Origin == null)
            return;

        if (!TryGetActiveRule(out _, out var rule, out _))
            return;

        if (!_attackerResolver.TryResolveAttacker(args.Origin.Value, out var attacker, out _))
            attacker = args.Origin.Value;

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

        args.Cancelled = true;
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
            rule.RoundTimeLimitSeconds = _roundTimeLimitSeconds;
            rule.NextCheck = Timing.CurTime + TimeSpan.FromSeconds(rule.CheckInterval);
        }
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
            _chat.DispatchServerAnnouncement(Loc.GetString("wh40k-team-time-limit-announce"));

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

}
