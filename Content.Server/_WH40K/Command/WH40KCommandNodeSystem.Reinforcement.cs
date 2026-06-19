using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.Stats;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Ghost.Roles.Raffles;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.GameMode;
using Content.Shared.Ghost;
using Content.Shared.Ghost.Roles.Raffles;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Command;

public sealed partial class WH40KCommandNodeSystem
{
    private const int ReinforcementManualDelaySeconds = 60;
    private const int ReinforcementAutoDelaySeconds = 300;
    private const int ReinforcementSharedCooldownSeconds = 600;
    private const int ReinforcementAutoCheckIntervalSeconds = 5;
    private const int ReinforcementMinAutoThresholdPercent = 20;
    private const int ReinforcementMaxAutoThresholdPercent = 50;
    private const int ReinforcementMaxTotalCount = 10;
    private const float ReinforcementSpawnZoneConnectDistance = 1.6f;
    private const string ReinforcementDefaultGroupKey = "w40k-cmd-reinforcement-group-line";

    private readonly record struct ReinforcementPendingEntry(
        string RoleId,
        ProtoId<JobPrototype> JobId,
        string Name,
        string Description,
        int Count,
        int UnitCost,
        int UnitFundsCost,
        int UnitInfluenceCost);

    private readonly record struct ReinforcementSpawnPointCandidate(
        EntityCoordinates Coordinates,
        MapId MapId,
        Vector2 WorldPosition);

    private sealed class ReinforcementSpawnZone
    {
        public readonly List<EntityCoordinates> Points = new();
    }

    private sealed class TeamReinforcementAutoConfig
    {
        public bool Enabled;
        public int ThresholdPercent = 30;
        public List<WH40KCommandReinforcementDraftEntry> Roles = new();
    }

    private sealed class TeamReinforcementPendingRequest
    {
        public WH40KCommandReinforcementRequestKind Kind;
        public TimeSpan ArrivalTime;
        public int TotalCount;
        public int TotalCost;
        public int TotalFundsCost;
        public int TotalInfluenceCost;
        public List<ReinforcementPendingEntry> Roles = new();
    }

    private enum ReinforcementRoleCapMode : byte
    {
        IgnoreCurrentCounts,
        RejectWhenExceeded,
        TrimToAvailable
    }

    private sealed class TeamReinforcementRuntime
    {
        public TimeSpan NextAvailable = TimeSpan.Zero;
        public TimeSpan NextAutoCheck = TimeSpan.Zero;
        public TeamReinforcementAutoConfig AutoConfig = new();
        public TeamReinforcementPendingRequest? PendingRequest;
    }

    private readonly Dictionary<string, TeamReinforcementRuntime> _teamReinforcementRuntime =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct TeamAliveSnapshot(int AliveCount, int TotalCount)
    {
        public int AlivePercent => TotalCount <= 0
            ? 0
            : (int) MathF.Round(AliveCount * 100f / TotalCount, MidpointRounding.AwayFromZero);
    }

    private void InitializeReinforcementUi()
    {
        Subs.BuiEvents<WH40KCommandNodeComponent>(WH40KCommandNodeUiKey.Reinforcement, subs =>
        {
            subs.Event<WH40KCommandNodeSubmitReinforcementRequestMessage>(OnReinforcementRequestSubmitted);
            subs.Event<WH40KCommandNodeSaveAutoReinforcementMessage>(OnAutoReinforcementSaved);
        });
    }

    private void UpdateReinforcementRuntime()
    {
        var now = _timing.CurTime;
        var teamIds = _teamRule.GetTeamIds();
        if (teamIds.Count == 0)
        {
            ResetReinforcementRuntime();
            return;
        }

        PruneReinforcementRuntime(teamIds);
        foreach (var teamId in teamIds)
        {
            if (string.IsNullOrWhiteSpace(teamId))
                continue;

            var runtime = GetOrCreateTeamReinforcementRuntime(teamId);
            TryProcessPendingRequest(teamId, runtime, now);
            TryProcessAutoRequest(teamId, runtime, now);
        }
    }

    private void ResetReinforcementRuntime()
    {
        _teamReinforcementRuntime.Clear();
    }

    private void PruneReinforcementRuntime(IReadOnlyCollection<string> activeTeamIds)
    {
        var active = new HashSet<string>(activeTeamIds, StringComparer.OrdinalIgnoreCase);
        foreach (var teamId in _teamReinforcementRuntime.Keys.ToArray())
        {
            if (!active.Contains(teamId))
                _teamReinforcementRuntime.Remove(teamId);
        }
    }

    private TeamReinforcementRuntime GetOrCreateTeamReinforcementRuntime(string teamId)
    {
        if (!_teamReinforcementRuntime.TryGetValue(teamId, out var runtime))
        {
            runtime = new TeamReinforcementRuntime();
            _teamReinforcementRuntime[teamId] = runtime;
        }

        return runtime;
    }

    private void OnReinforcementRequestSubmitted(
        Entity<WH40KCommandNodeComponent> ent,
        ref WH40KCommandNodeSubmitReinforcementRequestMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        if (!TryValidateReinforcementPhase(ent.Comp.TeamId, ent.Owner, args.Actor))
            return;

        var runtime = GetOrCreateTeamReinforcementRuntime(ent.Comp.TeamId);
        if (runtime.PendingRequest != null)
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-pending-exists"), ent.Owner, args.Actor);
            return;
        }

        if (GetRemainingReinforcementCooldown(ent.Comp.TeamId) > 0)
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-cooldown"), ent.Owner, args.Actor);
            return;
        }

        if (!TryBuildPendingEntries(
                ent.Comp.TeamId,
                args.Roles,
                false,
                ReinforcementRoleCapMode.RejectWhenExceeded,
                out var entries,
                out var totalCount,
                out var totalCost,
                out var totalFundsCost,
                out var totalInfluenceCost,
                out var errorKey))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, errorKey), ent.Owner, args.Actor);
            return;
        }

        if (!TrySpendTeamFundsAndInfluence(ent.Owner, ent.Comp.TeamId, totalFundsCost, totalInfluenceCost, "reinforcement-manual"))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-denied"), ent.Owner, args.Actor);
            return;
        }

        CreatePendingRequest(
            runtime,
            WH40KCommandReinforcementRequestKind.Manual,
            entries,
            totalCount,
            totalCost,
            totalFundsCost,
            totalInfluenceCost,
            ReinforcementManualDelaySeconds);
        RecordEconomySpendStats(
            args.Actor,
            ent.Comp.TeamId,
            WH40KPlayerStatKeys.EconomyCommandReinforcementCallCount,
            WH40KPlayerStatKeys.EconomyCommandReinforcementCost,
            totalFundsCost + totalInfluenceCost,
            "reinforcement-manual",
            null);

        _popup.PopupEntity(
            Loc.GetString(
                "w40k-cmd-reinforcement-request-created",
                ("count", totalCount),
                ("funds", totalFundsCost),
                ("influence", totalInfluenceCost),
                ("delay", ReinforcementManualDelaySeconds / 60)),
            ent.Owner,
            args.Actor);

        UpdateUi(ent);
    }

    private void OnAutoReinforcementSaved(
        Entity<WH40KCommandNodeComponent> ent,
        ref WH40KCommandNodeSaveAutoReinforcementMessage args)
    {
        if (!IsUserAllowedForTeam(args.Actor, ent.Comp.TeamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "wh40k-access-denied-wrong-team"), ent.Owner, args.Actor);
            return;
        }

        var runtime = GetOrCreateTeamReinforcementRuntime(ent.Comp.TeamId);
        var threshold = Math.Clamp(
            args.ThresholdPercent,
            ReinforcementMinAutoThresholdPercent,
            ReinforcementMaxAutoThresholdPercent);

        if (!args.Enabled)
        {
            runtime.AutoConfig.Enabled = false;
            runtime.AutoConfig.ThresholdPercent = threshold;
            if (args.Roles.Any(entry => !string.IsNullOrWhiteSpace(entry.RoleId) && entry.Count > 0))
            {
                if (!TryBuildPendingEntries(
                        ent.Comp.TeamId,
                        args.Roles,
                        true,
                        ReinforcementRoleCapMode.IgnoreCurrentCounts,
                        out var disabledEntries,
                        out _,
                        out _,
                        out _,
                        out _,
                        out var disabledErrorKey))
                {
                    _popup.PopupEntity(_culture.GetPlayerString(args.Actor, disabledErrorKey), ent.Owner, args.Actor);
                    return;
                }

                runtime.AutoConfig.Roles = disabledEntries
                    .Select(entry => new WH40KCommandReinforcementDraftEntry(entry.RoleId, entry.Count))
                    .ToList();
            }
            else
            {
                runtime.AutoConfig.Roles.Clear();
            }

            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, "w40k-cmd-reinforcement-auto-disabled"), ent.Owner, args.Actor);
            UpdateUi(ent);
            return;
        }

        if (!TryBuildPendingEntries(
                ent.Comp.TeamId,
                args.Roles,
                true,
                ReinforcementRoleCapMode.IgnoreCurrentCounts,
                out var entries,
                out _,
                out _,
                out _,
                out _,
                out var errorKey))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.Actor, errorKey), ent.Owner, args.Actor);
            return;
        }

        runtime.AutoConfig.Enabled = true;
        runtime.AutoConfig.ThresholdPercent = threshold;
        runtime.AutoConfig.Roles = entries
            .Select(entry => new WH40KCommandReinforcementDraftEntry(entry.RoleId, entry.Count))
            .ToList();

        _popup.PopupEntity(
            Loc.GetString(
                "w40k-cmd-reinforcement-auto-saved",
                ("threshold", threshold)),
            ent.Owner,
            args.Actor);

        UpdateUi(ent);
    }

    private bool TryValidateReinforcementPhase(string teamId, EntityUid popupSource, EntityUid actor)
    {
        var phase = _teamRule.GetCurrentPhase();
        if (phase < WH40KBattlePhase.Assault)
        {
            _popup.PopupEntity(_culture.GetPlayerString(actor, "w40k-cmd-reinforcement-phase-lock"), popupSource, actor);
            return false;
        }

        if (phase >= WH40KBattlePhase.Apocalypse)
        {
            _popup.PopupEntity(_culture.GetPlayerString(actor, "w40k-cmd-reinforcement-apocalypse-lock"), popupSource, actor);
            return false;
        }

        return true;
    }

    private bool TryBuildPendingEntries(
        string teamId,
        IReadOnlyCollection<WH40KCommandReinforcementDraftEntry> draftEntries,
        bool autoMode,
        ReinforcementRoleCapMode roleCapMode,
        out List<ReinforcementPendingEntry> entries,
        out int totalCount,
        out int totalCost,
        out int totalFundsCost,
        out int totalInfluenceCost,
        out string errorKey)
    {
        entries = new List<ReinforcementPendingEntry>();
        totalCount = 0;
        totalCost = 0;
        totalFundsCost = 0;
        totalInfluenceCost = 0;
        errorKey = "w40k-cmd-reinforcement-option-invalid";

        if (!TryResolveReinforcementProfileForTeam(teamId, out var profile))
            return false;

        var currentRoleCounts = roleCapMode != ReinforcementRoleCapMode.IgnoreCurrentCounts
            ? BuildCurrentReinforcementRoleCounts(teamId, profile)
            : null;

        var countsByRole = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in draftEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.RoleId) || entry.Count <= 0)
                continue;

            countsByRole[entry.RoleId] = countsByRole.GetValueOrDefault(entry.RoleId) + entry.Count;
        }

        if (countsByRole.Count == 0)
        {
            errorKey = "w40k-cmd-reinforcement-selection-empty";
            return false;
        }

        foreach (var option in profile.Options.OrderBy(x => x.SortOrder).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(option.Id))
                continue;

            if (!countsByRole.TryGetValue(option.Id, out var requestedCount) || requestedCount <= 0)
                continue;

            if (!IsReinforcementOptionUnlocked(teamId, option))
            {
                errorKey = "w40k-cmd-reinforcement-option-locked";
                return false;
            }

            var cap = GetReinforcementRoleCap(option);
            var effectiveRequestedCount = requestedCount;
            if (effectiveRequestedCount > cap)
            {
                if (roleCapMode == ReinforcementRoleCapMode.TrimToAvailable)
                {
                    effectiveRequestedCount = cap;
                }
                else
                {
                    errorKey = "w40k-cmd-reinforcement-role-cap-hit";
                    return false;
                }
            }

            var currentRoleCount = currentRoleCounts?.GetValueOrDefault(option.Id) ?? 0;
            if (roleCapMode == ReinforcementRoleCapMode.RejectWhenExceeded &&
                currentRoleCount + effectiveRequestedCount > cap)
            {
                errorKey = "w40k-cmd-reinforcement-role-cap-hit";
                return false;
            }

            if (roleCapMode == ReinforcementRoleCapMode.TrimToAvailable)
            {
                effectiveRequestedCount = Math.Min(
                    effectiveRequestedCount,
                    Math.Max(0, cap - currentRoleCount));

                if (effectiveRequestedCount <= 0)
                    continue;
            }

            if (autoMode && !option.AllowAuto)
            {
                errorKey = "w40k-cmd-reinforcement-auto-role-blocked";
                return false;
            }

            var unitInfluenceCost = GetReinforcementUnitCost(option);
            var unitFundsCost = WH40KCommandEconomyCalculator.GetReinforcementFundsCost(unitInfluenceCost);
            var optionTotalCost = unitInfluenceCost * effectiveRequestedCount;
            var optionTotalFundsCost = unitFundsCost * effectiveRequestedCount;
            totalCount += effectiveRequestedCount;
            totalCost += optionTotalCost;
            totalInfluenceCost += optionTotalCost;
            totalFundsCost += optionTotalFundsCost;

            entries.Add(new ReinforcementPendingEntry(
                option.Id,
                option.Job,
                ResolveReinforcementOptionName(option),
                ResolveReinforcementOptionDescription(option),
                effectiveRequestedCount,
                unitInfluenceCost,
                unitFundsCost,
                unitInfluenceCost));
        }

        if (entries.Count == 0)
        {
            errorKey = "w40k-cmd-reinforcement-selection-empty";
            return false;
        }

        if (totalCount > ReinforcementMaxTotalCount)
        {
            errorKey = "w40k-cmd-reinforcement-total-cap-hit";
            return false;
        }

        return true;
    }

    private void CreatePendingRequest(
        TeamReinforcementRuntime runtime,
        WH40KCommandReinforcementRequestKind kind,
        IReadOnlyCollection<ReinforcementPendingEntry> entries,
        int totalCount,
        int totalCost,
        int totalFundsCost,
        int totalInfluenceCost,
        int delaySeconds)
    {
        runtime.PendingRequest = new TeamReinforcementPendingRequest
        {
            Kind = kind,
            ArrivalTime = _timing.CurTime + TimeSpan.FromSeconds(delaySeconds),
            TotalCount = totalCount,
            TotalCost = totalCost,
            TotalFundsCost = totalFundsCost,
            TotalInfluenceCost = totalInfluenceCost,
            Roles = entries.ToList()
        };
        runtime.NextAvailable = _timing.CurTime + TimeSpan.FromSeconds(ReinforcementSharedCooldownSeconds);
        runtime.NextAutoCheck = _timing.CurTime + TimeSpan.FromSeconds(ReinforcementAutoCheckIntervalSeconds);
    }

    private void TryProcessPendingRequest(string teamId, TeamReinforcementRuntime runtime, TimeSpan now)
    {
        var pending = runtime.PendingRequest;
        if (pending == null || pending.ArrivalTime > now)
            return;

        if (TrySpawnPendingRequest(teamId, pending, out _))
        {
            runtime.PendingRequest = null;
            return;
        }

        TryAdjustTeamFunds(null, teamId, pending.TotalFundsCost, "reinforcement-refund");
        _teamRule.TryAdjustTeamInfluence(teamId, pending.TotalInfluenceCost, out _, out _, source: "reinforcement-refund");
        runtime.PendingRequest = null;
    }

    private void TryProcessAutoRequest(string teamId, TeamReinforcementRuntime runtime, TimeSpan now)
    {
        if (!runtime.AutoConfig.Enabled)
            return;

        if (runtime.PendingRequest != null)
            return;

        if (runtime.NextAutoCheck > now)
            return;

        runtime.NextAutoCheck = now + TimeSpan.FromSeconds(ReinforcementAutoCheckIntervalSeconds);

        var phase = _teamRule.GetCurrentPhase();
        if (phase != WH40KBattlePhase.Assault)
            return;

        if (GetRemainingReinforcementCooldown(teamId) > 0)
            return;

        var snapshot = BuildTeamAliveSnapshot(teamId);
        if (snapshot.TotalCount <= 0 || snapshot.AlivePercent > runtime.AutoConfig.ThresholdPercent)
            return;

        if (!TryBuildPendingEntries(
                teamId,
                runtime.AutoConfig.Roles,
                true,
                ReinforcementRoleCapMode.TrimToAvailable,
                out var entries,
                out var totalCount,
                out var totalCost,
                out var totalFundsCost,
                out var totalInfluenceCost,
                out _))
            return;

        if (!TryGetTeamFunds(null, teamId, out var funds) || funds < totalFundsCost)
            return;

        if (!_teamRule.TryGetTeamInfluencePoints(teamId, out var influence) || influence < totalInfluenceCost)
            return;

        if (!TrySpendTeamFundsAndInfluence(null, teamId, totalFundsCost, totalInfluenceCost, "reinforcement-auto"))
            return;

        CreatePendingRequest(
            runtime,
            WH40KCommandReinforcementRequestKind.Auto,
            entries,
            totalCount,
            totalCost,
            totalFundsCost,
            totalInfluenceCost,
            ReinforcementAutoDelaySeconds);
    }

    private bool TrySpawnPendingRequest(string teamId, TeamReinforcementPendingRequest pending, out int spawnedCount)
    {
        spawnedCount = 0;
        EntityUid? teamNode = null;
        var nodeQuery = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (nodeQuery.MoveNext(out var uid, out var node))
        {
            if (!string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            teamNode = uid;
            break;
        }

        var preferredMapId = teamNode != null
            ? Transform(teamNode.Value).MapID
            : MapId.Nullspace;
        var commandPosition = teamNode != null
            ? _transform.ToMapCoordinates(Transform(teamNode.Value).Coordinates).Position
            : (Vector2?) null;

        if (!TryCollectReinforcementSpawnZones(teamId, preferredMapId, commandPosition, out var zones))
            return false;

        var station = teamNode != null ? _stations.GetOwningStation(teamNode.Value) : null;
        var zoneStart = zones.Count <= 1 ? 0 : _random.Next(zones.Count);
        foreach (var role in pending.Roles)
        {
            for (var i = 0; i < role.Count; i++)
            {
                var zone = zones[(zoneStart + spawnedCount) % zones.Count];
                var coordinates = _random.Pick(zone.Points);

                var profile = HumanoidCharacterProfile.RandomWithSpecies(HumanoidCharacterProfile.DefaultSpecies);
                var spawned = _stationSpawning.SpawnPlayerMob(coordinates, role.JobId, profile, station);
                ApplyPendingReinforcementEquipmentOverrides(teamId, role.RoleId, spawned);
                ApplySpawnedReinforcementTeamData(
                    spawned,
                    teamId,
                    role.JobId,
                    role.Name,
                    role.Description);
                _reinforcementAi.TryReadyWeapon(spawned);
                _reinforcementAi.Enable(spawned, coordinates);
                spawnedCount++;
            }
        }

        return spawnedCount > 0;
    }

    private bool TryCollectReinforcementSpawnZones(
        string teamId,
        MapId preferredMapId,
        Vector2? commandPosition,
        out List<ReinforcementSpawnZone> zones)
    {
        zones = new List<ReinforcementSpawnZone>();
        var candidates = new List<ReinforcementSpawnPointCandidate>();

        var query = EntityQueryEnumerator<WH40KReinforcementSpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out _, out var marker, out var xform))
        {
            if (!string.Equals(marker.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            var mapCoordinates = _transform.ToMapCoordinates(xform.Coordinates, logError: false);
            if (mapCoordinates.MapId == MapId.Nullspace)
                continue;

            candidates.Add(new ReinforcementSpawnPointCandidate(
                xform.Coordinates,
                mapCoordinates.MapId,
                mapCoordinates.Position));
        }

        if (candidates.Count == 0)
            return false;

        if (preferredMapId != MapId.Nullspace && candidates.Any(candidate => candidate.MapId == preferredMapId))
            candidates.RemoveAll(candidate => candidate.MapId != preferredMapId);

        if (commandPosition != null && candidates.Count > 1)
        {
            var command = commandPosition.Value;
            var nonTerminalCandidates = candidates
                .Where(candidate =>
                    candidate.MapId != preferredMapId ||
                    Vector2.DistanceSquared(candidate.WorldPosition, command) >= 0.01f)
                .ToList();

            // Bad marker placement should degrade, not silently delete paid reinforcements.
            if (nonTerminalCandidates.Count > 0)
                candidates = nonTerminalCandidates;
        }

        if (candidates.Count == 0)
            return false;

        BuildConnectedReinforcementSpawnZones(candidates, zones);
        return zones.Count > 0;
    }

    private static void BuildConnectedReinforcementSpawnZones(
        IReadOnlyList<ReinforcementSpawnPointCandidate> candidates,
        List<ReinforcementSpawnZone> zones)
    {
        var visited = new bool[candidates.Count];
        var queue = new Queue<int>();
        var maxDistanceSquared = ReinforcementSpawnZoneConnectDistance * ReinforcementSpawnZoneConnectDistance;

        for (var i = 0; i < candidates.Count; i++)
        {
            if (visited[i])
                continue;

            var zone = new ReinforcementSpawnZone();
            visited[i] = true;
            queue.Enqueue(i);

            while (queue.TryDequeue(out var currentIndex))
            {
                var current = candidates[currentIndex];
                zone.Points.Add(current.Coordinates);

                for (var otherIndex = 0; otherIndex < candidates.Count; otherIndex++)
                {
                    if (visited[otherIndex])
                        continue;

                    var other = candidates[otherIndex];
                    if (current.MapId != other.MapId)
                        continue;

                    if (Vector2.DistanceSquared(current.WorldPosition, other.WorldPosition) > maxDistanceSquared)
                        continue;

                    visited[otherIndex] = true;
                    queue.Enqueue(otherIndex);
                }
            }

            if (zone.Points.Count > 0)
                zones.Add(zone);
        }
    }

    private void ApplySpawnedReinforcementTeamData(
        EntityUid entity,
        string teamId,
        ProtoId<JobPrototype> jobId,
        string roleName,
        string roleDescription)
    {
        var teamMember = EnsureComp<WH40KTeamMemberComponent>(entity);
        teamMember.TeamId = teamId;
        _teamNpcFactions.ApplyTeamFaction(entity, teamId);

        var icon = EnsureComp<WH40KTeamBattleFactionIconComponent>(entity);
        if (!string.Equals(icon.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            icon.TeamId = teamId;
            Dirty(entity, icon);
        }

        var rewardState = EnsureComp<WH40KReinforcementRewardStateComponent>(entity);
        rewardState.WasClaimedByPlayer = false;
        rewardState.ClaimedUserId = null;

        EnsureComp<GhostTakeoverAvailableComponent>(entity);
        var ghostRole = EnsureComp<GhostRoleComponent>(entity);
        ghostRole.JobProto = jobId;
        ghostRole.RoleName = ResolveLocalizedOrRaw(roleName);
        ghostRole.RoleDescription = ResolveLocalizedOrRaw(roleDescription);
        ghostRole.RaffleConfig ??= new GhostRoleRaffleConfig(new GhostRoleRaffleSettings
        {
            InitialDuration = ReinforcementRaffleDurationSeconds,
            JoinExtendsDurationBy = 0,
            MaxDuration = ReinforcementRaffleDurationSeconds
        });
        EnsureComp<WH40KReinforcementGhostRoleOneShotComponent>(entity);
    }

    private void ApplyPendingReinforcementEquipmentOverrides(string teamId, string roleId, EntityUid entity)
    {
        if (!TryResolveReinforcementProfileForTeam(teamId, out var profile) ||
            !TryResolveReinforcementOption(profile, roleId, out var option))
        {
            return;
        }

        ApplyReinforcementEquipmentOverrides(entity, option);
    }

    private WH40KCommandReinforcementBoundUserInterfaceState BuildReinforcementUiState(EntityUid? sourceUid, string teamId, string teamName)
    {
        _teamRule.TryGetTeamCommandPoints(teamId, out var commandPoints);
        _teamRule.TryGetTeamInfluencePoints(teamId, out var influencePoints);
        TryGetTeamFunds(sourceUid, teamId, out var funds);
        var runtime = GetOrCreateTeamReinforcementRuntime(teamId);
        var currentPhase = _teamRule.GetCurrentPhase();
        var snapshot = BuildTeamAliveSnapshot(teamId);
        var catalog = BuildReinforcementCatalogStates(teamId);
        var autoConfig = BuildAutoConfigState(runtime.AutoConfig, catalog);
        var pending = BuildPendingRequestState(runtime.PendingRequest);

        return new WH40KCommandReinforcementBoundUserInterfaceState(
            teamId,
            teamName,
            currentPhase,
            commandPoints,
            influencePoints,
            funds,
            GetRemainingReinforcementCooldown(teamId),
            ReinforcementManualDelaySeconds,
            ReinforcementAutoDelaySeconds,
            ReinforcementAutoCheckIntervalSeconds,
            ReinforcementMaxTotalCount,
            snapshot.AliveCount,
            snapshot.TotalCount,
            snapshot.AlivePercent,
            catalog,
            autoConfig,
            pending);
    }

    private WH40KCommandReinforcementCatalogEntryState[] BuildReinforcementCatalogStates(string teamId)
    {
        if (!TryResolveReinforcementProfileForTeam(teamId, out var profile) || profile.Options.Count == 0)
            return Array.Empty<WH40KCommandReinforcementCatalogEntryState>();

        var currentRoleCounts = BuildCurrentReinforcementRoleCounts(teamId, profile);
        var catalog = new List<WH40KCommandReinforcementCatalogEntryState>(profile.Options.Count);
        foreach (var option in profile.Options
                     .Where(option => !string.IsNullOrWhiteSpace(option.Id))
                     .Where(option => IsReinforcementOptionUnlocked(teamId, option))
                     .OrderBy(option => option.SortOrder)
                     .ThenBy(option => ResolveReinforcementOptionName(option), StringComparer.OrdinalIgnoreCase))
        {
            var unitInfluenceCost = GetReinforcementUnitCost(option);
            catalog.Add(new WH40KCommandReinforcementCatalogEntryState(
                option.Id,
                ResolveReinforcementOptionName(option),
                ResolveReinforcementOptionDescription(option),
                string.IsNullOrWhiteSpace(option.GroupKey) ? ReinforcementDefaultGroupKey : option.GroupKey,
                BuildReinforcementEquipmentSummary(option),
                option.PreviewPrototype.ToString(),
                unitInfluenceCost,
                WH40KCommandEconomyCalculator.GetReinforcementFundsCost(unitInfluenceCost),
                unitInfluenceCost,
                GetReinforcementRoleCap(option),
                Math.Max(0, currentRoleCounts.GetValueOrDefault(option.Id)),
                option.AllowAuto));
        }

        return catalog.ToArray();
    }

    private Dictionary<string, int> BuildCurrentReinforcementRoleCounts(
        string teamId,
        WH40KCommandReinforcementProfilePrototype profile)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var optionIdsByJob = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in profile.Options)
        {
            if (string.IsNullOrWhiteSpace(option.Id))
                continue;

            counts[option.Id] = 0;

            var jobId = option.Job.ToString();
            if (!optionIdsByJob.TryGetValue(jobId, out var optionIds))
            {
                optionIds = new List<string>();
                optionIdsByJob[jobId] = optionIds;
            }

            optionIds.Add(option.Id);
        }

        var teamMembers = EntityQueryEnumerator<WH40KTeamMemberComponent>();
        while (teamMembers.MoveNext(out var memberUid, out var teamMember))
        {
            if (!string.Equals(teamMember.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_mobState.IsAlive(memberUid) && !_mobState.IsCritical(memberUid))
                continue;

            var (roleId, _) = GetRoleInfo(memberUid, teamId);
            if (!optionIdsByJob.TryGetValue(roleId, out var optionIds))
                continue;

            foreach (var optionId in optionIds)
            {
                counts[optionId] = counts.GetValueOrDefault(optionId) + 1;
            }
        }

        return counts;
    }

    private WH40KCommandReinforcementAutoConfigState BuildAutoConfigState(
        TeamReinforcementAutoConfig config,
        IReadOnlyCollection<WH40KCommandReinforcementCatalogEntryState> catalog)
    {
        var validRoles = new List<WH40KCommandReinforcementDraftEntry>(config.Roles.Count);
        var totalCount = 0;
        var totalCost = 0;
        var totalFundsCost = 0;
        var totalInfluenceCost = 0;
        foreach (var role in config.Roles)
        {
            if (string.IsNullOrWhiteSpace(role.RoleId) || role.Count <= 0)
                continue;

            var catalogEntry = catalog.FirstOrDefault(x => string.Equals(x.RoleId, role.RoleId, StringComparison.OrdinalIgnoreCase));
            if (catalogEntry == null)
                continue;

            var clampedCount = Math.Clamp(role.Count, 1, catalogEntry.PerRoleCap);
            validRoles.Add(new WH40KCommandReinforcementDraftEntry(role.RoleId, clampedCount));
            totalCount += clampedCount;
            totalCost += clampedCount * catalogEntry.UnitCost;
            totalFundsCost += clampedCount * catalogEntry.UnitFundsCost;
            totalInfluenceCost += clampedCount * catalogEntry.UnitInfluenceCost;
        }

        return new WH40KCommandReinforcementAutoConfigState(
            config.Enabled,
            Math.Clamp(config.ThresholdPercent, ReinforcementMinAutoThresholdPercent, ReinforcementMaxAutoThresholdPercent),
            totalCount,
            totalCost,
            totalFundsCost,
            totalInfluenceCost,
            validRoles.ToArray());
    }

    private WH40KCommandReinforcementPendingRequestState? BuildPendingRequestState(TeamReinforcementPendingRequest? pending)
    {
        if (pending == null)
            return null;

        var roles = pending.Roles
            .Select(role => new WH40KCommandReinforcementPendingRoleState(
                role.RoleId,
                role.Name,
                role.Count,
                role.UnitCost,
                role.UnitCost * role.Count,
                role.UnitFundsCost,
                role.UnitInfluenceCost,
                role.UnitFundsCost * role.Count,
                role.UnitInfluenceCost * role.Count))
            .ToArray();

        return new WH40KCommandReinforcementPendingRequestState(
            pending.Kind,
            Math.Max(0, (int) Math.Ceiling((pending.ArrivalTime - _timing.CurTime).TotalSeconds)),
            pending.TotalCount,
            pending.TotalCost,
            pending.TotalFundsCost,
            pending.TotalInfluenceCost,
            roles);
    }

    private TeamAliveSnapshot BuildTeamAliveSnapshot(string teamId)
    {
        if (_teamRule.TryGetTeamAliveSnapshot(teamId, out var activeAlive, out var activeTotal))
            return new TeamAliveSnapshot(activeAlive, activeTotal);

        var alive = 0;
        var total = 0;

        var query = EntityQueryEnumerator<WH40KTeamMemberComponent>();
        while (query.MoveNext(out var uid, out var member))
        {
            if (!string.Equals(member.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryComp<WH40KReinforcementRewardStateComponent>(uid, out var rewardState) &&
                !rewardState.WasClaimedByPlayer)
            {
                continue;
            }

            total++;
            if (_mobState.IsAlive(uid))
                alive++;
        }

        return new TeamAliveSnapshot(alive, total);
    }

    private int GetRemainingReinforcementCooldown(string teamId)
    {
        var runtime = GetOrCreateTeamReinforcementRuntime(teamId);
        if (_timing.CurTime >= runtime.NextAvailable)
            return 0;

        return (int) Math.Ceiling((runtime.NextAvailable - _timing.CurTime).TotalSeconds);
    }

    private int GetMinimumReinforcementCost(string teamId, int fallback)
    {
        if (!TryResolveReinforcementProfileForTeam(teamId, out var profile) || profile.Options.Count == 0)
            return Math.Max(1, fallback);

        var min = int.MaxValue;
        foreach (var option in profile.Options)
        {
            if (!IsReinforcementOptionUnlocked(teamId, option))
                continue;

            min = Math.Min(min, GetReinforcementUnitCost(option));
        }

        return min == int.MaxValue
            ? Math.Max(1, fallback)
            : min;
    }

    private int GetReinforcementUnitCost(WH40KCommandReinforcementOptionPrototype option)
    {
        return Math.Max(1, option.BaseCost);
    }

    private static int GetReinforcementRoleCap(WH40KCommandReinforcementOptionPrototype option)
    {
        return Math.Clamp(option.MaxCount, 1, ReinforcementMaxTotalCount);
    }

    private string ResolveReinforcementOptionName(WH40KCommandReinforcementOptionPrototype option)
    {
        if (!string.IsNullOrWhiteSpace(option.NameKey))
            return option.NameKey;

        if (_proto.TryIndex<JobPrototype>(option.Job, out var job))
            return job.Name;

        return option.Id;
    }

    private string ResolveReinforcementOptionDescription(WH40KCommandReinforcementOptionPrototype option)
    {
        if (!string.IsNullOrWhiteSpace(option.DescriptionKey))
            return option.DescriptionKey;

        if (_proto.TryIndex<JobPrototype>(option.Job, out var job))
            return job.Description ?? option.Id;

        return option.Id;
    }
}
