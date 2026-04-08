using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Station.Events;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.Chat;
using Content.Shared._WH40K.LateJoin;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using NetUserId = Robust.Shared.Network.NetUserId;

namespace Content.Server._WH40K.LateJoin;

public sealed class WH40KFactionSystem : EntitySystem
{
    private const string TeamBattleRulePrototypeId = "WH40KTeamBattle";
    private const string BalanceBlockedLocKey = "wh40k-faction-balance-blocked";
    private const string StreakBlockedLocKey = "wh40k-faction-streak-blocked";
    private const string ReadySelectionRequiredLocKey = "wh40k-faction-ready-selection-required";
    private const string LateJoinSelectionRequiredLocKey = "wh40k-faction-latejoin-selection-required";
    private const string InvalidJobSelectionLocKey = "wh40k-faction-invalid-job-selection";
    private const int MaxAllowedTeamLead = 2;
    private const int SameFactionStreakLimit = 3;
    private static readonly TimeSpan LateJoinReservationLifetime = TimeSpan.FromMinutes(2);

    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamBattleRule = default!;

    private readonly Dictionary<NetUserId, string> _lobbySelections = new();
    private readonly Dictionary<NetUserId, PendingLateJoinSelection> _lateJoinSelections = new();
    private readonly Dictionary<NetUserId, List<string>> _recentCompletedFactions = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KRequestFactionsEvent>(OnRequestFactions);
        SubscribeNetworkEvent<WH40KSelectFactionEvent>(OnSelectFaction);
        SubscribeNetworkEvent<WH40KCancelFactionSelectionEvent>(OnCancelFactionSelection);

        SubscribeLocalEvent<PlayerBeforeReadyChangedEvent>(OnPlayerBeforeReadyChanged);
        SubscribeLocalEvent<PlayerReadyChangedEvent>(OnPlayerReadyChanged);
        SubscribeLocalEvent<StationJobsGetCandidatesEvent>(OnStationJobsGetCandidates);
        SubscribeLocalEvent<StationJobsGetOverflowCandidatesEvent>(OnStationJobsGetOverflowCandidates);
        SubscribeLocalEvent<IsRoleAllowedEvent>(OnIsRoleAllowed);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<RoundEndMessageEvent>(OnRoundEndMessage);
    }

    private void OnRequestFactions(WH40KRequestFactionsEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession is not { } session)
            return;

        var factions = BuildFactionList(session, msg.Purpose);
        RaiseNetworkEvent(new WH40KFactionsEvent(msg.Purpose, factions), session);
    }

    public void BroadcastFactionsToAll()
    {
        var factions = BuildFactionList(null, WH40KFactionSelectionPurpose.Preview);
        RaiseNetworkEvent(new WH40KFactionsEvent(WH40KFactionSelectionPurpose.Preview, factions));
    }

    private void OnSelectFaction(WH40KSelectFactionEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession is not { } session)
            return;

        var accepted = TrySelectFaction(session, msg.FactionId, msg.Purpose, out var canonicalFactionId, out var messageLocKey);
        var factions = BuildFactionList(session, msg.Purpose);
        RaiseNetworkEvent(new WH40KFactionSelectionResultEvent(msg.Purpose, canonicalFactionId, accepted, messageLocKey, factions), session);

        if (!accepted && !string.IsNullOrWhiteSpace(messageLocKey))
            RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = messageLocKey }, session);
    }

    private void OnCancelFactionSelection(WH40KCancelFactionSelectionEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession is not { } session)
            return;

        if (ResolvePurpose(msg.Purpose) == WH40KFactionSelectionPurpose.LateJoin)
            _lateJoinSelections.Remove(session.UserId);
    }

    private void OnPlayerBeforeReadyChanged(ref PlayerBeforeReadyChangedEvent args)
    {
        if (!args.Ready)
            return;

        if (!TryGetRuleDefinition(WH40KFactionSelectionPurpose.LobbyReady, out var rule))
            return;

        if (!_lobbySelections.TryGetValue(args.Player.UserId, out var factionId))
        {
            args.Cancelled = true;
            args.ReasonLocKey = ReadySelectionRequiredLocKey;
            return;
        }

        if (!CanSelectFaction(args.Player.UserId, factionId, rule, WH40KFactionSelectionPurpose.LobbyReady, out var messageLocKey))
        {
            args.Cancelled = true;
            args.ReasonLocKey = messageLocKey ?? ReadySelectionRequiredLocKey;
        }
    }

    private void OnPlayerReadyChanged(PlayerReadyChangedEvent args)
    {
        if (!args.Ready)
            _lobbySelections.Remove(args.Player.UserId);
    }

    private void OnStationJobsGetCandidates(ref StationJobsGetCandidatesEvent args)
    {
        if (!TryGetRuleDefinition(WH40KFactionSelectionPurpose.LobbyReady, out var rule))
            return;

        if (!_lobbySelections.TryGetValue(args.Player, out var factionId))
            return;

        args.Jobs.RemoveAll(jobId => !IsJobAllowedForFaction(rule, factionId, jobId));
    }

    private void OnStationJobsGetOverflowCandidates(ref StationJobsGetOverflowCandidatesEvent args)
    {
        if (!TryGetRuleDefinition(WH40KFactionSelectionPurpose.LobbyReady, out var rule))
            return;

        if (!_lobbySelections.TryGetValue(args.Player, out var factionId))
            return;

        args.Jobs.RemoveAll(jobId => !IsJobAllowedForFaction(rule, factionId, jobId));
    }

    private void OnIsRoleAllowed(ref IsRoleAllowedEvent args)
    {
        if (args.Jobs == null || args.Jobs.Count == 0)
            return;

        if (!TryGetRuleDefinition(WH40KFactionSelectionPurpose.LateJoin, out var rule))
            return;

        PruneExpiredLateJoinSelections();
        if (!_lateJoinSelections.TryGetValue(args.Player.UserId, out var pendingSelection))
        {
            args.Cancelled = true;
            RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = LateJoinSelectionRequiredLocKey }, args.Player);
            return;
        }

        foreach (var jobId in args.Jobs)
        {
            if (IsJobAllowedForFaction(rule, pendingSelection.FactionId, jobId))
                continue;

            args.Cancelled = true;
            RaiseNetworkEvent(new WH40KLocalizedChatEvent { LocKey = InvalidJobSelectionLocKey }, args.Player);
            return;
        }
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.Player == null)
            return;

        if (args.LateJoin)
            _lateJoinSelections.Remove(args.Player.UserId);
        else
            _lobbySelections.Remove(args.Player.UserId);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent _)
    {
        _lobbySelections.Clear();
        _lateJoinSelections.Clear();
    }

    private void OnRoundEndMessage(RoundEndMessageEvent args)
    {
        if (!TryGetKnownRuleDefinition(out _))
            return;

        var processed = new HashSet<NetUserId>();

        foreach (var playerInfo in args.AllPlayersEndInfo)
        {
            if (playerInfo.PlayerGuid is not { } userId || !processed.Add(userId))
                continue;

            if (!_teamBattleRule.TryGetTeamIdForUser(userId, out var teamId) || string.IsNullOrWhiteSpace(teamId))
                continue;

            RecordCompletedFaction(userId, teamId);
        }
    }

    private List<WH40KFactionInfo> BuildFactionList(ICommonSession? requester, WH40KFactionSelectionPurpose purpose)
    {
        if (!TryGetRuleDefinition(purpose, out var rule))
            return new List<WH40KFactionInfo>();

        var requesterId = requester?.UserId;
        var teamPlayerCounts = BuildConnectedTeamCounts(requesterId, ResolvePurpose(purpose));
        var result = new List<WH40KFactionInfo>(rule.Teams.Count);

        foreach (var team in rule.Teams)
        {
            if (string.IsNullOrWhiteSpace(team.Id))
                continue;

            var canSelect = true;
            string? disabledReason = null;
            if (requesterId != null)
                canSelect = CanSelectFaction(requesterId.Value, team.Id, rule, ResolvePurpose(purpose), out disabledReason);

            result.Add(new WH40KFactionInfo(
                team.Id,
                team.Name,
                team.Logo,
                new List<ProtoId<DepartmentPrototype>>(team.Departments),
                teamPlayerCounts.TryGetValue(team.Id, out var teamCount) ? teamCount : 0,
                canSelect,
                disabledReason));
        }

        return result;
    }

    private Dictionary<string, int> BuildConnectedTeamCounts(NetUserId? excludeUserId, WH40KFactionSelectionPurpose purpose)
    {
        PruneExpiredLateJoinSelections();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in _players.Sessions)
        {
            if (purpose == WH40KFactionSelectionPurpose.LobbyReady)
            {
                if (excludeUserId != session.UserId &&
                    _gameTicker.PlayerGameStatuses.TryGetValue(session.UserId, out var status) &&
                    status == PlayerGameStatus.ReadyToPlay &&
                    _lobbySelections.TryGetValue(session.UserId, out var readyFactionId))
                {
                    AddTeamCount(counts, readyFactionId);
                }

                continue;
            }

            if (session.AttachedEntity is { Valid: true } attached &&
                TryComp<WH40KTeamMemberComponent>(attached, out var teamMember) &&
                !string.IsNullOrWhiteSpace(teamMember.TeamId))
            {
                AddTeamCount(counts, teamMember.TeamId);
            }

            if (excludeUserId != session.UserId &&
                !_gameTicker.UserHasJoinedGame(session) &&
                _lateJoinSelections.TryGetValue(session.UserId, out var pendingSelection))
            {
                AddTeamCount(counts, pendingSelection.FactionId);
            }
        }

        return counts;
    }

    private bool TrySelectFaction(
        ICommonSession session,
        string factionId,
        WH40KFactionSelectionPurpose purpose,
        out string canonicalFactionId,
        out string? messageLocKey)
    {
        canonicalFactionId = string.Empty;
        messageLocKey = null;

        if (!TryGetRuleDefinition(purpose, out var rule) ||
            !TryResolveFactionId(rule, factionId, out canonicalFactionId))
        {
            messageLocKey = ResolvePurpose(purpose) == WH40KFactionSelectionPurpose.LobbyReady
                ? ReadySelectionRequiredLocKey
                : LateJoinSelectionRequiredLocKey;
            return false;
        }

        if (!CanSelectFaction(session.UserId, canonicalFactionId, rule, ResolvePurpose(purpose), out messageLocKey))
            return false;

        switch (ResolvePurpose(purpose))
        {
            case WH40KFactionSelectionPurpose.LobbyReady:
                _lobbySelections[session.UserId] = canonicalFactionId;
                _gameTicker.ToggleReady(session, true);
                break;
            case WH40KFactionSelectionPurpose.LateJoin:
                _lateJoinSelections[session.UserId] = new PendingLateJoinSelection(
                    canonicalFactionId,
                    _timing.CurTime + LateJoinReservationLifetime);
                break;
        }

        return true;
    }

    private bool CanSelectFaction(
        NetUserId userId,
        string factionId,
        WH40KTeamBattleRuleComponent rule,
        WH40KFactionSelectionPurpose purpose,
        out string? messageLocKey)
    {
        messageLocKey = null;

        if (!TryResolveFactionId(rule, factionId, out var canonicalFactionId))
        {
            messageLocKey = purpose == WH40KFactionSelectionPurpose.LobbyReady
                ? ReadySelectionRequiredLocKey
                : LateJoinSelectionRequiredLocKey;
            return false;
        }

        if (purpose == WH40KFactionSelectionPurpose.LobbyReady && HasFactionStreakBlock(userId, canonicalFactionId))
        {
            messageLocKey = StreakBlockedLocKey;
            return false;
        }

        var teamCounts = BuildConnectedTeamCounts(userId, purpose);
        if (WouldExceedBalance(rule, canonicalFactionId, teamCounts))
        {
            messageLocKey = BalanceBlockedLocKey;
            return false;
        }

        return true;
    }

    private bool TryGetRuleDefinition(WH40KFactionSelectionPurpose purpose, out WH40KTeamBattleRuleComponent rule)
    {
        rule = default!;
        return ResolvePurpose(purpose) switch
        {
            WH40KFactionSelectionPurpose.LobbyReady => TryGetLobbyRuleDefinition(out rule),
            WH40KFactionSelectionPurpose.LateJoin => TryGetActiveRuleDefinition(out rule),
            _ => TryGetKnownRuleDefinition(out rule),
        };
    }

    private WH40KFactionSelectionPurpose ResolvePurpose(WH40KFactionSelectionPurpose purpose)
    {
        if (purpose != WH40KFactionSelectionPurpose.Preview)
            return purpose;

        return _gameTicker.RunLevel == GameRunLevel.PreRoundLobby
            ? WH40KFactionSelectionPurpose.LobbyReady
            : WH40KFactionSelectionPurpose.LateJoin;
    }

    private bool TryGetKnownRuleDefinition(out WH40KTeamBattleRuleComponent rule)
    {
        if (TryGetActiveRuleDefinition(out rule))
            return true;

        if (TryGetAddedRuleDefinition(out rule))
            return true;

        return TryGetPresetRuleDefinition(out rule);
    }

    private bool TryGetLobbyRuleDefinition(out WH40KTeamBattleRuleComponent rule)
    {
        rule = default!;
        if (_gameTicker.RunLevel != GameRunLevel.PreRoundLobby)
            return false;

        if (TryGetAddedRuleDefinition(out rule))
            return true;

        return TryGetPresetRuleDefinition(out rule);
    }

    private bool TryGetActiveRuleDefinition(out WH40KTeamBattleRuleComponent rule)
    {
        var query = EntityQueryEnumerator<WH40KTeamBattleRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var component, out var gameRule))
        {
            if (!_gameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            rule = component;
            return true;
        }

        rule = default!;
        return false;
    }

    private bool TryGetAddedRuleDefinition(out WH40KTeamBattleRuleComponent rule)
    {
        var query = EntityQueryEnumerator<WH40KTeamBattleRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var component, out var gameRule))
        {
            if (!_gameTicker.IsGameRuleAdded(uid, gameRule))
                continue;

            rule = component;
            return true;
        }

        rule = default!;
        return false;
    }

    private bool TryGetPresetRuleDefinition(out WH40KTeamBattleRuleComponent rule)
    {
        rule = default!;
        var preset = _gameTicker.CurrentPreset ?? _gameTicker.Preset;
        if (preset == null || !preset.Rules.Any(ruleId => string.Equals(ruleId.Id, TeamBattleRulePrototypeId, StringComparison.Ordinal)))
            return false;

        if (!_prototype.TryIndex<EntityPrototype>(TeamBattleRulePrototypeId, out var prototype))
            return false;

        if (!prototype.TryGetComponent<WH40KTeamBattleRuleComponent>(out WH40KTeamBattleRuleComponent? prototypeRule, EntityManager.ComponentFactory))
            return false;

        if (prototypeRule == null)
            return false;

        rule = prototypeRule;
        return true;
    }

    private bool TryResolveFactionId(WH40KTeamBattleRuleComponent rule, string factionId, out string canonicalFactionId)
    {
        canonicalFactionId = string.Empty;

        foreach (var team in rule.Teams)
        {
            if (!string.Equals(team.Id, factionId, StringComparison.OrdinalIgnoreCase))
                continue;

            canonicalFactionId = team.Id;
            return true;
        }

        return false;
    }

    private static void AddTeamCount(IDictionary<string, int> counts, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;

        counts.TryGetValue(teamId, out var current);
        counts[teamId] = current + 1;
    }

    private bool WouldExceedBalance(
        WH40KTeamBattleRuleComponent rule,
        string factionId,
        IReadOnlyDictionary<string, int> teamCounts)
    {
        var selectedCount = teamCounts.TryGetValue(factionId, out var currentSelected) ? currentSelected : 0;
        var otherMax = 0;

        foreach (var team in rule.Teams)
        {
            if (string.Equals(team.Id, factionId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (teamCounts.TryGetValue(team.Id, out var teamCount) && teamCount > otherMax)
                otherMax = teamCount;
        }

        return selectedCount + 1 - otherMax > MaxAllowedTeamLead;
    }

    private bool HasFactionStreakBlock(NetUserId userId, string factionId)
    {
        if (!_recentCompletedFactions.TryGetValue(userId, out var history) || history.Count < SameFactionStreakLimit)
            return false;

        return history.All(teamId => string.Equals(teamId, factionId, StringComparison.OrdinalIgnoreCase));
    }

    private void RecordCompletedFaction(NetUserId userId, string factionId)
    {
        if (!_recentCompletedFactions.TryGetValue(userId, out var history))
        {
            history = new List<string>(SameFactionStreakLimit);
            _recentCompletedFactions[userId] = history;
        }

        history.Add(factionId);
        while (history.Count > SameFactionStreakLimit)
        {
            history.RemoveAt(0);
        }
    }

    private void PruneExpiredLateJoinSelections()
    {
        if (_lateJoinSelections.Count == 0)
            return;

        var now = _timing.CurTime;
        foreach (var (userId, selection) in _lateJoinSelections.ToArray())
        {
            if (selection.ExpiresAt > now)
                continue;

            _lateJoinSelections.Remove(userId);
        }
    }

    private bool IsJobAllowedForFaction(
        WH40KTeamBattleRuleComponent rule,
        string factionId,
        ProtoId<JobPrototype> jobId)
    {
        foreach (var team in rule.Teams)
        {
            if (!string.Equals(team.Id, factionId, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var departmentId in team.Departments)
            {
                if (!_prototype.TryIndex<DepartmentPrototype>(departmentId, out var department))
                    continue;

                if (department.Roles.Contains(jobId))
                    return true;
            }

            return false;
        }

        return false;
    }

    private readonly record struct PendingLateJoinSelection(string FactionId, TimeSpan ExpiresAt);
}
