using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Cargo.Components;
using Content.Server.Chat.Managers;
using Content.Server.Destructible;
using Content.Server.GameTicking;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.OreExtractor.Components;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.Command.Pinpointer;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.Influence;
using Content.Shared.Damage.Components;
using Content.Shared.Ghost;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Materials;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Storage.Components;
using Content.Shared.Tools.Components;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Command;

/// <summary>
/// Runtime scheduler for team-random events and dynamic missions.
/// Phase-3 adds gameplay-effect routing, map mission objectives, and reward application pipeline.
/// </summary>
public sealed class WH40KCommandEventMissionRuntimeSystem : EntitySystem
{
    private const string EventTeamMapId = "WH40KCommandTeamRandomEventTeamMap";
    private const string EventDefaultProfileId = "WH40KCommandTeamRandomEventProfileDefault";
    private const string DynamicMissionTeamMapId = "WH40KCommandDynamicMissionTeamMap";
    private const string DynamicMissionDefaultProfileId = "WH40KCommandDynamicMissionProfileDefault";

    private const string MissionZoneBeaconPrototypeId = "WH40KMissionZoneBeacon";
    private const string MissionAirdropParachutePrototypeId = "WH40KMissionAirdropParachute";
    private const string MissionCargoCratePrototypeId = "WH40KMissionCargoCrate";
    private const string MissionCargoCrateDeliveryPrototypeId = "WH40KMissionCargoCrateDelivery";
    private const string TacticalCallDiscountTokenId = "tactical_call_discount";
    private const string IntelEventRollHasteTokenId = "intel_event_roll_haste";
    private const int IntelCounterJamDelaySeconds = 60;
    private const int IntelCounterJamActiveEventReductionSeconds = 30;
    private const int TechArchiveTacticalDiscountSeconds = 120;

    private static readonly HashSet<string> IntelCounterJamMissionIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "imp_vox_litany_relay",
            "her_heresy_broadcast",
            "global_radar_tower_uplink"
        };

    private static readonly HashSet<string> TechMissionOfferRefreshMissionIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "global_tactical_archive_recovery"
        };

    private const int CooldownPreviewLimit = 3;

    private const float MissionZoneRadius = 4.5f;
    private const float MissionZoneProgressGoal = 100f;
    private const float MissionZoneProgressPerSecond = 5f;
    private const float MissionZoneDecayPerSecond = 1.2f;

    private const float MissionCargoDeliveryRadius = 5.0f;
    private const float MissionParachuteTravelSeconds = 6f;
    private const float MissionParachuteSpawnHeight = 11f;
    private const float MissionBannerDetectionRadius = 1.45f;
    private const float MissionDeliveryMarkerRadius = 5.0f;
    private const float MissionAirdropMinDistanceFromCommandNode = 18f;
    private const float MissionCargoDebugLogIntervalSeconds = 4.0f;

    private enum MissionObjectiveType : byte
    {
        ZoneControl = 0,
        CargoDelivery = 1,
        BannerHold = 2
    }

    private enum MissionOutcomeTier : byte
    {
        Major = 0,
        Minor = 1,
        Timeout = 2,
        Failure = 3
    }

    private enum TeamEventMissionBias : byte
    {
        None = 0,
        Momentum = 1,
        Stabilizer = 2
    }

    public readonly record struct WH40KCommandFactionMissionOffer(
        string MissionId,
        string Title,
        string Description,
        int DurationSeconds,
        int RewardMajorDevelopmentPoints,
        int RewardMinorDevelopmentPoints,
        int RewardTimeoutDevelopmentPoints,
        int RewardFailureDevelopmentPoints,
        int RewardTempoBonusPercent,
        string RewardTokenId,
        int RewardTokenDurationSeconds);

    public readonly record struct WH40KCommandEnemyCounterMission(
        string MissionId,
        string Title,
        string Description);

    public readonly record struct WH40KMissionPinpointerTargetState(
        bool HasActiveMission,
        string MissionId,
        WH40KMissionObjectiveType ObjectiveType,
        WH40KCommandDynamicMissionScope Scope,
        EntityUid? TargetUid,
        string TargetName);

    private sealed class TeamEventGameplayProfile
    {
        public float OutgoingDamageMultiplier = 1f;
        public float IncomingDamageMultiplier = 1f;
        public float MedicalDelayMultiplier = 1f;
        public float ConstructionDelayMultiplier = 1f;
        public bool IgnorePullSlowdown = false;

        public float MissionProgressMultiplier = 1f;
        public float EnemyMissionProgressMultiplier = 1f;

        public int PeriodicDevelopmentPoints;
        public int PeriodicIntervalSeconds;

        public float CooldownAccelerationPerSecond;

        public bool HasEntityModifiers =>
            Math.Abs(OutgoingDamageMultiplier - 1f) > 0.0001f ||
            Math.Abs(IncomingDamageMultiplier - 1f) > 0.0001f ||
            Math.Abs(MedicalDelayMultiplier - 1f) > 0.0001f ||
            Math.Abs(ConstructionDelayMultiplier - 1f) > 0.0001f ||
            IgnorePullSlowdown;
    }

    private sealed class MissionObjectiveRuntime
    {
        public MissionObjectiveType Type;
        public MapCoordinates Anchor;

        public EntityUid? BeaconUid;
        public EntityUid? CargoUid;
        public EntityUid? ParachuteUid;

        public float Radius;
        public float ProgressGoal;
        public float ProgressPerSecond;

        public bool CargoSpawned;
        public Vector2 ParachuteStartWorld;
        public Vector2 ParachuteTargetWorld;
        public TimeSpan ParachuteStartAt;
        public TimeSpan ParachuteEndAt;
        public List<EntityUid> DeliveryMarkerUids = new();
        public TimeSpan LastCargoDebugLogAt = TimeSpan.Zero;

        public readonly Dictionary<string, float> TeamProgress =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct CargoDeliveryDebugSnapshot(
        int NodeCount,
        int SameMapNodeCount,
        string ClosestNodeTeamId,
        float ClosestNodeDistance,
        bool ClosestInsideRadius,
        MapId CargoMapId,
        Vector2 CargoWorld);

    private sealed class ActiveEventRuntime
    {
        public string EventId = string.Empty;
        public string Title = string.Empty;
        public string Description = string.Empty;
        public int DurationSeconds;
        public TimeSpan StartedAt;
        public TimeSpan EndsAt;
    }

    private sealed class TeamEventRuntime
    {
        public string TeamId = string.Empty;
        public TimeSpan NextRollAt = TimeSpan.Zero;
        public string LastEventId = string.Empty;
        public ActiveEventRuntime? ActiveEvent;
        public Dictionary<string, TimeSpan> CooldownEnds = new(StringComparer.OrdinalIgnoreCase);

        public string AppliedEventId = string.Empty;
        public TimeSpan LastGameplayPulseAt = TimeSpan.Zero;
        public TeamEventMissionBias PendingBias = TeamEventMissionBias.None;
    }

    private sealed class ActiveMissionRuntime
    {
        public string MissionId = string.Empty;
        public string Title = string.Empty;
        public string Description = string.Empty;
        public WH40KCommandDynamicMissionScope Scope;
        public string TeamId = string.Empty;
        public int DurationSeconds;
        public TimeSpan StartedAt;
        public TimeSpan EndsAt;

        public int RewardMajorDevelopmentPoints;
        public int RewardMinorDevelopmentPoints;
        public int RewardTimeoutDevelopmentPoints;
        public int RewardFailureDevelopmentPoints;
        public int RewardTempoBonusPercent;
        public string RewardTokenId = string.Empty;
        public int RewardTokenDurationSeconds;
        public WH40KCommandMissionObjectiveType ObjectiveType = WH40KCommandMissionObjectiveType.Auto;
        public List<string> RequiredObjectiveEntityPrototypes = new();

        public List<string> Tags = new();

        public MissionObjectiveRuntime? Objective;
        public TimeSpan LastProgressTick = TimeSpan.Zero;
    }

    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedRoofSystem _roof = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<string, TeamEventRuntime> _teamEvents =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TimeSpan> _nextTeamMissionRollAt =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActiveMissionRuntime?> _teamMissions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TimeSpan> _globalMissionCooldownEnds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, TimeSpan>> _teamMissionCooldownEnds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingFactionMissionOfferRefreshTeams =
        new(StringComparer.OrdinalIgnoreCase);
    private ISawmill _sawmill = default!;

    private ActiveMissionRuntime? _globalMission;
    private TimeSpan _nextGlobalMissionRollAt = TimeSpan.Zero;
    private TimeSpan _nextRuntimeTickAt = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.mission-runtime");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextRuntimeTickAt)
            return;

        _nextRuntimeTickAt = now + TimeSpan.FromSeconds(1);

        var teamIds = _teamRule.GetTeamIds();
        if (teamIds.Count == 0)
        {
            ResetRuntime();
            return;
        }

        EnsureRuntimeState(teamIds, now);
        PruneRemovedTeams(teamIds);
        RemoveExpiredMissionCooldowns(now);

        UpdateTeamEvents(teamIds, now);
        UpdateTeamEventGameplayEffects(teamIds, now);

        UpdateGlobalMission(teamIds, now);
        UpdateTeamMissions(teamIds, now);
    }

    public WH40KCommandTeamEventRuntimeState BuildTeamEventRuntimeState(string teamId)
    {
        var now = _timing.CurTime;
        if (string.IsNullOrWhiteSpace(teamId) || !_teamEvents.TryGetValue(teamId, out var runtime))
            return CreateInactiveTeamEventState();

        if (!TryResolveEventProfileForTeam(teamId, out var profile))
            return CreateInactiveTeamEventState();

        var active = runtime.ActiveEvent;
        var hasActive = active is not null && active.EndsAt > now;
        var activeRemainingSeconds = hasActive
            ? Math.Max(0, (int) Math.Ceiling((active!.EndsAt - now).TotalSeconds))
            : 0;
        var activeDurationSeconds = hasActive ? Math.Max(1, active!.DurationSeconds) : 0;
        var activeId = hasActive ? active!.EventId : string.Empty;
        var activeTitle = hasActive ? active!.Title : string.Empty;
        var activeDescription = hasActive ? active!.Description : string.Empty;
        var nextRollSeconds = runtime.NextRollAt > now
            ? Math.Max(0, (int) Math.Ceiling((runtime.NextRollAt - now).TotalSeconds))
            : 0;

        var eventConfigById = BuildEventConfigMap(profile);
        var cooldowns = new List<WH40KCommandEventCooldownRuntimeState>(runtime.CooldownEnds.Count);
        foreach (var (eventId, cooldownEndAt) in runtime.CooldownEnds)
        {
            if (cooldownEndAt <= now)
                continue;

            var remainingSeconds = Math.Max(0, (int) Math.Ceiling((cooldownEndAt - now).TotalSeconds));
            var title = eventConfigById.TryGetValue(eventId, out var config)
                ? ResolveEventTitle(config)
                : eventId;
            var description = eventConfigById.TryGetValue(eventId, out var configDesc)
                ? ResolveEventDescription(configDesc)
                : string.Empty;
            cooldowns.Add(new WH40KCommandEventCooldownRuntimeState(eventId, title, description, remainingSeconds));
        }

        var cooldownPreview = cooldowns
            .OrderBy(entry => entry.RemainingSeconds)
            .Take(CooldownPreviewLimit)
            .ToArray();

        return new WH40KCommandTeamEventRuntimeState(
            hasProfile: true,
            hasActiveEvent: hasActive,
            activeEventId: activeId,
            activeEventTitle: activeTitle,
            activeEventDescription: activeDescription,
            activeRemainingSeconds: activeRemainingSeconds,
            activeDurationSeconds: activeDurationSeconds,
            nextRollSeconds: nextRollSeconds,
            cooldowns: cooldownPreview);
    }

    public WH40KCommandMissionRuntimeState BuildGlobalMissionRuntimeState()
    {
        return BuildMissionRuntimeState(_globalMission, _timing.CurTime);
    }

    public WH40KCommandMissionRuntimeState BuildTeamMissionRuntimeState(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId) || !_teamMissions.TryGetValue(teamId, out var mission))
            return CreateInactiveMissionState();

        return BuildMissionRuntimeState(mission, _timing.CurTime);
    }

    public bool TryGetMissionPinpointerTarget(
        string teamId,
        WH40KMissionPinpointerPreset preset,
        bool includeGlobalFallback,
        out WH40KMissionPinpointerTargetState state)
    {
        state = CreateInactiveMissionPinpointerState(preset);

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        var now = _timing.CurTime;
        if (!TryResolveMissionForPinpointer(teamId, preset, includeGlobalFallback, now, out var mission))
            return false;

        state = BuildMissionPinpointerTargetState(mission, preset);
        return true;
    }

    public WH40KCommandEnemyCounterMission[] BuildEnemyFactionCounterMissions(string observerTeamId)
    {
        if (string.IsNullOrWhiteSpace(observerTeamId))
            return Array.Empty<WH40KCommandEnemyCounterMission>();

        var now = _timing.CurTime;
        var result = new List<WH40KCommandEnemyCounterMission>();
        foreach (var (teamId, mission) in _teamMissions)
        {
            if (mission is null || mission.EndsAt <= now)
                continue;

            if (string.Equals(teamId, observerTeamId, StringComparison.OrdinalIgnoreCase))
                continue;

            var objectiveType = mission.Objective?.Type ?? ResolveObjectiveType(mission);
            var description = objectiveType == MissionObjectiveType.CargoDelivery
                ? Loc.GetString(
                    "wh40k-command-runtime-enemy-counter-cargo-description",
                    ("enemy", ResolveTeamName(teamId)))
                : Loc.GetString(
                    "wh40k-command-runtime-enemy-counter-control-description",
                    ("enemy", ResolveTeamName(teamId)));

            result.Add(new WH40KCommandEnemyCounterMission(
                MissionId: $"enemy-counter-{mission.MissionId}",
                Title: Loc.GetString(
                    "wh40k-command-runtime-enemy-counter-title",
                    ("enemy", ResolveTeamName(teamId)),
                    ("mission", ResolveLocalizedOrRaw(mission.Title))),
                Description: description));
        }

        return result.ToArray();
    }

    public bool TryHandleFultonExtraction(EntityUid extractedEntity, string extractorTeamId, out string missionId)
    {
        missionId = string.Empty;

        if (string.IsNullOrWhiteSpace(extractorTeamId) || Deleted(extractedEntity))
            return false;

        var now = _timing.CurTime;
        var teamIds = _teamRule.GetTeamIds();
        if (teamIds.Count == 0)
            return false;

        if (IsActiveCargoMissionExtractionTarget(_globalMission, extractedEntity, now) &&
            _globalMission is { } globalMission)
        {
            ResolveCompletedMission(globalMission, teamIds, extractorTeamId, MissionOutcomeTier.Major, timedOut: false);
            missionId = globalMission.MissionId;
            _globalMission = null;

            if (TryResolveDynamicMissionProfileForTeam(string.Empty, out var profile) && profile.Enabled)
            {
                _nextGlobalMissionRollAt = now + TimeSpan.FromSeconds(
                    RollIntervalSeconds(profile.RespawnIntervalSecondsMin, profile.RespawnIntervalSecondsMax));
            }
            else
            {
                _nextGlobalMissionRollAt = now + TimeSpan.FromSeconds(600);
            }

            return true;
        }

        foreach (var (teamId, mission) in _teamMissions.ToArray())
        {
            if (!IsActiveCargoMissionExtractionTarget(mission, extractedEntity, now) || mission is null)
                continue;

            var outcome = string.Equals(teamId, extractorTeamId, StringComparison.OrdinalIgnoreCase)
                ? MissionOutcomeTier.Major
                : MissionOutcomeTier.Failure;

            ResolveCompletedMission(mission, teamIds, extractorTeamId, outcome, timedOut: false);
            missionId = mission.MissionId;
            _teamMissions[teamId] = null;
            _nextTeamMissionRollAt[teamId] = TimeSpan.Zero;
            TryApplyPendingFactionMissionOfferRefresh(teamId);
            return true;
        }

        return false;
    }

    private static bool IsActiveCargoMissionExtractionTarget(
        ActiveMissionRuntime? mission,
        EntityUid extractedEntity,
        TimeSpan now)
    {
        if (mission is null || mission.EndsAt <= now || mission.Objective is not { } objective)
            return false;

        var objectiveType = mission.Objective?.Type ?? ResolveObjectiveType(mission);
        if (objectiveType != MissionObjectiveType.CargoDelivery)
            return false;

        return objective.CargoUid is { } cargoUid && cargoUid == extractedEntity;
    }

    private bool TryResolveMissionForPinpointer(
        string teamId,
        WH40KMissionPinpointerPreset preset,
        bool includeGlobalFallback,
        TimeSpan now,
        out ActiveMissionRuntime mission)
    {
        mission = default!;

        if (_teamMissions.TryGetValue(teamId, out var teamMission) &&
            IsMissionEligibleForPinpointer(teamMission, teamId, preset, now))
        {
            mission = teamMission!;
            return true;
        }

        if (includeGlobalFallback &&
            IsMissionEligibleForPinpointer(_globalMission, teamId, preset, now))
        {
            mission = _globalMission!;
            return true;
        }

        return false;
    }

    private bool IsMissionEligibleForPinpointer(
        ActiveMissionRuntime? mission,
        string requestTeamId,
        WH40KMissionPinpointerPreset preset,
        TimeSpan now)
    {
        if (mission is null || mission.EndsAt <= now)
            return false;

        if (mission.Scope == WH40KCommandDynamicMissionScope.Faction &&
            !string.Equals(mission.TeamId, requestTeamId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var objectiveType = mission.Objective?.Type ?? ResolveObjectiveType(mission);
        return IsObjectiveTypeMatchPreset(objectiveType, preset);
    }

    private WH40KMissionPinpointerTargetState BuildMissionPinpointerTargetState(
        ActiveMissionRuntime mission,
        WH40KMissionPinpointerPreset preset)
    {
        var objectiveType = mission.Objective?.Type ?? ResolveObjectiveType(mission);
        var targetUid = ResolveMissionPinpointerTargetEntity(mission, objectiveType);
        var targetName = ResolveMissionPinpointerTargetName(mission, preset, targetUid is not null);

        return new WH40KMissionPinpointerTargetState(
            HasActiveMission: true,
            MissionId: mission.MissionId,
            ObjectiveType: ToPublicMissionObjectiveType(objectiveType),
            Scope: mission.Scope,
            TargetUid: targetUid,
            TargetName: targetName);
    }

    private EntityUid? ResolveMissionPinpointerTargetEntity(
        ActiveMissionRuntime mission,
        MissionObjectiveType objectiveType)
    {
        if (mission.Objective is not { } objective)
            return null;

        if (objectiveType == MissionObjectiveType.CargoDelivery)
        {
            if (objective.CargoUid is { } cargoUid && Exists(cargoUid))
                return cargoUid;

            if (objective.ParachuteUid is { } parachuteUid && Exists(parachuteUid))
                return parachuteUid;
        }

        if (objective.BeaconUid is { } beaconUid && Exists(beaconUid))
            return beaconUid;

        foreach (var markerUid in objective.DeliveryMarkerUids)
        {
            if (Exists(markerUid))
                return markerUid;
        }

        return null;
    }

    private string ResolveMissionPinpointerTargetName(
        ActiveMissionRuntime mission,
        WH40KMissionPinpointerPreset preset,
        bool hasTarget)
    {
        var title = ResolveLocalizedOrRaw(mission.Title);
        if (!string.IsNullOrWhiteSpace(title))
            return title;

        return hasTarget
            ? GetPinpointerPresetDisplayName(preset)
            : GetPinpointerPresetFallbackName(preset);
    }

    private static bool IsObjectiveTypeMatchPreset(MissionObjectiveType objectiveType, WH40KMissionPinpointerPreset preset)
    {
        return preset switch
        {
            WH40KMissionPinpointerPreset.Relay => objectiveType == MissionObjectiveType.ZoneControl,
            WH40KMissionPinpointerPreset.Cargo => objectiveType == MissionObjectiveType.CargoDelivery,
            WH40KMissionPinpointerPreset.Banner => objectiveType == MissionObjectiveType.BannerHold,
            _ => false
        };
    }

    private static string GetPinpointerPresetDisplayName(WH40KMissionPinpointerPreset preset)
    {
        return preset switch
        {
            WH40KMissionPinpointerPreset.Relay => "Mission relay objective",
            WH40KMissionPinpointerPreset.Cargo => "Mission cargo objective",
            WH40KMissionPinpointerPreset.Banner => "Mission banner objective",
            _ => "Mission objective"
        };
    }

    private static string GetPinpointerPresetFallbackName(WH40KMissionPinpointerPreset preset)
    {
        return preset switch
        {
            WH40KMissionPinpointerPreset.Relay => "Relay mission target unavailable",
            WH40KMissionPinpointerPreset.Cargo => "Cargo mission target unavailable",
            WH40KMissionPinpointerPreset.Banner => "Banner mission target unavailable",
            _ => "Mission target unavailable"
        };
    }

    private static WH40KMissionPinpointerTargetState CreateInactiveMissionPinpointerState(WH40KMissionPinpointerPreset preset)
    {
        return new WH40KMissionPinpointerTargetState(
            HasActiveMission: false,
            MissionId: string.Empty,
            ObjectiveType: WH40KMissionObjectiveType.Unknown,
            Scope: WH40KCommandDynamicMissionScope.Global,
            TargetUid: null,
            TargetName: GetPinpointerPresetFallbackName(preset));
    }

    public WH40KCommandFactionMissionOffer[] RollFactionMissionOffers(
        string teamId,
        int count,
        IReadOnlyCollection<string>? excludedMissionIds = null)
    {
        if (string.IsNullOrWhiteSpace(teamId) || count <= 0)
            return Array.Empty<WH40KCommandFactionMissionOffer>();

        if (!TryResolveDynamicMissionProfileForTeam(teamId, out var profile) || !profile.Enabled)
            return Array.Empty<WH40KCommandFactionMissionOffer>();

        var teamIds = GetLiveTeamIds(teamId);
        var excluded = excludedMissionIds is not null && excludedMissionIds.Count > 0
            ? new HashSet<string>(
                excludedMissionIds.Where(static id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase)
            : null;
        var now = _timing.CurTime;

        var pool = BuildWeightedMissionCandidates(
                profile,
                WH40KCommandDynamicMissionScope.Faction,
                teamId,
                teamIds,
                now)
            .Where(candidate => excluded is null || !excluded.Contains(candidate.Mission.Id))
            .ToList();

        var offers = new List<WH40KCommandFactionMissionOffer>(Math.Min(count, pool.Count));
        while (offers.Count < count && pool.Count > 0)
        {
            if (!TryPickWeighted(pool, out var selected))
                break;

            offers.Add(BuildFactionMissionOffer(selected));
            pool.RemoveAll(entry => string.Equals(entry.Mission.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        }

        return offers.ToArray();
    }

    public bool TryGetFactionMissionOffer(string teamId, string missionId, out WH40KCommandFactionMissionOffer offer)
    {
        offer = default;
        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(missionId))
            return false;

        if (!TryResolveDynamicMissionProfileForTeam(teamId, out var profile) || !profile.Enabled)
            return false;

        var teamIds = GetLiveTeamIds(teamId);
        var now = _timing.CurTime;
        if (!TryFindFactionMissionConfig(profile, teamId, missionId, teamIds, now, out var config))
            return false;

        offer = BuildFactionMissionOffer(config);
        return true;
    }

    public bool TryStartFactionMission(string teamId, string missionId, out WH40KCommandMissionRuntimeState startedMission)
    {
        startedMission = CreateInactiveMissionState();
        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(missionId))
            return false;

        if (!TryResolveDynamicMissionProfileForTeam(teamId, out var profile) || !profile.Enabled)
            return false;

        var teamIds = GetLiveTeamIds(teamId);
        var now = _timing.CurTime;
        EnsureRuntimeState(teamIds, now);
        PruneRemovedTeams(teamIds);

        if (_teamMissions.TryGetValue(teamId, out var activeMission) && activeMission is not null)
            return false;

        if (!TryFindFactionMissionConfig(profile, teamId, missionId, teamIds, now, out var config))
            return false;

        var mission = BuildActiveMissionRuntime(config, teamId, teamIds, now);
        _teamMissions[teamId] = mission;
        _nextTeamMissionRollAt[teamId] = TimeSpan.Zero;
        AnnounceMissionStarted(mission);
        startedMission = BuildMissionRuntimeState(mission, now);
        return true;
    }

    private void UpdateTeamEvents(IReadOnlyList<string> teamIds, TimeSpan now)
    {
        foreach (var teamId in teamIds)
        {
            if (!_teamEvents.TryGetValue(teamId, out var runtime))
                continue;

            if (!TryResolveEventProfileForTeam(teamId, out var profile))
                continue;

            if (runtime.ActiveEvent is { } active && active.EndsAt <= now)
                runtime.ActiveEvent = null;

            RemoveExpiredCooldowns(runtime, now);

            if (runtime.NextRollAt == TimeSpan.Zero)
                runtime.NextRollAt = now + TimeSpan.FromSeconds(
                    RollIntervalSeconds(profile.RollIntervalSecondsMin, profile.RollIntervalSecondsMax));

            if (runtime.ActiveEvent is not null)
                continue;

            if (now < runtime.NextRollAt)
                continue;

            if (TryRollTeamEvent(profile, runtime, teamIds, now))
                AnnounceTeamEventStarted(teamId, runtime.ActiveEvent!);

            runtime.NextRollAt = now + TimeSpan.FromSeconds(
                RollIntervalSeconds(profile.RollIntervalSecondsMin, profile.RollIntervalSecondsMax));
        }
    }

    private void UpdateGlobalMission(IReadOnlyList<string> teamIds, TimeSpan now)
    {
        if (!TryResolveDynamicMissionProfileForTeam(string.Empty, out var profile) || !profile.Enabled)
            return;

        if (_globalMission is { } activeGlobal)
        {
            if (TryUpdateMissionObjective(activeGlobal, teamIds, now, out var winnerTeamId, out var outcomeTier))
            {
                ResolveCompletedMission(activeGlobal, teamIds, winnerTeamId, outcomeTier, timedOut: false);
                _globalMission = null;
                _nextGlobalMissionRollAt = now + TimeSpan.FromSeconds(
                    RollIntervalSeconds(profile.RespawnIntervalSecondsMin, profile.RespawnIntervalSecondsMax));
            }
            else if (activeGlobal.EndsAt <= now)
            {
                ResolveGlobalMissionTimeout(activeGlobal, teamIds);
                _globalMission = null;
                _nextGlobalMissionRollAt = now + TimeSpan.FromSeconds(
                    RollIntervalSeconds(profile.RespawnIntervalSecondsMin, profile.RespawnIntervalSecondsMax));
            }
        }

        if (_nextGlobalMissionRollAt == TimeSpan.Zero)
            _nextGlobalMissionRollAt = now + TimeSpan.FromSeconds(
                Math.Max(1, profile.FirstSpawnAfterRoundStartSeconds));

        if (_globalMission is not null || now < _nextGlobalMissionRollAt)
            return;

        if (TryRollMission(profile, WH40KCommandDynamicMissionScope.Global, string.Empty, teamIds, now, out var rolledMission))
        {
            _globalMission = rolledMission;
            AnnounceMissionStarted(rolledMission);
            return;
        }

        _nextGlobalMissionRollAt = now + TimeSpan.FromSeconds(
            RollIntervalSeconds(profile.RespawnIntervalSecondsMin, profile.RespawnIntervalSecondsMax));
    }

    private void UpdateTeamMissions(IReadOnlyList<string> teamIds, TimeSpan now)
    {
        foreach (var teamId in teamIds)
        {
            if (!TryResolveDynamicMissionProfileForTeam(teamId, out var profile) || !profile.Enabled)
                continue;

            var activeTeamMission = _teamMissions.GetValueOrDefault(teamId);
            if (activeTeamMission is null)
                continue;

            if (TryUpdateMissionObjective(activeTeamMission, teamIds, now, out var winnerTeamId, out var outcomeTier))
            {
                ResolveCompletedMission(activeTeamMission, teamIds, winnerTeamId, outcomeTier, timedOut: false);
                _teamMissions[teamId] = null;
                _nextTeamMissionRollAt[teamId] = TimeSpan.Zero;
                TryApplyPendingFactionMissionOfferRefresh(teamId);
                continue;
            }

            if (activeTeamMission.EndsAt <= now)
            {
                ResolveFactionMissionTimeout(activeTeamMission);
                _teamMissions[teamId] = null;
                _nextTeamMissionRollAt[teamId] = TimeSpan.Zero;
                TryApplyPendingFactionMissionOfferRefresh(teamId);
            }
        }
    }

    private bool TryRollTeamEvent(
        WH40KCommandTeamRandomEventProfilePrototype profile,
        TeamEventRuntime runtime,
        IReadOnlyList<string> teamIds,
        TimeSpan now)
    {
        var phase = _teamRule.GetCurrentPhase();
        var teamId = runtime.TeamId;
        var doctrineId = ResolveActiveDoctrineId(teamId);
        var levelGap = GetTrailingLevelGap(teamId, teamIds);

        var weightedCandidates = new List<(WH40KCommandTeamRandomEventConfig Event, float Weight)>();
        foreach (var eventConfig in profile.Events)
        {
            if (string.IsNullOrWhiteSpace(eventConfig.Id))
                continue;

            if (profile.AntiRepeat &&
                !string.IsNullOrWhiteSpace(runtime.LastEventId) &&
                string.Equals(runtime.LastEventId, eventConfig.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (runtime.CooldownEnds.TryGetValue(eventConfig.Id, out var cooldownEndAt) && cooldownEndAt > now)
                continue;

            if (eventConfig.AllowedPhases.Count > 0 && !eventConfig.AllowedPhases.Contains(phase))
                continue;

            if (!AreSubsystemRequirementsSatisfied(teamId, eventConfig.RequiredSubsystems))
                continue;

            var weight = Math.Max(0.001f, eventConfig.BaseWeight);
            if (!string.IsNullOrWhiteSpace(doctrineId) &&
                eventConfig.DoctrineWeightBias.TryGetValue(doctrineId, out var doctrineBias))
            {
                weight *= Math.Max(0.05f, doctrineBias);
            }

            if (levelGap > 0)
            {
                var trailingBonus = Math.Min(
                    Math.Max(0f, eventConfig.MaxTrailingWeightBonus),
                    levelGap * Math.Max(0f, eventConfig.TrailingWeightBonusPerLevelGap));
                weight *= Math.Max(0.01f, 1f + trailingBonus);
            }

            weight *= GetEventBiasWeight(runtime.PendingBias, eventConfig.Tags);

            if (weight <= 0f)
                continue;

            weightedCandidates.Add((eventConfig, weight));
        }

        if (weightedCandidates.Count == 0)
            return false;

        if (!TryPickWeighted(weightedCandidates, out var selected))
            return false;

        var durationSeconds = Math.Max(1, selected.DurationSeconds);
        var cooldownSeconds = Math.Max(1, selected.CooldownSeconds);
        var endAt = now + TimeSpan.FromSeconds(durationSeconds);

        runtime.ActiveEvent = new ActiveEventRuntime
        {
            EventId = selected.Id,
            Title = ResolveEventTitle(selected),
            Description = ResolveEventDescription(selected),
            DurationSeconds = durationSeconds,
            StartedAt = now,
            EndsAt = endAt
        };

        runtime.CooldownEnds[selected.Id] = endAt + TimeSpan.FromSeconds(cooldownSeconds);
        runtime.LastEventId = selected.Id;
        runtime.PendingBias = TeamEventMissionBias.None;
        runtime.LastGameplayPulseAt = now;
        return true;
    }

    private bool TryRollMission(
        WH40KCommandDynamicMissionProfilePrototype profile,
        WH40KCommandDynamicMissionScope scope,
        string teamId,
        IReadOnlyList<string> teamIds,
        TimeSpan now,
        out ActiveMissionRuntime mission)
    {
        mission = default!;
        var weightedCandidates = BuildWeightedMissionCandidates(profile, scope, teamId, teamIds, now);

        if (weightedCandidates.Count == 0)
            return false;

        if (!TryPickWeighted(weightedCandidates, out var selected))
            return false;

        mission = BuildActiveMissionRuntime(selected, teamId, teamIds, now);
        return true;
    }

    private List<(WH40KCommandDynamicMissionConfig Mission, float Weight)> BuildWeightedMissionCandidates(
        WH40KCommandDynamicMissionProfilePrototype profile,
        WH40KCommandDynamicMissionScope scope,
        string teamId,
        IReadOnlyList<string> teamIds,
        TimeSpan now)
    {
        var weightedCandidates = BuildWeightedMissionCandidatesInternal(
            profile,
            scope,
            teamId,
            teamIds,
            now,
            ignoreCooldown: false);

        if (weightedCandidates.Count > 0)
            return weightedCandidates;

        // Fallback path: if the whole pool is temporarily blocked by anti-repeat cooldowns,
        // still provide candidates to avoid empty mission board state.
        return BuildWeightedMissionCandidatesInternal(
            profile,
            scope,
            teamId,
            teamIds,
            now,
            ignoreCooldown: true);
    }

    private List<(WH40KCommandDynamicMissionConfig Mission, float Weight)> BuildWeightedMissionCandidatesInternal(
        WH40KCommandDynamicMissionProfilePrototype profile,
        WH40KCommandDynamicMissionScope scope,
        string teamId,
        IReadOnlyList<string> teamIds,
        TimeSpan now,
        bool ignoreCooldown)
    {
        var weightedCandidates = new List<(WH40KCommandDynamicMissionConfig Mission, float Weight)>();
        var levelGap = scope == WH40KCommandDynamicMissionScope.Faction
            ? GetTrailingLevelGap(teamId, teamIds)
            : 0;

        foreach (var config in profile.Missions)
        {
            if (string.IsNullOrWhiteSpace(config.Id))
                continue;

            if (config.Scope != scope)
                continue;

            if (scope == WH40KCommandDynamicMissionScope.Faction &&
                !string.IsNullOrWhiteSpace(config.TeamId) &&
                !string.Equals(config.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var requirementTeam = scope == WH40KCommandDynamicMissionScope.Faction ? teamId : string.Empty;
            if (!AreSubsystemRequirementsSatisfied(requirementTeam, config.RequiredSubsystems))
                continue;

            if (!ignoreCooldown && IsMissionOnCooldown(scope, teamId, config.Id, now))
                continue;

            var weight = Math.Max(0.001f, config.BaseWeight);
            if (levelGap > 0)
            {
                var bonus = Math.Min(0.3f, 0.08f * levelGap);
                weight *= Math.Max(0.01f, 1f + bonus);
            }

            weight *= scope == WH40KCommandDynamicMissionScope.Faction
                ? GetMissionSynergyWeight(teamId, config.Tags)
                : GetGlobalMissionSynergyWeight(teamIds, config.Tags);

            if (weight <= 0f)
                continue;

            weightedCandidates.Add((config, weight));
        }

        return weightedCandidates;
    }

    private bool TryFindFactionMissionConfig(
        WH40KCommandDynamicMissionProfilePrototype profile,
        string teamId,
        string missionId,
        IReadOnlyList<string> teamIds,
        TimeSpan now,
        out WH40KCommandDynamicMissionConfig config)
    {
        config = default!;
        if (string.IsNullOrWhiteSpace(missionId))
            return false;

        var candidates = BuildWeightedMissionCandidates(
            profile,
            WH40KCommandDynamicMissionScope.Faction,
            teamId,
            teamIds,
            now);

        foreach (var candidate in candidates)
        {
            if (!string.Equals(candidate.Mission.Id, missionId, StringComparison.OrdinalIgnoreCase))
                continue;

            config = candidate.Mission;
            return true;
        }

        return false;
    }

    private ActiveMissionRuntime BuildActiveMissionRuntime(
        WH40KCommandDynamicMissionConfig selected,
        string teamId,
        IReadOnlyList<string> teamIds,
        TimeSpan now)
    {
        var durationSeconds = Math.Max(1, selected.DurationSeconds);
        var mission = new ActiveMissionRuntime
        {
            MissionId = selected.Id,
            Title = ResolveMissionTitle(selected),
            Description = ResolveMissionDescription(selected),
            Scope = selected.Scope,
            TeamId = teamId,
            DurationSeconds = durationSeconds,
            StartedAt = now,
            EndsAt = now + TimeSpan.FromSeconds(durationSeconds),
            RewardMajorDevelopmentPoints = Math.Max(0, selected.RewardMajorDevelopmentPoints),
            RewardMinorDevelopmentPoints = Math.Max(0, selected.RewardMinorDevelopmentPoints),
            RewardTimeoutDevelopmentPoints = Math.Max(0, selected.RewardTimeoutDevelopmentPoints),
            RewardFailureDevelopmentPoints = Math.Max(0, selected.RewardFailureDevelopmentPoints),
            RewardTempoBonusPercent = Math.Max(0, selected.RewardTempoBonusPercent),
            RewardTokenId = selected.RewardTokenId ?? string.Empty,
            RewardTokenDurationSeconds = Math.Max(0, selected.RewardTokenDurationSeconds),
            ObjectiveType = selected.ObjectiveType,
            RequiredObjectiveEntityPrototypes = selected.ObjectiveRequiredEntityPrototypes
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Tags = selected.Tags.Where(static tag => !string.IsNullOrWhiteSpace(tag)).ToList()
        };

        var resolvedObjectiveType = ResolveObjectiveType(mission);
        _sawmill.Info(
            $"[MissionDebug] Start mission '{mission.MissionId}' scope={mission.Scope} team='{mission.TeamId}' objectiveConfigured={mission.ObjectiveType} objectiveResolved={resolvedObjectiveType} tags=[{string.Join(", ", mission.Tags)}]");

        if ((mission.ObjectiveType == WH40KCommandMissionObjectiveType.BannerHold ||
             HasAnyTag(mission.Tags, "banner", "standard")) &&
            mission.RequiredObjectiveEntityPrototypes.Count == 0)
        {
            mission.RequiredObjectiveEntityPrototypes = ResolveDefaultRequiredObjectivePrototypes(mission.TeamId);
        }

        if (!TryInitializeMissionObjective(mission, teamIds, now))
        {
            _sawmill.Warning(
                $"[MissionDebug] Mission '{mission.MissionId}' objective initialization failed. scope={mission.Scope} team='{mission.TeamId}' objectiveResolved={resolvedObjectiveType}");
        }
        else if (mission.Objective is { } objective)
        {
            _sawmill.Info(
                $"[MissionDebug] Mission '{mission.MissionId}' objective initialized. type={objective.Type} anchorTile={FormatMapCoordinates(objective.Anchor)} anchorWorld={FormatWorldCoordinates(objective.Anchor.Position)} map={objective.Anchor.MapId} radius={objective.Radius:0.##}");
        }

        return mission;
    }

    private static WH40KCommandFactionMissionOffer BuildFactionMissionOffer(WH40KCommandDynamicMissionConfig config)
    {
        return new WH40KCommandFactionMissionOffer(
            config.Id,
            ResolveMissionTitle(config),
            ResolveMissionDescription(config),
            Math.Max(1, config.DurationSeconds),
            Math.Max(0, config.RewardMajorDevelopmentPoints),
            Math.Max(0, config.RewardMinorDevelopmentPoints),
            Math.Max(0, config.RewardTimeoutDevelopmentPoints),
            Math.Max(0, config.RewardFailureDevelopmentPoints),
            Math.Max(0, config.RewardTempoBonusPercent),
            config.RewardTokenId ?? string.Empty,
            Math.Max(0, config.RewardTokenDurationSeconds));
    }

    private List<string> GetLiveTeamIds(string requiredTeamId)
    {
        var teamIds = _teamRule.GetTeamIds()
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(requiredTeamId) &&
            !teamIds.Any(id => string.Equals(id, requiredTeamId, StringComparison.OrdinalIgnoreCase)))
        {
            teamIds.Add(requiredTeamId);
        }

        return teamIds;
    }

    private void UpdateTeamEventGameplayEffects(IReadOnlyList<string> teamIds, TimeSpan now)
    {
        var activeByTeam = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var teamId in teamIds)
        {
            if (!_teamEvents.TryGetValue(teamId, out var runtime))
                continue;

            var active = runtime.ActiveEvent;
            if (active is null || active.EndsAt <= now)
            {
                if (!string.IsNullOrWhiteSpace(runtime.AppliedEventId))
                    AnnounceTeamEventEnded(teamId, runtime.AppliedEventId, active?.Title ?? string.Empty);

                runtime.AppliedEventId = string.Empty;
                runtime.LastGameplayPulseAt = TimeSpan.Zero;
                continue;
            }

            activeByTeam[teamId] = active.EventId;

            var profile = ResolveTeamEventGameplayProfile(active.EventId);
            ApplyTeamEventEffectsToTeam(teamId, active.EventId, profile);
            ApplyTeamEventCooldownAcceleration(teamId, profile, now);
            ApplyTeamEventPeriodicRewards(teamId, runtime, profile, now);

            runtime.AppliedEventId = active.EventId;
        }

        ReconcileTeamEventEffectComponents(activeByTeam);
    }

    private void ApplyTeamEventEffectsToTeam(
        string teamId,
        string eventId,
        TeamEventGameplayProfile profile)
    {
        if (!profile.HasEntityModifiers)
            return;

        var query = EntityQueryEnumerator<WH40KTeamMemberComponent>();
        while (query.MoveNext(out var uid, out var member))
        {
            if (!string.Equals(member.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            var hadEffect = TryComp(uid, out WH40KTeamEventEffectComponent? effect);
            effect ??= EnsureComp<WH40KTeamEventEffectComponent>(uid);

            var ignorePullSlowdownChanged = effect.IgnorePullSlowdown != profile.IgnorePullSlowdown;
            var changed = false;
            changed |= SetIfDifferent(ref effect.TeamId, teamId);
            changed |= SetIfDifferent(ref effect.EventId, eventId);
            changed |= SetIfDifferent(ref effect.OutgoingDamageMultiplier, Math.Max(0.05f, profile.OutgoingDamageMultiplier));
            changed |= SetIfDifferent(ref effect.IncomingDamageMultiplier, Math.Max(0.05f, profile.IncomingDamageMultiplier));
            changed |= SetIfDifferent(ref effect.MedicalDelayMultiplier, Math.Max(0.05f, profile.MedicalDelayMultiplier));
            changed |= SetIfDifferent(ref effect.ConstructionDelayMultiplier, Math.Max(0.05f, profile.ConstructionDelayMultiplier));
            changed |= SetIfDifferent(ref effect.IgnorePullSlowdown, profile.IgnorePullSlowdown);

            if (!hadEffect || changed)
                Dirty(uid, effect);

            if (!hadEffect || ignorePullSlowdownChanged)
                _movement.RefreshMovementSpeedModifiers(uid);
        }
    }

    private void ReconcileTeamEventEffectComponents(IReadOnlyDictionary<string, string> activeByTeam)
    {
        var query = EntityQueryEnumerator<WH40KTeamEventEffectComponent>();
        while (query.MoveNext(out var uid, out var effect))
        {
            if (!TryComp<WH40KTeamMemberComponent>(uid, out var member))
            {
                RemComp<WH40KTeamEventEffectComponent>(uid);
                _movement.RefreshMovementSpeedModifiers(uid);
                continue;
            }

            if (!activeByTeam.TryGetValue(member.TeamId, out var activeEventId) ||
                !string.Equals(activeEventId, effect.EventId, StringComparison.OrdinalIgnoreCase))
            {
                RemComp<WH40KTeamEventEffectComponent>(uid);
                _movement.RefreshMovementSpeedModifiers(uid);
                continue;
            }

            if (!string.Equals(effect.TeamId, member.TeamId, StringComparison.OrdinalIgnoreCase))
            {
                effect.TeamId = member.TeamId;
                Dirty(uid, effect);
            }
        }
    }

    private void ApplyTeamEventCooldownAcceleration(string teamId, TeamEventGameplayProfile profile, TimeSpan now)
    {
        if (profile.CooldownAccelerationPerSecond <= 0f)
            return;

        var reduction = TimeSpan.FromSeconds(Math.Max(0.01f, profile.CooldownAccelerationPerSecond));

        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out var uid, out var node))
        {
            if (!string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (node.NextBattleTacticChangeAvailable > now)
                node.NextBattleTacticChangeAvailable = MaxTime(now, node.NextBattleTacticChangeAvailable - reduction);

            if (node.NextReinforcementAvailable > now)
                node.NextReinforcementAvailable = MaxTime(now, node.NextReinforcementAvailable - reduction);

            Dirty(uid, node);
        }
    }

    private void ApplyTeamEventPeriodicRewards(
        string teamId,
        TeamEventRuntime runtime,
        TeamEventGameplayProfile profile,
        TimeSpan now)
    {
        if (profile.PeriodicDevelopmentPoints <= 0 || profile.PeriodicIntervalSeconds <= 0)
            return;

        if (runtime.LastGameplayPulseAt == TimeSpan.Zero)
            runtime.LastGameplayPulseAt = now;

        var interval = TimeSpan.FromSeconds(profile.PeriodicIntervalSeconds);
        while (runtime.LastGameplayPulseAt + interval <= now)
        {
            runtime.LastGameplayPulseAt += interval;

            if (!ApplyTeamDevelopmentPoints(teamId, profile.PeriodicDevelopmentPoints, "team-event-periodic"))
                continue;

            DispatchTeamMessage(
                teamId,
                Loc.GetString("wh40k-command-runtime-event-periodic-bonus",
                    ("team", ResolveTeamName(teamId)),
                    ("points", profile.PeriodicDevelopmentPoints)));
        }
    }

    private static bool SetIfDifferent(ref string current, string next)
    {
        if (string.Equals(current, next, StringComparison.Ordinal))
            return false;

        current = next;
        return true;
    }

    private static bool SetIfDifferent(ref float current, float next)
    {
        if (MathF.Abs(current - next) <= 0.0001f)
            return false;

        current = next;
        return true;
    }

    private static bool SetIfDifferent(ref bool current, bool next)
    {
        if (current == next)
            return false;

        current = next;
        return true;
    }

    private bool TryInitializeMissionObjective(
        ActiveMissionRuntime mission,
        IReadOnlyList<string> teamIds,
        TimeSpan now)
    {
        var objectiveType = ResolveObjectiveType(mission);
        if (!TryPickMissionAnchor(teamIds, mission.TeamId, objectiveType, out var anchor))
        {
            _sawmill.Warning(
                $"[MissionDebug] Mission '{mission.MissionId}' failed to pick anchor. type={objectiveType} scope={mission.Scope} team='{mission.TeamId}'");
            return false;
        }

        var objective = new MissionObjectiveRuntime
        {
            Anchor = anchor,
            Radius = MissionZoneRadius,
            ProgressGoal = MissionZoneProgressGoal,
            ProgressPerSecond = MissionZoneProgressPerSecond,
            Type = objectiveType
        };

        if (_proto.HasIndex<EntityPrototype>(MissionZoneBeaconPrototypeId))
            objective.BeaconUid = Spawn(MissionZoneBeaconPrototypeId, anchor);

        if (objective.BeaconUid is { } beaconUid)
        {
            ConfigureObjectiveVisualMarker(
                beaconUid,
                mission,
                objective.Type == MissionObjectiveType.CargoDelivery
                    ? "wh40k-command-runtime-marker-airdrop-zone"
                    : mission.Title,
                objective.Radius,
                mission.Scope == WH40KCommandDynamicMissionScope.Faction ? mission.TeamId : string.Empty);
        }

        foreach (var teamId in teamIds)
            objective.TeamProgress[teamId] = 0f;

        if (objective.Type == MissionObjectiveType.CargoDelivery)
        {
            objective.Radius = MissionCargoDeliveryRadius;
            objective.ProgressGoal = 1f;
            objective.ProgressPerSecond = 0f;

            if (objective.BeaconUid is { } objectiveBeacon && Exists(objectiveBeacon) &&
                TryComp<WH40KMissionObjectiveVisualComponent>(objectiveBeacon, out var objectiveVisual))
            {
                objectiveVisual.Radius = MissionDeliveryMarkerRadius;
                objectiveVisual.Label = "wh40k-command-runtime-marker-airdrop-zone";
                Dirty(objectiveBeacon, objectiveVisual);
            }

            objective.ParachuteTargetWorld = anchor.Position;
            objective.ParachuteStartWorld = anchor.Position + new Vector2(
                _random.NextFloat(-2f, 2f),
                MissionParachuteSpawnHeight);
            objective.ParachuteStartAt = now;
            objective.ParachuteEndAt = now + TimeSpan.FromSeconds(MissionParachuteTravelSeconds);

            if (_proto.HasIndex<EntityPrototype>(MissionAirdropParachutePrototypeId))
            {
                var startCoords = new MapCoordinates(objective.ParachuteStartWorld, anchor.MapId);
                objective.ParachuteUid = Spawn(MissionAirdropParachutePrototypeId, startCoords);
                objective.CargoSpawned = false;

                if (objective.ParachuteUid is { } parachuteUid)
                {
                    ConfigureObjectiveVisualMarker(
                        parachuteUid,
                        mission,
                        mission.Title,
                        Math.Max(1.2f, MissionCargoDeliveryRadius * 0.42f),
                        mission.Scope == WH40KCommandDynamicMissionScope.Faction ? mission.TeamId : string.Empty);
                }

                SpawnCargoDeliveryMarkers(mission, objective);
                AnnounceAirdropInbound(mission);
            }
            else
            {
                objective.CargoUid = SpawnMissionCargoCrate(mission, anchor);
                objective.CargoSpawned = objective.CargoUid is not null;
                SpawnCargoDeliveryMarkers(mission, objective);
            }

            _sawmill.Info(
                $"[MissionDebug] Cargo objective prepared for mission '{mission.MissionId}'. anchorTile={FormatMapCoordinates(anchor)} anchorWorld={FormatWorldCoordinates(anchor.Position)} map={anchor.MapId} parachuteSpawned={objective.ParachuteUid is not null} cargoSpawned={objective.CargoSpawned} cargoUid={(objective.CargoUid?.ToString() ?? "null")}");
        }

        mission.Objective = objective;
        mission.LastProgressTick = now;
        return true;
    }

    private bool TryUpdateMissionObjective(
        ActiveMissionRuntime mission,
        IReadOnlyList<string> teamIds,
        TimeSpan now,
        out string winnerTeamId,
        out MissionOutcomeTier outcomeTier)
    {
        winnerTeamId = string.Empty;
        outcomeTier = MissionOutcomeTier.Timeout;

        if (mission.Objective is not { } objective)
            return false;

        UpdateParachuteDescent(mission, objective, now);

        switch (objective.Type)
        {
            case MissionObjectiveType.ZoneControl:
                return UpdateZoneObjective(mission, objective, teamIds, now, out winnerTeamId, out outcomeTier);

            case MissionObjectiveType.BannerHold:
                return UpdateBannerHoldObjective(mission, objective, teamIds, now, out winnerTeamId, out outcomeTier);

            case MissionObjectiveType.CargoDelivery:
                return UpdateCargoObjective(mission, objective, teamIds, now, out winnerTeamId, out outcomeTier);

            default:
                return false;
        }
    }

    private bool UpdateZoneObjective(
        ActiveMissionRuntime mission,
        MissionObjectiveRuntime objective,
        IReadOnlyList<string> teamIds,
        TimeSpan now,
        out string winnerTeamId,
        out MissionOutcomeTier outcomeTier)
    {
        winnerTeamId = string.Empty;
        outcomeTier = MissionOutcomeTier.Timeout;

        var deltaSeconds = mission.LastProgressTick == TimeSpan.Zero
            ? 1f
            : Math.Max(0.1f, (float) (now - mission.LastProgressTick).TotalSeconds);
        mission.LastProgressTick = now;

        var presence = GatherTeamPresence(objective.Anchor.MapId, objective.Anchor.Position, objective.Radius);
        if (presence.Count == 0)
            return false;

        if (mission.Scope == WH40KCommandDynamicMissionScope.Faction)
        {
            var targetTeam = mission.TeamId;
            if (string.IsNullOrWhiteSpace(targetTeam))
                return false;

            var ownCount = presence.GetValueOrDefault(targetTeam, 0);
            var enemyCount = 0;
            foreach (var (team, count) in presence)
            {
                if (string.Equals(team, targetTeam, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (count > enemyCount)
                    enemyCount = count;
            }

            var current = objective.TeamProgress.GetValueOrDefault(targetTeam, 0f);
            if (ownCount > enemyCount)
            {
                var advantage = ownCount - enemyCount;
                var progressMultiplier = GetMissionProgressMultiplier(targetTeam, teamIds, now);
                current += objective.ProgressPerSecond * advantage * deltaSeconds * progressMultiplier;
            }
            else if (enemyCount > ownCount)
            {
                var disadvantage = enemyCount - ownCount;
                current -= objective.ProgressPerSecond * 0.8f * disadvantage * deltaSeconds;
            }
            else if (ownCount == 0)
            {
                current -= MissionZoneDecayPerSecond * deltaSeconds;
            }

            current = Math.Clamp(current, 0f, objective.ProgressGoal);
            objective.TeamProgress[targetTeam] = current;

            if (current < objective.ProgressGoal)
                return false;

            winnerTeamId = targetTeam;
            outcomeTier = MissionOutcomeTier.Major;
            return true;
        }

        var topTeam = string.Empty;
        var topCount = 0;
        var secondCount = 0;

        foreach (var (team, count) in presence)
        {
            if (count > topCount)
            {
                secondCount = topCount;
                topCount = count;
                topTeam = team;
            }
            else if (count > secondCount)
            {
                secondCount = count;
            }
        }

        if (string.IsNullOrWhiteSpace(topTeam) || topCount <= secondCount)
            return false;

        var advantageCount = Math.Max(1, topCount - secondCount);
        var topProgressMultiplier = GetMissionProgressMultiplier(topTeam, teamIds, now);

        var topProgress = objective.TeamProgress.GetValueOrDefault(topTeam, 0f);
        topProgress += objective.ProgressPerSecond * advantageCount * deltaSeconds * topProgressMultiplier;
        topProgress = Math.Clamp(topProgress, 0f, objective.ProgressGoal);
        objective.TeamProgress[topTeam] = topProgress;

        foreach (var team in teamIds)
        {
            if (string.Equals(team, topTeam, StringComparison.OrdinalIgnoreCase))
                continue;

            var decay = objective.TeamProgress.GetValueOrDefault(team, 0f);
            if (decay <= 0f)
                continue;

            decay -= MissionZoneDecayPerSecond * deltaSeconds;
            objective.TeamProgress[team] = Math.Clamp(decay, 0f, objective.ProgressGoal);
        }

        if (topProgress < objective.ProgressGoal)
            return false;

        winnerTeamId = topTeam;
        outcomeTier = MissionOutcomeTier.Major;
        return true;
    }

    private bool UpdateBannerHoldObjective(
        ActiveMissionRuntime mission,
        MissionObjectiveRuntime objective,
        IReadOnlyList<string> teamIds,
        TimeSpan now,
        out string winnerTeamId,
        out MissionOutcomeTier outcomeTier)
    {
        winnerTeamId = string.Empty;
        outcomeTier = MissionOutcomeTier.Timeout;

        var targetTeam = mission.TeamId;
        if (string.IsNullOrWhiteSpace(targetTeam))
            return false;

        if (!HasRequiredObjectiveEntitiesInZone(mission, objective.Anchor.MapId, objective.Anchor.Position, MissionBannerDetectionRadius))
        {
            var decayDeltaSeconds = mission.LastProgressTick == TimeSpan.Zero
                ? 1f
                : Math.Max(0.1f, (float) (now - mission.LastProgressTick).TotalSeconds);
            mission.LastProgressTick = now;

            var current = objective.TeamProgress.GetValueOrDefault(targetTeam, 0f);
            current = Math.Clamp(current - MissionZoneDecayPerSecond * decayDeltaSeconds, 0f, objective.ProgressGoal);
            objective.TeamProgress[targetTeam] = current;
            return false;
        }

        return UpdateZoneObjective(mission, objective, teamIds, now, out winnerTeamId, out outcomeTier);
    }

    private bool UpdateCargoObjective(
        ActiveMissionRuntime mission,
        MissionObjectiveRuntime objective,
        IReadOnlyList<string> teamIds,
        TimeSpan now,
        out string winnerTeamId,
        out MissionOutcomeTier outcomeTier)
    {
        winnerTeamId = string.Empty;
        outcomeTier = MissionOutcomeTier.Timeout;

        if (!objective.CargoSpawned)
        {
            MaybeLogCargoObjective(
                mission,
                objective,
                now,
                "cargo not spawned yet");
            return false;
        }

        if (objective.CargoUid is not { } cargoUid || !Exists(cargoUid))
        {
            MaybeLogCargoObjective(
                mission,
                objective,
                now,
                "cargo entity missing or deleted");

            if (mission.Scope == WH40KCommandDynamicMissionScope.Faction)
            {
                winnerTeamId = mission.TeamId;
                outcomeTier = MissionOutcomeTier.Failure;
                return true;
            }

            return false;
        }

        if (!TryResolveCargoDeliveredTeam(cargoUid, out var deliveredTeamId, out var snapshot))
        {
            MaybeLogCargoObjective(
                mission,
                objective,
                now,
                "cargo not inside delivery radius",
                snapshot);
            return false;
        }

        _sawmill.Info(
            $"[MissionDebug] Cargo objective complete for mission '{mission.MissionId}'. deliveredTeam='{deliveredTeamId}' expectedTeam='{mission.TeamId}' scope={mission.Scope}");

        if (mission.Scope == WH40KCommandDynamicMissionScope.Global)
        {
            winnerTeamId = deliveredTeamId;
            outcomeTier = MissionOutcomeTier.Major;
            return true;
        }

        winnerTeamId = mission.TeamId;
        outcomeTier = string.Equals(deliveredTeamId, mission.TeamId, StringComparison.OrdinalIgnoreCase)
            ? MissionOutcomeTier.Major
            : MissionOutcomeTier.Failure;
        return true;
    }

    private void UpdateParachuteDescent(ActiveMissionRuntime mission, MissionObjectiveRuntime objective, TimeSpan now)
    {
        if (objective.Type != MissionObjectiveType.CargoDelivery)
            return;

        if (objective.CargoSpawned)
            return;

        if (objective.ParachuteUid is { } parachuteUid && Exists(parachuteUid))
        {
            var total = Math.Max(0.1f, (float) (objective.ParachuteEndAt - objective.ParachuteStartAt).TotalSeconds);
            var elapsed = Math.Clamp((float) (now - objective.ParachuteStartAt).TotalSeconds, 0f, total);
            var t = elapsed / total;
            var eased = 1f - MathF.Pow(1f - t, 2f);
            var world = Vector2.Lerp(objective.ParachuteStartWorld, objective.ParachuteTargetWorld, eased);
            _transform.SetWorldPosition(parachuteUid, world);
        }

        if (now < objective.ParachuteEndAt)
            return;

        if (objective.ParachuteUid is { } activeParachute && Exists(activeParachute))
            QueueDel(activeParachute);

        objective.CargoUid = SpawnMissionCargoCrate(mission, objective.Anchor);
        objective.CargoSpawned = objective.CargoUid is not null;

        if (objective.CargoUid is { } cargoUid)
        {
            ConfigureObjectiveVisualMarker(
                cargoUid,
                mission,
                mission.Title,
                Math.Max(1.2f, MissionCargoDeliveryRadius * 0.42f),
                mission.Scope == WH40KCommandDynamicMissionScope.Faction ? mission.TeamId : string.Empty);
        }

        _sawmill.Info(
            $"[MissionDebug] Airdrop cargo spawned for mission '{mission.MissionId}'. cargoUid={(objective.CargoUid?.ToString() ?? "null")} cargoTile={FormatMapCoordinates(objective.Anchor)} cargoWorld={FormatWorldCoordinates(objective.Anchor.Position)} map={objective.Anchor.MapId}");

        AnnounceAirdropLanded(mission);
    }

    private EntityUid? SpawnMissionCargoCrate(ActiveMissionRuntime mission, MapCoordinates coordinates)
    {
        var lootableCargo = IsLootCargoMission(mission);
        var preferredPrototype = lootableCargo && _proto.HasIndex<EntityPrototype>(MissionCargoCratePrototypeId)
            ? MissionCargoCratePrototypeId
            : _proto.HasIndex<EntityPrototype>(MissionCargoCrateDeliveryPrototypeId)
                ? MissionCargoCrateDeliveryPrototypeId
                : MissionCargoCratePrototypeId;

        var protoId = _proto.HasIndex<EntityPrototype>(preferredPrototype)
            ? preferredPrototype
            : "CrateGenericSteel";

        var cargoUid = Spawn(protoId, coordinates);
        if (!lootableCargo)
            HardenDeliveryCargo(cargoUid);

        return cargoUid;
    }

    private void HardenDeliveryCargo(EntityUid cargoUid)
    {
        RemComp<DamageableComponent>(cargoUid);
        RemComp<DestructibleComponent>(cargoUid);
        RemComp<WeldableComponent>(cargoUid);
        RemComp<EntityStorageComponent>(cargoUid);
        RemComp<ContainerManagerComponent>(cargoUid);
    }

    private void ConfigureObjectiveVisualMarker(
        EntityUid uid,
        ActiveMissionRuntime mission,
        string label,
        float radius,
        string teamVisibilityId)
    {
        var visual = EnsureComp<WH40KMissionObjectiveVisualComponent>(uid);
        visual.TeamId = teamVisibilityId;
        visual.Label = label;
        visual.Radius = Math.Clamp(radius, 0.45f, 12f);
        visual.Color = string.IsNullOrWhiteSpace(teamVisibilityId)
            ? Color.FromHex("#FFD250")
            : Color.White;
        visual.Pulse = true;
        Dirty(uid, visual);
    }

    private void SpawnCargoDeliveryMarkers(ActiveMissionRuntime mission, MissionObjectiveRuntime objective)
    {
        if (!_proto.HasIndex<EntityPrototype>(MissionZoneBeaconPrototypeId))
            return;

        var xformQuery = GetEntityQuery<TransformComponent>();
        var nodeQuery = EntityQueryEnumerator<WH40KCommandNodeComponent, TransformComponent>();
        while (nodeQuery.MoveNext(out _, out var node, out var nodeXform))
        {
            if (string.IsNullOrWhiteSpace(node.TeamId) || nodeXform.MapID == MapId.Nullspace)
                continue;

            if (mission.Scope == WH40KCommandDynamicMissionScope.Faction &&
                !string.Equals(node.TeamId, mission.TeamId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nodeWorld = _transform.GetWorldPosition(nodeXform, xformQuery);
            var coordinates = new MapCoordinates(nodeWorld, nodeXform.MapID);
            var markerUid = Spawn(MissionZoneBeaconPrototypeId, coordinates);
            ConfigureObjectiveVisualMarker(
                markerUid,
                mission,
                "wh40k-command-runtime-marker-delivery-zone",
                MissionDeliveryMarkerRadius,
                node.TeamId);
            objective.DeliveryMarkerUids.Add(markerUid);
        }
    }

    private void AnnounceAirdropInbound(ActiveMissionRuntime mission)
    {
        var missionTitle = ResolveLocalizedOrRaw(mission.Title);
        var coords = BuildMissionCoordinateText(mission);
        if (mission.Scope == WH40KCommandDynamicMissionScope.Global)
        {
            _chat.DispatchServerAnnouncement(
                Loc.GetString("wh40k-command-runtime-mission-airdrop-inbound-global",
                    ("mission", missionTitle),
                    ("coords", coords)));
            return;
        }

        DispatchTeamMessage(
            mission.TeamId,
            Loc.GetString("wh40k-command-runtime-mission-airdrop-inbound-faction",
                ("mission", missionTitle),
                ("coords", coords)));
    }

    private void AnnounceAirdropLanded(ActiveMissionRuntime mission)
    {
        var missionTitle = ResolveLocalizedOrRaw(mission.Title);
        var coords = BuildMissionCoordinateText(mission);
        if (mission.Scope == WH40KCommandDynamicMissionScope.Global)
        {
            _chat.DispatchServerAnnouncement(
                Loc.GetString("wh40k-command-runtime-mission-airdrop-landed-global",
                    ("mission", missionTitle),
                    ("coords", coords)));
            return;
        }

        DispatchTeamMessage(
            mission.TeamId,
            Loc.GetString("wh40k-command-runtime-mission-airdrop-landed-faction",
                ("mission", missionTitle),
                ("coords", coords)));
    }

    private string BuildMissionCoordinateText(ActiveMissionRuntime mission)
    {
        if (mission.Objective is not { } objective)
            return Loc.GetString("wh40k-command-runtime-coordinates-unknown");

        return FormatMapCoordinates(objective.Anchor);
    }

    private bool HasRequiredObjectiveEntitiesInZone(
        ActiveMissionRuntime mission,
        MapId mapId,
        Vector2 center,
        float radius)
    {
        var requiredPrototypes = mission.RequiredObjectiveEntityPrototypes;
        if (requiredPrototypes.Count == 0)
            return true;

        if (mapId == MapId.Nullspace)
            return false;

        var requiredSet = new HashSet<string>(requiredPrototypes, StringComparer.OrdinalIgnoreCase);
        var radiusSquared = Math.Max(0.2f, radius) * Math.Max(0.2f, radius);
        var xformQuery = GetEntityQuery<TransformComponent>();
        var metaQuery = GetEntityQuery<MetaDataComponent>();
        var transformEnumerator = EntityQueryEnumerator<TransformComponent>();
        while (transformEnumerator.MoveNext(out var uid, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var world = _transform.GetWorldPosition(xform, xformQuery);
            if ((world - center).LengthSquared() > radiusSquared)
                continue;

            if (!metaQuery.TryGetComponent(uid, out var meta) || meta.EntityPrototype is not { } proto)
                continue;

            if (requiredSet.Contains(proto.ID))
                return true;
        }

        return false;
    }

    private Dictionary<string, int> GatherTeamPresence(MapId mapId, Vector2 center, float radius)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (mapId == MapId.Nullspace)
            return result;

        var radiusSquared = Math.Max(0.1f, radius) * Math.Max(0.1f, radius);
        var xformQuery = GetEntityQuery<TransformComponent>();

        var query = EntityQueryEnumerator<WH40KTeamMemberComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var member, out var xform))
        {
            if (xform.MapID != mapId || !_mobState.IsAlive(uid))
                continue;

            var world = _transform.GetWorldPosition(xform, xformQuery);
            if ((world - center).LengthSquared() > radiusSquared)
                continue;

            result[member.TeamId] = result.GetValueOrDefault(member.TeamId, 0) + 1;
        }

        return result;
    }

    private float GetMissionProgressMultiplier(string teamId, IReadOnlyList<string> teamIds, TimeSpan now)
    {
        var multiplier = 1f;

        if (_teamEvents.TryGetValue(teamId, out var ownRuntime) &&
            ownRuntime.ActiveEvent is { } ownEvent &&
            ownEvent.EndsAt > now)
        {
            multiplier *= ResolveTeamEventGameplayProfile(ownEvent.EventId).MissionProgressMultiplier;
        }

        foreach (var otherTeamId in teamIds)
        {
            if (string.Equals(otherTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_teamEvents.TryGetValue(otherTeamId, out var runtime) ||
                runtime.ActiveEvent is not { } enemyEvent ||
                enemyEvent.EndsAt <= now)
            {
                continue;
            }

            var enemyMultiplier = ResolveTeamEventGameplayProfile(enemyEvent.EventId).EnemyMissionProgressMultiplier;
            multiplier *= enemyMultiplier;
        }

        return Math.Clamp(multiplier, 0.2f, 3f);
    }

    private bool TryResolveCargoDeliveredTeam(
        EntityUid cargoUid,
        out string teamId,
        out CargoDeliveryDebugSnapshot snapshot)
    {
        teamId = string.Empty;
        snapshot = default;

        if (!TryComp(cargoUid, out TransformComponent? cargoXform))
        {
            snapshot = new CargoDeliveryDebugSnapshot(
                NodeCount: 0,
                SameMapNodeCount: 0,
                ClosestNodeTeamId: string.Empty,
                ClosestNodeDistance: -1f,
                ClosestInsideRadius: false,
                CargoMapId: MapId.Nullspace,
                CargoWorld: Vector2.Zero);
            return false;
        }

        if (cargoXform.MapID == MapId.Nullspace)
        {
            snapshot = new CargoDeliveryDebugSnapshot(
                NodeCount: 0,
                SameMapNodeCount: 0,
                ClosestNodeTeamId: string.Empty,
                ClosestNodeDistance: -1f,
                ClosestInsideRadius: false,
                CargoMapId: cargoXform.MapID,
                CargoWorld: Vector2.Zero);
            return false;
        }

        var xformQuery = GetEntityQuery<TransformComponent>();
        var cargoWorld = _transform.GetWorldPosition(cargoXform, xformQuery);
        var radiusSquared = MissionCargoDeliveryRadius * MissionCargoDeliveryRadius;

        var nodeCount = 0;
        var sameMapNodeCount = 0;
        var bestInsideDistanceSquared = float.MaxValue;
        var closestNodeDistanceSquared = float.MaxValue;
        var closestNodeTeamId = string.Empty;

        var nodeQuery = EntityQueryEnumerator<WH40KCommandNodeComponent, TransformComponent>();
        while (nodeQuery.MoveNext(out _, out var node, out var nodeXform))
        {
            if (string.IsNullOrWhiteSpace(node.TeamId))
                continue;

            nodeCount++;

            if (nodeXform.MapID != cargoXform.MapID)
                continue;

            sameMapNodeCount++;
            var nodeWorld = _transform.GetWorldPosition(nodeXform, xformQuery);
            var distanceSquared = (nodeWorld - cargoWorld).LengthSquared();

            if (distanceSquared < closestNodeDistanceSquared)
            {
                closestNodeDistanceSquared = distanceSquared;
                closestNodeTeamId = node.TeamId;
            }

            if (distanceSquared > radiusSquared)
                continue;

            if (distanceSquared >= bestInsideDistanceSquared)
                continue;

            bestInsideDistanceSquared = distanceSquared;
            teamId = node.TeamId;
        }

        snapshot = new CargoDeliveryDebugSnapshot(
            NodeCount: nodeCount,
            SameMapNodeCount: sameMapNodeCount,
            ClosestNodeTeamId: closestNodeTeamId,
            ClosestNodeDistance: closestNodeDistanceSquared < float.MaxValue
                ? MathF.Sqrt(closestNodeDistanceSquared)
                : -1f,
            ClosestInsideRadius: closestNodeDistanceSquared <= radiusSquared,
            CargoMapId: cargoXform.MapID,
            CargoWorld: cargoWorld);

        return !string.IsNullOrWhiteSpace(teamId);
    }

    private void MaybeLogCargoObjective(
        ActiveMissionRuntime mission,
        MissionObjectiveRuntime objective,
        TimeSpan now,
        string reason,
        CargoDeliveryDebugSnapshot? snapshot = null)
    {
        if (!ShouldLogCargoObjective(objective, now))
            return;

        var anchorText = FormatMapCoordinates(objective.Anchor);
        var anchorWorldText = FormatWorldCoordinates(objective.Anchor.Position);
        var details = snapshot is { } info
            ? $" cargoMap={info.CargoMapId} cargoTile={FormatMapCoordinates(new MapCoordinates(info.CargoWorld, info.CargoMapId))} cargoWorld={FormatWorldCoordinates(info.CargoWorld)} nodeCount={info.NodeCount} sameMapNodes={info.SameMapNodeCount} closestNodeTeam='{info.ClosestNodeTeamId}' closestNodeDistance={info.ClosestNodeDistance:0.##} insideRadius={info.ClosestInsideRadius}"
            : string.Empty;

        _sawmill.Info(
            $"[MissionDebug] Cargo objective pending for mission '{mission.MissionId}'. scope={mission.Scope} team='{mission.TeamId}' reason={reason} cargoUid={(objective.CargoUid?.ToString() ?? "null")} anchorTile={anchorText} anchorWorld={anchorWorldText} anchorMap={objective.Anchor.MapId}.{details}");
    }

    private static bool ShouldLogCargoObjective(MissionObjectiveRuntime objective, TimeSpan now)
    {
        if (objective.LastCargoDebugLogAt != TimeSpan.Zero &&
            now - objective.LastCargoDebugLogAt < TimeSpan.FromSeconds(MissionCargoDebugLogIntervalSeconds))
        {
            return false;
        }

        objective.LastCargoDebugLogAt = now;
        return true;
    }

    private bool TryPickMissionAnchor(
        IReadOnlyList<string> teamIds,
        string preferredTeamId,
        MissionObjectiveType objectiveType,
        out MapCoordinates target)
    {
        target = default;

        var mapId = _gameTicker.DefaultMap;
        if (mapId == MapId.Nullspace)
            return false;

        _ = teamIds;
        var candidates = BuildMissionAnchorSeedCandidates(mapId, preferredTeamId);
        if (candidates.Count == 0)
            return false;

        var enforceNodeDistance = objectiveType == MissionObjectiveType.CargoDelivery;
        if (TryResolveMissionAnchorFromSeeds(candidates, requireUnroofed: true, enforceNodeDistance, out target))
            return true;

        if (enforceNodeDistance &&
            TryResolveMissionAnchorFromSeeds(candidates, requireUnroofed: true, enforceNodeDistance: false, out target))
        {
            return true;
        }

        if (enforceNodeDistance)
            return false;

        return TryResolveMissionAnchorFromSeeds(candidates, requireUnroofed: false, enforceNodeDistance: false, out target);
    }

    private List<MapCoordinates> BuildMissionAnchorSeedCandidates(MapId mapId, string preferredTeamId)
    {
        var candidates = new List<MapCoordinates>();
        var dedupe = new HashSet<string>(StringComparer.Ordinal);
        var xformQuery = GetEntityQuery<TransformComponent>();

        void TryAddCandidate(MapCoordinates coordinates)
        {
            if (coordinates.MapId != mapId || coordinates.MapId == MapId.Nullspace)
                return;

            var key = $"{coordinates.MapId}:{(int) MathF.Round(coordinates.Position.X * 2f)}:{(int) MathF.Round(coordinates.Position.Y * 2f)}";
            if (!dedupe.Add(key))
                return;

            candidates.Add(coordinates);
        }

        var points = EntityQueryEnumerator<WH40KInfluencePointComponent, TransformComponent>();
        while (points.MoveNext(out _, out _, out var pointXform))
        {
            if (pointXform.MapID != mapId)
                continue;

            var worldPos = _transform.GetWorldPosition(pointXform, xformQuery);
            TryAddCandidate(new MapCoordinates(worldPos, pointXform.MapID));
        }

        if (!string.IsNullOrWhiteSpace(preferredTeamId))
        {
            var preferredMembers = EntityQueryEnumerator<WH40KTeamMemberComponent, TransformComponent>();
            while (preferredMembers.MoveNext(out var memberUid, out var member, out var memberXform))
            {
                if (memberXform.MapID != mapId || !_mobState.IsAlive(memberUid))
                    continue;

                if (!string.Equals(member.TeamId, preferredTeamId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var worldPos = _transform.GetWorldPosition(memberXform, xformQuery);
                TryAddCandidate(new MapCoordinates(worldPos, memberXform.MapID));
            }
        }

        var members = EntityQueryEnumerator<WH40KTeamMemberComponent, TransformComponent>();
        while (members.MoveNext(out var memberUid, out _, out var memberXform))
        {
            if (memberXform.MapID != mapId || !_mobState.IsAlive(memberUid))
                continue;

            var worldPos = _transform.GetWorldPosition(memberXform, xformQuery);
            TryAddCandidate(new MapCoordinates(worldPos, memberXform.MapID));
        }

        var nodes = EntityQueryEnumerator<WH40KCommandNodeComponent, TransformComponent>();
        while (nodes.MoveNext(out _, out var node, out var nodeXform))
        {
            if (nodeXform.MapID != mapId)
                continue;

            if (!string.IsNullOrWhiteSpace(preferredTeamId) &&
                !string.IsNullOrWhiteSpace(node.TeamId) &&
                !string.Equals(node.TeamId, preferredTeamId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var worldPos = _transform.GetWorldPosition(nodeXform, xformQuery);
            TryAddCandidate(new MapCoordinates(worldPos, nodeXform.MapID));
        }

        return candidates;
    }

    private bool TryResolveMissionAnchorFromSeeds(
        IReadOnlyList<MapCoordinates> seeds,
        bool requireUnroofed,
        bool enforceNodeDistance,
        out MapCoordinates target)
    {
        target = default;
        if (seeds.Count == 0)
            return false;

        const int randomAttempts = 30;
        for (var i = 0; i < randomAttempts; i++)
        {
            var center = seeds[_random.Next(seeds.Count)];
            var offset = _random.NextVector2(0.7f, 3.4f);
            var candidate = new MapCoordinates(center.Position + offset, center.MapId);
            if (!IsMissionAnchorAllowed(candidate, requireUnroofed, enforceNodeDistance))
                continue;

            target = candidate;
            return true;
        }

        var startIndex = _random.Next(seeds.Count);
        for (var i = 0; i < seeds.Count; i++)
        {
            var candidate = seeds[(startIndex + i) % seeds.Count];
            if (!IsMissionAnchorAllowed(candidate, requireUnroofed, enforceNodeDistance))
                continue;

            target = candidate;
            return true;
        }

        return false;
    }

    private bool IsMissionAnchorAllowed(MapCoordinates candidate, bool requireUnroofed, bool enforceNodeDistance)
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

        if (requireUnroofed && IsRoovedTile(gridUid, grid, tileIndices))
            return false;

        if (enforceNodeDistance && IsNearCommandNode(candidate.MapId, candidate.Position, MissionAirdropMinDistanceFromCommandNode))
            return false;

        return true;
    }

    private bool IsRoovedTile(EntityUid gridUid, MapGridComponent grid, Vector2i tileIndices)
    {
        if (HasComp<ImplicitRoofComponent>(gridUid))
            return true;

        if (!TryComp<RoofComponent>(gridUid, out var roofComp))
            return false;

        return _roof.IsRooved((gridUid, grid, roofComp), tileIndices);
    }

    private bool IsNearCommandNode(MapId mapId, Vector2 worldPosition, float minDistance)
    {
        if (mapId == MapId.Nullspace)
            return false;

        var minDistanceSquared = minDistance * minDistance;
        var xformQuery = GetEntityQuery<TransformComponent>();
        var nodeQuery = EntityQueryEnumerator<WH40KCommandNodeComponent, TransformComponent>();
        while (nodeQuery.MoveNext(out _, out _, out var nodeXform))
        {
            if (nodeXform.MapID != mapId)
                continue;

            var nodeWorld = _transform.GetWorldPosition(nodeXform, xformQuery);
            if ((nodeWorld - worldPosition).LengthSquared() > minDistanceSquared)
                continue;

            return true;
        }

        return false;
    }

    private void ResolveCompletedMission(
        ActiveMissionRuntime mission,
        IReadOnlyList<string> teamIds,
        string winnerTeamId,
        MissionOutcomeTier outcomeTier,
        bool timedOut)
    {
        RegisterMissionCooldown(mission, _timing.CurTime);

        if (mission.Scope == WH40KCommandDynamicMissionScope.Global)
        {
            ResolveGlobalMissionCompletion(mission, teamIds, winnerTeamId, outcomeTier, timedOut);
        }
        else
        {
            ResolveFactionMissionCompletion(mission, winnerTeamId, outcomeTier, timedOut);
        }
    }

    private void ResolveGlobalMissionCompletion(
        ActiveMissionRuntime mission,
        IReadOnlyList<string> teamIds,
        string winnerTeamId,
        MissionOutcomeTier outcomeTier,
        bool timedOut)
    {
        switch (outcomeTier)
        {
            case MissionOutcomeTier.Major:
            {
                foreach (var teamId in teamIds)
                {
                    var tier = string.Equals(teamId, winnerTeamId, StringComparison.OrdinalIgnoreCase)
                        ? MissionOutcomeTier.Major
                        : MissionOutcomeTier.Failure;
                    ApplyMissionOutcomeForTeam(teamId, mission, tier);
                }

                _chat.DispatchServerAnnouncement(
                    Loc.GetString("wh40k-command-runtime-mission-global-resolved-major",
                        ("mission", ResolveLocalizedOrRaw(mission.Title)),
                        ("winner", ResolveTeamName(winnerTeamId))));
                CleanupMissionObjectiveRuntime(
                    mission,
                    keepCargo: mission.Objective?.Type == MissionObjectiveType.CargoDelivery && IsLootCargoMission(mission));
                return;
            }

            case MissionOutcomeTier.Minor:
            {
                foreach (var teamId in teamIds)
                {
                    var tier = string.Equals(teamId, winnerTeamId, StringComparison.OrdinalIgnoreCase)
                        ? MissionOutcomeTier.Minor
                        : MissionOutcomeTier.Timeout;
                    ApplyMissionOutcomeForTeam(teamId, mission, tier);
                }

                _chat.DispatchServerAnnouncement(
                    Loc.GetString("wh40k-command-runtime-mission-global-resolved-minor",
                        ("mission", ResolveLocalizedOrRaw(mission.Title)),
                        ("winner", ResolveTeamName(winnerTeamId))));
                CleanupMissionObjectiveRuntime(mission, keepCargo: false);
                return;
            }

            case MissionOutcomeTier.Timeout:
            {
                foreach (var teamId in teamIds)
                    ApplyMissionOutcomeForTeam(teamId, mission, MissionOutcomeTier.Timeout);

                _chat.DispatchServerAnnouncement(
                    Loc.GetString("wh40k-command-runtime-mission-global-timeout",
                        ("mission", ResolveLocalizedOrRaw(mission.Title))));
                CleanupMissionObjectiveRuntime(mission, keepCargo: false);
                return;
            }

            case MissionOutcomeTier.Failure:
            {
                foreach (var teamId in teamIds)
                    ApplyMissionOutcomeForTeam(teamId, mission, MissionOutcomeTier.Failure);

                _chat.DispatchServerAnnouncement(
                    Loc.GetString("wh40k-command-runtime-mission-global-failed",
                        ("mission", ResolveLocalizedOrRaw(mission.Title))));
                CleanupMissionObjectiveRuntime(mission, keepCargo: false);
                return;
            }
        }
    }

    private void ResolveFactionMissionCompletion(
        ActiveMissionRuntime mission,
        string winnerTeamId,
        MissionOutcomeTier outcomeTier,
        bool timedOut)
    {
        var targetTeam = mission.TeamId;
        if (string.IsNullOrWhiteSpace(targetTeam))
        {
            CleanupMissionObjectiveRuntime(mission, keepCargo: false);
            return;
        }

        ApplyMissionOutcomeForTeam(targetTeam, mission, outcomeTier);

        var outcomeText = outcomeTier switch
        {
            MissionOutcomeTier.Major => Loc.GetString("wh40k-command-runtime-mission-tier-major"),
            MissionOutcomeTier.Minor => Loc.GetString("wh40k-command-runtime-mission-tier-minor"),
            MissionOutcomeTier.Timeout => Loc.GetString("wh40k-command-runtime-mission-tier-timeout"),
            MissionOutcomeTier.Failure => Loc.GetString("wh40k-command-runtime-mission-tier-failure"),
            _ => Loc.GetString("wh40k-command-runtime-mission-tier-timeout")
        };

        DispatchTeamMessage(
            targetTeam,
            Loc.GetString("wh40k-command-runtime-mission-faction-resolved",
                ("mission", ResolveLocalizedOrRaw(mission.Title)),
                ("team", ResolveTeamName(targetTeam)),
                ("outcome", outcomeText)));

        var keepCargo = mission.Objective?.Type == MissionObjectiveType.CargoDelivery &&
                        outcomeTier == MissionOutcomeTier.Major &&
                        IsLootCargoMission(mission);
        CleanupMissionObjectiveRuntime(mission, keepCargo);
    }

    private void ResolveGlobalMissionTimeout(ActiveMissionRuntime mission, IReadOnlyList<string> teamIds)
    {
        if (mission.Objective is not { } objective)
        {
            ResolveGlobalMissionCompletion(mission, teamIds, string.Empty, MissionOutcomeTier.Timeout, timedOut: true);
            return;
        }

        var topTeam = string.Empty;
        var topProgress = 0f;
        var tiedTop = false;

        foreach (var teamId in teamIds)
        {
            var value = objective.TeamProgress.GetValueOrDefault(teamId, 0f);
            if (value > topProgress + 0.01f)
            {
                topProgress = value;
                topTeam = teamId;
                tiedTop = false;
            }
            else if (value > 0f && Math.Abs(value - topProgress) <= 0.01f)
            {
                tiedTop = true;
            }
        }

        if (string.IsNullOrWhiteSpace(topTeam) || topProgress <= 0f || tiedTop)
        {
            ResolveGlobalMissionCompletion(mission, teamIds, string.Empty, MissionOutcomeTier.Timeout, timedOut: true);
            return;
        }

        ResolveGlobalMissionCompletion(mission, teamIds, topTeam, MissionOutcomeTier.Minor, timedOut: true);
    }

    private void ResolveFactionMissionTimeout(ActiveMissionRuntime mission)
    {
        var targetTeam = mission.TeamId;
        if (string.IsNullOrWhiteSpace(targetTeam))
        {
            CleanupMissionObjectiveRuntime(mission, keepCargo: false);
            return;
        }

        var progress = mission.Objective?.TeamProgress.GetValueOrDefault(targetTeam, 0f) ?? 0f;
        var goal = mission.Objective?.ProgressGoal ?? MissionZoneProgressGoal;

        var outcome = progress switch
        {
            _ when progress >= goal * 0.5f => MissionOutcomeTier.Minor,
            > 0f => MissionOutcomeTier.Timeout,
            _ => MissionOutcomeTier.Failure
        };

        ResolveFactionMissionCompletion(mission, targetTeam, outcome, timedOut: true);
    }

    private void ApplyMissionOutcomeForTeam(string teamId, ActiveMissionRuntime mission, MissionOutcomeTier tier)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;

        var objectiveType = mission.Objective?.Type ?? ResolveObjectiveType(mission);
        var rewardPoints = GetMissionRewardPoints(mission, tier);
        var tempoBonusPoints = tier == MissionOutcomeTier.Major && mission.RewardTempoBonusPercent > 0
            ? Math.Max(0, (int) Math.Ceiling(rewardPoints * (mission.RewardTempoBonusPercent / 100f)))
            : 0;

        var totalPoints = rewardPoints + tempoBonusPoints;
        if (totalPoints > 0)
            ApplyTeamDevelopmentPoints(teamId, totalPoints, $"mission-{tier.ToString().ToLowerInvariant()}");

        if (tier == MissionOutcomeTier.Major &&
            !string.IsNullOrWhiteSpace(mission.RewardTokenId) &&
            mission.RewardTokenDurationSeconds > 0)
        {
            ApplyMissionTokenReward(teamId, mission.RewardTokenId, mission.RewardTokenDurationSeconds);
        }

        ApplyMissionSpecificOutcomeHandlers(teamId, mission, tier);
        ApplyMissionOutcomeBias(teamId, tier);

        DispatchTeamMessage(
            teamId,
            Loc.GetString("wh40k-command-runtime-mission-outcome-team",
                ("mission", ResolveLocalizedOrRaw(mission.Title)),
                ("tier", ResolveMissionOutcomeTierLabel(tier)),
                ("points", totalPoints)));

        RaiseLocalEvent(new WH40KMissionOutcomeAppliedEvent(
            teamId,
            mission.MissionId,
            ToPublicMissionObjectiveType(objectiveType),
            mission.Scope,
            ToPublicMissionOutcomeTier(tier),
            Math.Max(0, totalPoints),
            mission.StartedAt.Ticks));
    }

    private void ApplyMissionSpecificOutcomeHandlers(string teamId, ActiveMissionRuntime mission, MissionOutcomeTier tier)
    {
        if (tier != MissionOutcomeTier.Major || string.IsNullOrWhiteSpace(teamId))
            return;

        if (IntelCounterJamMissionIds.Contains(mission.MissionId))
            ApplyEnemyTeamEventRollDelay(
                teamId,
                IntelCounterJamDelaySeconds,
                IntelCounterJamActiveEventReductionSeconds);

        if (TechMissionOfferRefreshMissionIds.Contains(mission.MissionId))
        {
            TryRefreshFactionMissionOfferSet(teamId);
            ApplyTacticalCallDiscountToken(teamId, TechArchiveTacticalDiscountSeconds);
            DispatchTeamMessage(
                teamId,
                Loc.GetString(
                    "wh40k-command-runtime-mission-tech-discount-applied",
                    ("duration", TechArchiveTacticalDiscountSeconds)));
        }
    }

    private void ApplyEnemyTeamEventRollDelay(
        string sourceTeamId,
        int delaySeconds,
        int activeEventReductionSeconds = 0)
    {
        if (delaySeconds <= 0)
            return;

        var now = _timing.CurTime;
        var delay = TimeSpan.FromSeconds(delaySeconds);
        var activeEventReduction = TimeSpan.FromSeconds(Math.Max(0, activeEventReductionSeconds));
        var affectedTeams = 0;
        var affectedActiveEvents = 0;

        foreach (var (teamId, runtime) in _teamEvents)
        {
            if (string.Equals(teamId, sourceTeamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (runtime.NextRollAt == TimeSpan.Zero)
                runtime.NextRollAt = now + TimeSpan.FromSeconds(1);

            runtime.NextRollAt += delay;
            affectedTeams++;

            if (activeEventReduction <= TimeSpan.Zero ||
                runtime.ActiveEvent is not { } activeEvent ||
                activeEvent.EndsAt <= now)
            {
                continue;
            }

            var minEndAt = now + TimeSpan.FromSeconds(1);
            var adjustedEndAt = MaxTime(minEndAt, activeEvent.EndsAt - activeEventReduction);
            if (adjustedEndAt >= activeEvent.EndsAt)
                continue;

            activeEvent.EndsAt = adjustedEndAt;
            activeEvent.DurationSeconds = Math.Max(
                1,
                (int) Math.Ceiling((activeEvent.EndsAt - activeEvent.StartedAt).TotalSeconds));
            affectedActiveEvents++;
        }

        if (affectedTeams <= 0)
            return;

        DispatchTeamMessage(
            sourceTeamId,
            Loc.GetString(
                "wh40k-command-runtime-mission-intel-jam-applied",
                ("duration", delaySeconds),
                ("targets", affectedTeams)));

        if (affectedActiveEvents <= 0 || activeEventReductionSeconds <= 0)
            return;

        DispatchTeamMessage(
            sourceTeamId,
            Loc.GetString(
                "wh40k-command-runtime-mission-intel-jam-active-cut",
                ("duration", activeEventReductionSeconds),
                ("targets", affectedActiveEvents)));
    }

    private void TryRefreshFactionMissionOfferSet(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return;

        if (_teamMissions.GetValueOrDefault(teamId) is not null)
        {
            _pendingFactionMissionOfferRefreshTeams.Add(teamId);
            DispatchTeamMessage(
                teamId,
                Loc.GetString("wh40k-command-runtime-mission-tech-offers-refresh-deferred"));
            return;
        }

        if (!TryForceRefreshFactionMissionOfferSet(teamId))
            return;

        DispatchTeamMessage(
            teamId,
            Loc.GetString("wh40k-command-runtime-mission-tech-offers-refreshed"));
    }

    private void TryApplyPendingFactionMissionOfferRefresh(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId) ||
            !_pendingFactionMissionOfferRefreshTeams.Contains(teamId) ||
            _teamMissions.GetValueOrDefault(teamId) is not null ||
            !TryForceRefreshFactionMissionOfferSet(teamId))
        {
            return;
        }

        DispatchTeamMessage(
            teamId,
            Loc.GetString("wh40k-command-runtime-mission-tech-offers-refreshed-deferred"));
    }

    private bool TryForceRefreshFactionMissionOfferSet(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        var refreshed = false;
        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out var uid, out var node))
        {
            if (!string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            node.ActiveMissionTaskId = string.Empty;
            node.MissionBoardOfferedTaskIds.Clear();
            Dirty(uid, node);
            refreshed = true;
        }

        if (!refreshed)
            return false;

        _pendingFactionMissionOfferRefreshTeams.Remove(teamId);
        return true;
    }

    private int GetMissionRewardPoints(ActiveMissionRuntime mission, MissionOutcomeTier tier)
    {
        return tier switch
        {
            MissionOutcomeTier.Major => Math.Max(0, mission.RewardMajorDevelopmentPoints),
            MissionOutcomeTier.Minor => Math.Max(0, mission.RewardMinorDevelopmentPoints),
            MissionOutcomeTier.Timeout => Math.Max(0, mission.RewardTimeoutDevelopmentPoints),
            MissionOutcomeTier.Failure => Math.Max(0, mission.RewardFailureDevelopmentPoints),
            _ => 0
        };
    }

    private static string ResolveMissionOutcomeTierLabel(MissionOutcomeTier tier)
    {
        return tier switch
        {
            MissionOutcomeTier.Major => Robust.Shared.Localization.Loc.GetString("wh40k-command-runtime-mission-tier-major"),
            MissionOutcomeTier.Minor => Robust.Shared.Localization.Loc.GetString("wh40k-command-runtime-mission-tier-minor"),
            MissionOutcomeTier.Timeout => Robust.Shared.Localization.Loc.GetString("wh40k-command-runtime-mission-tier-timeout"),
            MissionOutcomeTier.Failure => Robust.Shared.Localization.Loc.GetString("wh40k-command-runtime-mission-tier-failure"),
            _ => Robust.Shared.Localization.Loc.GetString("wh40k-command-runtime-mission-tier-timeout")
        };
    }

    private static WH40KMissionOutcomeTier ToPublicMissionOutcomeTier(MissionOutcomeTier tier)
    {
        return tier switch
        {
            MissionOutcomeTier.Major => WH40KMissionOutcomeTier.Major,
            MissionOutcomeTier.Minor => WH40KMissionOutcomeTier.Minor,
            MissionOutcomeTier.Timeout => WH40KMissionOutcomeTier.Timeout,
            MissionOutcomeTier.Failure => WH40KMissionOutcomeTier.Failure,
            _ => WH40KMissionOutcomeTier.Failure
        };
    }

    private static WH40KMissionObjectiveType ToPublicMissionObjectiveType(MissionObjectiveType type)
    {
        return type switch
        {
            MissionObjectiveType.CargoDelivery => WH40KMissionObjectiveType.CargoDelivery,
            MissionObjectiveType.ZoneControl => WH40KMissionObjectiveType.ZoneControl,
            MissionObjectiveType.BannerHold => WH40KMissionObjectiveType.BannerHold,
            _ => WH40KMissionObjectiveType.Unknown
        };
    }

    private bool ApplyTeamDevelopmentPoints(string teamId, int points, string source)
    {
        if (string.IsNullOrWhiteSpace(teamId) || points <= 0)
            return false;

        if (!_teamRule.TryAdjustTeamFrontPoints(teamId, points, out var resolvedTeamId, out _, out _, source: source))
            return false;

        _teamRule.TryAdjustTeamCommandPoints(resolvedTeamId, points, out _, out _, source: source);
        return true;
    }

    private void ApplyMissionTokenReward(string teamId, string tokenId, int durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(tokenId) || string.IsNullOrWhiteSpace(teamId) || durationSeconds <= 0)
            return;

        if (string.Equals(tokenId, TacticalCallDiscountTokenId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyTacticalCallDiscountToken(teamId, durationSeconds);
            DispatchTeamMessage(
                teamId,
                Loc.GetString("wh40k-command-runtime-mission-token-applied",
                    ("duration", durationSeconds)));
            return;
        }

        if (string.Equals(tokenId, IntelEventRollHasteTokenId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyTeamEventRollHasteToken(teamId, durationSeconds);
            DispatchTeamMessage(
                teamId,
                Loc.GetString("wh40k-command-runtime-mission-token-applied-event-roll",
                    ("duration", durationSeconds)));
        }
    }

    private void ApplyTacticalCallDiscountToken(string teamId, int durationSeconds)
    {
        var now = _timing.CurTime;
        var reduction = TimeSpan.FromSeconds(Math.Max(1, durationSeconds));

        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out var uid, out var node))
        {
            if (!string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (node.NextBattleTacticChangeAvailable > now)
                node.NextBattleTacticChangeAvailable = MaxTime(now, node.NextBattleTacticChangeAvailable - reduction);

            if (node.NextReinforcementAvailable > now)
                node.NextReinforcementAvailable = MaxTime(now, node.NextReinforcementAvailable - reduction);

            Dirty(uid, node);
        }
    }

    private void ApplyTeamEventRollHasteToken(string teamId, int durationSeconds)
    {
        if (!_teamEvents.TryGetValue(teamId, out var runtime))
            return;

        var now = _timing.CurTime;
        var reduction = TimeSpan.FromSeconds(Math.Max(1, durationSeconds));

        if (runtime.NextRollAt == TimeSpan.Zero)
            runtime.NextRollAt = now + TimeSpan.FromSeconds(1);

        runtime.NextRollAt = MaxTime(now, runtime.NextRollAt - reduction);
    }

    private void ApplyMissionOutcomeBias(string teamId, MissionOutcomeTier tier)
    {
        if (string.IsNullOrWhiteSpace(teamId) || !_teamEvents.TryGetValue(teamId, out var runtime))
            return;

        runtime.PendingBias = tier switch
        {
            MissionOutcomeTier.Major => TeamEventMissionBias.Momentum,
            MissionOutcomeTier.Minor => TeamEventMissionBias.Momentum,
            MissionOutcomeTier.Failure => TeamEventMissionBias.Stabilizer,
            _ => runtime.PendingBias
        };
    }

    private void CleanupMissionObjectiveRuntime(ActiveMissionRuntime mission, bool keepCargo)
    {
        if (mission.Objective is not { } objective)
            return;

        if (objective.ParachuteUid is { } parachuteUid && Exists(parachuteUid))
            QueueDel(parachuteUid);

        if (objective.BeaconUid is { } beaconUid && Exists(beaconUid))
            QueueDel(beaconUid);

        if (!keepCargo && objective.CargoUid is { } cargoUid && Exists(cargoUid))
            QueueDel(cargoUid);

        foreach (var markerUid in objective.DeliveryMarkerUids)
        {
            if (Exists(markerUid))
                QueueDel(markerUid);
        }
        objective.DeliveryMarkerUids.Clear();

        mission.Objective = null;
    }

    private static float GetEventBiasWeight(TeamEventMissionBias bias, IReadOnlyList<string> tags)
    {
        if (bias == TeamEventMissionBias.None || tags.Count == 0)
            return 1f;

        return bias switch
        {
            TeamEventMissionBias.Momentum when HasAnyTag(tags, "combat", "offense", "pressure", "control") => 1.25f,
            TeamEventMissionBias.Stabilizer when HasAnyTag(tags, "defense", "support", "economy", "logistics", "sustain") => 1.25f,
            _ => 1f
        };
    }

    private float GetMissionSynergyWeight(string teamId, IReadOnlyList<string> tags)
    {
        if (string.IsNullOrWhiteSpace(teamId) || tags.Count == 0)
            return 1f;

        if (!_teamEvents.TryGetValue(teamId, out var runtime) || runtime.ActiveEvent is not { } active)
            return 1f;

        var eventId = active.EventId;
        if (string.IsNullOrWhiteSpace(eventId))
            return 1f;

        return eventId.ToLowerInvariant() switch
        {
            "fireline_surge" when HasAnyTag(tags, "raid", "assault", "breakthrough") => 1.2f,
            "iron_discipline" when HasAnyTag(tags, "hold", "control", "defense") => 1.15f,
            "logistics_corridor" when HasAnyTag(tags, "logistics", "cargo", "convoy", "resource", "escort") => 1.25f,
            "medicae_push" when HasAnyTag(tags, "medicae", "rescue", "hospital") => 1.25f,
            "relay_overclock" when HasAnyTag(tags, "relay", "uplink", "intel") => 1.2f,
            "scrap_windfall" when HasAnyTag(tags, "salvage", "resource", "industry") => 1.2f,
            "vox_jamming_pulse" when HasAnyTag(tags, "intel", "relay", "broadcast") => 1.18f,
            "servitor_rush" when HasAnyTag(tags, "fortification", "restart", "reboot", "build") => 1.2f,
            "suppression_grid" when HasAnyTag(tags, "hold", "control", "objective") => 1.25f,
            "counter_battery_window" when HasAnyTag(tags, "artillery", "raid", "assault") => 1.16f,
            _ => 1f
        };
    }

    private float GetGlobalMissionSynergyWeight(IReadOnlyList<string> teamIds, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0 || teamIds.Count == 0)
            return 1f;

        var best = 1f;
        foreach (var teamId in teamIds)
            best = Math.Max(best, GetMissionSynergyWeight(teamId, tags));

        return Math.Clamp(best, 0.8f, 1.35f);
    }

    private static bool HasAnyTag(IReadOnlyList<string> tags, params string[] wanted)
    {
        if (tags.Count == 0 || wanted.Length == 0)
            return false;

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            foreach (var target in wanted)
            {
                if (string.Equals(tag, target, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static List<string> ResolveDefaultRequiredObjectivePrototypes(string teamId)
    {
        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "WHChaosBanner" };

        return new List<string> { "WHGvardiaBanner", "WHGvardiaBanner2", "MechanicusBanner" };
    }

    private static TeamEventGameplayProfile ResolveTeamEventGameplayProfile(string eventId)
    {
        var id = eventId?.Trim().ToLowerInvariant();
        return id switch
        {
            "fireline_surge" => new TeamEventGameplayProfile
            {
                OutgoingDamageMultiplier = 1.15f,
                MissionProgressMultiplier = 1.05f
            },

            "iron_discipline" => new TeamEventGameplayProfile
            {
                IncomingDamageMultiplier = 0.88f
            },

            "logistics_corridor" => new TeamEventGameplayProfile
            {
                MissionProgressMultiplier = 1.28f,
                PeriodicDevelopmentPoints = 1,
                PeriodicIntervalSeconds = 20
            },

            "medicae_push" => new TeamEventGameplayProfile
            {
                MedicalDelayMultiplier = 0.7f
            },

            "relay_overclock" => new TeamEventGameplayProfile
            {
                CooldownAccelerationPerSecond = 1f
            },

            "scrap_windfall" => new TeamEventGameplayProfile
            {
                PeriodicDevelopmentPoints = 1,
                PeriodicIntervalSeconds = 18
            },

            "vox_jamming_pulse" => new TeamEventGameplayProfile
            {
                EnemyMissionProgressMultiplier = 0.82f
            },

            "servitor_rush" => new TeamEventGameplayProfile
            {
                ConstructionDelayMultiplier = 0.72f
            },

            "suppression_grid" => new TeamEventGameplayProfile
            {
                IncomingDamageMultiplier = 0.92f,
                MissionProgressMultiplier = 1.35f
            },

            "counter_battery_window" => new TeamEventGameplayProfile
            {
                OutgoingDamageMultiplier = 1.05f,
                IncomingDamageMultiplier = 0.94f
            },

            _ => new TeamEventGameplayProfile()
        };
    }

    private static MissionObjectiveType ResolveObjectiveType(ActiveMissionRuntime mission)
    {
        if (mission.ObjectiveType == WH40KCommandMissionObjectiveType.CargoDelivery)
            return MissionObjectiveType.CargoDelivery;

        if (mission.ObjectiveType == WH40KCommandMissionObjectiveType.BannerHold)
            return MissionObjectiveType.BannerHold;

        if (mission.ObjectiveType == WH40KCommandMissionObjectiveType.ZoneControl)
            return MissionObjectiveType.ZoneControl;

        if (HasAnyTag(mission.Tags, "banner", "standard"))
            return MissionObjectiveType.BannerHold;

        if (IsCargoMission(mission))
            return MissionObjectiveType.CargoDelivery;

        return MissionObjectiveType.ZoneControl;
    }

    private static bool IsCargoMission(ActiveMissionRuntime mission)
    {
        if (string.Equals(mission.MissionId, "global_high_value_cargo", StringComparison.OrdinalIgnoreCase))
            return true;

        return HasAnyTag(mission.Tags, "cargo", "convoy", "escort", "intercept", "recovery", "resource", "salvage");
    }

    private static bool IsLootCargoMission(ActiveMissionRuntime mission)
    {
        return HasAnyTag(mission.Tags, "loot", "salvage", "plunder");
    }

    private void AnnounceTeamEventStarted(string teamId, ActiveEventRuntime active)
    {
        var title = ResolveLocalizedOrRaw(active.Title);
        DispatchTeamMessage(
            teamId,
            Loc.GetString("wh40k-command-runtime-event-started",
                ("event", title),
                ("duration", Math.Max(1, active.DurationSeconds))));
    }

    private void AnnounceTeamEventEnded(string teamId, string eventId, string eventTitle)
    {
        var title = ResolveEndedEventTitle(teamId, eventId, eventTitle);
        DispatchTeamMessage(
            teamId,
            Loc.GetString("wh40k-command-runtime-event-ended",
                ("event", title)));
    }

    private string ResolveEndedEventTitle(string teamId, string eventId, string eventTitle)
    {
        if (!string.IsNullOrWhiteSpace(eventTitle))
            return ResolveLocalizedOrRaw(eventTitle);

        if (string.IsNullOrWhiteSpace(eventId))
            return string.Empty;

        if (TryResolveEventProfileForTeam(teamId, out var profile))
        {
            foreach (var config in profile.Events)
            {
                if (!string.Equals(config.Id, eventId, StringComparison.OrdinalIgnoreCase))
                    continue;

                return ResolveLocalizedOrRaw(ResolveEventTitle(config));
            }
        }

        return ResolveLocalizedOrRaw(eventId);
    }

    private void AnnounceMissionStarted(ActiveMissionRuntime mission)
    {
        var missionTitle = ResolveLocalizedOrRaw(mission.Title);
        var coords = BuildMissionCoordinateText(mission);
        if (mission.Scope == WH40KCommandDynamicMissionScope.Global)
        {
            _chat.DispatchServerAnnouncement(
                Loc.GetString("wh40k-command-runtime-mission-global-started",
                    ("mission", missionTitle),
                    ("duration", mission.DurationSeconds),
                    ("coords", coords)));
            return;
        }

        DispatchTeamMessage(
            mission.TeamId,
            Loc.GetString("wh40k-command-runtime-mission-faction-started",
                ("mission", missionTitle),
                ("duration", mission.DurationSeconds),
                ("coords", coords)));

        AnnounceEnemyFactionCounterMission(mission);
    }

    private void AnnounceEnemyFactionCounterMission(ActiveMissionRuntime mission)
    {
        if (mission.Scope != WH40KCommandDynamicMissionScope.Faction ||
            string.IsNullOrWhiteSpace(mission.TeamId))
        {
            return;
        }

        var messageKey = (mission.Objective?.Type ?? ResolveObjectiveType(mission)) == MissionObjectiveType.CargoDelivery
            ? "wh40k-command-runtime-mission-enemy-counter-cargo"
            : "wh40k-command-runtime-mission-enemy-counter-control";
        var missionTitle = ResolveLocalizedOrRaw(mission.Title);
        var enemyName = ResolveTeamName(mission.TeamId);

        foreach (var teamId in _teamRule.GetTeamIds())
        {
            if (string.Equals(teamId, mission.TeamId, StringComparison.OrdinalIgnoreCase))
                continue;

            DispatchTeamMessage(
                teamId,
                Loc.GetString(
                    messageKey,
                    ("enemy", enemyName),
                    ("mission", missionTitle)));
        }
    }

    private static TimeSpan MaxTime(TimeSpan a, TimeSpan b)
    {
        return a >= b ? a : b;
    }

    private string ResolveTeamName(string teamId)
    {
        return _teamRule.TryGetTeamDisplayName(teamId, out var teamName)
            ? teamName
            : teamId;
    }

    private string FormatMapCoordinates(MapCoordinates coordinates)
    {
        if (_mapManager.TryFindGridAt(coordinates, out var gridUid, out var grid))
        {
            var tile = _map.WorldToTile(gridUid, grid, coordinates.Position);
            return Loc.GetString(
                "wh40k-command-runtime-coordinates",
                ("x", tile.X),
                ("y", tile.Y));
        }

        return Loc.GetString(
            "wh40k-command-runtime-coordinates",
            ("x", (int) MathF.Round(coordinates.Position.X)),
            ("y", (int) MathF.Round(coordinates.Position.Y)));
    }

    private static string FormatWorldCoordinates(Vector2 worldPosition)
    {
        return $"WX:{worldPosition.X:0.##} WY:{worldPosition.Y:0.##}";
    }

    private void DispatchTeamMessage(string teamId, string message)
    {
        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(message))
            return;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } attached)
                continue;

            if (HasComp<GhostComponent>(attached))
            {
                _chat.DispatchServerMessage(session, message);
                continue;
            }

            if (!_teamRule.TryGetTeamIdFromEntity(attached, out var playerTeam))
                continue;

            if (!string.Equals(playerTeam, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            _chat.DispatchServerMessage(session, message);
        }
    }

    private string ResolveLocalizedOrRaw(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (Loc.TryGetString(text, out var localized))
            return localized ?? text;

        return text;
    }

    private bool AreSubsystemRequirementsSatisfied(
        string teamId,
        IReadOnlyList<WH40KCommandRuntimeSubsystem> requirements)
    {
        foreach (var requirement in requirements)
        {
            switch (requirement)
            {
                case WH40KCommandRuntimeSubsystem.Cargo:
                    if (!HasCargoSubsystemForTeam(teamId))
                        return false;
                    break;

                case WH40KCommandRuntimeSubsystem.Reclaimer:
                    if (!HasReclaimerSubsystem())
                        return false;
                    break;

                case WH40KCommandRuntimeSubsystem.OreExtractor:
                    if (!HasOreExtractorSubsystemForTeam(teamId))
                        return false;
                    break;

                case WH40KCommandRuntimeSubsystem.MissionBoard:
                    break;
            }
        }

        return true;
    }

    private bool HasCargoSubsystemForTeam(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        var query = EntityQueryEnumerator<CargoLogisticsTierComponent>();
        while (query.MoveNext(out _, out var logistics))
        {
            foreach (var (_, accountTeamId) in logistics.AccountTeams)
            {
                if (string.Equals(accountTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private bool HasReclaimerSubsystem()
    {
        var query = EntityQueryEnumerator<MaterialReclaimerComponent>();
        return query.MoveNext(out _, out _);
    }

    private bool HasOreExtractorSubsystemForTeam(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        var query = EntityQueryEnumerator<WH40KOreExtractorComponent>();
        while (query.MoveNext(out _, out var extractor))
        {
            if (TracksTeam(extractor.TeamIds, extractor.TeamId, teamId))
                return true;
        }

        return false;
    }

    private int GetTrailingLevelGap(string teamId, IReadOnlyList<string> teamIds)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return 0;

        if (!_teamRule.TryGetTeamProgress(teamId, out var ownLevel, out _, out _))
            ownLevel = 1;

        var highestEnemy = ownLevel;
        foreach (var otherTeamId in teamIds)
        {
            if (string.Equals(otherTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_teamRule.TryGetTeamProgress(otherTeamId, out var otherLevel, out _, out _))
                continue;

            if (otherLevel > highestEnemy)
                highestEnemy = otherLevel;
        }

        return Math.Max(0, highestEnemy - ownLevel);
    }

    private string ResolveActiveDoctrineId(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return string.Empty;

        var query = EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out _, out var node))
        {
            if (!string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!node.DoctrineLocked || string.IsNullOrWhiteSpace(node.ActiveDoctrineId))
                continue;

            return node.ActiveDoctrineId;
        }

        return string.Empty;
    }

    private void EnsureRuntimeState(IReadOnlyList<string> teamIds, TimeSpan now)
    {
        foreach (var teamId in teamIds)
        {
            if (string.IsNullOrWhiteSpace(teamId))
                continue;

            if (!_teamEvents.ContainsKey(teamId))
                _teamEvents[teamId] = new TeamEventRuntime { TeamId = teamId };

            if (!_nextTeamMissionRollAt.ContainsKey(teamId))
            {
                if (TryResolveDynamicMissionProfileForTeam(teamId, out var missionProfile))
                    _nextTeamMissionRollAt[teamId] = now + TimeSpan.FromSeconds(Math.Max(1, missionProfile.FirstSpawnAfterRoundStartSeconds));
                else
                    _nextTeamMissionRollAt[teamId] = now + TimeSpan.FromSeconds(720);
            }

            if (!_teamMissions.ContainsKey(teamId))
                _teamMissions[teamId] = null;
        }

        if (_nextGlobalMissionRollAt == TimeSpan.Zero)
        {
            if (TryResolveDynamicMissionProfileForTeam(string.Empty, out var missionProfile))
                _nextGlobalMissionRollAt = now + TimeSpan.FromSeconds(Math.Max(1, missionProfile.FirstSpawnAfterRoundStartSeconds));
            else
                _nextGlobalMissionRollAt = now + TimeSpan.FromSeconds(720);
        }
    }

    private void PruneRemovedTeams(IReadOnlyList<string> teamIds)
    {
        if (teamIds.Count == 0)
        {
            ResetRuntime();
            return;
        }

        var liveTeams = new HashSet<string>(teamIds, StringComparer.OrdinalIgnoreCase);

        foreach (var teamId in _teamEvents.Keys.ToArray())
        {
            if (!liveTeams.Contains(teamId))
                _teamEvents.Remove(teamId);
        }

        foreach (var teamId in _nextTeamMissionRollAt.Keys.ToArray())
        {
            if (!liveTeams.Contains(teamId))
                _nextTeamMissionRollAt.Remove(teamId);
        }

        foreach (var teamId in _teamMissions.Keys.ToArray())
        {
            if (!liveTeams.Contains(teamId))
                _teamMissions.Remove(teamId);
        }

        foreach (var teamId in _teamMissionCooldownEnds.Keys.ToArray())
        {
            if (!liveTeams.Contains(teamId))
                _teamMissionCooldownEnds.Remove(teamId);
        }

        foreach (var teamId in _pendingFactionMissionOfferRefreshTeams.ToArray())
        {
            if (!liveTeams.Contains(teamId))
                _pendingFactionMissionOfferRefreshTeams.Remove(teamId);
        }
    }

    private void RemoveExpiredCooldowns(TeamEventRuntime runtime, TimeSpan now)
    {
        foreach (var eventId in runtime.CooldownEnds.Keys.ToArray())
        {
            if (runtime.CooldownEnds[eventId] <= now)
                runtime.CooldownEnds.Remove(eventId);
        }
    }

    private void RemoveExpiredMissionCooldowns(TimeSpan now)
    {
        foreach (var missionId in _globalMissionCooldownEnds.Keys.ToArray())
        {
            if (_globalMissionCooldownEnds[missionId] <= now)
                _globalMissionCooldownEnds.Remove(missionId);
        }

        foreach (var (teamId, cooldowns) in _teamMissionCooldownEnds.ToArray())
        {
            foreach (var missionId in cooldowns.Keys.ToArray())
            {
                if (cooldowns[missionId] <= now)
                    cooldowns.Remove(missionId);
            }

            if (cooldowns.Count == 0)
                _teamMissionCooldownEnds.Remove(teamId);
        }
    }

    private bool IsMissionOnCooldown(
        WH40KCommandDynamicMissionScope scope,
        string teamId,
        string missionId,
        TimeSpan now)
    {
        if (string.IsNullOrWhiteSpace(missionId))
            return false;

        if (scope == WH40KCommandDynamicMissionScope.Global)
            return _globalMissionCooldownEnds.TryGetValue(missionId, out var cooldownEndAt) && cooldownEndAt > now;

        if (!_teamMissionCooldownEnds.TryGetValue(teamId, out var teamCooldowns))
            return false;

        return teamCooldowns.TryGetValue(missionId, out var teamCooldownEndAt) && teamCooldownEndAt > now;
    }

    private void RegisterMissionCooldown(ActiveMissionRuntime mission, TimeSpan now)
    {
        if (string.IsNullOrWhiteSpace(mission.MissionId))
            return;

        var profileTeamId = mission.Scope == WH40KCommandDynamicMissionScope.Faction ? mission.TeamId : string.Empty;
        if (!TryResolveDynamicMissionProfileForTeam(profileTeamId, out var profile))
            return;

        var cooldownSeconds = GetMissionRepeatCooldownSeconds(profile);
        var cooldownEndAt = now + TimeSpan.FromSeconds(cooldownSeconds);
        if (mission.Scope == WH40KCommandDynamicMissionScope.Global)
        {
            _globalMissionCooldownEnds[mission.MissionId] = cooldownEndAt;
            return;
        }

        if (!_teamMissionCooldownEnds.TryGetValue(mission.TeamId, out var teamCooldowns))
        {
            teamCooldowns = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            _teamMissionCooldownEnds[mission.TeamId] = teamCooldowns;
        }

        teamCooldowns[mission.MissionId] = cooldownEndAt;
    }

    private static int GetMissionRepeatCooldownSeconds(WH40KCommandDynamicMissionProfilePrototype profile)
    {
        var sourceSeconds = profile.RespawnIntervalSecondsMin > 0
            ? profile.RespawnIntervalSecondsMin
            : profile.FirstSpawnAfterRoundStartSeconds;
        var scaled = (int) MathF.Ceiling(Math.Max(120f, sourceSeconds * 0.75f));
        return Math.Clamp(scaled, 120, 1200);
    }

    private bool TryResolveEventProfileForTeam(string teamId, out WH40KCommandTeamRandomEventProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveEventProfileIdForTeam(teamId);
        if (_proto.TryIndex(profileId, out WH40KCommandTeamRandomEventProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (_proto.TryIndex(EventDefaultProfileId, out WH40KCommandTeamRandomEventProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private ProtoId<WH40KCommandTeamRandomEventProfilePrototype> ResolveEventProfileIdForTeam(string teamId)
    {
        if (!_proto.TryIndex(EventTeamMapId, out WH40KCommandTeamRandomEventTeamMapPrototype? teamMap))
            return EventDefaultProfileId;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            if (teamMap.TeamProfiles.TryGetValue(teamId, out var directProfile))
                return directProfile;

            foreach (var (mappedTeamId, mappedProfile) in teamMap.TeamProfiles)
            {
                if (string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    return mappedProfile;
            }
        }

        return teamMap.DefaultProfile;
    }

    private bool TryResolveDynamicMissionProfileForTeam(string teamId, out WH40KCommandDynamicMissionProfilePrototype profile)
    {
        profile = default!;
        var profileId = ResolveDynamicMissionProfileIdForTeam(teamId);
        if (_proto.TryIndex(profileId, out WH40KCommandDynamicMissionProfilePrototype? indexedProfile))
        {
            profile = indexedProfile;
            return true;
        }

        if (_proto.TryIndex(DynamicMissionDefaultProfileId, out WH40KCommandDynamicMissionProfilePrototype? fallbackProfile))
        {
            profile = fallbackProfile;
            return true;
        }

        return false;
    }

    private ProtoId<WH40KCommandDynamicMissionProfilePrototype> ResolveDynamicMissionProfileIdForTeam(string teamId)
    {
        if (!_proto.TryIndex(DynamicMissionTeamMapId, out WH40KCommandDynamicMissionTeamMapPrototype? teamMap))
            return DynamicMissionDefaultProfileId;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            if (teamMap.TeamProfiles.TryGetValue(teamId, out var directProfile))
                return directProfile;

            foreach (var (mappedTeamId, mappedProfile) in teamMap.TeamProfiles)
            {
                if (string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    return mappedProfile;
            }
        }

        return teamMap.DefaultProfile;
    }

    private static Dictionary<string, WH40KCommandTeamRandomEventConfig> BuildEventConfigMap(
        WH40KCommandTeamRandomEventProfilePrototype profile)
    {
        var map = new Dictionary<string, WH40KCommandTeamRandomEventConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in profile.Events)
        {
            if (string.IsNullOrWhiteSpace(config.Id))
                continue;

            map[config.Id] = config;
        }

        return map;
    }

    private static string ResolveEventTitle(WH40KCommandTeamRandomEventConfig config)
    {
        return string.IsNullOrWhiteSpace(config.Title) ? config.Id : config.Title;
    }

    private static string ResolveEventDescription(WH40KCommandTeamRandomEventConfig config)
    {
        return string.IsNullOrWhiteSpace(config.Description) ? config.Id : config.Description;
    }

    private static string ResolveMissionTitle(WH40KCommandDynamicMissionConfig config)
    {
        var titleKey = $"wh40k-command-runtime-mission-{NormalizeMissionIdForLoc(config.Id)}-title";
        if (!string.IsNullOrWhiteSpace(config.Id))
            return titleKey;

        return string.IsNullOrWhiteSpace(config.Title) ? config.Id : config.Title;
    }

    private static string ResolveMissionDescription(WH40KCommandDynamicMissionConfig config)
    {
        var descriptionKey = $"wh40k-command-runtime-mission-{NormalizeMissionIdForLoc(config.Id)}-description";
        if (!string.IsNullOrWhiteSpace(config.Id))
            return descriptionKey;

        return string.IsNullOrWhiteSpace(config.Description) ? config.Id : config.Description;
    }

    private static string NormalizeMissionIdForLoc(string missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId))
            return "unknown";

        return missionId
            .Trim()
            .ToLowerInvariant()
            .Replace("-", "_")
            .Replace(" ", "_");
    }

    private WH40KCommandTeamEventRuntimeState CreateInactiveTeamEventState()
    {
        return new WH40KCommandTeamEventRuntimeState(
            hasProfile: false,
            hasActiveEvent: false,
            activeEventId: string.Empty,
            activeEventTitle: string.Empty,
            activeEventDescription: string.Empty,
            activeRemainingSeconds: 0,
            activeDurationSeconds: 0,
            nextRollSeconds: 0,
            cooldowns: Array.Empty<WH40KCommandEventCooldownRuntimeState>());
    }

    private static WH40KCommandMissionRuntimeState CreateInactiveMissionState()
    {
        return new WH40KCommandMissionRuntimeState(
            isActive: false,
            missionId: string.Empty,
            missionTitle: string.Empty,
            missionDescription: string.Empty,
            scope: WH40KCommandDynamicMissionScope.Global,
            teamId: string.Empty,
            remainingSeconds: 0,
            durationSeconds: 0,
            rewardMajorDevelopmentPoints: 0,
            rewardMinorDevelopmentPoints: 0,
            rewardTimeoutDevelopmentPoints: 0,
            rewardFailureDevelopmentPoints: 0,
            rewardTempoBonusPercent: 0,
            rewardTokenId: string.Empty,
            rewardTokenDurationSeconds: 0);
    }

    private static WH40KCommandMissionRuntimeState BuildMissionRuntimeState(ActiveMissionRuntime? activeMission, TimeSpan now)
    {
        if (activeMission is null)
            return CreateInactiveMissionState();

        var remainingSeconds = activeMission.EndsAt > now
            ? Math.Max(0, (int) Math.Ceiling((activeMission.EndsAt - now).TotalSeconds))
            : 0;

        return new WH40KCommandMissionRuntimeState(
            isActive: true,
            missionId: activeMission.MissionId,
            missionTitle: activeMission.Title,
            missionDescription: activeMission.Description,
            scope: activeMission.Scope,
            teamId: activeMission.TeamId,
            remainingSeconds: remainingSeconds,
            durationSeconds: Math.Max(1, activeMission.DurationSeconds),
            rewardMajorDevelopmentPoints: Math.Max(0, activeMission.RewardMajorDevelopmentPoints),
            rewardMinorDevelopmentPoints: Math.Max(0, activeMission.RewardMinorDevelopmentPoints),
            rewardTimeoutDevelopmentPoints: Math.Max(0, activeMission.RewardTimeoutDevelopmentPoints),
            rewardFailureDevelopmentPoints: Math.Max(0, activeMission.RewardFailureDevelopmentPoints),
            rewardTempoBonusPercent: Math.Max(0, activeMission.RewardTempoBonusPercent),
            rewardTokenId: activeMission.RewardTokenId ?? string.Empty,
            rewardTokenDurationSeconds: Math.Max(0, activeMission.RewardTokenDurationSeconds));
    }

    private int RollIntervalSeconds(int minSeconds, int maxSeconds)
    {
        var min = Math.Max(1, minSeconds);
        var max = Math.Max(min, maxSeconds);
        return _random.Next(min, max + 1);
    }

    private bool TryPickWeighted<T>(IReadOnlyList<(T Entry, float Weight)> weighted, out T selected)
    {
        selected = default!;
        if (weighted.Count == 0)
            return false;

        var totalWeight = 0f;
        foreach (var (_, weight) in weighted)
        {
            if (weight > 0f)
                totalWeight += weight;
        }

        if (totalWeight <= 0f)
            return false;

        var roll = _random.NextFloat() * totalWeight;
        foreach (var (entry, weight) in weighted)
        {
            if (weight <= 0f)
                continue;

            roll -= weight;
            if (roll <= 0f)
            {
                selected = entry;
                return true;
            }
        }

        selected = weighted[^1].Entry;
        return true;
    }

    private static bool TracksTeam(IReadOnlyCollection<string> teamIds, string teamId, string targetTeamId)
    {
        if (teamIds.Count > 0)
            return teamIds.Any(id => string.Equals(id, targetTeamId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(teamId))
            return string.Equals(teamId, targetTeamId, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private void ResetRuntime()
    {
        if (_globalMission is { } activeGlobal)
            CleanupMissionObjectiveRuntime(activeGlobal, keepCargo: false);

        foreach (var mission in _teamMissions.Values)
        {
            if (mission is null)
                continue;

            CleanupMissionObjectiveRuntime(mission, keepCargo: false);
        }

        var eventEffects = EntityQueryEnumerator<WH40KTeamEventEffectComponent>();
        while (eventEffects.MoveNext(out var uid, out _))
        {
            RemComp<WH40KTeamEventEffectComponent>(uid);
            _movement.RefreshMovementSpeedModifiers(uid);
        }

        _teamEvents.Clear();
        _nextTeamMissionRollAt.Clear();
        _teamMissions.Clear();
        _globalMissionCooldownEnds.Clear();
        _teamMissionCooldownEnds.Clear();
        _pendingFactionMissionOfferRefreshTeams.Clear();
        _globalMission = null;
        _nextGlobalMissionRollAt = TimeSpan.Zero;
    }
}
