using System.Numerics;
using Content.Server.Hands.Systems;
using Content.Server.Light.EntitySystems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.Objectives.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.VendingMachines;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.HeavyBolter;
using Content.Shared._WH40K.Influence;
using Content.Shared._WH40K.Mortar;
using Content.Shared.Access.Components;
using Content.Shared.CombatMode;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.LandMines;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Slippery;
using Content.Shared.VendingMachines;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weather;
using Robust.Shared.Physics;
using Content.Shared.Wires;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

/// <summary>
/// Wave-defense capability layer:
/// 1) role-gated mine handling (Sapper-first),
/// 2) no-gear loadout acquire/equip loop,
/// 3) weather shelter enter/exit with bounded timeout,
/// 4) service logistics jobs (open/acquire/deliver/restock) with reservation safety,
/// 5) role-gated deploy flow (limited engineering actions with caps/timeouts),
/// 6) influence-point capture/defense steering for WH40K team members,
/// 7) enemy objective assault with blocked/unreachable fallback telemetry,
/// 8) collective team director layer (macro orders with bounded stability guards).
/// </summary>
public sealed class NPCWaveCapabilitySystem : EntitySystem
{
    [Dependency] private readonly NPCBenchmarkSystem _bench = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly ItemToggleSystem _itemToggle = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedWeatherSystem _weather = default!;
    [Dependency] private readonly NPCWaveCommunicationSystem _waveComms = default!;
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly SharedWiresSystem _wires = default!;
    [Dependency] private readonly SharedWieldableSystem _wieldable = default!;
    [Dependency] private readonly VendingMachineSystem _vending = default!;
    [Dependency] private readonly RoofSystem _roof = default!;
    [Dependency] private readonly PathfindingSystem _pathfinding = default!;

    private readonly Dictionary<EntityUid, WaveRuntimeState> _states = new();
    private readonly List<EntityUid> _statePruneBuffer = new();
    private readonly HashSet<EntityUid> _lookupBuffer = new();
    private readonly HashSet<Entity<LandMineComponent>> _mineLookupBuffer = new();
    private readonly Dictionary<EntityUid, ServiceReservationState> _serviceReservations = new();
    private readonly List<EntityUid> _serviceReservationPruneBuffer = new();
    private readonly List<ServiceMachineCandidate> _serviceMachineCandidates = new();
    private readonly List<(EntityUid Uid, Vector2 Position)> _teamDirectorMembers = new();
    private readonly List<(int Index, float Distance)> _teamDirectorDistances = new();
    private readonly Dictionary<string, TeamDirectorState> _teamDirectorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _teamDirectorPruneBuffer = new();
    private readonly HashSet<EntProtoId> _aggressiveWeatherIds = new()
    {
        "WHMetalHail",
        "WHIonStorm",
        "WHAcidRain",
        "WHSandHurricane",
        "WHToxicAshFront",
        "WHGellarTremor",
        "WHMachineCorrosionStorm",
        "WHBlackFront",
    };

    private bool _enabled = true;
    private float _updateIntervalSeconds = 0.22f;
    private float _hazardScanIntervalSeconds = 0.28f;
    private float _hazardScanRadius = 2.3f;
    private float _hazardMemoryTtlSeconds = 2.5f;
    private float _loadoutScanIntervalSeconds = 0.24f;
    private float _loadoutSearchRadius = 8f;
    private float _loadoutReadyTimeoutSeconds = 8f;
    private float _weatherScanIntervalSeconds = 0.30f;
    private int _shelterSearchRadiusTiles = 12;
    private float _shelterTimeoutSeconds = 3.5f;
    private float _shelterReentryCooldownSeconds = 1.6f;
    private float _serviceScanIntervalSeconds = 0.24f;
    private float _serviceSearchRadius = 12f;
    private float _serviceReservationTtlSeconds = 9f;
    private float _serviceJobTimeoutSeconds = 18f;
    private float _deployScanIntervalSeconds = 0.30f;
    private float _deploySearchRadius = 10f;
    private float _deployJobTimeoutSeconds = 14f;
    private int _deployMaxPerNpc = 1;
    private float _influenceScanIntervalSeconds = 0.30f;
    private float _influenceSearchRadius = 18f;
    private float _influenceHoldRadiusFactor = 0.65f;
    private float _objectiveScanIntervalSeconds = 0.32f;
    private float _objectiveSearchRadius = 24f;
    private float _objectiveHoldRadiusFactor = 0.65f;
    private float _objectivePathChunkDistance = 40f;
    private float _objectiveStagingSearchRadius = 96f;
    private float _objectiveStagingMinGain = 10f;
    private int _objectiveNoPathFallbackRetries = 3;
    private int _objectiveNoPathUnreachableRetries = 6;
    private bool _directorEnabled = true;
    private float _directorTickIntervalSeconds = 0.35f;
    private float _directorOrderTtlSeconds = 1.8f;
    private float _directorHysteresisScoreDelta = 9f;
    private float _directorReassignCooldownSeconds = 1.2f;
    private float _directorUrgentPreemptCooldownSeconds = 0.8f;
    private float _directorDefenseThreatRadius = 10f;
    private float _directorDefenseThreatMemorySeconds = 10f;
    private int _directorResupplyShortageThreshold = 1;
    private float _commsScanIntervalSeconds = 0.80f;
    private TimeSpan _nextStatePruneTime = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityEnabled, value => _enabled = value, true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityUpdateIntervalSeconds, value => _updateIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityHazardScanIntervalSeconds, value => _hazardScanIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityHazardScanRadius, value => _hazardScanRadius = MathF.Max(0.5f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityHazardMemoryTtlSeconds, value => _hazardMemoryTtlSeconds = MathF.Max(0.2f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, value => _loadoutScanIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityLoadoutSearchRadius, value => _loadoutSearchRadius = MathF.Max(1f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, value => _loadoutReadyTimeoutSeconds = MathF.Max(0.5f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityWeatherScanIntervalSeconds, value => _weatherScanIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityShelterSearchRadiusTiles, value => _shelterSearchRadiusTiles = Math.Max(2, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityShelterTimeoutSeconds, value => _shelterTimeoutSeconds = MathF.Max(0.5f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityShelterReentryCooldownSeconds, value => _shelterReentryCooldownSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, value => _serviceScanIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityServiceSearchRadius, value => _serviceSearchRadius = MathF.Max(1f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, value => _serviceReservationTtlSeconds = MathF.Max(0.5f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, value => _serviceJobTimeoutSeconds = MathF.Max(0.5f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityDeployScanIntervalSeconds, value => _deployScanIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityDeploySearchRadius, value => _deploySearchRadius = MathF.Max(1f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityDeployJobTimeoutSeconds, value => _deployJobTimeoutSeconds = MathF.Max(0.5f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityDeployMaxPerNpc, value => _deployMaxPerNpc = Math.Max(0, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, value => _influenceScanIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityInfluenceSearchRadius, value => _influenceSearchRadius = MathF.Max(1f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityInfluenceHoldRadiusFactor, value => _influenceHoldRadiusFactor = Math.Clamp(value, 0.2f, 1f), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, value => _objectiveScanIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityObjectiveSearchRadius, value => _objectiveSearchRadius = MathF.Max(1f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, value => _objectiveHoldRadiusFactor = Math.Clamp(value, 0.2f, 1f), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, value => _objectiveNoPathFallbackRetries = Math.Max(1, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, value => _objectiveNoPathUnreachableRetries = Math.Max(_objectiveNoPathFallbackRetries + 1, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorEnabled, value => _directorEnabled = value, true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorTickIntervalSeconds, value => _directorTickIntervalSeconds = MathF.Max(0.05f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorOrderTtlSeconds, value => _directorOrderTtlSeconds = MathF.Max(0.2f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorHysteresisScoreDelta, value => _directorHysteresisScoreDelta = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorReassignCooldownSeconds, value => _directorReassignCooldownSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds, value => _directorUrgentPreemptCooldownSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorDefenseThreatRadius, value => _directorDefenseThreatRadius = MathF.Max(1f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorDefenseThreatMemorySeconds, value => _directorDefenseThreatMemorySeconds = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, CCVars.NPCWaveDirectorResupplyShortageThreshold, value => _directorResupplyShortageThreshold = Math.Max(1, value), true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        using var benchScope = _bench.Measure("npc.wave.capability.update");

        var now = _timing.CurTime;

        if (now >= _nextStatePruneTime)
        {
            _nextStatePruneTime = now + TimeSpan.FromSeconds(2);
            _statePruneBuffer.Clear();
            _serviceReservationPruneBuffer.Clear();
            _teamDirectorPruneBuffer.Clear();

            foreach (var uid in _states.Keys)
            {
                if (TerminatingOrDeleted(uid) || !HasComp<HTNComponent>(uid))
                    _statePruneBuffer.Add(uid);
            }

            foreach (var uid in _statePruneBuffer)
            {
                if (_states.TryGetValue(uid, out var stale))
                    ReleaseServiceReservation(uid, stale);
                _states.Remove(uid);
            }

            foreach (var (machine, reservation) in _serviceReservations)
            {
                if (TerminatingOrDeleted(machine) ||
                    TerminatingOrDeleted(reservation.Owner) ||
                    reservation.ExpiresAt <= now)
                {
                    _serviceReservationPruneBuffer.Add(machine);
                }
            }

            foreach (var machine in _serviceReservationPruneBuffer)
            {
                _serviceReservations.Remove(machine);
            }

            foreach (var teamId in _teamDirectorStates.Keys)
            {
                if (!TeamHasWaveMembers(teamId))
                    _teamDirectorPruneBuffer.Add(teamId);
            }

            foreach (var teamId in _teamDirectorPruneBuffer)
            {
                _teamDirectorStates.Remove(teamId);
            }
        }

        var processed = 0;
        var query = EntityQueryEnumerator<ActiveNPCComponent, HTNComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var htn, out var xform))
        {
            if (!IsWaveRole(htn))
            {
                _states.Remove(uid);
                continue;
            }

            if (!_states.TryGetValue(uid, out var state))
            {
                state = new WaveRuntimeState
                {
                    NextUpdateTime = now + TimeSpan.FromSeconds(_random.NextFloat(0f, _updateIntervalSeconds))
                };
                _states[uid] = state;
            }

            if (now < state.NextUpdateTime)
                continue;

            state.NextUpdateTime = now + TimeSpan.FromSeconds(_updateIntervalSeconds);
            state.ObjectivePathRequestsThisTick = 0;

            RunHazardLayer(uid, htn, xform, state, now);
            RunLoadoutLayer(uid, htn, xform, state, now);
            RunWeatherLayer(uid, htn, xform, state, now);
            RunDirectorLayer(uid, htn, xform, state, now);
            RunInfluenceLayer(uid, htn, xform, state, now);
            RunObjectiveLayer(uid, htn, xform, state, now);
            RunServiceLayer(uid, htn, xform, state, now);
            RunDeployLayer(uid, htn, xform, state, now);
            RunCommsLayer(uid, htn, state, now);

            if (state.ObjectivePathRequestsThisTick > 0)
                _bench.RecordCount("npc.wave.path.requests_per_tick", state.ObjectivePathRequestsThisTick);

            processed++;
        }

        _bench.RecordCount("npc.wave.capability.entities", processed);
    }

    private bool IsWaveRole(HTNComponent htn)
    {
        return htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveRoleEnabled, out var enabled, EntityManager) && enabled;
    }

    private void RunHazardLayer(EntityUid uid, HTNComponent htn, TransformComponent xform, WaveRuntimeState state, TimeSpan now)
    {
        if (now < state.NextHazardScanTime)
            return;

        state.NextHazardScanTime = now + TimeSpan.FromSeconds(_hazardScanIntervalSeconds);
        PruneHazardMemory(state, now);
        if (state.HazardAvoidUntil <= now)
        {
            state.HazardAvoidCoordinates = EntityCoordinates.Invalid;
            state.HazardFocus = EntityUid.Invalid;
        }

        var hazardScanRadius = GetHazardScanRadius(state);
        var canNeutralizeMines =
            htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveMineHandlingEnabled, out var mineHandlingEnabled, EntityManager) &&
            mineHandlingEnabled;

        if (TryFindNearestArmedMine(uid, xform, hazardScanRadius, out var mine, out var mineXform, out var distance))
        {
            var wasKnown =
                state.HazardMemory.TryGetValue(mine, out var knownUntil) &&
                knownUntil > now;
            state.HazardMemory[mine] = now + TimeSpan.FromSeconds(_hazardMemoryTtlSeconds);

            if (!wasKnown)
                _bench.RecordCount("npc.wave.hazard.memory_add", 1);

            if (!canNeutralizeMines)
            {
                _bench.RecordCount("npc.wave.hazard.mine_skip_non_sapper", 1);

                if (distance <= SharedInteractionSystem.InteractionRange + 0.25f &&
                    TryComp(uid, out NPCSteeringComponent? steering))
                {
                    steering.CurrentPath.Clear();
                    steering.NextPathRequestTime = TimeSpan.Zero;
                    _bench.RecordCount("npc.wave.hazard.mine_avoid_repath", 1);
                }

                return;
            }

            if (distance > SharedInteractionSystem.InteractionRange + 0.1f)
            {
                _steering.TryRegister(uid, mineXform.Coordinates);
                _bench.RecordCount("npc.wave.hazard.mine_approach", 1);
                return;
            }

            _bench.RecordCount("npc.wave.hazard.mine_neutralize_attempt", 1);

            var neutralized = false;
            if (TryComp(mine, out ItemToggleComponent? toggle))
            {
                neutralized = _itemToggle.TryDeactivate((mine, toggle), uid, predicted: false, showPopup: false);

                // Some armable mine setups can reject normal toggle flow; sapper fallback guarantees role behavior.
                if (!neutralized && toggle.Activated)
                {
                    toggle.Activated = false;
                    Dirty(mine, toggle);
                    neutralized = true;
                    _bench.RecordCount("npc.wave.hazard.mine_neutralize_forced", 1);
                }
            }

            if (neutralized)
            {
                _bench.RecordCount("npc.wave.hazard.mine_neutralize_success", 1);
                state.HazardMemory[mine] = now + TimeSpan.FromSeconds(_hazardMemoryTtlSeconds);
                _waveComms.TryMineCleared(uid, mine);
            }
            else
            {
                _bench.RecordCount("npc.wave.hazard.mine_neutralize_fail", 1);
            }

            return;
        }

        if (!TryFindNearestEnvironmentalHazard(uid, xform, hazardScanRadius, out var hazard, out var hazardXform, out var hazardDistance))
            return;

        if (!ShouldReactToEnvironmentalHazard(uid, htn, xform, state, hazard, hazardXform, hazardDistance))
        {
            _bench.RecordCount("npc.wave.hazard.environment_skip_offroute", 1);
            return;
        }

        var wasKnownHazard =
            state.HazardMemory.TryGetValue(hazard, out var hazardKnownUntil) &&
            hazardKnownUntil > now;
        state.HazardMemory[hazard] = now + TimeSpan.FromSeconds(_hazardMemoryTtlSeconds);

        if (!wasKnownHazard)
            _bench.RecordCount("npc.wave.hazard.environment_memory_add", 1);

        if (!TryBuildHazardDetour(uid, htn, xform, hazard, hazardXform.Coordinates, out var detourCoordinates))
        {
            _bench.RecordCount("npc.wave.hazard.environment_detour_fail", 1);
            return;
        }

        state.HazardFocus = hazard;
        state.HazardAvoidCoordinates = ResolveWaveSteeringCoordinates(uid, detourCoordinates, avoidHazards: true);
        state.HazardAvoidUntil = now + TimeSpan.FromSeconds(MathF.Max(1.8f, _updateIntervalSeconds * 8f));
        if (TryComp(uid, out NPCSteeringComponent? hazardSteering))
        {
            hazardSteering.CurrentPath.Clear();
            hazardSteering.PathfindToken?.Cancel();
            hazardSteering.PathfindToken = null;
            hazardSteering.NextPathRequestTime = TimeSpan.Zero;
            _bench.RecordCount("npc.wave.hazard.environment_repath", 1);
        }

        _steering.TryRegister(uid, detourCoordinates);
        _bench.RecordCount("npc.wave.hazard.environment_detour", 1);
    }

    private float GetHazardScanRadius(WaveRuntimeState state)
    {
        var radius = _hazardScanRadius;

        if (state.DirectorOrder == WaveDirectorOrder.PushObjective ||
            state.DirectorOrder == WaveDirectorOrder.BreachLane ||
            state.DirectorOrder == WaveDirectorOrder.DefendBase)
        {
            radius += 1.5f;
        }

        if (state.ObjectiveNoPathActive)
            radius += MathF.Min(1.25f, state.ObjectiveNoPathStreak * 0.2f);

        return MathF.Max(0.5f, radius);
    }

    private bool ShouldReactToEnvironmentalHazard(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        WaveRuntimeState state,
        EntityUid hazard,
        TransformComponent hazardXform,
        float hazardDistance)
    {
        if (hazardDistance <= 0.9f)
            return true;

        // Distant environmental hazards are already discouraged by pathfinding tile costs.
        // Reserve explicit local detours for near-contact cases, otherwise squads thrash by
        // repeatedly clearing and rebuilding paths around the same hazard strip.
        if (hazardDistance > 1.65f &&
            TryComp(uid, out NPCSteeringComponent? steering) &&
            steering.CurrentPath.Count > 0 &&
            !state.ObjectiveNoPathActive)
        {
            _bench.RecordCount("npc.wave.hazard.environment_skip_path_owned", 1);
            return false;
        }

        if (state.HazardFocus == hazard &&
            state.HazardAvoidUntil > _timing.CurTime &&
            hazardDistance > 1.1f)
        {
            return false;
        }

        if (!TryGetHazardTravelDirection(uid, htn, xform, out var travelDirection))
            return false;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);
        var hazardMap = _transform.ToMapCoordinates(hazardXform.Coordinates);
        if (origin.MapId == MapId.Nullspace || hazardMap.MapId != origin.MapId)
            return false;

        var toHazard = hazardMap.Position - origin.Position;
        if (toHazard.LengthSquared() < 0.01f)
            return true;

        var hazardDirection = Vector2.Normalize(toHazard);
        return Vector2.Dot(hazardDirection, travelDirection) >= 0.25f;
    }

    private bool TryGetHazardTravelDirection(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        out Vector2 direction)
    {
        direction = Vector2.Zero;
        var origin = _transform.ToMapCoordinates(xform.Coordinates);
        if (origin.MapId == MapId.Nullspace)
            return false;

        if (TryComp(uid, out NPCSteeringComponent? steering) &&
            steering.Coordinates.IsValid(EntityManager))
        {
            var steerMap = _transform.ToMapCoordinates(steering.Coordinates);
            if (steerMap.MapId == origin.MapId)
                direction = steerMap.Position - origin.Position;
        }

        if (direction.LengthSquared() < 0.25f &&
            htn.Blackboard.TryGetValue<EntityCoordinates>("TargetCoordinates", out var targetCoordinates, EntityManager) &&
            targetCoordinates.IsValid(EntityManager))
        {
            var targetMap = _transform.ToMapCoordinates(targetCoordinates);
            if (targetMap.MapId == origin.MapId)
                direction = targetMap.Position - origin.Position;
        }

        if (direction.LengthSquared() < 0.25f &&
            htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var orderedTarget, EntityManager) &&
            orderedTarget != EntityUid.Invalid &&
            TryComp(orderedTarget, out TransformComponent? targetXform))
        {
            var targetMap = _transform.ToMapCoordinates(targetXform.Coordinates);
            if (targetMap.MapId == origin.MapId)
                direction = targetMap.Position - origin.Position;
        }

        if (direction.LengthSquared() < 0.25f)
            return false;

        direction = Vector2.Normalize(direction);
        return true;
    }

    private void RunLoadoutLayer(EntityUid uid, HTNComponent htn, TransformComponent xform, WaveRuntimeState state, TimeSpan now)
    {
        if (now < state.NextLoadoutScanTime)
            return;

        state.NextLoadoutScanTime = now + TimeSpan.FromSeconds(_loadoutScanIntervalSeconds);

        if (!htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveLoadoutAcquireEnabled, out var loadoutEnabled, EntityManager) ||
            !loadoutEnabled ||
            !TryComp(uid, out HandsComponent? hands))
        {
            return;
        }

        if (TryGetHeldCombatItem(uid, hands, out var heldCombatItem))
        {
            TryEquipHeldCombatItem(uid, heldCombatItem);
            FinalizeLoadoutReadiness(state, now);
            return;
        }

        if (state.LoadoutStartTime == null)
        {
            state.LoadoutStartTime = now;
            state.LoadoutTimeoutReported = false;
        }
        else if (!state.LoadoutTimeoutReported &&
                 (now - state.LoadoutStartTime.Value).TotalSeconds > _loadoutReadyTimeoutSeconds)
        {
            _bench.RecordCount("npc.wave.loadout.ready_timeout", 1);
            state.LoadoutTimeoutReported = true;
        }

        if (!TryFindNearestLoadoutItem(uid, xform, _loadoutSearchRadius, out var source, out var sourceXform, out var distance))
        {
            _bench.RecordCount("npc.wave.loadout.search_miss", 1);
            return;
        }

        _bench.RecordCount("npc.wave.loadout.search_hit", 1);

        if (distance > SharedInteractionSystem.InteractionRange + 0.1f)
        {
            _steering.TryRegister(uid, sourceXform.Coordinates);
            _bench.RecordCount("npc.wave.loadout.seek_source", 1);
            return;
        }

        _bench.RecordCount("npc.wave.loadout.acquire_attempt", 1);

        var acquired = _interaction.InteractionActivate(uid, source);

        if (!acquired)
        {
            acquired = _hands.TryPickupAnyHand(uid, source, checkActionBlocker: false, animateUser: false, animate: false, handsComp: hands);
        }

        if (acquired)
            _bench.RecordCount("npc.wave.loadout.acquire_success", 1);
        else
            _bench.RecordCount("npc.wave.loadout.acquire_fail", 1);
    }

    private void RunWeatherLayer(EntityUid uid, HTNComponent htn, TransformComponent xform, WaveRuntimeState state, TimeSpan now)
    {
        if (now < state.NextWeatherScanTime)
            return;

        state.NextWeatherScanTime = now + TimeSpan.FromSeconds(_weatherScanIntervalSeconds);

        if (!htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveWeatherShelterEnabled, out var shelterEnabled, EntityManager) ||
            !shelterEnabled)
        {
            if (state.ShelterActive)
                ExitShelter(uid, htn, xform, state, now, timeoutExit: false);
            return;
        }

        if (now < state.ShelterCooldownUntil)
            return;

        if (!TryGetActiveAggressiveWeather(xform.MapID, out var weatherProto))
        {
            if (state.ShelterActive)
                ExitShelter(uid, htn, xform, state, now, timeoutExit: false);
            return;
        }

        var exposed = _weather.CanWeatherAffectEntity(uid, weatherProto, xform);

        if (!state.ShelterActive)
        {
            if (!exposed)
                return;

            if (!TryFindNearestRoofedCoordinates(xform, out var shelterCoordinates))
            {
                _bench.RecordCount("npc.wave.weather.shelter_search_miss", 1);
                return;
            }

            state.ShelterActive = true;
            state.ShelterEnteredAt = now;
            state.ShelterCoordinates = shelterCoordinates;
            state.ShelterReturnCoordinates = TryGetReturnCoordinates(uid, xform);
            htn.Blackboard.SetValue(NPCBlackboard.WaveShelterActive, true);

            _steering.TryRegister(uid, shelterCoordinates);
            _bench.RecordCount("npc.wave.weather.shelter_enter", 1);
            return;
        }

        if (!state.ShelterCoordinates.IsValid(EntityManager))
        {
            state.ShelterActive = false;
            htn.Blackboard.Remove<bool>(NPCBlackboard.WaveShelterActive);
            return;
        }

        _steering.TryRegister(uid, state.ShelterCoordinates);

        if ((now - state.ShelterEnteredAt).TotalSeconds >= _shelterTimeoutSeconds)
        {
            ExitShelter(uid, htn, xform, state, now, timeoutExit: true);
        }
    }

    private void RunServiceLayer(EntityUid uid, HTNComponent htn, TransformComponent xform, WaveRuntimeState state, TimeSpan now)
    {
        if (now < state.NextServiceScanTime)
            return;

        state.NextServiceScanTime = now + TimeSpan.FromSeconds(_serviceScanIntervalSeconds);

        if (!htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveServiceEnabled, out var serviceEnabled, EntityManager) ||
            !serviceEnabled ||
            !TryComp(uid, out HandsComponent? hands))
        {
            if (state.ServiceJobActive)
                AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        if (state.DirectorOrder != WaveDirectorOrder.None &&
            state.DirectorOrder != WaveDirectorOrder.Resupply &&
            state.DirectorOrder != WaveDirectorOrder.Regroup)
        {
            if (state.ServiceJobActive)
                AbortServiceJob(uid, state, now, timeout: false);

            _bench.RecordCount("npc.wave.service.director_skip", 1);
            return;
        }

        if (HasCombatTarget(uid, htn))
        {
            if (state.ServiceJobActive)
            {
                _bench.RecordCount("npc.wave.service.combat_preempt", 1);
                AbortServiceJob(uid, state, now, timeout: false);
            }

            return;
        }

        if (state.ServiceRestockPending)
        {
            if (now < state.ServicePendingRestockUntil)
                return;

            ResolvePendingServiceRestock(uid, state, now);
            return;
        }

        if (state.ServiceJobActive &&
            (now - state.ServiceStartedAt).TotalSeconds > _serviceJobTimeoutSeconds)
        {
            _bench.RecordCount("npc.wave.service.job_timeout", 1);
            AbortServiceJob(uid, state, now, timeout: true);
            return;
        }

        if (!state.ServiceJobActive)
        {
            if (!TryAssignServiceJob(uid, xform, hands, state, now))
                return;
        }

        if (state.ServiceMachine == EntityUid.Invalid ||
            !TryComp(state.ServiceMachine, out VendingMachineComponent? machineComp))
        {
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        if (!MachineNeedsRestock(machineComp))
        {
            CompleteServiceJob(uid, state);
            return;
        }

        RefreshServiceReservation(uid, state, now);

        if (TryGetHeldCompatibleRestockPackage(uid, hands, machineComp.PackPrototypeId, out var heldPackage))
        {
            ProcessServiceDelivery(uid, xform, state, now, heldPackage, machineComp);
            return;
        }

        if (!EnsureServiceAcquireCapacity(uid, hands, machineComp.PackPrototypeId))
        {
            _bench.RecordCount("npc.wave.service.acquire_blocked_no_hand", 1);
            return;
        }

        ProcessServiceAcquire(uid, xform, state, now, hands);
    }

    private void RunDeployLayer(EntityUid uid, HTNComponent htn, TransformComponent xform, WaveRuntimeState state, TimeSpan now)
    {
        if (now < state.NextDeployScanTime)
            return;

        state.NextDeployScanTime = now + TimeSpan.FromSeconds(_deployScanIntervalSeconds);

        var deployEnabled =
            htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveDeployEnabled, out var deployFlag, EntityManager) &&
            deployFlag;
        var hasHands = TryComp(uid, out HandsComponent? hands);

        if (!deployEnabled ||
            _deployMaxPerNpc <= 0 ||
            !hasHands)
        {
            if (state.DeployJobActive)
                AbortDeployJob(state);

            if (!deployEnabled &&
                TryGetHeldDeployableItem(uid, hands, out _, out _))
            {
                _bench.RecordCount("npc.wave.deploy.role_blocked_non_enabled", 1);
            }

            return;
        }

        if (HasCombatTarget(uid, htn))
        {
            if (state.DeployJobActive)
            {
                _bench.RecordCount("npc.wave.deploy.combat_preempt", 1);
                AbortDeployJob(state);
            }

            return;
        }

        if (state.DeployCompletedCount >= _deployMaxPerNpc)
        {
            _bench.RecordCount("npc.wave.deploy.cap_reached", 1);
            return;
        }

        if (state.DeployJobActive)
        {
            ProcessActiveDeployJob(uid, hands!, state, now);
            return;
        }

        if (!TryGetHeldDeployableItem(uid, hands, out var heldDeployItem, out var heldPlacement))
        {
            if (!TryFindNearestDeployItem(uid, xform, _deploySearchRadius, out var source, out var sourceXform))
                return;

            var sourceDistance = (_transform.ToMapCoordinates(sourceXform.Coordinates).Position -
                                  _transform.ToMapCoordinates(xform.Coordinates).Position).Length();

            if (sourceDistance > SharedInteractionSystem.InteractionRange + 0.1f)
            {
                _steering.TryRegister(uid, sourceXform.Coordinates);
                _bench.RecordCount("npc.wave.deploy.seek_source", 1);
                return;
            }

            _bench.RecordCount("npc.wave.deploy.acquire_attempt", 1);
            var acquired = _interaction.InteractionActivate(uid, source);
            if (!acquired)
            {
                acquired = _hands.TryPickupAnyHand(uid, source, checkActionBlocker: false, animateUser: false, animate: false, handsComp: hands);
            }

            if (acquired)
                _bench.RecordCount("npc.wave.deploy.acquire_success", 1);
            else
                _bench.RecordCount("npc.wave.deploy.acquire_fail", 1);

            return;
        }

        if (!TryFindDeployCoordinates(uid, xform, heldPlacement, out var deployCoordinates, out var deployDirection))
        {
            _bench.RecordCount("npc.wave.deploy.fail", 1);
            _bench.RecordCount("npc.wave.deploy.fail_no_spot", 1);
            return;
        }

        if (!_interaction.InRangeUnobstructed(uid, deployCoordinates, heldPlacement.Range))
        {
            _steering.TryRegister(uid, deployCoordinates);
            _bench.RecordCount("npc.wave.deploy.seek_target", 1);
            return;
        }

        var attempt = new HandheldEntityPlacementAttemptEvent(uid, deployCoordinates, deployDirection);
        RaiseLocalEvent(heldDeployItem, attempt);
        _bench.RecordCount("npc.wave.deploy.attempt", 1);

        if (attempt.Cancelled || attempt.DeployDelay <= TimeSpan.Zero)
        {
            _bench.RecordCount("npc.wave.deploy.fail", 1);
            _bench.RecordCount("npc.wave.deploy.fail_attempt_rejected", 1);
            return;
        }

        state.DeployJobActive = true;
        state.DeployItem = heldDeployItem;
        state.DeployCoordinates = attempt.Coordinates;
        state.DeployDirection = attempt.Direction;
        state.DeployStartedAt = now;
        state.DeployResolveAt = now + attempt.DeployDelay;
        _bench.RecordCount("npc.wave.deploy.job_started", 1);
    }

    private void RunDirectorLayer(EntityUid uid, HTNComponent htn, TransformComponent xform, WaveRuntimeState state, TimeSpan now)
    {
        if (!_directorEnabled ||
            !htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveDirectorEnabled, out var directorRoleEnabled, EntityManager) ||
            !directorRoleEnabled)
        {
            ClearDirectorOrder(htn, state);
            return;
        }

        if (!TryResolveNpcTeamId(uid, out var teamId))
        {
            ClearDirectorOrder(htn, state);
            _bench.RecordCount("npc.wave.director.no_team", 1);
            return;
        }

        if (!_teamDirectorStates.TryGetValue(teamId, out var directorState))
        {
            directorState = new TeamDirectorState
            {
                NextEvaluateTime = now + TimeSpan.FromSeconds(_random.NextFloat(0f, _directorTickIntervalSeconds))
            };
            _teamDirectorStates[teamId] = directorState;
        }

        if (now >= directorState.NextEvaluateTime)
        {
            directorState.NextEvaluateTime = now + TimeSpan.FromSeconds(_directorTickIntervalSeconds);
            EvaluateTeamDirector(teamId, now, directorState);
        }

        var role = ResolveDirectorRole(htn);
        var assignedOrder = ResolveDirectorOrderForRole(directorState, role);
        if (assignedOrder == WaveDirectorOrder.None)
            assignedOrder = WaveDirectorOrder.Regroup;

        if (state.DirectorOrder != assignedOrder)
        {
            state.DirectorOrder = assignedOrder;
            _bench.RecordCount("npc.wave.director.order_assigned", 1);
            _bench.RecordCount($"npc.wave.director.order_assigned.{GetDirectorOrderToken(assignedOrder)}", 1);
        }

        htn.Blackboard.SetValue(NPCBlackboard.WaveDirectorOrder, GetDirectorOrderToken(assignedOrder));
        ApplyDirectorTarget(htn, directorState, assignedOrder, xform);
    }

    private void EvaluateTeamDirector(string teamId, TimeSpan now, TeamDirectorState state)
    {
        _bench.RecordCount("npc.wave.director.tick", 1);

        if (!TryBuildTeamDirectorSnapshot(teamId, out var snapshot))
        {
            state.ActiveOrder = WaveDirectorOrder.None;
            state.ActiveOrderScore = 0f;
            state.BaseThreatHoldUntil = TimeSpan.Zero;
            state.TeamMapId = MapId.Nullspace;
            state.TeamCenter = Vector2.Zero;
            state.TeamSpreadRadius = 0f;
            state.TeamMemberCount = 0;
            state.RallyLeader = EntityUid.Invalid;
            state.EnemyObjectiveTarget = EntityUid.Invalid;
            state.InfluenceTarget = EntityUid.Invalid;
            state.DefenseThreatTarget = EntityUid.Invalid;
            state.FriendlyObjectiveAnchor = EntityUid.Invalid;
            state.SupplyShortage = false;
            state.HasEnemyObjective = false;
            state.HasInfluenceOpportunity = false;
            state.BaseUnderThreat = false;
            return;
        }

        ApplyDirectorDefenseThreatMemory(state, ref snapshot, now);

        state.SupplyShortage = snapshot.SupplyShortage;
        state.HasEnemyObjective = snapshot.HasEnemyObjective;
        state.HasInfluenceOpportunity = snapshot.HasInfluenceOpportunity;
        state.BaseUnderThreat = snapshot.BaseUnderThreat;
        state.HasLogistics = snapshot.HasLogistics;
        state.HasBreacher = snapshot.HasBreacher;
        state.TeamMapId = snapshot.TeamMapId;
        state.TeamCenter = snapshot.Center;
        state.TeamSpreadRadius = snapshot.SpreadRadius;
        state.TeamMemberCount = snapshot.MemberCount;
        state.RallyLeader = snapshot.RallyLeader;
        state.EnemyObjectiveTarget = snapshot.EnemyObjectiveTarget;
        state.InfluenceTarget = snapshot.InfluenceTarget;
        state.DefenseThreatTarget = snapshot.DefenseThreatTarget;
        state.FriendlyObjectiveAnchor = snapshot.FriendlyObjectiveAnchor;

        var candidateOrder = ResolveTeamCandidateOrder(snapshot, out var candidateScore, out var urgent);
        if (candidateOrder == WaveDirectorOrder.None)
            candidateOrder = WaveDirectorOrder.Regroup;

        if (state.ActiveOrder == WaveDirectorOrder.None)
        {
            _bench.RecordCount("npc.wave.director.decision.bootstrap", 1);
            AssignTeamDirectorOrder(state, candidateOrder, candidateScore, now, preempted: false);
            return;
        }

        state.ActiveOrderScore = GetDirectorOrderScore(state.ActiveOrder, snapshot);
        if (candidateOrder == state.ActiveOrder)
        {
            state.ActiveOrderScore = candidateScore;
            if (state.OrderExpiresAt <= now)
                state.OrderExpiresAt = now + TimeSpan.FromSeconds(_directorOrderTtlSeconds);
            _bench.RecordCount("npc.wave.director.decision.keep_current", 1);
            return;
        }

        if (urgent)
        {
            if (now < state.LastPreemptAt + TimeSpan.FromSeconds(_directorUrgentPreemptCooldownSeconds))
            {
                _bench.RecordCount("npc.wave.director.decision.urgent_cooldown_hold", 1);
                return;
            }

            AssignTeamDirectorOrder(state, candidateOrder, candidateScore, now, preempted: true);
            return;
        }

        if (now < state.OrderExpiresAt)
        {
            _bench.RecordCount("npc.wave.director.decision.ttl_hold", 1);
            return;
        }

        if (now < state.LastOrderSwitchAt + TimeSpan.FromSeconds(_directorReassignCooldownSeconds))
        {
            _bench.RecordCount("npc.wave.director.decision.reassign_cooldown_hold", 1);
            return;
        }

        if (state.ActiveOrderScore + _directorHysteresisScoreDelta >= candidateScore &&
            GetDirectorPriorityRank(candidateOrder) <= GetDirectorPriorityRank(state.ActiveOrder))
        {
            _bench.RecordCount("npc.wave.director.decision.hysteresis_hold", 1);
            return;
        }

        AssignTeamDirectorOrder(state, candidateOrder, candidateScore, now, preempted: false);
    }

    private void ApplyDirectorDefenseThreatMemory(TeamDirectorState state, ref TeamDirectorSnapshot snapshot, TimeSpan now)
    {
        if (snapshot.BaseUnderThreat)
        {
            state.BaseThreatHoldUntil = now + TimeSpan.FromSeconds(_directorDefenseThreatMemorySeconds);
            return;
        }

        if (state.BaseThreatHoldUntil <= now ||
            snapshot.FriendlyObjectiveAnchor == EntityUid.Invalid)
        {
            return;
        }

        snapshot.BaseUnderThreat = true;
        if (snapshot.DefenseThreatTarget == EntityUid.Invalid &&
            state.DefenseThreatTarget != EntityUid.Invalid &&
            !TerminatingOrDeleted(state.DefenseThreatTarget))
        {
            snapshot.DefenseThreatTarget = state.DefenseThreatTarget;
        }

        _bench.RecordCount("npc.wave.director.decision.base_threat_memory_hold", 1);
    }

    private void AssignTeamDirectorOrder(
        TeamDirectorState state,
        WaveDirectorOrder order,
        float score,
        TimeSpan now,
        bool preempted)
    {
        var switched = state.ActiveOrder != WaveDirectorOrder.None &&
                       state.ActiveOrder != order;

        state.ActiveOrder = order;
        state.ActiveOrderScore = score;
        state.OrderExpiresAt = now + TimeSpan.FromSeconds(_directorOrderTtlSeconds);
        state.LastOrderSwitchAt = now;

        _bench.RecordCount("npc.wave.director.order_issued", 1);
        _bench.RecordCount($"npc.wave.director.order_issued.{GetDirectorOrderToken(order)}", 1);

        if (switched)
            _bench.RecordCount("npc.wave.director.order_switch", 1);

        if (!preempted)
            return;

        state.LastPreemptAt = now;
        _bench.RecordCount("npc.wave.director.order_preempt", 1);
    }

    private bool TryBuildTeamDirectorSnapshot(string teamId, out TeamDirectorSnapshot snapshot)
    {
        snapshot = default;
        _teamDirectorMembers.Clear();
        var totalPosition = Vector2.Zero;
        var teamMapId = MapId.Nullspace;
        var bestLeaderPriority = int.MinValue;

        var memberQuery = EntityQueryEnumerator<ActiveNPCComponent, HTNComponent, TransformComponent>();
        while (memberQuery.MoveNext(out var uid, out _, out var htn, out var xform))
        {
            if (!IsWaveRole(htn))
                continue;

            if (!TryResolveNpcTeamId(uid, out var memberTeamId) ||
                !string.Equals(memberTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var mapCoords = _transform.ToMapCoordinates(xform.Coordinates);
            if (mapCoords.MapId == MapId.Nullspace)
                continue;

            if (teamMapId == MapId.Nullspace)
                teamMapId = mapCoords.MapId;

            if (mapCoords.MapId != teamMapId)
                continue;

            totalPosition += mapCoords.Position;
            _teamDirectorMembers.Add((uid, mapCoords.Position));

            var role = ResolveDirectorRole(htn);
            var leadPriority = GetDirectorLeadPriority(role);
            if (leadPriority > bestLeaderPriority)
            {
                bestLeaderPriority = leadPriority;
                snapshot.RallyLeader = uid;
            }

            if (role == WaveDirectorRole.Logistics)
                snapshot.HasLogistics = true;
            else if (role == WaveDirectorRole.Breacher)
                snapshot.HasBreacher = true;
        }

        var memberCount = _teamDirectorMembers.Count;
        if (memberCount <= 0 || teamMapId == MapId.Nullspace)
            return false;

        snapshot.TeamMapId = teamMapId;
        snapshot.Center = totalPosition / memberCount;
        snapshot.MemberCount = memberCount;

        var outlierCount = memberCount switch
        {
            >= 8 => 2,
            >= 4 => 1,
            _ => 0,
        };

        if (outlierCount > 0)
        {
            _teamDirectorDistances.Clear();
            for (var i = 0; i < memberCount; i++)
            {
                var distance = (_teamDirectorMembers[i].Position - snapshot.Center).LengthSquared();
                _teamDirectorDistances.Add((i, distance));
            }

            _teamDirectorDistances.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
            var excluded = new bool[memberCount];
            for (var i = 0; i < outlierCount && i < _teamDirectorDistances.Count; i++)
            {
                excluded[_teamDirectorDistances[i].Index] = true;
            }

            var trimmedTotal = Vector2.Zero;
            var trimmedCount = 0;
            for (var i = 0; i < memberCount; i++)
            {
                if (excluded[i])
                    continue;

                trimmedTotal += _teamDirectorMembers[i].Position;
                trimmedCount++;
            }

            if (trimmedCount >= Math.Max(3, memberCount - outlierCount))
                snapshot.Center = trimmedTotal / trimmedCount;
        }

        foreach (var (_, memberPosition) in _teamDirectorMembers)
        {
            var spread = (memberPosition - snapshot.Center).Length();
            if (spread > snapshot.SpreadRadius)
                snapshot.SpreadRadius = spread;
        }

        var phase = _teamRule.GetCurrentPhase();
        var bestEnemyObjectiveDistance = float.MaxValue;
        var bestInfluencePriority = int.MinValue;
        var bestInfluenceDistance = float.MaxValue;
        var bestFriendlyObjectiveDistance = float.MaxValue;
        var shortageCount = 0;

        var objectiveQuery = EntityQueryEnumerator<WH40KObjectiveComponent, TransformComponent>();
        while (objectiveQuery.MoveNext(out var objectiveUid, out var objective, out var objectiveXform))
        {
            if (objective.Destroyed ||
                objective.Destroying ||
                string.IsNullOrWhiteSpace(objective.TeamId))
            {
                continue;
            }

            var objectiveMap = _transform.ToMapCoordinates(objectiveXform.Coordinates);
            if (objectiveMap.MapId != snapshot.TeamMapId)
                continue;

            var distanceToCenter = (objectiveMap.Position - snapshot.Center).Length();
            var isFriendlyObjective = string.Equals(objective.TeamId, teamId, StringComparison.OrdinalIgnoreCase);
            if (isFriendlyObjective)
            {
                if (distanceToCenter < bestFriendlyObjectiveDistance)
                {
                    snapshot.FriendlyObjectiveAnchor = objectiveUid;
                    bestFriendlyObjectiveDistance = distanceToCenter;
                }

                if (TryFindThreatNearObjective(teamId, objectiveUid, _directorDefenseThreatRadius, out var threatTarget))
                {
                    snapshot.BaseUnderThreat = true;
                    if (snapshot.DefenseThreatTarget == EntityUid.Invalid)
                        snapshot.DefenseThreatTarget = threatTarget;
                }

                continue;
            }

            if (distanceToCenter >= bestEnemyObjectiveDistance)
                continue;

            snapshot.HasEnemyObjective = true;
            snapshot.EnemyObjectiveTarget = objectiveUid;
            bestEnemyObjectiveDistance = distanceToCenter;
        }

        var influenceQuery = EntityQueryEnumerator<WH40KInfluencePointComponent, TransformComponent>();
        while (influenceQuery.MoveNext(out var pointUid, out var point, out var pointXform))
        {
            if (phase < point.CaptureEnabledFromPhase)
                continue;

            var pointMap = _transform.ToMapCoordinates(pointXform.Coordinates);
            if (pointMap.MapId != snapshot.TeamMapId)
                continue;

            var priority = GetDirectorInfluencePriority(point, teamId);
            if (priority <= 0)
                continue;

            var distance = (pointMap.Position - snapshot.Center).Length();
            if (priority < bestInfluencePriority ||
                (priority == bestInfluencePriority && distance >= bestInfluenceDistance))
            {
                continue;
            }

            snapshot.HasInfluenceOpportunity = true;
            snapshot.InfluenceTarget = pointUid;
            bestInfluencePriority = priority;
            bestInfluenceDistance = distance;
        }

        var vendingQuery = EntityQueryEnumerator<VendingMachineComponent, TransformComponent>();
        while (vendingQuery.MoveNext(out _, out var machine, out var machineXform))
        {
            if (machine.Broken || !MachineNeedsRestock(machine))
                continue;

            var machineMap = _transform.ToMapCoordinates(machineXform.Coordinates);
            if (machineMap.MapId != snapshot.TeamMapId)
                continue;

            shortageCount++;
            if (shortageCount >= _directorResupplyShortageThreshold)
            {
                snapshot.SupplyShortage = true;
                break;
            }
        }

        return true;
    }

    private bool TryFindThreatNearObjective(string teamId, EntityUid objective, float radius, out EntityUid threatTarget)
    {
        threatTarget = EntityUid.Invalid;
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            objective,
            radius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        var hasTeamFaction = TryResolveTeamFactionId(teamId, out var teamFactionId);
        var fallbackThreat = EntityUid.Invalid;

        foreach (var candidate in _lookupBuffer)
        {
            if (candidate == objective ||
                TerminatingOrDeleted(candidate))
                continue;

            if (TryComp(candidate, out MobStateComponent? mobState) &&
                mobState.CurrentState == MobState.Dead)
                continue;

            var hostileByTeam = false;
            if (TryResolveNpcTeamId(candidate, out var candidateTeamId))
            {
                hostileByTeam = !string.Equals(candidateTeamId, teamId, StringComparison.OrdinalIgnoreCase);
            }
            else if (hasTeamFaction &&
                     TryComp(candidate, out NpcFactionMemberComponent? candidateFaction))
            {
                hostileByTeam =
                    _npcFaction.IsFactionHostile(teamFactionId, (candidate, candidateFaction)) ||
                    !_npcFaction.IsFactionFriendly(teamFactionId, (candidate, candidateFaction));
            }

            if (!hostileByTeam)
                continue;

            // Prefer active combat agents as immediate defense trigger.
            if (HasComp<ActiveNPCComponent>(candidate) ||
                HasComp<CombatModeComponent>(candidate))
            {
                threatTarget = candidate;
                return true;
            }

            // Keep broader hostile fallback to avoid missing urgent threat
            // when attacker is not currently in explicit combat mode.
            if (fallbackThreat == EntityUid.Invalid)
                fallbackThreat = candidate;
        }

        if (fallbackThreat == EntityUid.Invalid)
            return false;

        threatTarget = fallbackThreat;
        return true;
    }

    private static bool TryResolveTeamFactionId(string teamId, out string factionId)
    {
        factionId = string.Empty;
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        switch (teamId.Trim())
        {
            case "Imperium":
                factionId = "Imperium";
                return true;
            case "Heretics":
                factionId = "Heretics";
                return true;
            default:
                return false;
        }
    }

    private static int GetDirectorInfluencePriority(WH40KInfluencePointComponent point, string teamId)
    {
        if (string.Equals(point.OwnerTeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(point.CapturingTeamId) &&
                !string.Equals(point.CapturingTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                return 5;
            }

            return 0;
        }

        if (string.Equals(point.CapturingTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            return 4;

        if (string.IsNullOrWhiteSpace(point.OwnerTeamId))
            return 3;

        return 4;
    }

    private WaveDirectorOrder ResolveTeamCandidateOrder(
        TeamDirectorSnapshot snapshot,
        out float score,
        out bool urgent)
    {
        urgent = false;

        if (snapshot.BaseUnderThreat)
        {
            urgent = true;
            score = 100f;
            _bench.RecordCount("npc.wave.director.decision.base_under_threat", 1);
            return WaveDirectorOrder.DefendBase;
        }

        if (snapshot.SupplyShortage && snapshot.HasLogistics)
        {
            score = 78f;
            _bench.RecordCount("npc.wave.director.decision.supply_shortage", 1);
            return WaveDirectorOrder.Resupply;
        }

        if (snapshot.HasEnemyObjective)
        {
            score = 64f;
            _bench.RecordCount("npc.wave.director.decision.enemy_objective", 1);
            return WaveDirectorOrder.PushObjective;
        }

        if (snapshot.HasInfluenceOpportunity)
        {
            score = 56f;
            _bench.RecordCount("npc.wave.director.decision.influence_opportunity", 1);
            return WaveDirectorOrder.CaptureInfluence;
        }

        score = 20f;
        _bench.RecordCount("npc.wave.director.decision.regroup", 1);
        return WaveDirectorOrder.Regroup;
    }

    private static float GetDirectorOrderScore(WaveDirectorOrder order, TeamDirectorSnapshot snapshot)
    {
        return order switch
        {
            WaveDirectorOrder.DefendBase => snapshot.BaseUnderThreat ? 100f : 15f,
            WaveDirectorOrder.Resupply => snapshot.SupplyShortage && snapshot.HasLogistics ? 78f : 24f,
            WaveDirectorOrder.PushObjective => snapshot.HasEnemyObjective ? 64f : 12f,
            WaveDirectorOrder.BreachLane => snapshot.HasEnemyObjective ? 62f : 12f,
            WaveDirectorOrder.CaptureInfluence => snapshot.HasInfluenceOpportunity ? 56f : 18f,
            WaveDirectorOrder.Regroup => 20f,
            _ => 0f,
        };
    }

    private static int GetDirectorPriorityRank(WaveDirectorOrder order)
    {
        return order switch
        {
            WaveDirectorOrder.DefendBase => 5,
            WaveDirectorOrder.Resupply => 4,
            WaveDirectorOrder.PushObjective => 3,
            WaveDirectorOrder.BreachLane => 3,
            WaveDirectorOrder.CaptureInfluence => 2,
            WaveDirectorOrder.Regroup => 1,
            _ => 0,
        };
    }

    private static int GetDirectorLeadPriority(WaveDirectorRole role)
    {
        return role switch
        {
            // Tactical coordinators issue intent, but should not be the physical march lead.
            // Objective pushes stay much more coherent when the lead anchor is a frontline role.
            WaveDirectorRole.Assault => 6,
            WaveDirectorRole.Breacher => 5,
            WaveDirectorRole.Coordinator => 4,
            WaveDirectorRole.Support => 3,
            WaveDirectorRole.Sapper => 2,
            WaveDirectorRole.Logistics => 1,
            _ => 0,
        };
    }

    private WaveDirectorOrder ResolveDirectorOrderForRole(TeamDirectorState state, WaveDirectorRole role)
    {
        if (role == WaveDirectorRole.Logistics)
            return state.SupplyShortage ? WaveDirectorOrder.Resupply : WaveDirectorOrder.Regroup;

        var order = state.ActiveOrder;
        if (order == WaveDirectorOrder.Resupply)
        {
            if (state.BaseUnderThreat)
                order = WaveDirectorOrder.DefendBase;
            else if (state.HasEnemyObjective)
                order = WaveDirectorOrder.PushObjective;
            else if (state.HasInfluenceOpportunity)
                order = WaveDirectorOrder.CaptureInfluence;
            else
                order = WaveDirectorOrder.Regroup;
        }

        if (role == WaveDirectorRole.Breacher &&
            order == WaveDirectorOrder.PushObjective)
        {
            return WaveDirectorOrder.BreachLane;
        }

        return order;
    }

    private void ApplyDirectorTarget(HTNComponent htn, TeamDirectorState state, WaveDirectorOrder order, TransformComponent xform)
    {
        EntityUid target = EntityUid.Invalid;

        if (order == WaveDirectorOrder.PushObjective ||
            order == WaveDirectorOrder.BreachLane)
        {
            target = state.EnemyObjectiveTarget;
        }
        else if (order == WaveDirectorOrder.DefendBase)
        {
            target = state.DefenseThreatTarget;
        }

        if (target != EntityUid.Invalid &&
            !TerminatingOrDeleted(target))
        {
            if (TryComp(target, out TransformComponent? targetXform))
            {
                var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
                var targetMap = _transform.ToMapCoordinates(targetXform.Coordinates);
                if (npcMap.MapId == targetMap.MapId)
                    htn.Blackboard.SetValue(NPCBlackboard.CurrentOrderedTarget, target);
            }
        }
        else
        {
            htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
        }
    }

    private void ClearDirectorOrder(HTNComponent htn, WaveRuntimeState state)
    {
        state.DirectorOrder = WaveDirectorOrder.None;
        htn.Blackboard.Remove<string>(NPCBlackboard.WaveDirectorOrder);
        htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
    }

    private bool TeamHasWaveMembers(string teamId)
    {
        var query = EntityQueryEnumerator<ActiveNPCComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out _, out var htn))
        {
            if (!IsWaveRole(htn))
                continue;

            if (!TryResolveNpcTeamId(uid, out var memberTeamId))
                continue;

            if (string.Equals(memberTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetDirectorOrderToken(WaveDirectorOrder order)
    {
        return order switch
        {
            WaveDirectorOrder.DefendBase => "defend_base",
            WaveDirectorOrder.PushObjective => "push_objective",
            WaveDirectorOrder.CaptureInfluence => "capture_influence",
            WaveDirectorOrder.Resupply => "resupply",
            WaveDirectorOrder.BreachLane => "breach_lane",
            WaveDirectorOrder.Regroup => "regroup",
            _ => "none",
        };
    }

    private static WaveDirectorRole ResolveDirectorRole(HTNComponent htn)
    {
        return htn.RootTask.Task switch
        {
            "WH40KWaveAssaultRoot" => WaveDirectorRole.Assault,
            "WH40KWaveBreacherRoot" => WaveDirectorRole.Breacher,
            "WH40KWaveSapperRoot" => WaveDirectorRole.Sapper,
            "WH40KWaveSupportRoot" => WaveDirectorRole.Support,
            "WH40KWaveLogisticsRoot" => WaveDirectorRole.Logistics,
            "WH40KWaveCoordinatorRoot" => WaveDirectorRole.Coordinator,
            _ => WaveDirectorRole.Unknown,
        };
    }

    private void RunInfluenceLayer(EntityUid uid, HTNComponent htn, TransformComponent xform, WaveRuntimeState state, TimeSpan now)
    {
        if (now < state.NextInfluenceScanTime)
            return;

        state.NextInfluenceScanTime = now + TimeSpan.FromSeconds(_influenceScanIntervalSeconds);

        if (state.DirectorOrder != WaveDirectorOrder.None &&
            state.DirectorOrder != WaveDirectorOrder.CaptureInfluence &&
            state.DirectorOrder != WaveDirectorOrder.Regroup)
        {
            state.InfluenceTargetPoint = EntityUid.Invalid;
            _bench.RecordCount("npc.wave.influence.director_skip", 1);
            return;
        }

        if (!htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveInfluenceEnabled, out var influenceEnabled, EntityManager) ||
            !influenceEnabled)
        {
            state.InfluenceTargetPoint = EntityUid.Invalid;
            _bench.RecordCount("npc.wave.influence.role_disabled_skip", 1);
            return;
        }

        if (HasCombatTarget(uid, htn))
        {
            state.InfluenceTargetPoint = EntityUid.Invalid;
            _bench.RecordCount("npc.wave.influence.combat_preempt", 1);
            return;
        }

        if (!TryResolveNpcTeamId(uid, out var teamId))
        {
            state.InfluenceTargetPoint = EntityUid.Invalid;
            _bench.RecordCount("npc.wave.influence.no_team", 1);
            return;
        }

        var phase = _teamRule.GetCurrentPhase();
        if (!TryFindBestInfluencePoint(uid, xform, teamId, phase, _influenceSearchRadius, out var point, out var pointXform, out var holdRadius))
        {
            state.InfluenceTargetPoint = EntityUid.Invalid;
            _bench.RecordCount("npc.wave.influence.search_miss", 1);
            return;
        }

        _bench.RecordCount("npc.wave.influence.search_hit", 1);

        if (state.InfluenceTargetPoint != point)
        {
            state.InfluenceTargetPoint = point;
            _bench.RecordCount("npc.wave.influence.point_acquired", 1);
        }

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        var pointMap = _transform.ToMapCoordinates(pointXform.Coordinates);
        var distance = (pointMap.Position - npcMap.Position).Length();

        _steering.TryRegister(uid, pointXform.Coordinates);
        if (distance <= holdRadius)
            _bench.RecordCount("npc.wave.influence.hold_point", 1);
        else
            _bench.RecordCount("npc.wave.influence.seek_point", 1);
    }

    private void RunObjectiveLayer(EntityUid uid, HTNComponent htn, TransformComponent xform, WaveRuntimeState state, TimeSpan now)
    {
        if (now < state.NextObjectiveScanTime)
            return;

        state.NextObjectiveScanTime = now + TimeSpan.FromSeconds(_objectiveScanIntervalSeconds);

        if (state.DirectorOrder != WaveDirectorOrder.None &&
            state.DirectorOrder != WaveDirectorOrder.PushObjective &&
            state.DirectorOrder != WaveDirectorOrder.BreachLane)
        {
            state.ObjectiveTarget = EntityUid.Invalid;
            state.ObjectiveNoPathActive = false;
            state.ObjectiveNoPathStreak = 0;
            _bench.RecordCount("npc.wave.objective.director_skip", 1);
            return;
        }

        if (!htn.Blackboard.TryGetValue<bool>(NPCBlackboard.WaveObjectiveEnabled, out var objectiveEnabled, EntityManager) ||
            !objectiveEnabled)
        {
            state.ObjectiveTarget = EntityUid.Invalid;
            state.ObjectiveNoPathActive = false;
            state.ObjectiveNoPathStreak = 0;
            _bench.RecordCount("npc.wave.objective.role_disabled_skip", 1);
            return;
        }

        if (!TryResolveNpcTeamId(uid, out var teamId))
        {
            state.ObjectiveTarget = EntityUid.Invalid;
            state.ObjectiveNoPathActive = false;
            state.ObjectiveNoPathStreak = 0;
            _bench.RecordCount("npc.wave.objective.no_team", 1);
            return;
        }

        var objectiveBlockerTarget = EntityUid.Invalid;
        var hasObjectiveBlockerTarget =
            htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentObjectiveBlockerTarget, out var blockerTarget, EntityManager) &&
            blockerTarget != EntityUid.Invalid;
        if (hasObjectiveBlockerTarget)
            objectiveBlockerTarget = blockerTarget;

        if (TryGetCombatTarget(uid, htn, out var combatTarget) &&
            (state.ObjectiveTarget == EntityUid.Invalid ||
             (combatTarget != state.ObjectiveTarget &&
              (!hasObjectiveBlockerTarget || combatTarget != objectiveBlockerTarget))))
        {
            if (!ShouldObjectiveCombatPreempt(uid, combatTarget))
            {
                ClearIncidentalObjectiveCombatTarget(uid, htn, combatTarget);
            }
            else
            {
            _bench.RecordCount("npc.wave.objective.combat_preempt", 1);
            return;
            }
        }

        if (state.HazardAvoidUntil > now &&
            state.HazardAvoidCoordinates.IsValid(EntityManager))
        {
            if (state.HazardFocus != EntityUid.Invalid &&
                TryComp(state.HazardFocus, out TransformComponent? activeHazardXform))
            {
                var currentNpcMap = _transform.ToMapCoordinates(xform.Coordinates);
                var hazardMap = _transform.ToMapCoordinates(activeHazardXform.Coordinates);
                if (currentNpcMap.MapId == hazardMap.MapId)
                {
                    var hazardDistance = (hazardMap.Position - currentNpcMap.Position).Length();
                    var releaseHazard = hazardDistance > MathF.Max(2.6f, GetHazardScanRadius(state));
                    if (!releaseHazard &&
                        TryGetHazardTravelDirection(uid, htn, xform, out var travelDirection))
                    {
                        var toHazard = hazardMap.Position - currentNpcMap.Position;
                        if (toHazard.LengthSquared() > 0.01f)
                        {
                            var hazardAhead = Vector2.Dot(Vector2.Normalize(toHazard), travelDirection) >= 0.05f;
                            releaseHazard = !hazardAhead && hazardDistance > 1.45f;
                        }
                    }

                    if (releaseHazard)
                    {
                        state.HazardAvoidUntil = TimeSpan.Zero;
                        state.HazardAvoidCoordinates = EntityCoordinates.Invalid;
                        state.HazardFocus = EntityUid.Invalid;
                        _bench.RecordCount("npc.wave.hazard.environment_release", 1);
                    }
                }
            }
        }

        if (state.HazardAvoidUntil > now &&
            state.HazardAvoidCoordinates.IsValid(EntityManager))
        {
            var detourCoordinates = ResolveWaveSteeringCoordinates(uid, state.HazardAvoidCoordinates, avoidHazards: true);
            if (!detourCoordinates.Equals(state.HazardAvoidCoordinates))
                state.HazardAvoidCoordinates = detourCoordinates;

            _steering.TryRegister(uid, detourCoordinates);
            _bench.RecordCount("npc.wave.objective.hazard_detour_hold", 1);
            return;
        }

        var objective = EntityUid.Invalid;
        WH40KObjectiveComponent objectiveComp = default!;
        TransformComponent objectiveXform = default!;
        var holdRadius = 0f;

        var hasObjectiveTarget =
            (state.DirectorOrder == WaveDirectorOrder.PushObjective || state.DirectorOrder == WaveDirectorOrder.BreachLane) &&
            TryFindDirectorObjectiveTarget(htn, xform, teamId, out objective, out objectiveComp, out objectiveXform, out holdRadius);

        if (!hasObjectiveTarget)
            hasObjectiveTarget = TryFindBestEnemyObjectiveTarget(uid, xform, teamId, _objectiveSearchRadius, out objective, out objectiveComp, out objectiveXform, out holdRadius);

        if (!hasObjectiveTarget)
        {
            state.ObjectiveTarget = EntityUid.Invalid;
            state.ObjectiveNoPathActive = false;
            state.ObjectiveNoPathStreak = 0;
            htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
            htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentObjectiveBlockerTarget);
            _bench.RecordCount("npc.wave.objective.search_miss", 1);
            return;
        }

        _bench.RecordCount("npc.wave.objective.search_hit", 1);

        if (state.ObjectiveTarget != objective)
        {
            state.ObjectiveTarget = objective;
            state.ObjectiveNoPathActive = false;
            state.ObjectiveNoPathStreak = 0;
            htn.Blackboard.SetValue(NPCBlackboard.CurrentOrderedTarget, objective);
            _bench.RecordCount("npc.wave.objective_target_selected", 1);
        }

        SanitizeObjectiveCombatTargets(uid, objective, objectiveBlockerTarget);

        // Loadout/service utility logic can leave item targets in the combat keys.
        // Clear these so objective assault can promote the real structure target.
        if (htn.Blackboard.TryGetValue<EntityUid>("Target", out var currentTarget, EntityManager) &&
            currentTarget != EntityUid.Invalid &&
            currentTarget != objective &&
            !IsCombatTargetCandidate(uid, currentTarget))
        {
            htn.Blackboard.Remove<EntityUid>("Target");
            htn.Blackboard.Remove<EntityCoordinates>("TargetCoordinates");
            _bench.RecordCount("npc.wave.objective.clear_noncombat_target", 1);
        }

        // Only dedicated breachers should convert objective pressure into destructive routing.
        // Giving every ranged role smash/pry causes false wall-punching when a longer valid route exists.
        if ((state.DirectorOrder == WaveDirectorOrder.PushObjective ||
             state.DirectorOrder == WaveDirectorOrder.BreachLane) &&
            ResolveDirectorRole(htn) == WaveDirectorRole.Breacher)
        {
            var navSmashEnabled =
                htn.Blackboard.TryGetValue<bool>(NPCBlackboard.NavSmash, out var navSmashValue, EntityManager) &&
                navSmashValue;
            var navPryEnabled =
                htn.Blackboard.TryGetValue<bool>(NPCBlackboard.NavPry, out var navPryValue, EntityManager) &&
                navPryValue;

            if (!navSmashEnabled)
            {
                htn.Blackboard.SetValue(NPCBlackboard.NavSmash, true);
                _bench.RecordCount("npc.wave.objective.breach_assist_enabled", 1);
            }

            if (!navPryEnabled)
            {
                htn.Blackboard.SetValue(NPCBlackboard.NavPry, true);
                _bench.RecordCount("npc.wave.objective.pry_assist_enabled", 1);
            }
        }

        if (objectiveComp.Destroyed)
        {
            state.ObjectiveTarget = EntityUid.Invalid;
            state.ObjectiveNoPathActive = false;
            state.ObjectiveNoPathStreak = 0;
            htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
            htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentObjectiveBlockerTarget);
            if (htn.Blackboard.TryGetValue<EntityUid>("Target", out var destroyedTarget, EntityManager) &&
                destroyedTarget == objective)
            {
                htn.Blackboard.Remove<EntityUid>("Target");
                htn.Blackboard.Remove<EntityCoordinates>("TargetCoordinates");
            }
            _bench.RecordCount("npc.wave.objective_attack_success", 1);
            return;
        }

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        var objectiveMap = _transform.ToMapCoordinates(objectiveXform.Coordinates);
        var distance = (objectiveMap.Position - npcMap.Position).Length();
        HandsComponent? objectiveHands = null;
        var hasObjectiveGun = false;
        var hasObjectiveCombatItem = false;
        if (TryComp(uid, out objectiveHands))
        {
            hasObjectiveCombatItem = TryEnsureActiveHeldCombatItem(uid, objectiveHands);
            hasObjectiveGun = _gun.TryGetGun(uid, out _);

            if (!hasObjectiveCombatItem &&
                TryReacquireNearbyCombatItem(uid, xform, objectiveHands))
            {
                hasObjectiveCombatItem = TryEnsureActiveHeldCombatItem(uid, objectiveHands);
                hasObjectiveGun = _gun.TryGetGun(uid, out _);
            }
        }

        // Keep objective attack radius tied to combat capability, not objective scan range.
        // If this radius is too wide, squads stall outside effective gun distance and never
        // convert objective pressure into real damage.
        var objectiveEngageRadius = holdRadius;
        if (htn.Blackboard.TryGetValue<float>("RangedRange", out var rangedRange, EntityManager) &&
            rangedRange > 0.1f)
        {
            objectiveEngageRadius = MathF.Max(objectiveEngageRadius, Math.Clamp(rangedRange * 0.9f, 3f, 28f));
        }
        else if (htn.Blackboard.TryGetValue<float>("MeleeRange", out var meleeRange, EntityManager) &&
                 meleeRange > 0.1f)
        {
            objectiveEngageRadius = MathF.Max(objectiveEngageRadius, Math.Clamp(meleeRange + 0.75f, 1.5f, 4f));
        }
        else
        {
            objectiveEngageRadius = MathF.Max(objectiveEngageRadius, 12f);
        }

        if (state.ObjectiveNoPathActive &&
            state.ObjectiveNoPathStreak >= Math.Max(2, _objectiveNoPathFallbackRetries))
        {
            objectiveEngageRadius = MathF.Min(MathF.Max(objectiveEngageRadius + 2f, holdRadius), 32f);
            _bench.RecordCount("npc.wave.objective_attack_standoff_pathblocked", 1);
        }

        // Keep objective assault activation broad enough for real map geometry where
        // objective center tiles are often blocked by fixtures/walls.
        objectiveEngageRadius = MathF.Max(objectiveEngageRadius, hasObjectiveGun ? 26f : 20f);

        UpdateObjectiveMotionStallState(uid, xform, state, distance, objectiveEngageRadius);
        var routePressure = GetObjectiveRoutePressure(uid, teamId, state);
        if (state.ObjectiveMotionStallSamples == 3 && routePressure > 0)
            _bench.RecordCount("npc.wave.pathblocked.stall_lane_shift", 1);

        var objectiveAttackRadius = objectiveEngageRadius;
        if (state.ObjectiveMotionStallSamples >= 4)
        {
            objectiveAttackRadius = MathF.Min(32f, MathF.Max(objectiveAttackRadius, objectiveEngageRadius + 6f));
            _bench.RecordCount("npc.wave.objective.attack_stall_extend", 1);
        }

        var objectiveSightCollision =
            CollisionGroup.Impassable |
            CollisionGroup.InteractImpassable |
            CollisionGroup.BulletImpassable;
        var hasDirectObjectiveSight =
            distance <= MathF.Min(56f, objectiveAttackRadius + 10f) &&
            _interaction.InRangeUnobstructed(
                uid,
                objective,
                distance + 0.35f,
                objectiveSightCollision);

        var approachRadius = Math.Clamp(
            MathF.Max(holdRadius + 0.7f, MathF.Min(objectiveEngageRadius - 0.25f, 3.2f)),
            0.9f,
            MathF.Max(1.2f, objectiveEngageRadius));
        var objectiveApproachTarget = GetObjectiveApproachCoordinates(
            uid,
            htn,
            xform,
            teamId,
            objective,
            objectiveXform,
            approachRadius,
            routePressure,
            state.ObjectiveNoPathStreak,
            standoff: false);

        EntityCoordinates objectiveSteeringTarget;
        TransformComponent chunkPointXform = default!;
        var farRoute = distance > _objectivePathChunkDistance;
        var hasStagingPoint =
            farRoute &&
            TryFindObjectiveStagingPoint(uid, xform, teamId, objectiveMap.Position, distance, out _, out chunkPointXform);
        var chunkDestination = objectiveMap.Position;
        if (hasStagingPoint)
        {
            var stagingMap = _transform.ToMapCoordinates(chunkPointXform.Coordinates);
            if (stagingMap.MapId == npcMap.MapId)
                chunkDestination = stagingMap.Position;
            else
                hasStagingPoint = false;
        }

        EntityCoordinates chunkCoordinates = default;
        var forceLeaderFollow =
            (state.DirectorOrder == WaveDirectorOrder.PushObjective || state.DirectorOrder == WaveDirectorOrder.BreachLane) &&
            ShouldForceObjectiveLeaderFollow(uid, teamId, npcMap.MapId, distance, objectiveEngageRadius);
        EntityCoordinates leaderFollowCoordinates = default;
        var hasLeaderFollowCoordinates =
            distance > objectiveEngageRadius + 4f &&
            TryGetObjectiveLeaderFollowCoordinates(
                uid,
                htn,
                xform,
                teamId,
                objectiveMap.Position,
                out leaderFollowCoordinates);
        var useChunkStep =
            farRoute &&
            !(forceLeaderFollow && hasLeaderFollowCoordinates) &&
            TryGetObjectiveChunkCoordinates(
                uid,
                htn,
                xform,
                teamId,
                chunkDestination,
                routePressure,
                state.ObjectiveNoPathStreak,
                out chunkCoordinates);
        var useLeaderFollow =
            hasLeaderFollowCoordinates &&
            (!useChunkStep || forceLeaderFollow);
        EntityCoordinates leaderMarchCoordinates = default;
        var useLeaderMarch =
            !useLeaderFollow &&
            distance > 96f &&
            (state.DirectorOrder == WaveDirectorOrder.PushObjective || state.DirectorOrder == WaveDirectorOrder.BreachLane) &&
            TryGetObjectiveLeaderMarchCoordinates(
                uid,
                htn,
                xform,
                teamId,
                objectiveMap.Position,
                out leaderMarchCoordinates);
        EntityUid ingressDoor = EntityUid.Invalid;
        EntityCoordinates ingressCoordinates = default;
        var hasIngressDoor =
            !useChunkStep &&
            distance <= MathF.Max(20f, MathF.Min(_objectivePathChunkDistance + 8f, 54f)) &&
            !hasDirectObjectiveSight &&
            TryGetObjectiveIngressDoor(
                uid,
                teamId,
                xform,
                objective,
                objectiveXform,
                out ingressDoor,
                out ingressCoordinates);
        var useIngressApproach = hasIngressDoor;

        if (useChunkStep)
        {
            objectiveSteeringTarget = chunkCoordinates;
            if (hasStagingPoint &&
                state.ObjectiveNoPathActive &&
                state.ObjectiveNoPathStreak >= Math.Max(2, _objectiveNoPathFallbackRetries))
            {
                _bench.RecordCount("npc.wave.objective.staging_seek", 1);
            }
            else
            {
                _bench.RecordCount("npc.wave.objective.chunk_step_seek", 1);
            }
        }
        else if (useLeaderFollow)
        {
            objectiveSteeringTarget = leaderFollowCoordinates;
            _bench.RecordCount("npc.wave.objective.follow_leader_seek", 1);
        }
        else if (useLeaderMarch)
        {
            objectiveSteeringTarget = leaderMarchCoordinates;
            _bench.RecordCount("npc.wave.objective.march_slot_seek", 1);
        }
        else if (hasStagingPoint)
        {
            objectiveSteeringTarget = chunkPointXform.Coordinates;
            _bench.RecordCount("npc.wave.objective.staging_seek", 1);
        }
        else if (useIngressApproach)
        {
            objectiveSteeringTarget = ingressCoordinates;
            _bench.RecordCount("npc.wave.objective.ingress_seek", 1);
        }
        else if (distance <= objectiveEngageRadius && distance > holdRadius)
        {
            var standoffRadius = Math.Clamp(objectiveEngageRadius - 0.75f, 3f, 14f);
            objectiveSteeringTarget = GetObjectiveApproachCoordinates(
                uid,
                htn,
                xform,
                teamId,
                objective,
                objectiveXform,
                standoffRadius,
                routePressure,
                state.ObjectiveNoPathStreak,
                standoff: true);
            _bench.RecordCount("npc.wave.objective.standoff_slot_seek", 1);
            _bench.RecordCount("npc.wave.objective.standoff_lane_seek", 1);
        }
        else
        {
            // Objectives are often physically blocked structures; seek an approach ring,
            // not the impassable center tile.
            objectiveSteeringTarget = objectiveApproachTarget;
            _bench.RecordCount("npc.wave.objective.approach_seek", 1);
            _bench.RecordCount("npc.wave.objective.assault_lane_seek", 1);
        }

        if (TryGetObjectiveRegroupCoordinates(
                uid,
                htn,
                xform,
                teamId,
                state.DirectorOrder,
                objectiveMap.Position,
                objectiveEngageRadius,
                out var regroupCoordinates))
        {
            objectiveSteeringTarget = regroupCoordinates;
            _bench.RecordCount("npc.wave.objective.regroup_seek", 1);
        }

        var objectiveSteeringRange = MathF.Max(0.8f, Math.Clamp(holdRadius + 0.35f, 0.8f, 2.4f));
        var forceObjectiveRetarget =
            state.ObjectiveMotionStallSamples >= 4 ||
            ShouldForceObjectiveRetarget(uid, objectiveSteeringTarget, distance, objectiveEngageRadius);
        RefreshObjectiveSteeringTarget(
            uid,
            objectiveSteeringTarget,
            objectiveSteeringRange,
            forceReset: forceObjectiveRetarget);
        state.ObjectivePathRequestsThisTick++;

        var queueDepth = _pathfinding.GetQueueDepth();
        if (queueDepth > state.ObjectivePathQueueDepthPeak)
        {
            state.ObjectivePathQueueDepthPeak = queueDepth;
            _bench.RecordCount("npc.wave.path.queue_depth_peak", queueDepth);
        }

        if (distance <= objectiveAttackRadius)
        {
            htn.Blackboard.SetValue(NPCBlackboard.CurrentOrderedTarget, objective);
            // Drive HTN movement toward reachable approach/standoff coordinates, not the
            // often-impassable objective center tile.
            htn.Blackboard.SetValue("TargetCoordinates", objectiveSteeringTarget);

            var attackTarget = objective;
            var usingBlockerTarget = false;
            if (!hasDirectObjectiveSight)
            {
                if (HasBlockingObjectiveIngressDoor(ingressDoor))
                {
                    attackTarget = ingressDoor;
                    usingBlockerTarget = true;
                    _bench.RecordCount("npc.wave.objective.blocker_ingress_door", 1);
                }
                else
                {
                    usingBlockerTarget = TryGetObjectiveBlockerTarget(
                        uid,
                        xform,
                        objective,
                        objectiveSteeringTarget,
                        ResolveDirectorRole(htn),
                        ingressDoor,
                        out attackTarget);
                }
            }

            if (usingBlockerTarget)
                htn.Blackboard.SetValue(NPCBlackboard.CurrentObjectiveBlockerTarget, attackTarget);
            else
                htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentObjectiveBlockerTarget);

            if (!htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager) ||
                target != attackTarget)
            {
                htn.Blackboard.SetValue("Target", attackTarget);
                if (usingBlockerTarget)
                {
                    _bench.RecordCount("npc.wave.objective.blocker_target", 1);
                }
                else
                {
                    _bench.RecordCount("npc.wave.objective_attack_started", 1);

                    if (distance > holdRadius)
                        _bench.RecordCount("npc.wave.objective_attack_started_standoff", 1);
                }
            }

            // Objective structures are not always selected by generic NearbyGunTargets utility.
            // Force ranged-combat component targeting so objective-capable armed NPCs can shoot.
            if (objectiveHands != null &&
                hasObjectiveGun)
            {
                var ranged = EnsureComp<NPCRangedCombatComponent>(uid);
                ranged.Target = attackTarget;
                ranged.Status = CombatStatus.Normal;
                _bench.RecordCount(
                    usingBlockerTarget
                        ? "npc.wave.objective.blocker_ranged_forced"
                        : "npc.wave.objective.ranged_forced",
                    1);
            }
            else if (objectiveHands != null &&
                     hasObjectiveCombatItem &&
                     TryComp(uid, out NPCMeleeCombatComponent? melee))
            {
                melee.Target = attackTarget;
                melee.Status = CombatStatus.Normal;
                _bench.RecordCount(
                    usingBlockerTarget
                        ? "npc.wave.objective.blocker_melee_forced"
                        : "npc.wave.objective.melee_forced",
                    1);
            }
        }

        var blockedByPathing = false;
        if (TryComp(uid, out NPCSteeringComponent? steering))
        {
            blockedByPathing =
                steering.Status == SteeringStatus.NoPath ||
                steering.FailedPathCount > 0 ||
                steering.PathRequestBackoffSeconds > 0f;
        }

        if (!blockedByPathing)
        {
            if (state.ObjectiveNoPathActive && state.ObjectiveNoPathStreak > 0)
                _bench.RecordCount("npc.wave.pathblocked.replan_success", 1);

            state.ObjectiveNoPathActive = false;
            state.ObjectiveNoPathStreak = 0;
            return;
        }

        state.ObjectiveNoPathActive = true;
        state.ObjectiveNoPathStreak++;
        _bench.RecordCount("npc.wave.pathblocked.retry_bounded", 1);

        _waveComms.TryTacticalOrder(uid, objective);

        var canSmash =
            htn.Blackboard.TryGetValue<bool>(NPCBlackboard.NavSmash, out var navSmash, EntityManager) &&
            navSmash;
        var fallbackBase = _objectiveNoPathFallbackRetries;
        var unreachableBase = Math.Max(_objectiveNoPathUnreachableRetries, fallbackBase + 1);
        var fallbackLimit = canSmash
            ? fallbackBase + 1
            : fallbackBase;
        var unreachableLimit = canSmash
            ? unreachableBase + 2
            : unreachableBase;
        // Long cross-map pushes need more bounded retries before declaring unreachable.
        // Use objective tuning knobs for scaling so recovery remains configurable.
        var longRouteScale = MathF.Max(
            12f,
            MathF.Min(_objectivePathChunkDistance + _objectiveStagingMinGain, _objectiveStagingSearchRadius * 0.75f));
        var longRouteExtra = Math.Clamp((int) MathF.Floor(distance / longRouteScale), 0, 8);
        fallbackLimit += longRouteExtra;
        unreachableLimit += longRouteExtra * 2;

        if (state.ObjectiveNoPathStreak >= fallbackLimit)
            _bench.RecordCount("npc.wave.pathblocked.fallback", 1);

        if (state.ObjectiveNoPathStreak < unreachableLimit)
            return;

        _bench.RecordCount("npc.wave.pathblocked.unreachable", 1);
        state.ObjectiveTarget = EntityUid.Invalid;
        state.ObjectiveNoPathActive = false;
        state.ObjectiveNoPathStreak = 0;
        htn.Blackboard.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
        if (htn.Blackboard.TryGetValue<EntityUid>("Target", out var objectiveTarget, EntityManager) &&
            objectiveTarget == objective)
        {
            htn.Blackboard.Remove<EntityUid>("Target");
            htn.Blackboard.Remove<EntityCoordinates>("TargetCoordinates");
        }
    }

    private void RunCommsLayer(EntityUid uid, HTNComponent htn, WaveRuntimeState state, TimeSpan now)
    {
        if (now < state.NextCommsScanTime)
            return;

        state.NextCommsScanTime = now + TimeSpan.FromSeconds(_commsScanIntervalSeconds);

        if (htn.RootTask.Task != "WH40KWaveCoordinatorRoot" &&
            htn.RootTask.Task != "WH40KWaveSupportRoot" &&
            htn.RootTask.Task != "WH40KWaveBreacherRoot")
        {
            return;
        }

        if (!TryGetCombatTarget(uid, htn, out var target))
            return;

        _waveComms.TryTacticalOrder(uid, target);
    }

    private bool TryResolveNpcTeamId(EntityUid uid, out string teamId)
    {
        if (TryComp(uid, out WH40KTeamMemberComponent? member) &&
            !string.IsNullOrWhiteSpace(member.TeamId))
        {
            teamId = member.TeamId;
            return true;
        }

        if (_teamRule.TryGetTeamIdFromEntity(uid, out var resolvedTeamId) &&
            !string.IsNullOrWhiteSpace(resolvedTeamId))
        {
            teamId = resolvedTeamId;
            return true;
        }

        teamId = string.Empty;
        return false;
    }

    private bool TryFindBestInfluencePoint(
        EntityUid uid,
        TransformComponent xform,
        string teamId,
        WH40KBattlePhase phase,
        float searchRadius,
        out EntityUid pointUid,
        out TransformComponent pointXform,
        out float holdRadius)
    {
        pointUid = EntityUid.Invalid;
        pointXform = default!;
        holdRadius = 0f;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);
        var bestPriority = int.MinValue;
        var bestDistance = float.MaxValue;

        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            searchRadius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (!TryComp(candidate, out WH40KInfluencePointComponent? point) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            if (phase < point.CaptureEnabledFromPhase)
                continue;

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var priority = GetInfluencePriority(point, teamId);
            if (priority <= 0)
                continue;

            var distance = (candidateMap.Position - origin.Position).Length();
            if (priority < bestPriority ||
                (priority == bestPriority && distance >= bestDistance))
            {
                continue;
            }

            pointUid = candidate;
            pointXform = candidateXform;
            holdRadius = MathF.Max(0.6f, MathF.Max(0.5f, point.CaptureRadius) * _influenceHoldRadiusFactor);
            bestPriority = priority;
            bestDistance = distance;
        }

        return pointUid != EntityUid.Invalid;
    }

    private bool TryFindBestEnemyObjectiveTarget(
        EntityUid uid,
        TransformComponent xform,
        string teamId,
        float searchRadius,
        out EntityUid objectiveUid,
        out WH40KObjectiveComponent objectiveComp,
        out TransformComponent objectiveXform,
        out float holdRadius)
    {
        objectiveUid = EntityUid.Invalid;
        objectiveComp = default!;
        objectiveXform = default!;
        holdRadius = 0f;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);
        var bestDistance = float.MaxValue;

        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            searchRadius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (!TryComp(candidate, out WH40KObjectiveComponent? objective) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            if (objective == null || candidateXform == null)
                continue;

            if (objective.Destroyed || objective.Destroying || string.IsNullOrWhiteSpace(objective.TeamId))
                continue;

            if (string.Equals(objective.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                _bench.RecordCount("npc.wave.objective_target_rejected_same_team", 1);
                continue;
            }

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var distance = (candidateMap.Position - origin.Position).Length();
            if (distance >= bestDistance)
                continue;

            objectiveUid = candidate;
            objectiveComp = objective;
            objectiveXform = candidateXform;
            holdRadius = MathF.Max(0.8f, 2.2f * _objectiveHoldRadiusFactor);
            bestDistance = distance;
        }

        return objectiveUid != EntityUid.Invalid;
    }

    private bool TryFindObjectiveStagingPoint(
        EntityUid uid,
        TransformComponent xform,
        string teamId,
        Vector2 objectivePosition,
        float npcObjectiveDistance,
        out EntityUid pointUid,
        out TransformComponent pointXform)
    {
        pointUid = EntityUid.Invalid;
        pointXform = default!;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);
        var bestScore = float.MaxValue;
        var query = EntityQueryEnumerator<WH40KInfluencePointComponent, TransformComponent>();

        while (query.MoveNext(out var candidate, out var point, out var candidateXform))
        {
            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var npcDistance = (candidateMap.Position - origin.Position).Length();
            if (npcDistance > _objectiveStagingSearchRadius)
                continue;

            var ownedByTeam = string.Equals(point.OwnerTeamId, teamId, StringComparison.OrdinalIgnoreCase);
            var capturedByTeam = string.Equals(point.CapturingTeamId, teamId, StringComparison.OrdinalIgnoreCase);
            if (ownedByTeam && (string.IsNullOrWhiteSpace(point.CapturingTeamId) || capturedByTeam))
                continue;

            var objectiveDistance = (candidateMap.Position - objectivePosition).Length();
            if (objectiveDistance >= npcObjectiveDistance - _objectiveStagingMinGain)
                continue;

            var score = objectiveDistance + npcDistance * 0.45f;
            if (score >= bestScore)
                continue;

            pointUid = candidate;
            pointXform = candidateXform;
            bestScore = score;
        }

        return pointUid != EntityUid.Invalid;
    }

    private bool TryGetObjectiveChunkCoordinates(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        string teamId,
        Vector2 destinationPosition,
        int routePressure,
        int laneSwitchCount,
        out EntityCoordinates chunkCoordinates)
    {
        chunkCoordinates = EntityCoordinates.Invalid;

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        if (npcMap.MapId == MapId.Nullspace)
            return false;

        var originPosition = npcMap.Position;
        var centerDirection = destinationPosition - npcMap.Position;
        if (centerDirection.LengthSquared() < 0.25f)
            centerDirection = destinationPosition - originPosition;

        if (centerDirection.LengthSquared() < 0.25f)
            centerDirection = Vector2.UnitX;

        centerDirection = Vector2.Normalize(centerDirection);
        var centerLateral = new Vector2(-centerDirection.Y, centerDirection.X);
        if (_teamDirectorStates.TryGetValue(teamId, out var directorState) &&
            directorState.TeamMapId == npcMap.MapId &&
            directorState.TeamMemberCount >= 3 &&
            directorState.TeamSpreadRadius >= 8f &&
            directorState.RallyLeader != uid)
        {
            var relativeToCenter = npcMap.Position - directorState.TeamCenter;
            var forwardFromCenter = Vector2.Dot(relativeToCenter, centerDirection);
            var lateralFromCenter = MathF.Abs(Vector2.Dot(relativeToCenter, centerLateral));
            var cohesionBlend = directorState.TeamSpreadRadius >= 16f
                ? 0.6f
                : 0.4f;

            if (forwardFromCenter > 10f)
                cohesionBlend *= 0.25f;

            if (forwardFromCenter > 18f ||
                ((destinationPosition - npcMap.Position).Length() <= 80f && forwardFromCenter > 6f))
            {
                cohesionBlend = 0f;
                _bench.RecordCount("npc.wave.objective.chunk_cohesion_front_skip", 1);
            }
            else if (forwardFromCenter < -8f && lateralFromCenter > 8f)
            {
                cohesionBlend = MathF.Min(0.72f, cohesionBlend + 0.12f);
                _bench.RecordCount("npc.wave.objective.chunk_cohesion_rear_boost", 1);
            }

            if (cohesionBlend > 0.01f)
            {
                originPosition = Vector2.Lerp(npcMap.Position, directorState.TeamCenter, cohesionBlend);
                _bench.RecordCount("npc.wave.objective.chunk_cohesion", 1);
            }
        }

        var toDestination = destinationPosition - originPosition;
        var destinationDistance = toDestination.Length();
        if (destinationDistance <= 1f)
            return false;

        var direction = Vector2.Normalize(toDestination);
        if (TryGetObjectiveLaneAnchor(teamId, npcMap.MapId, out var anchorPosition))
        {
            var anchorDirection = destinationPosition - anchorPosition;
            if (anchorDirection.LengthSquared() > 0.25f)
            {
                anchorDirection = Vector2.Normalize(anchorDirection);
                var blendedDirection = direction * 0.65f + anchorDirection * 0.35f;
                if (blendedDirection.LengthSquared() > 0.01f)
                    direction = Vector2.Normalize(blendedDirection);
            }
        }

        var lateralDirection = new Vector2(-direction.Y, direction.X);
        var role = ResolveDirectorRole(htn);
        var stepDistance = Math.Clamp(
            MathF.Min(_objectivePathChunkDistance * 0.45f, destinationDistance - 0.75f),
            5.5f,
            _objectivePathChunkDistance * 0.70f);
        if (routePressure > 0)
        {
            var retryCompression = routePressure < 3
                ? routePressure * 0.55f
                : 1.10f + (routePressure - 2) * 1.35f;
            stepDistance = MathF.Max(4.25f, stepDistance - retryCompression);
        }

        var lateral = GetObjectiveMarchLateralOffset(uid, role, routePressure, laneSwitchCount);
        var chunkWorld = originPosition + direction * stepDistance + lateralDirection * lateral;
        return TryMapPositionToCoordinates(npcMap.MapId, chunkWorld, out chunkCoordinates);
    }

    private bool TryGetObjectiveRegroupCoordinates(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        string teamId,
        WaveDirectorOrder order,
        Vector2 objectivePosition,
        float objectiveEngageRadius,
        out EntityCoordinates regroupCoordinates)
    {
        regroupCoordinates = EntityCoordinates.Invalid;

        if (!_teamDirectorStates.TryGetValue(teamId, out var directorState) ||
            directorState.TeamMemberCount < 3)
        {
            return false;
        }

        var role = ResolveDirectorRole(htn);

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        if (npcMap.MapId == MapId.Nullspace ||
            npcMap.MapId != directorState.TeamMapId)
        {
            return false;
        }

        if (!TryFindTeamRegroupAnchor(teamId, npcMap.MapId, out var anchorPosition))
            return false;

        var nearbyAllies = CountNearbyWaveAllies(uid, teamId, xform, 10f);
        var distanceToAnchor = (anchorPosition - npcMap.Position).Length();
        var formationSplit = directorState.TeamSpreadRadius >= 12f;
        if (order == WaveDirectorOrder.Regroup)
        {
            if (distanceToAnchor <= 2.5f)
                return false;
        }
        else
        {
            if ((objectivePosition - npcMap.Position).Length() <= objectiveEngageRadius + 4f)
                return false;

            var objectiveDirection = objectivePosition - anchorPosition;
            if (objectiveDirection.LengthSquared() <= 1f)
                objectiveDirection = objectivePosition - directorState.TeamCenter;

            if (objectiveDirection.LengthSquared() <= 1f)
                return false;

            objectiveDirection = Vector2.Normalize(objectiveDirection);
            var lateralDirection = new Vector2(-objectiveDirection.Y, objectiveDirection.X);
            var relativeToCenter = npcMap.Position - directorState.TeamCenter;
            var relativeToAnchor = npcMap.Position - anchorPosition;
            var forward = Vector2.Dot(relativeToCenter, objectiveDirection);
            var forwardFromAnchor = Vector2.Dot(relativeToAnchor, objectiveDirection);
            var lateral = MathF.Abs(Vector2.Dot(relativeToCenter, lateralDirection));
            var lateralFromAnchor = MathF.Abs(Vector2.Dot(relativeToAnchor, lateralDirection));
            var objectivePush =
                order == WaveDirectorOrder.PushObjective ||
                order == WaveDirectorOrder.BreachLane;

            if (objectivePush)
            {
                // Objective pressure should pull the squad forward. During an assault only rear or
                // badly isolated members regroup; frontline members keep advancing.
                var leaderOverextended =
                    uid == directorState.RallyLeader &&
                    directorState.TeamSpreadRadius >= 20f &&
                    forwardFromAnchor > 12f &&
                    nearbyAllies == 0;

                if (uid == directorState.RallyLeader &&
                    !leaderOverextended)
                    return false;

                if (forwardFromAnchor >= -0.75f &&
                    distanceToAnchor <= 8f &&
                    nearbyAllies >= 1)
                {
                    return false;
                }

                var straggling = forwardFromAnchor < -4.5f && distanceToAnchor > 4.5f;
                var splitRear =
                    formationSplit &&
                    forwardFromAnchor < -2.5f &&
                    distanceToAnchor > 7.5f &&
                    nearbyAllies <= 1;
                var lostFlank =
                    lateralFromAnchor > 8.5f &&
                    nearbyAllies == 0 &&
                    forwardFromAnchor < 2.5f;
                var isolated =
                    distanceToAnchor > 14f &&
                    nearbyAllies == 0 &&
                    forwardFromAnchor < 1.5f;

                // Breachers should not abandon the breach lane unless they are clearly lagging behind.
                if (role == WaveDirectorRole.Breacher &&
                    order == WaveDirectorOrder.BreachLane &&
                    !straggling &&
                    !splitRear)
                {
                    return false;
                }

                var requiresRegroup =
                    leaderOverextended ||
                    straggling ||
                    splitRear ||
                    lostFlank ||
                    isolated;

                if (!requiresRegroup)
                    return false;

                if (TryGetCombatTarget(uid, htn, out _))
                    return false;

                if (leaderOverextended)
                    _bench.RecordCount("npc.wave.objective.regroup_leader_hold", 1);
                if (splitRear)
                    _bench.RecordCount("npc.wave.objective.regroup_split_seek", 1);
                if (straggling)
                    _bench.RecordCount("npc.wave.objective.regroup_straggler_seek", 1);
                if (lostFlank || isolated)
                    _bench.RecordCount("npc.wave.objective.regroup_isolated_seek", 1);
            }
            else
            {
                var overextended = forward > 10f && nearbyAllies == 0;
                var isolated = distanceToAnchor > 14f && nearbyAllies == 0;
                var flanked = lateral > 9f && nearbyAllies == 0;
                if (!overextended && !isolated && !flanked)
                    return false;

                if (TryGetCombatTarget(uid, htn, out _))
                    return false;
            }
        }

        const int regroupSlots = 6;
        var slot = (uid.GetHashCode() & int.MaxValue) % regroupSlots;
        var angle = MathF.Tau * slot / regroupSlots;
        var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 1.8f;
        return TryMapPositionToCoordinates(npcMap.MapId, anchorPosition + offset, out regroupCoordinates);
    }

    private void RefreshObjectiveSteeringTarget(EntityUid uid, EntityCoordinates targetCoordinates, float range, bool forceReset = false)
    {
        targetCoordinates = ResolveWaveSteeringCoordinates(uid, targetCoordinates, avoidHazards: true);

        if (forceReset &&
            TryComp(uid, out NPCSteeringComponent? forcedSteering))
        {
            forcedSteering.PathfindToken?.Cancel();
            forcedSteering.PathfindToken = null;
            forcedSteering.CurrentPath.Clear();
            forcedSteering.Coordinates = targetCoordinates;
            forcedSteering.Range = MathF.Max(forcedSteering.Range, range);
            forcedSteering.Status = SteeringStatus.Moving;
            forcedSteering.NextPathRequestTime = TimeSpan.Zero;
            forcedSteering.PathRequestBackoffSeconds = 0f;
            forcedSteering.FailedPathCount = 0;
            forcedSteering.ObstacleFailureCount = 0;
            forcedSteering.LastObstacleFailureTime = TimeSpan.Zero;
            forcedSteering.LastStuckCoordinates = Transform(uid).Coordinates;
            forcedSteering.LastStuckTime = _timing.CurTime;
            forcedSteering.LaneRotateSign = 1;
            _bench.RecordCount("npc.wave.objective.steering_force_reset", 1);
            return;
        }

        if (TryComp(uid, out NPCSteeringComponent? steering) &&
            steering.Status == SteeringStatus.Moving &&
            (steering.CurrentPath.Count > 0 || steering.Pathfind) &&
            steering.PathRequestBackoffSeconds <= 0f &&
            steering.FailedPathCount == 0 &&
            steering.Coordinates.IsValid(EntityManager) &&
            steering.Coordinates.TryDistance(EntityManager, targetCoordinates, out var steeringDelta) &&
            steeringDelta <= 1.75f)
        {
            steering.Range = MathF.Max(steering.Range, range);
            return;
        }

        _steering.TryRegister(uid, targetCoordinates);
        if (TryComp(uid, out NPCSteeringComponent? refreshedSteering))
            refreshedSteering.Range = MathF.Max(refreshedSteering.Range, range);
    }

    private bool ShouldForceObjectiveRetarget(
        EntityUid uid,
        EntityCoordinates targetCoordinates,
        float objectiveDistance,
        float objectiveEngageRadius)
    {
        if (objectiveDistance <= objectiveEngageRadius + 6f ||
            !targetCoordinates.IsValid(EntityManager) ||
            !TryComp(uid, out NPCSteeringComponent? steering))
        {
            return false;
        }

        if (steering.Status == SteeringStatus.InRange)
        {
            _bench.RecordCount("npc.wave.objective.steering_force_reset_far_inrange", 1);
            return true;
        }

        if (steering.Status == SteeringStatus.Moving &&
            steering.CurrentPath.Count == 0 &&
            !steering.Pathfind &&
            steering.Coordinates.Equals(targetCoordinates))
        {
            _bench.RecordCount("npc.wave.objective.steering_force_reset_empty_path", 1);
            return true;
        }

        return false;
    }

    private EntityCoordinates ResolveWaveSteeringCoordinates(EntityUid uid, EntityCoordinates targetCoordinates, bool avoidHazards)
    {
        if (!targetCoordinates.IsValid(EntityManager))
            return targetCoordinates;

        if (TryProjectWaveSteeringCoordinates(uid, targetCoordinates, avoidHazards, out var projected))
        {
            if (!projected.Equals(targetCoordinates))
            {
                _bench.RecordCount(
                    avoidHazards
                        ? "npc.wave.steering_target.projected"
                        : "npc.wave.steering_target.projected_hazard",
                    1);
            }

            return projected;
        }

        _bench.RecordCount(
            avoidHazards
                ? "npc.wave.steering_target.project_failed"
                : "npc.wave.steering_target.project_failed_hazard",
            1);
        return targetCoordinates;
    }

    private bool TryProjectWaveSteeringCoordinates(
        EntityUid uid,
        EntityCoordinates targetCoordinates,
        bool avoidHazards,
        out EntityCoordinates projectedCoordinates)
    {
        projectedCoordinates = EntityCoordinates.Invalid;

        if (!targetCoordinates.IsValid(EntityManager))
            return false;

        var targetMap = _transform.ToMapCoordinates(targetCoordinates);
        if (targetMap.MapId == MapId.Nullspace)
            return false;

        var pathFlags = TryComp(uid, out NPCSteeringComponent? steering)
            ? steering.Flags
            : _pathfinding.GetFlags(uid);

        return TryProjectWaveSteeringCoordinates(
            targetMap.MapId,
            targetMap.Position,
            pathFlags,
            avoidHazards,
            out projectedCoordinates);
    }

    private bool TryProjectWaveSteeringCoordinates(
        MapId mapId,
        Vector2 desiredWorldPosition,
        PathFlags pathFlags,
        bool avoidHazards,
        out EntityCoordinates projectedCoordinates)
    {
        projectedCoordinates = EntityCoordinates.Invalid;
        var found = false;
        var bestScore = float.MaxValue;

        TryScoreWaveSteeringCandidate(
            mapId,
            desiredWorldPosition,
            desiredWorldPosition,
            pathFlags,
            avoidHazards,
            ref found,
            ref bestScore,
            ref projectedCoordinates);

        if (!found)
        {
            Span<float> radii = stackalloc float[7]
            {
                0.55f,
                0.85f,
                1.15f,
                1.45f,
                1.85f,
                2.35f,
                2.85f,
            };

            for (var radiusIndex = 0; radiusIndex < radii.Length; radiusIndex++)
            {
                var radius = radii[radiusIndex];
                for (var step = 0; step < 12; step++)
                {
                    var angle = MathF.Tau * step / 12f;
                    var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    var candidateWorld = desiredWorldPosition + offset;

                    TryScoreWaveSteeringCandidate(
                        mapId,
                        desiredWorldPosition,
                        candidateWorld,
                        pathFlags,
                        avoidHazards,
                        ref found,
                        ref bestScore,
                        ref projectedCoordinates);
                }

                if (found)
                    break;
            }
        }

        return found;
    }

    private void TryScoreWaveSteeringCandidate(
        MapId mapId,
        Vector2 desiredWorldPosition,
        Vector2 candidateWorldPosition,
        PathFlags pathFlags,
        bool avoidHazards,
        ref bool found,
        ref float bestScore,
        ref EntityCoordinates bestCoordinates)
    {
        if (!TryMapPositionToCoordinates(mapId, candidateWorldPosition, out var candidateCoordinates))
            return;

        var poly = _pathfinding.GetPoly(candidateCoordinates);
        if (poly == null ||
            !IsWaveSteeringPolyTraversable(poly, pathFlags, avoidHazards))
        {
            return;
        }

        var score = (candidateWorldPosition - desiredWorldPosition).Length();
        if (!poly.Data.IsFreeSpace)
            score += 0.35f;

        if ((poly.Data.Flags & PathfindingBreadcrumbFlag.Hazard) != 0x0)
            score += avoidHazards ? 10f : 1.5f;

        if (!found || score < bestScore)
        {
            found = true;
            bestScore = score;
            bestCoordinates = candidateCoordinates;
        }
    }

    private static bool IsWaveSteeringPolyTraversable(PathPoly poly, PathFlags pathFlags, bool avoidHazards)
    {
        if (!poly.IsValid())
            return false;

        var flags = poly.Data.Flags;
        var isHazard = (flags & PathfindingBreadcrumbFlag.Hazard) != 0x0;
        if (isHazard && avoidHazards)
            return false;

        var hasHardBlock = poly.Data.CollisionLayer != 0 || poly.Data.CollisionMask != 0;
        if (!hasHardBlock)
            return true;

        var isDoor = (flags & PathfindingBreadcrumbFlag.Door) != 0x0;
        var isAccess = (flags & PathfindingBreadcrumbFlag.Access) != 0x0;
        var isClimb = (flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

        if (isDoor)
        {
            if (!isAccess && (pathFlags & PathFlags.Interact) != 0x0)
                return true;

            if ((pathFlags & PathFlags.Prying) != 0x0)
                return true;
        }

        if (isClimb && (pathFlags & PathFlags.Climbing) != 0x0)
            return true;

        if (poly.Data.Damage > 0f && (pathFlags & PathFlags.Smashing) != 0x0)
            return true;

        return false;
    }

    private bool TryFindTeamRegroupAnchor(string teamId, MapId mapId, out Vector2 anchorPosition)
    {
        anchorPosition = Vector2.Zero;

        if (!_teamDirectorStates.TryGetValue(teamId, out var directorState) ||
            directorState.TeamMapId != mapId)
        {
            return false;
        }

        if (directorState.RallyLeader != EntityUid.Invalid &&
            TryComp(directorState.RallyLeader, out TransformComponent? leaderXform))
        {
            var leaderMap = _transform.ToMapCoordinates(leaderXform.Coordinates);
            if (leaderMap.MapId == mapId)
            {
                var leaderOffset = (leaderMap.Position - directorState.TeamCenter).Length();
                if (leaderOffset <= 14f || directorState.TeamMemberCount <= 2)
                {
                    anchorPosition = Vector2.Lerp(directorState.TeamCenter, leaderMap.Position, 0.55f);
                    return true;
                }

                var leaderVector = leaderMap.Position - directorState.TeamCenter;
                if (leaderVector.LengthSquared() > 0.01f)
                {
                    var clippedOffset = MathF.Min(leaderOffset * 0.72f, 12f);
                    anchorPosition = directorState.TeamCenter + Vector2.Normalize(leaderVector) * clippedOffset;
                    _bench.RecordCount("npc.wave.objective.regroup_anchor_leader_clip", 1);
                    return true;
                }
            }
        }

        anchorPosition = directorState.TeamCenter;
        _bench.RecordCount("npc.wave.objective.regroup_anchor_center_fallback", 1);
        return directorState.TeamMemberCount > 0;
    }

    private EntityCoordinates GetObjectiveApproachCoordinates(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        string teamId,
        EntityUid objectiveUid,
        TransformComponent objectiveXform,
        float radius,
        int routePressure,
        int laneSwitchCount,
        bool standoff)
    {
        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        var objectiveMap = _transform.ToMapCoordinates(objectiveXform.Coordinates);
        if (npcMap.MapId == MapId.Nullspace || npcMap.MapId != objectiveMap.MapId)
            return objectiveXform.Coordinates;

        var laneAnchor = npcMap.Position;
        if (TryGetObjectiveLaneAnchor(teamId, npcMap.MapId, out var anchor))
            laneAnchor = anchor;

        var assaultDirection = objectiveMap.Position - laneAnchor;
        if (assaultDirection.LengthSquared() < 1f)
            assaultDirection = objectiveMap.Position - npcMap.Position;

        if (assaultDirection.LengthSquared() < 0.01f)
            assaultDirection = Vector2.UnitX;

        assaultDirection = Vector2.Normalize(assaultDirection);
        var lateralDirection = new Vector2(-assaultDirection.Y, assaultDirection.X);
        var role = ResolveDirectorRole(htn);
        var depth = GetObjectiveLaneDepth(role, radius, routePressure, standoff);
        var lateral = GetObjectiveLaneLateralOffset(uid, role, routePressure, laneSwitchCount, standoff);
        var approachWorld = objectiveMap.Position - assaultDirection * depth + lateralDirection * lateral;

        var objectiveDistance = (objectiveMap.Position - npcMap.Position).Length();
        if ((standoff || objectiveDistance <= 48f) &&
            TryGetObjectiveSightedApproachCoordinates(
                uid,
                objectiveUid,
                npcMap.MapId,
                objectiveMap.Position,
                assaultDirection,
                lateralDirection,
                depth,
                lateral,
                standoff,
                out var sightedCoordinates))
        {
            _bench.RecordCount(
                standoff
                    ? "npc.wave.objective.standoff_los_slot"
                    : "npc.wave.objective.approach_los_slot",
                1);
            return sightedCoordinates;
        }

        if (TryMapPositionToCoordinates(npcMap.MapId, approachWorld, out var approachCoordinates))
            return approachCoordinates;

        return objectiveXform.Coordinates.Offset(-assaultDirection * depth + lateralDirection * lateral);
    }

    private bool TryGetObjectiveSightedApproachCoordinates(
        EntityUid uid,
        EntityUid objectiveUid,
        MapId mapId,
        Vector2 objectivePosition,
        Vector2 assaultDirection,
        Vector2 lateralDirection,
        float depth,
        float lateral,
        bool standoff,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        var baseSign = lateral >= 0f ? 1f : -1f;
        Span<float> depthCandidates = stackalloc float[4]
        {
            depth,
            MathF.Max(1.25f, depth - (standoff ? 1.25f : 0.75f)),
            depth + 2.25f,
            depth + 4.25f,
        };
        Span<float> lateralCandidates = stackalloc float[7]
        {
            lateral,
            lateral + baseSign * 2.2f,
            lateral - baseSign * 2.2f,
            0f,
            -lateral,
            lateral + baseSign * 4.4f,
            lateral - baseSign * 4.4f,
        };

        foreach (var depthCandidate in depthCandidates)
        {
            foreach (var lateralCandidate in lateralCandidates)
            {
                var candidateWorld =
                    objectivePosition -
                    assaultDirection * depthCandidate +
                    lateralDirection * lateralCandidate;

                if (!TryMapPositionToCoordinates(mapId, candidateWorld, out var candidateCoordinates))
                    continue;

                var sightRange = (objectivePosition - candidateWorld).Length() + 0.25f;
                if (!_interaction.InRangeUnobstructed(
                        new MapCoordinates(candidateWorld, mapId),
                        objectiveUid,
                        sightRange,
                        CollisionGroup.Impassable |
                        CollisionGroup.InteractImpassable |
                        CollisionGroup.BulletImpassable))
                {
                    continue;
                }

                coordinates = candidateCoordinates;
                return true;
            }
        }

        return false;
    }

    private bool TryGetObjectiveIngressCoordinates(
        EntityUid uid,
        string teamId,
        TransformComponent xform,
        EntityUid objectiveUid,
        TransformComponent objectiveXform,
        out EntityCoordinates ingressCoordinates)
    {
        return TryGetObjectiveIngressDoor(
            uid,
            teamId,
            xform,
            objectiveUid,
            objectiveXform,
            out _,
            out ingressCoordinates);
    }

    private bool TryGetObjectiveIngressDoor(
        EntityUid uid,
        string teamId,
        TransformComponent xform,
        EntityUid objectiveUid,
        TransformComponent objectiveXform,
        out EntityUid ingressDoor,
        out EntityCoordinates ingressCoordinates)
    {
        ingressDoor = EntityUid.Invalid;
        ingressCoordinates = EntityCoordinates.Invalid;

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        var objectiveMap = _transform.ToMapCoordinates(objectiveXform.Coordinates);
        if (npcMap.MapId == MapId.Nullspace ||
            npcMap.MapId != objectiveMap.MapId)
        {
            return false;
        }

        var preferredDirection = npcMap.Position - objectiveMap.Position;
        if (TryGetObjectiveLaneAnchor(teamId, npcMap.MapId, out var anchor))
            preferredDirection = anchor - objectiveMap.Position;

        if (preferredDirection.LengthSquared() < 0.01f)
            preferredDirection = Vector2.UnitX;

        preferredDirection = Vector2.Normalize(preferredDirection);

        var bestScore = float.MaxValue;
        foreach (var candidate in _lookup.GetEntitiesInRange<DoorComponent>(objectiveMap, 8.5f))
        {
            var doorUid = candidate.Owner;
            if (doorUid == objectiveUid ||
                TerminatingOrDeleted(doorUid) ||
                !TryComp(doorUid, out TransformComponent? doorXform))
            {
                continue;
            }

            var doorMap = _transform.ToMapCoordinates(doorXform.Coordinates);
            if (doorMap.MapId != objectiveMap.MapId)
                continue;

            var offset = doorMap.Position - objectiveMap.Position;
            var objectiveDistance = offset.Length();
            if (objectiveDistance < 0.4f || objectiveDistance > 8.5f)
                continue;

            var ingressDirection = offset / objectiveDistance;
            var alignment = Vector2.Dot(ingressDirection, preferredDirection);
            var npcDistance = (doorMap.Position - npcMap.Position).Length();
            var score =
                objectiveDistance +
                MathF.Max(0f, 1f - alignment) * 4.5f +
                npcDistance * 0.04f;

            if (candidate.Comp.State is DoorState.Open or DoorState.Opening)
                score -= 0.75f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            ingressDoor = doorUid;
            ingressCoordinates = doorXform.Coordinates;
        }

        if (ingressDoor == EntityUid.Invalid)
            return false;

        _bench.RecordCount("npc.wave.objective.ingress_candidate", 1);
        return true;
    }

    private bool TryGetObjectiveBlockerTarget(
        EntityUid uid,
        TransformComponent xform,
        EntityUid objectiveUid,
        EntityCoordinates strategicTarget,
        WaveDirectorRole role,
        EntityUid preferredIngressDoor,
        out EntityUid blockerTarget)
    {
        blockerTarget = EntityUid.Invalid;

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        if (npcMap.MapId == MapId.Nullspace)
            return false;

        var targetMap = _transform.ToMapCoordinates(strategicTarget);
        var aimDirection = targetMap.Position - npcMap.Position;
        if (aimDirection.LengthSquared() < 0.01f &&
            TryComp(objectiveUid, out TransformComponent? objectiveXform))
        {
            aimDirection = _transform.ToMapCoordinates(objectiveXform.Coordinates).Position - npcMap.Position;
        }

        if (aimDirection.LengthSquared() < 0.01f)
            return false;

        aimDirection = Vector2.Normalize(aimDirection);
        var pathFlags = TryComp(uid, out NPCSteeringComponent? steering)
            ? steering.Flags
            : _pathfinding.GetFlags(uid);
        var blockerSearchRadius = role == WaveDirectorRole.Breacher
            ? 6.75f
            : 5.0f;
        var objectiveMap = TryComp(objectiveUid, out TransformComponent? objectiveTargetXform)
            ? _transform.ToMapCoordinates(objectiveTargetXform.Coordinates)
            : default;
        var npcObjectiveDistance =
            objectiveMap.MapId == npcMap.MapId
                ? (objectiveMap.Position - npcMap.Position).Length()
                : float.MaxValue;

        var bestScore = float.MaxValue;
        foreach (var candidate in _lookup.GetEntitiesInRange<TransformComponent>(npcMap, blockerSearchRadius))
        {
            var candidateUid = candidate.Owner;
            if (candidateUid == uid ||
                candidateUid == objectiveUid ||
                TerminatingOrDeleted(candidateUid))
            {
                continue;
            }

            var isDoor = TryComp(candidateUid, out DoorComponent? door);
            var isDamageable = HasComp<DamageableComponent>(candidateUid);
            if (!isDoor && !isDamageable)
                continue;

            if (HasComp<WH40KObjectiveComponent>(candidateUid))
                continue;

            if (!candidate.Comp.Anchored ||
                !IsCombatTargetCandidate(uid, candidateUid))
            {
                continue;
            }

            if (IsEnvironmentalHazardCandidate(candidateUid))
                continue;

            if (isDoor &&
                door != null &&
                door.State is DoorState.Open or DoorState.Opening)
            {
                continue;
            }

            if (!TryComp(candidateUid, out FixturesComponent? candidateFixtures) ||
                !HasMovementBlockingFixture(candidateFixtures))
            {
                _bench.RecordCount("npc.wave.objective.blocker_skip_nonblocking", 1);
                continue;
            }

            if (!TryComp(candidateUid, out PhysicsComponent? candidateBody) ||
                !candidateBody.CanCollide ||
                !candidateBody.Hard)
            {
                continue;
            }

            if (isDoor &&
                HasComp<AccessReaderComponent>(candidateUid) &&
                (pathFlags & (PathFlags.Prying | PathFlags.Smashing)) == 0)
            {
                _bench.RecordCount("npc.wave.objective.blocker_skip_locked_door", 1);
                continue;
            }

            var blockerBounds = _lookup.GetWorldAABB(candidateUid, candidate.Comp);
            var blockerSize = blockerBounds.Size;
            var blockerExtent = MathF.Max(blockerSize.X, blockerSize.Y);
            var blockerArea = blockerSize.X * blockerSize.Y;
            if (!isDoor &&
                blockerExtent < 0.55f &&
                blockerArea < 0.22f)
            {
                continue;
            }

            var candidateMap = _transform.ToMapCoordinates(candidate.Comp.Coordinates);
            if (candidateMap.MapId != npcMap.MapId)
                continue;

            var offset = candidateMap.Position - npcMap.Position;
            var distance = offset.Length();
            if (distance < 0.35f || distance > blockerSearchRadius)
                continue;

            var alignment = Vector2.Dot(offset / distance, aimDirection);
            if (alignment < 0.15f)
                continue;

            var projection = Vector2.Dot(offset, aimDirection);
            var perpendicularOffset = offset - aimDirection * projection;
            var corridorDistance = perpendicularOffset.Length();
            if (corridorDistance > 1.35f)
                continue;

            var score =
                distance +
                (1f - alignment) * 2.75f +
                corridorDistance * 1.9f;

            if (preferredIngressDoor != EntityUid.Invalid)
            {
                if (candidateUid == preferredIngressDoor)
                {
                    score -= 4.5f;
                }
                else if (role != WaveDirectorRole.Breacher && !isDoor)
                {
                    continue;
                }
                else if (!isDoor)
                {
                    score += 3.5f;
                }
            }

            if (objectiveMap.MapId == candidateMap.MapId)
            {
                var candidateObjectiveDistance = (objectiveMap.Position - candidateMap.Position).Length();
                if (candidateObjectiveDistance > npcObjectiveDistance + 1.25f && !isDoor)
                    score += 2.5f;
            }

            if (isDoor)
                score -= 0.85f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            blockerTarget = candidateUid;
        }

        return blockerTarget != EntityUid.Invalid;
    }

    private bool HasBlockingObjectiveIngressDoor(EntityUid ingressDoor)
    {
        return ingressDoor != EntityUid.Invalid &&
               TryComp(ingressDoor, out DoorComponent? ingressDoorComp) &&
               ingressDoorComp.State is not (DoorState.Open or DoorState.Opening);
    }

    private static bool HasMovementBlockingFixture(FixturesComponent fixtures)
    {
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            if ((fixture.CollisionMask & PathfindingSystem.PathfindingCollisionLayer) != 0x0 ||
                (fixture.CollisionLayer & PathfindingSystem.PathfindingCollisionMask) != 0x0)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldForceObjectiveLeaderFollow(
        EntityUid uid,
        string teamId,
        MapId mapId,
        float objectiveDistance,
        float objectiveEngageRadius)
    {
        if (!_teamDirectorStates.TryGetValue(teamId, out var directorState) ||
            directorState.TeamMapId != mapId ||
            directorState.TeamMemberCount < 3 ||
            directorState.RallyLeader == EntityUid.Invalid ||
            directorState.RallyLeader == uid)
        {
            return false;
        }

        if (objectiveDistance <= objectiveEngageRadius + 8f)
            return false;

        return directorState.TeamSpreadRadius >= 10f;
    }

    private bool TryGetObjectiveLaneAnchor(string teamId, MapId mapId, out Vector2 anchorPosition)
    {
        anchorPosition = Vector2.Zero;

        if (!_teamDirectorStates.TryGetValue(teamId, out var directorState) ||
            directorState.TeamMapId != mapId ||
            directorState.TeamMemberCount <= 0)
        {
            return false;
        }

        anchorPosition = directorState.TeamCenter;
        if (directorState.RallyLeader == EntityUid.Invalid ||
            !TryComp(directorState.RallyLeader, out TransformComponent? leaderXform))
        {
            return true;
        }

        var leaderMap = _transform.ToMapCoordinates(leaderXform.Coordinates);
        if (leaderMap.MapId != mapId)
            return true;

        var leaderOffset = (leaderMap.Position - directorState.TeamCenter).Length();
        var leaderWeight = leaderOffset <= 12f
            ? 0.35f
            : 0.15f;
        anchorPosition = Vector2.Lerp(directorState.TeamCenter, leaderMap.Position, leaderWeight);
        return true;
    }

    private static float GetObjectiveLaneDepth(WaveDirectorRole role, float radius, int routePressure, bool standoff)
    {
        var roleBias = role switch
        {
            WaveDirectorRole.Breacher => -0.45f,
            WaveDirectorRole.Assault => -0.15f,
            WaveDirectorRole.Sapper => 0.10f,
            WaveDirectorRole.Support => 0.35f,
            WaveDirectorRole.Coordinator => 0.65f,
            WaveDirectorRole.Logistics => 1.05f,
            _ => 0f,
        };

        var noPathBias = MathF.Min(routePressure * (standoff ? 0.35f : 0.22f), standoff ? 2.4f : 1.5f);
        return MathF.Max(0.6f, radius + roleBias + noPathBias);
    }

    private static float GetObjectiveLaneLateralOffset(
        EntityUid uid,
        WaveDirectorRole role,
        int routePressure,
        int laneSwitchCount,
        bool standoff)
    {
        var roleOffset = role switch
        {
            WaveDirectorRole.Coordinator => 0f,
            WaveDirectorRole.Assault => -0.75f,
            WaveDirectorRole.Breacher => 0.75f,
            WaveDirectorRole.Sapper => -1.35f,
            WaveDirectorRole.Support => 1.35f,
            WaveDirectorRole.Logistics => 1.9f,
            _ => 0f,
        };

        var jitter = ((uid.GetHashCode() >> 3) & 0x3) switch
        {
            0 => -0.30f,
            1 => -0.10f,
            2 => 0.10f,
            _ => 0.30f,
        };

        var spreadScale = standoff ? 1.15f : 0.95f;
        spreadScale += MathF.Min(routePressure * 0.10f, 0.55f);
        var offset = (roleOffset + jitter) * spreadScale;
        if (laneSwitchCount > 0 &&
            ((laneSwitchCount & 1) == 1))
        {
            offset = -offset;
        }

        return offset;
    }

    private static float GetObjectiveMarchLateralOffset(
        EntityUid uid,
        WaveDirectorRole role,
        int routePressure,
        int laneSwitchCount)
    {
        var assaultOffset = ((uid.GetHashCode() >> 1) & 1) == 0
            ? -0.20f
            : 0.20f;
        var roleOffset = role switch
        {
            WaveDirectorRole.Coordinator => 0f,
            WaveDirectorRole.Assault => assaultOffset,
            WaveDirectorRole.Breacher => 0.45f,
            WaveDirectorRole.Sapper => -0.45f,
            WaveDirectorRole.Support => 0.65f,
            WaveDirectorRole.Logistics => 0.85f,
            _ => 0f,
        };

        if (routePressure > 0)
        {
            if (MathF.Abs(roleOffset) < 0.01f)
            {
                roleOffset = ((uid.GetHashCode() >> 2) & 1) == 0
                    ? -0.30f
                    : 0.30f;
            }

            if (laneSwitchCount > 0 &&
                ((laneSwitchCount & 1) == 1))
            {
                roleOffset = -roleOffset;
            }

            roleOffset *= 1f + MathF.Min(0.65f, routePressure * 0.18f);

            if (routePressure >= 3)
            {
                var sidestep = 0.85f + MathF.Min(2.25f, (routePressure - 2) * 0.45f);
                roleOffset += MathF.CopySign(sidestep, roleOffset);
            }
        }

        return roleOffset;
    }

    private bool TryGetObjectiveLeaderFollowCoordinates(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        string teamId,
        Vector2 objectivePosition,
        out EntityCoordinates followCoordinates)
    {
        followCoordinates = EntityCoordinates.Invalid;

        if (!_teamDirectorStates.TryGetValue(teamId, out var directorState) ||
            directorState.TeamMapId == MapId.Nullspace ||
            directorState.RallyLeader == EntityUid.Invalid ||
            directorState.RallyLeader == uid ||
            directorState.TeamSpreadRadius < 9f ||
            !TryComp(directorState.RallyLeader, out TransformComponent? leaderXform))
        {
            return false;
        }

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        var leaderMap = _transform.ToMapCoordinates(leaderXform.Coordinates);
        if (npcMap.MapId == MapId.Nullspace ||
            npcMap.MapId != leaderMap.MapId)
        {
            return false;
        }

        var toLeader = leaderMap.Position - npcMap.Position;
        var distanceToLeader = toLeader.Length();
        if (distanceToLeader < 8f)
            return false;

        var toObjectiveFromLeader = objectivePosition - leaderMap.Position;
        if (toObjectiveFromLeader.LengthSquared() < 0.5f)
            return false;

        var leaderForward = Vector2.Normalize(toObjectiveFromLeader);
        var npcLag = Vector2.Dot(leaderMap.Position - npcMap.Position, leaderForward);
        if (npcLag < 4.5f)
            return false;

        var lateralDir = new Vector2(-leaderForward.Y, leaderForward.X);
        var role = ResolveDirectorRole(htn);
        var roleLateral = role switch
        {
            WaveDirectorRole.Coordinator => 0f,
            WaveDirectorRole.Assault => ((uid.GetHashCode() >> 1) & 1) == 0 ? -0.55f : 0.55f,
            WaveDirectorRole.Breacher => 0.85f,
            WaveDirectorRole.Sapper => -0.85f,
            WaveDirectorRole.Support => 1.15f,
            WaveDirectorRole.Logistics => 1.35f,
            _ => 0f,
        };

        var backoff = Math.Clamp(2.8f + npcLag * 0.12f, 2.8f, 6.2f);
        var followWorld = leaderMap.Position - leaderForward * backoff + lateralDir * roleLateral;
        var toFollowSlot = followWorld - npcMap.Position;
        var followDistance = toFollowSlot.Length();
        if (followDistance > 18f)
        {
            followWorld = npcMap.Position + Vector2.Normalize(toFollowSlot) * Math.Clamp(followDistance * 0.45f, 10f, 16f);
            _bench.RecordCount("npc.wave.objective.follow_leader_hop", 1);
        }

        return TryMapPositionToCoordinates(npcMap.MapId, followWorld, out followCoordinates);
    }

    private bool TryGetObjectiveLeaderMarchCoordinates(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        string teamId,
        Vector2 objectivePosition,
        out EntityCoordinates marchCoordinates)
    {
        marchCoordinates = EntityCoordinates.Invalid;

        if (!_teamDirectorStates.TryGetValue(teamId, out var directorState) ||
            directorState.TeamMapId == MapId.Nullspace ||
            directorState.RallyLeader == EntityUid.Invalid ||
            directorState.RallyLeader == uid ||
            !TryComp(directorState.RallyLeader, out TransformComponent? leaderXform))
        {
            return false;
        }

        if (TryComp(directorState.RallyLeader, out NPCSteeringComponent? leaderSteering) &&
            (leaderSteering.Status == SteeringStatus.NoPath ||
             leaderSteering.PathRequestBackoffSeconds > 0f))
        {
            return false;
        }

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        var leaderMap = _transform.ToMapCoordinates(leaderXform.Coordinates);
        if (npcMap.MapId == MapId.Nullspace ||
            npcMap.MapId != leaderMap.MapId)
        {
            return false;
        }

        var toObjectiveFromLeader = objectivePosition - leaderMap.Position;
        if (toObjectiveFromLeader.LengthSquared() < 1f)
            return false;

        var leaderForward = Vector2.Normalize(toObjectiveFromLeader);
        var lateralDir = new Vector2(-leaderForward.Y, leaderForward.X);
        var role = ResolveDirectorRole(htn);
        var roleLateral = role switch
        {
            WaveDirectorRole.Coordinator => 0.35f,
            WaveDirectorRole.Assault => ((uid.GetHashCode() >> 1) & 1) == 0 ? -0.70f : 0.70f,
            WaveDirectorRole.Breacher => 1.15f,
            WaveDirectorRole.Sapper => -1.15f,
            WaveDirectorRole.Support => 1.55f,
            WaveDirectorRole.Logistics => -1.55f,
            _ => 0f,
        };
        var backoff = role switch
        {
            WaveDirectorRole.Breacher => 1.8f,
            WaveDirectorRole.Assault => 2.4f,
            WaveDirectorRole.Sapper => 2.8f,
            WaveDirectorRole.Support => 3.2f,
            WaveDirectorRole.Coordinator => 3.5f,
            WaveDirectorRole.Logistics => 4f,
            _ => 2.8f,
        };

        var marchWorld = leaderMap.Position - leaderForward * backoff + lateralDir * roleLateral;
        var toMarchSlot = marchWorld - npcMap.Position;
        var marchDistance = toMarchSlot.Length();
        if (marchDistance > 20f)
        {
            marchWorld = npcMap.Position + Vector2.Normalize(toMarchSlot) * Math.Clamp(marchDistance * 0.42f, 11f, 17f);
            _bench.RecordCount("npc.wave.objective.march_slot_hop", 1);
        }

        return TryMapPositionToCoordinates(npcMap.MapId, marchWorld, out marchCoordinates);
    }

    private bool TryFindDirectorObjectiveTarget(
        HTNComponent htn,
        TransformComponent xform,
        string teamId,
        out EntityUid objectiveUid,
        out WH40KObjectiveComponent objectiveComp,
        out TransformComponent objectiveXform,
        out float holdRadius)
    {
        objectiveUid = EntityUid.Invalid;
        objectiveComp = default!;
        objectiveXform = default!;
        holdRadius = 0f;

        if (!htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var orderedTarget, EntityManager) ||
            orderedTarget == EntityUid.Invalid ||
            TerminatingOrDeleted(orderedTarget))
        {
            _bench.RecordCount("npc.wave.objective.director_target_missing", 1);
            return false;
        }

        if (!TryComp(orderedTarget, out WH40KObjectiveComponent? objective) ||
            !TryComp(orderedTarget, out TransformComponent? orderedXform) ||
            objective == null ||
            orderedXform == null)
        {
            _bench.RecordCount("npc.wave.objective.director_target_invalid", 1);
            return false;
        }

        if (objective.Destroyed ||
            objective.Destroying ||
            string.IsNullOrWhiteSpace(objective.TeamId))
        {
            _bench.RecordCount("npc.wave.objective.director_target_invalid", 1);
            return false;
        }

        if (string.Equals(objective.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            _bench.RecordCount("npc.wave.objective.director_target_same_team", 1);
            return false;
        }

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        var targetMap = _transform.ToMapCoordinates(orderedXform.Coordinates);
        if (npcMap.MapId != targetMap.MapId)
        {
            _bench.RecordCount("npc.wave.objective.director_target_map_mismatch", 1);
            return false;
        }

        objectiveUid = orderedTarget;
        objectiveComp = objective;
        objectiveXform = orderedXform;
        holdRadius = MathF.Max(0.8f, 2.2f * _objectiveHoldRadiusFactor);
        _bench.RecordCount("npc.wave.objective.director_target_hit", 1);
        return true;
    }

    private int GetInfluencePriority(WH40KInfluencePointComponent point, string teamId)
    {
        if (string.Equals(point.OwnerTeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(point.CapturingTeamId) &&
                !string.Equals(point.CapturingTeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                _bench.RecordCount("npc.wave.influence.defend_owned_contested", 1);
                return 5;
            }

            return 0;
        }

        if (string.Equals(point.CapturingTeamId, teamId, StringComparison.OrdinalIgnoreCase))
        {
            _bench.RecordCount("npc.wave.influence.assist_friendly_capture", 1);
            return 4;
        }

        if (string.IsNullOrWhiteSpace(point.OwnerTeamId))
        {
            _bench.RecordCount("npc.wave.influence.capture_neutral", 1);
            return 3;
        }

        _bench.RecordCount("npc.wave.influence.capture_enemy_owned", 1);
        return 4;
    }

    private void ProcessActiveDeployJob(EntityUid uid, HandsComponent hands, WaveRuntimeState state, TimeSpan now)
    {
        if (!state.DeployJobActive)
            return;

        if (state.DeployItem == EntityUid.Invalid ||
            TerminatingOrDeleted(state.DeployItem) ||
            !TryComp(state.DeployItem, out HandheldEntityPlacementComponent? placement))
        {
            _bench.RecordCount("npc.wave.deploy.fail", 1);
            _bench.RecordCount("npc.wave.deploy.fail_item_missing", 1);
            AbortDeployJob(state);
            return;
        }

        if (!IsHoldingEntity(uid, hands, state.DeployItem))
        {
            _bench.RecordCount("npc.wave.deploy.fail", 1);
            _bench.RecordCount("npc.wave.deploy.fail_lost_hand", 1);
            AbortDeployJob(state);
            return;
        }

        if ((now - state.DeployStartedAt).TotalSeconds > _deployJobTimeoutSeconds)
        {
            _bench.RecordCount("npc.wave.deploy.timeout", 1);
            _bench.RecordCount("npc.wave.deploy.fail", 1);
            AbortDeployJob(state);
            return;
        }

        if (now < state.DeployResolveAt)
        {
            if (state.DeployCoordinates.IsValid(EntityManager))
                _steering.TryRegister(uid, state.DeployCoordinates);
            return;
        }

        if (!_interaction.InRangeUnobstructed(uid, state.DeployCoordinates, placement.Range))
        {
            _bench.RecordCount("npc.wave.deploy.fail", 1);
            _bench.RecordCount("npc.wave.deploy.fail_out_of_range", 1);
            AbortDeployJob(state);
            return;
        }

        var complete = new HandheldEntityPlacementCompleteEvent(uid, state.DeployCoordinates, state.DeployDirection);
        RaiseLocalEvent(state.DeployItem, complete);

        if (!complete.Handled)
        {
            _bench.RecordCount("npc.wave.deploy.fail", 1);
            _bench.RecordCount("npc.wave.deploy.fail_complete_unhandled", 1);
            AbortDeployJob(state);
            return;
        }

        if (TryComp(state.DeployItem, out WH40KMortarComponent? mortar) &&
            mortar.Deployed)
        {
            _bench.RecordCount("npc.wave.deploy.mortar_placed", 1);
        }

        if (TryComp(state.DeployItem, out WH40KHeavyBolterComponent? bolter) &&
            bolter.Deployed)
        {
            _bench.RecordCount("npc.wave.deploy.heavy_bolter_placed", 1);
        }

        state.DeployCompletedCount++;
        _bench.RecordCount("npc.wave.deploy.success", 1);
        state.ResetDeploy();
    }

    private void AbortDeployJob(WaveRuntimeState state)
    {
        state.ResetDeploy();
    }

    private bool TryAssignServiceJob(
        EntityUid uid,
        TransformComponent xform,
        HandsComponent hands,
        WaveRuntimeState state,
        TimeSpan now)
    {
        if (TryFindNearestRestockMachineForHeldPackage(uid, xform, hands, _serviceSearchRadius, now, out var heldMachine))
        {
            state.ServiceJobActive = true;
            state.ServiceStartedAt = now;
            state.ServiceMachine = heldMachine;
            state.ServiceSourceItem = EntityUid.Invalid;
            state.ServiceSourceStorage = EntityUid.Invalid;
            state.ServiceRestockPending = false;
            state.ServicePendingRestockItem = EntityUid.Invalid;
            state.ServicePendingRestockUntil = TimeSpan.Zero;
            _bench.RecordCount("npc.wave.service.job_assigned", 1);
            _bench.RecordCount("npc.wave.service.job_assigned_held", 1);
            return true;
        }

        if (!TryFindBestServiceMachineWithSource(
                uid,
                xform,
                _serviceSearchRadius,
                now,
                out var machine,
                out _,
                out var sourceItem,
                out var sourceStorage))
        {
            return false;
        }

        state.ServiceJobActive = true;
        state.ServiceStartedAt = now;
        state.ServiceMachine = machine;
        state.ServiceSourceItem = sourceItem;
        state.ServiceSourceStorage = sourceStorage;
        state.ServiceRestockPending = false;
        state.ServicePendingRestockItem = EntityUid.Invalid;
        state.ServicePendingRestockUntil = TimeSpan.Zero;
        _bench.RecordCount("npc.wave.service.job_assigned", 1);
        return true;
    }

    private bool TryFindNearestRestockMachineForHeldPackage(
        EntityUid uid,
        TransformComponent xform,
        HandsComponent hands,
        float radius,
        TimeSpan now,
        out EntityUid machine)
    {
        machine = EntityUid.Invalid;
        var origin = _transform.ToMapCoordinates(xform.Coordinates);

        _serviceMachineCandidates.Clear();
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            radius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (!TryComp(candidate, out VendingMachineComponent? candidateMachine) ||
                candidateMachine.Broken ||
                !MachineNeedsRestock(candidateMachine) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var distance = (candidateMap.Position - origin.Position).Length();
            _serviceMachineCandidates.Add(new ServiceMachineCandidate(candidate, candidateMachine, distance));
        }

        if (_serviceMachineCandidates.Count == 0)
            return false;

        _serviceMachineCandidates.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        foreach (var candidate in _serviceMachineCandidates)
        {
            if (!HasHeldCompatibleRestockPackage(uid, hands, candidate.MachineComp.PackPrototypeId))
                continue;

            if (!TryReserveServiceMachine(uid, candidate.Machine, now))
            {
                _bench.RecordCount("npc.wave.service.reservation_conflict", 1);
                continue;
            }

            machine = candidate.Machine;
            return true;
        }

        return false;
    }

    private bool TryFindBestServiceMachineWithSource(
        EntityUid uid,
        TransformComponent xform,
        float radius,
        TimeSpan now,
        out EntityUid machine,
        out VendingMachineComponent machineComp,
        out EntityUid sourceItem,
        out EntityUid sourceStorage)
    {
        machine = EntityUid.Invalid;
        machineComp = default!;
        sourceItem = EntityUid.Invalid;
        sourceStorage = EntityUid.Invalid;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);

        _serviceMachineCandidates.Clear();
        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            radius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (!TryComp(candidate, out VendingMachineComponent? candidateMachine) ||
                candidateMachine.Broken ||
                !MachineNeedsRestock(candidateMachine) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var distance = (candidateMap.Position - origin.Position).Length();
            _serviceMachineCandidates.Add(new ServiceMachineCandidate(candidate, candidateMachine, distance));
        }

        if (_serviceMachineCandidates.Count == 0)
            return false;

        _serviceMachineCandidates.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        foreach (var candidate in _serviceMachineCandidates)
        {
            if (!TryReserveServiceMachine(uid, candidate.Machine, now))
            {
                _bench.RecordCount("npc.wave.service.reservation_conflict", 1);
                continue;
            }

            if (!TryFindNearestCompatibleRestockSource(
                    uid,
                    xform,
                    radius,
                    candidate.MachineComp.PackPrototypeId,
                    out sourceItem,
                    out sourceStorage))
            {
                _serviceReservations.Remove(candidate.Machine);
                _bench.RecordCount("npc.wave.service.source_search_miss", 1);
                _bench.RecordCount("npc.wave.service.machine_skip_no_source", 1);
                continue;
            }

            machine = candidate.Machine;
            machineComp = candidate.MachineComp;
            return true;
        }

        return false;
    }

    private void ProcessServiceAcquire(
        EntityUid uid,
        TransformComponent xform,
        WaveRuntimeState state,
        TimeSpan now,
        HandsComponent hands)
    {
        if (!TryComp(state.ServiceMachine, out VendingMachineComponent? serviceMachineComp))
        {
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        var requiredPackId = serviceMachineComp.PackPrototypeId;

        if (state.ServiceSourceItem != EntityUid.Invalid &&
            Exists(state.ServiceSourceItem) &&
            TryComp(state.ServiceSourceItem, out TransformComponent? sourceItemXform))
        {
            var sourceDistance = (_transform.ToMapCoordinates(sourceItemXform.Coordinates).Position -
                                  _transform.ToMapCoordinates(xform.Coordinates).Position).Length();

            if (sourceDistance > SharedInteractionSystem.InteractionRange + 0.1f)
            {
                _steering.TryRegister(uid, sourceItemXform.Coordinates);
                _bench.RecordCount("npc.wave.service.seek_source", 1);
                return;
            }

            _bench.RecordCount("npc.wave.service.acquire_attempt", 1);
            var acquired = _interaction.InteractionActivate(uid, state.ServiceSourceItem);
            if (!acquired)
            {
                acquired = _hands.TryPickupAnyHand(uid, state.ServiceSourceItem, checkActionBlocker: false, animateUser: false, animate: false, handsComp: hands);
            }

            if (acquired)
            {
                _bench.RecordCount("npc.wave.service.acquire_success", 1);
                state.ServiceSourceItem = EntityUid.Invalid;
            }
            else
            {
                _bench.RecordCount("npc.wave.service.acquire_fail", 1);
            }

            return;
        }

        state.ServiceSourceItem = EntityUid.Invalid;

        if (state.ServiceSourceStorage != EntityUid.Invalid &&
            Exists(state.ServiceSourceStorage) &&
            TryComp(state.ServiceSourceStorage, out EntityStorageComponent? sourceStorageComp) &&
            TryComp(state.ServiceSourceStorage, out TransformComponent? sourceStorageXform))
        {
            var sourceDistance = (_transform.ToMapCoordinates(sourceStorageXform.Coordinates).Position -
                                  _transform.ToMapCoordinates(xform.Coordinates).Position).Length();

            if (sourceDistance > SharedInteractionSystem.InteractionRange + 0.1f)
            {
                _steering.TryRegister(uid, sourceStorageXform.Coordinates);
                _bench.RecordCount("npc.wave.service.seek_source", 1);
                return;
            }

            if (TryFindContainedCompatibleRestock(sourceStorageComp, requiredPackId, out _))
            {
                _bench.RecordCount("npc.wave.service.source_open_attempt", 1);
                if (_entityStorage.TryOpenStorage(uid, state.ServiceSourceStorage))
                    _bench.RecordCount("npc.wave.service.source_open_success", 1);
                else
                _bench.RecordCount("npc.wave.service.source_open_fail", 1);
            }

            if (TryFindNearestCompatibleRestockSource(
                    uid,
                    xform,
                    MathF.Max(2f, _serviceSearchRadius),
                    requiredPackId,
                    out var sourceItem,
                    out var sourceStorage))
            {
                state.ServiceSourceItem = sourceItem;
                state.ServiceSourceStorage = sourceStorage;
                return;
            }
        }
        else
        {
            state.ServiceSourceStorage = EntityUid.Invalid;
        }

        if (!TryFindNearestCompatibleRestockSource(
                uid,
                xform,
                _serviceSearchRadius,
                requiredPackId,
                out var rediscoveredItem,
                out var rediscoveredStorage))
        {
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        state.ServiceSourceItem = rediscoveredItem;
        state.ServiceSourceStorage = rediscoveredStorage;
    }

    private void ProcessServiceDelivery(
        EntityUid uid,
        TransformComponent xform,
        WaveRuntimeState state,
        TimeSpan now,
        EntityUid heldPackage,
        VendingMachineComponent machineComp)
    {
        if (!TryComp(state.ServiceMachine, out TransformComponent? machineXform))
        {
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        var machineDistance = (_transform.ToMapCoordinates(machineXform.Coordinates).Position -
                               _transform.ToMapCoordinates(xform.Coordinates).Position).Length();

        if (machineDistance > SharedInteractionSystem.InteractionRange + 0.1f)
        {
            _steering.TryRegister(uid, machineXform.Coordinates);
            _bench.RecordCount("npc.wave.service.seek_target", 1);
            return;
        }

        if (!TryComp(state.ServiceMachine, out WiresPanelComponent? panel))
        {
            _bench.RecordCount("npc.wave.service.panel_closed_abort", 1);
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        if (!panel.Open)
        {
            _bench.RecordCount("npc.wave.service.panel_open_attempt", 1);
            if (_wires.TogglePanel(state.ServiceMachine, panel, true, uid))
            {
                _bench.RecordCount("npc.wave.service.panel_open_success", 1);
            }
            else
            {
                _bench.RecordCount("npc.wave.service.panel_closed_abort", 1);
                AbortServiceJob(uid, state, now, timeout: false);
                return;
            }
        }

        if (!TryComp(heldPackage, out VendingMachineRestockComponent? restockComp))
        {
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        _bench.RecordCount("npc.wave.service.restock_attempt", 1);

        if (!_vending.TryMatchPackageToMachine(
                heldPackage,
                restockComp,
                machineComp,
                uid,
                state.ServiceMachine))
        {
            _bench.RecordCount("npc.wave.service.restock_start_fail", 1);
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        if (!_vending.TryAccessMachine(
                heldPackage,
                restockComp,
                machineComp,
                uid,
                state.ServiceMachine))
        {
            _bench.RecordCount("npc.wave.service.panel_closed_abort", 1);
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        state.ServiceRestockPending = true;
        state.ServicePendingRestockItem = heldPackage;
        state.ServicePendingRestockUntil = now + restockComp.RestockDelay;
    }

    private void ResolvePendingServiceRestock(EntityUid uid, WaveRuntimeState state, TimeSpan now)
    {
        if (state.ServiceMachine == EntityUid.Invalid ||
            !TryComp(state.ServiceMachine, out VendingMachineComponent? machineComp))
        {
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        if (state.ServicePendingRestockItem == EntityUid.Invalid ||
            !Exists(state.ServicePendingRestockItem) ||
            !TryComp(state.ServicePendingRestockItem, out VendingMachineRestockComponent? restockComp))
        {
            _bench.RecordCount("npc.wave.service.restock_timeout", 1);
            _bench.RecordCount("npc.wave.service.job_timeout", 1);
            AbortServiceJob(uid, state, now, timeout: true);
            return;
        }

        if (!TryComp(uid, out HandsComponent? hands) ||
            !IsHoldingEntity(uid, hands, state.ServicePendingRestockItem))
        {
            _bench.RecordCount("npc.wave.service.restock_timeout", 1);
            _bench.RecordCount("npc.wave.service.job_timeout", 1);
            AbortServiceJob(uid, state, now, timeout: true);
            return;
        }

        if (!TryComp(state.ServiceMachine, out WiresPanelComponent? panel) || !panel.Open)
        {
            _bench.RecordCount("npc.wave.service.panel_closed_abort", 1);
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        if (!_vending.TryMatchPackageToMachine(
                state.ServicePendingRestockItem,
                restockComp,
                machineComp,
                uid,
                state.ServiceMachine))
        {
            _bench.RecordCount("npc.wave.service.restock_start_fail", 1);
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        if (!_vending.TryAccessMachine(
                state.ServicePendingRestockItem,
                restockComp,
                machineComp,
                uid,
                state.ServiceMachine))
        {
            _bench.RecordCount("npc.wave.service.panel_closed_abort", 1);
            AbortServiceJob(uid, state, now, timeout: false);
            return;
        }

        _vending.TryRestockInventory(state.ServiceMachine, machineComp);
        QueueDel(state.ServicePendingRestockItem);
        _bench.RecordCount("npc.wave.service.restock_success", 1);
        CompleteServiceJob(uid, state);
    }

    private bool IsHoldingEntity(EntityUid uid, HandsComponent hands, EntityUid item)
    {
        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (held == item)
                return true;
        }

        return false;
    }

    private bool TryGetHeldDeployableItem(
        EntityUid uid,
        HandsComponent? hands,
        out EntityUid deployItem,
        out HandheldEntityPlacementComponent placement)
    {
        deployItem = EntityUid.Invalid;
        placement = default!;

        if (hands == null)
            return false;

        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (!TryComp(held, out HandheldEntityPlacementComponent? heldPlacement) ||
                !IsSupportedDeployItem(held))
            {
                continue;
            }

            deployItem = held;
            placement = heldPlacement;
            return true;
        }

        return false;
    }

    private bool TryFindNearestDeployItem(
        EntityUid uid,
        TransformComponent xform,
        float radius,
        out EntityUid source,
        out TransformComponent sourceXform)
    {
        source = EntityUid.Invalid;
        sourceXform = default!;
        var bestDistance = float.MaxValue;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);

        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            radius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (!IsDeployItemCandidate(candidate))
                continue;

            if (!TryComp(candidate, out TransformComponent? candidateXform))
                continue;

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var distance = (candidateMap.Position - origin.Position).Length();
            if (distance >= bestDistance)
                continue;

            source = candidate;
            sourceXform = candidateXform;
            bestDistance = distance;
        }

        return source != EntityUid.Invalid;
    }

    private bool IsDeployItemCandidate(EntityUid candidate)
    {
        if (!TryComp(candidate, out HandheldEntityPlacementComponent? _) ||
            !TryComp(candidate, out ItemComponent? _) ||
            !TryComp(candidate, out TransformComponent? xform))
        {
            return false;
        }

        if (xform.Anchored || xform.MapID == MapId.Nullspace)
            return false;

        return IsSupportedDeployItem(candidate);
    }

    private bool IsSupportedDeployItem(EntityUid candidate)
    {
        if (TryComp(candidate, out WH40KMortarComponent? mortar))
            return !mortar.Deployed;

        if (TryComp(candidate, out WH40KHeavyBolterComponent? bolter))
            return !bolter.Deployed;

        return false;
    }

    private bool TryFindDeployCoordinates(
        EntityUid uid,
        TransformComponent xform,
        HandheldEntityPlacementComponent placement,
        out EntityCoordinates coordinates,
        out Direction direction)
    {
        coordinates = EntityCoordinates.Invalid;
        direction = Direction.North;

        if (xform.GridUid == null ||
            !TryComp<MapGridComponent>(xform.GridUid, out var grid))
        {
            return false;
        }

        var gridUid = xform.GridUid.Value;
        var center = _map.LocalToTile(gridUid, grid, xform.Coordinates);

        var offsets = new (Vector2i Offset, Direction Direction)[]
        {
            (new Vector2i(0, 1), Direction.North),
            (new Vector2i(1, 0), Direction.East),
            (new Vector2i(0, -1), Direction.South),
            (new Vector2i(-1, 0), Direction.West),
        };

        var start = _random.Next(offsets.Length);

        for (var i = 0; i < offsets.Length; i++)
        {
            var index = (start + i) % offsets.Length;
            var candidate = center + offsets[index].Offset;
            if (!_map.TryGetTileRef(gridUid, grid, candidate, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            var candidateCoordinates = new EntityCoordinates(gridUid, candidate + grid.TileSizeHalfVector);
            if (!_interaction.InRangeUnobstructed(uid, candidateCoordinates, placement.Range))
                continue;

            coordinates = candidateCoordinates;
            direction = offsets[index].Direction;
            return true;
        }

        if (!_map.TryGetTileRef(gridUid, grid, center, out var centerTile) || centerTile.Tile.IsEmpty)
            return false;

        var fallbackCoordinates = new EntityCoordinates(gridUid, center + grid.TileSizeHalfVector);
        if (!_interaction.InRangeUnobstructed(uid, fallbackCoordinates, placement.Range))
            return false;

        coordinates = fallbackCoordinates;
        direction = Direction.North;
        return true;
    }

    private bool HasCombatTarget(EntityUid uid, HTNComponent htn)
    {
        return TryGetCombatTarget(uid, htn, out _);
    }

    private bool TryGetCombatTarget(EntityUid uid, HTNComponent htn, out EntityUid target)
    {
        if (htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.UtilityTarget, out var utilityTarget, EntityManager) &&
            utilityTarget != EntityUid.Invalid)
        {
            if (IsCombatTargetCandidate(uid, utilityTarget))
            {
                target = utilityTarget;
                return true;
            }
        }

        if (htn.Blackboard.TryGetValue<EntityUid>("Target", out var directTarget, EntityManager) &&
            directTarget != EntityUid.Invalid)
        {
            if (IsCombatTargetCandidate(uid, directTarget))
            {
                target = directTarget;
                return true;
            }
        }

        target = EntityUid.Invalid;
        return false;
    }

    private bool TryEnsureActiveHeldGun(EntityUid uid, HandsComponent hands)
    {
        if (_gun.TryGetGun(uid, out _))
            return true;

        foreach (var handId in hands.SortedHands)
        {
            if (!_hands.TryGetHeldItem((uid, hands), handId, out var held) ||
                held == null ||
                !HasComp<GunComponent>(held.Value))
            {
                continue;
            }

            _hands.TrySetActiveHand((uid, hands), handId);
            if (_gun.TryGetGun(uid, out _))
                return true;
        }

        return false;
    }

    private bool TryEnsureActiveHeldCombatItem(EntityUid uid, HandsComponent hands)
    {
        if (TryEnsureActiveHeldGun(uid, hands))
            return true;

        foreach (var handId in hands.SortedHands)
        {
            if (!_hands.TryGetHeldItem((uid, hands), handId, out var held) ||
                held == null)
            {
                continue;
            }

            if (!HasComp<MeleeWeaponComponent>(held.Value) &&
                !HasComp<GunComponent>(held.Value))
            {
                continue;
            }

            _hands.TrySetActiveHand((uid, hands), handId);
            TryEquipHeldCombatItem(uid, held.Value);
            return true;
        }

        return false;
    }

    private bool TryReacquireNearbyCombatItem(EntityUid uid, TransformComponent xform, HandsComponent hands)
    {
        if (_hands.GetEmptyHandCount((uid, hands)) <= 0)
            return false;

        if (!TryFindNearestLoadoutItem(uid, xform, 2.75f, out var source, out _, out var distance) ||
            distance > SharedInteractionSystem.InteractionRange + 0.15f)
        {
            return false;
        }

        _bench.RecordCount("npc.wave.objective.rearm_attempt", 1);

        var acquired = _interaction.InteractionActivate(uid, source);
        if (!acquired)
        {
            acquired = _hands.TryPickupAnyHand(uid, source, checkActionBlocker: false, animateUser: false, animate: false, handsComp: hands);
        }

        if (!acquired)
            return false;

        if (TryGetHeldCombatItem(uid, hands, out var heldCombatItem))
            TryEquipHeldCombatItem(uid, heldCombatItem);

        _bench.RecordCount("npc.wave.objective.rearm_success", 1);
        return true;
    }

    private bool IsCombatTargetCandidate(EntityUid uid, EntityUid candidate)
    {
        if (candidate == EntityUid.Invalid ||
            TerminatingOrDeleted(candidate) ||
            !Exists(candidate))
        {
            return false;
        }

        if (TryComp(candidate, out WH40KObjectiveComponent? objective))
        {
            if (objective.Destroyed ||
                objective.Destroying ||
                string.IsNullOrWhiteSpace(objective.TeamId))
            {
                _bench.RecordCount("npc.wave.target.reject_invalid_objective", 1);
                return false;
            }

            if (TryResolveNpcTeamId(uid, out var teamId) &&
                string.Equals(objective.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                _bench.RecordCount("npc.wave.target.reject_friendly_objective", 1);
                return false;
            }

            return true;
        }

        if (HasComp<ItemComponent>(candidate))
        {
            _bench.RecordCount("npc.wave.target.reject_item", 1);
            return false;
        }

        if (TryComp(uid, out NpcFactionMemberComponent? ourFaction) &&
            TryComp(candidate, out NpcFactionMemberComponent? otherFaction))
        {
            var friendlyToOther = _npcFaction.IsEntityFriendly((uid, ourFaction), (candidate, otherFaction));
            var otherFriendlyToUs = _npcFaction.IsEntityFriendly((candidate, otherFaction), (uid, ourFaction));
            if (friendlyToOther || otherFriendlyToUs)
            {
                _bench.RecordCount("npc.wave.target.reject_friendly", 1);
                return false;
            }
        }

        return true;
    }

    private bool TryFindNearestCompatibleRestockSource(
        EntityUid uid,
        TransformComponent xform,
        float radius,
        string requiredPackId,
        out EntityUid sourceItem,
        out EntityUid sourceStorage)
    {
        sourceItem = EntityUid.Invalid;
        sourceStorage = EntityUid.Invalid;
        var bestDistance = float.MaxValue;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);

        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            radius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (!TryComp(candidate, out TransformComponent? candidateXform))
                continue;

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var distance = (candidateMap.Position - origin.Position).Length();
            if (distance >= bestDistance)
                continue;

            if (TryComp(candidate, out VendingMachineRestockComponent? restockComp))
            {
                if (!_vending.IsPackageCompatibleWithPack(restockComp, requiredPackId))
                {
                    _bench.RecordCount("npc.wave.service.source_skip_incompatible", 1);
                    continue;
                }

                _bench.RecordCount("npc.wave.service.source_candidate_compatible", 1);

                if (!HasComp<ItemComponent>(candidate) || candidateXform.Anchored)
                    continue;

                sourceItem = candidate;
                sourceStorage = EntityUid.Invalid;
                bestDistance = distance;
                _bench.RecordCount("npc.wave.service.source_selected_item", 1);
                continue;
            }

            if (!TryComp(candidate, out EntityStorageComponent? storageComp))
                continue;

            if (!TryFindContainedCompatibleRestock(storageComp, requiredPackId, out _))
                continue;

            sourceItem = EntityUid.Invalid;
            sourceStorage = candidate;
            bestDistance = distance;
            _bench.RecordCount("npc.wave.service.source_selected_storage", 1);
        }

        return sourceItem != EntityUid.Invalid || sourceStorage != EntityUid.Invalid;
    }

    private bool TryFindContainedCompatibleRestock(
        EntityStorageComponent storageComp,
        string requiredPackId,
        out EntityUid sourceItem)
    {
        sourceItem = EntityUid.Invalid;

        foreach (var contained in storageComp.Contents.ContainedEntities)
        {
            if (!Exists(contained) ||
                !TryComp(contained, out VendingMachineRestockComponent? restockComp) ||
                !_vending.IsPackageCompatibleWithPack(restockComp, requiredPackId))
            {
                continue;
            }

            sourceItem = contained;
            _bench.RecordCount("npc.wave.service.source_storage_match", 1);
            return true;
        }

        return false;
    }

    private bool TryGetHeldCompatibleRestockPackage(
        EntityUid uid,
        HandsComponent hands,
        string requiredPackId,
        out EntityUid heldPackage)
    {
        heldPackage = EntityUid.Invalid;

        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (!TryComp(held, out VendingMachineRestockComponent? restockComp))
            {
                continue;
            }

            if (!_vending.IsPackageCompatibleWithPack(restockComp, requiredPackId))
            {
                _bench.RecordCount("npc.wave.service.held_incompatible_seen", 1);
                continue;
            }

            _bench.RecordCount("npc.wave.service.held_compatible_found", 1);
            heldPackage = held;
            return true;
        }

        return false;
    }

    private bool HasHeldCompatibleRestockPackage(
        EntityUid uid,
        HandsComponent hands,
        string requiredPackId)
    {
        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (TryComp(held, out VendingMachineRestockComponent? restockComp) &&
                _vending.IsPackageCompatibleWithPack(restockComp, requiredPackId))
            {
                return true;
            }
        }

        return false;
    }

    private bool EnsureServiceAcquireCapacity(EntityUid uid, HandsComponent hands, string requiredPackId)
    {
        if (_hands.GetEmptyHandCount((uid, hands)) > 0)
            return true;

        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (!TryComp(held, out VendingMachineRestockComponent? restockComp) ||
                _vending.IsPackageCompatibleWithPack(restockComp, requiredPackId))
            {
                continue;
            }

            if (_hands.TryDrop((uid, hands), held, checkActionBlocker: false, doDropInteraction: false))
            {
                _bench.RecordCount("npc.wave.service.drop_incompatible_held", 1);
                return true;
            }

            _bench.RecordCount("npc.wave.service.drop_incompatible_fail", 1);
        }

        return _hands.GetEmptyHandCount((uid, hands)) > 0;
    }

    private bool MachineNeedsRestock(VendingMachineComponent machineComp)
    {
        if (!_proto.TryIndex(machineComp.PackPrototypeId, out VendingMachineInventoryPrototype? inventoryProto))
            return false;

        foreach (var (productId, expectedAmount) in inventoryProto.StartingInventory)
        {
            if (!machineComp.Inventory.TryGetValue(productId, out var current))
                return true;

            if (current.Amount < expectedAmount)
                return true;
        }

        return false;
    }

    private bool TryReserveServiceMachine(EntityUid uid, EntityUid machine, TimeSpan now)
    {
        if (_serviceReservations.TryGetValue(machine, out var reservation))
        {
            if (reservation.Owner == uid)
            {
                reservation.ExpiresAt = now + TimeSpan.FromSeconds(_serviceReservationTtlSeconds);
                return true;
            }

            if (!TerminatingOrDeleted(reservation.Owner) &&
                reservation.ExpiresAt > now)
            {
                return false;
            }
        }

        _serviceReservations[machine] = new ServiceReservationState
        {
            Owner = uid,
            ExpiresAt = now + TimeSpan.FromSeconds(_serviceReservationTtlSeconds),
        };
        return true;
    }

    private void RefreshServiceReservation(EntityUid uid, WaveRuntimeState state, TimeSpan now)
    {
        if (state.ServiceMachine == EntityUid.Invalid)
            return;

        if (!_serviceReservations.TryGetValue(state.ServiceMachine, out var reservation) ||
            reservation.Owner != uid)
        {
            return;
        }

        reservation.ExpiresAt = now + TimeSpan.FromSeconds(_serviceReservationTtlSeconds);
    }

    private void CompleteServiceJob(EntityUid uid, WaveRuntimeState state)
    {
        _waveComms.TryServiceReport(uid, state.ServiceMachine);
        _bench.RecordCount("npc.wave.service.job_completed", 1);
        ReleaseServiceReservation(uid, state);
        state.ResetService();
    }

    private void AbortServiceJob(EntityUid uid, WaveRuntimeState state, TimeSpan now, bool timeout)
    {
        if (timeout)
            _bench.RecordCount("npc.wave.service.job_timeout", 1);

        if (state.ServiceJobActive)
            _bench.RecordCount("npc.wave.service.job_aborted", 1);

        ReleaseServiceReservation(uid, state);
        state.ResetService();
        state.NextServiceScanTime = now + TimeSpan.FromSeconds(_serviceScanIntervalSeconds);
    }

    private void ReleaseServiceReservation(EntityUid uid, WaveRuntimeState state)
    {
        if (state.ServiceMachine == EntityUid.Invalid)
            return;

        if (_serviceReservations.TryGetValue(state.ServiceMachine, out var reservation) &&
            reservation.Owner == uid)
        {
            _serviceReservations.Remove(state.ServiceMachine);
        }
    }

    private void ExitShelter(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        WaveRuntimeState state,
        TimeSpan now,
        bool timeoutExit)
    {
        if (timeoutExit)
            _bench.RecordCount("npc.wave.weather.shelter_timeout", 1);

        if (state.ShelterReturnCoordinates.IsValid(EntityManager))
            _steering.TryRegister(uid, state.ShelterReturnCoordinates);
        else
            _steering.TryRegister(uid, xform.Coordinates);

        state.ShelterActive = false;
        state.ShelterCoordinates = EntityCoordinates.Invalid;
        state.ShelterReturnCoordinates = EntityCoordinates.Invalid;
        state.ShelterCooldownUntil = now + TimeSpan.FromSeconds(_shelterReentryCooldownSeconds);
        htn.Blackboard.Remove<bool>(NPCBlackboard.WaveShelterActive);
        _bench.RecordCount("npc.wave.weather.shelter_exit", 1);
    }

    private void FinalizeLoadoutReadiness(WaveRuntimeState state, TimeSpan now)
    {
        if (state.LoadoutStartTime == null)
            return;

        var elapsed = now - state.LoadoutStartTime.Value;
        if (elapsed.TotalSeconds <= _loadoutReadyTimeoutSeconds)
        {
            _bench.RecordCount("npc.wave.loadout.ready_bounded", 1);
            _bench.RecordDuration("npc.wave.loadout.ready_ms", elapsed.TotalMilliseconds);
        }
        else
        {
            _bench.RecordCount("npc.wave.loadout.ready_timeout", 1);
        }

        state.LoadoutStartTime = null;
        state.LoadoutTimeoutReported = false;
    }

    private bool TryGetHeldCombatItem(EntityUid uid, HandsComponent hands, out EntityUid heldCombatItem)
    {
        heldCombatItem = EntityUid.Invalid;
        EntityUid fallbackMelee = EntityUid.Invalid;

        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (HasComp<GunComponent>(held))
            {
                heldCombatItem = held;
                return true;
            }

            if (fallbackMelee == EntityUid.Invalid && HasComp<MeleeWeaponComponent>(held))
                fallbackMelee = held;
        }

        if (fallbackMelee == EntityUid.Invalid)
            return false;

        heldCombatItem = fallbackMelee;
        return true;
    }

    private void TryEquipHeldCombatItem(EntityUid uid, EntityUid heldCombatItem)
    {
        if (TryComp(uid, out HandsComponent? hands))
        {
            foreach (var handId in hands.SortedHands)
            {
                if (!_hands.TryGetHeldItem((uid, hands), handId, out var held) ||
                    held == null ||
                    held.Value != heldCombatItem)
                {
                    continue;
                }

                _hands.TrySetActiveHand((uid, hands), handId);
                break;
            }
        }

        if (!TryComp(heldCombatItem, out WieldableComponent? wieldable) || wieldable.Wielded)
            return;

        _bench.RecordCount("npc.wave.loadout.equip_attempt", 1);
        if (_wieldable.TryWield(heldCombatItem, wieldable, uid))
            _bench.RecordCount("npc.wave.loadout.equip_success", 1);
        else
            _bench.RecordCount("npc.wave.loadout.equip_fail", 1);
    }

    private bool ShouldObjectiveCombatPreempt(EntityUid uid, EntityUid combatTarget)
    {
        if (combatTarget == EntityUid.Invalid ||
            TerminatingOrDeleted(combatTarget))
        {
            return false;
        }

        if (TryComp(combatTarget, out WH40KObjectiveComponent? objective))
        {
            return !objective.Destroyed &&
                   !objective.Destroying &&
                   !string.IsNullOrWhiteSpace(objective.TeamId);
        }

        if (HasComp<ActiveNPCComponent>(combatTarget))
            return true;

        if (TryComp(combatTarget, out MobStateComponent? mobState) &&
            mobState.CurrentState != MobState.Dead)
        {
            return true;
        }

        if (TryComp(uid, out NpcFactionMemberComponent? ourFaction) &&
            TryComp(combatTarget, out NpcFactionMemberComponent? otherFaction))
        {
            return !_npcFaction.IsEntityFriendly((uid, ourFaction), (combatTarget, otherFaction)) ||
                   !_npcFaction.IsEntityFriendly((combatTarget, otherFaction), (uid, ourFaction));
        }

        return false;
    }

    private void ClearIncidentalObjectiveCombatTarget(EntityUid uid, HTNComponent htn, EntityUid combatTarget)
    {
        var cleared = false;

        if (htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.UtilityTarget, out var utilityTarget, EntityManager) &&
            utilityTarget == combatTarget)
        {
            htn.Blackboard.Remove<EntityUid>(NPCBlackboard.UtilityTarget);
            cleared = true;
        }

        if (htn.Blackboard.TryGetValue<EntityUid>("Target", out var directTarget, EntityManager) &&
            directTarget == combatTarget)
        {
            htn.Blackboard.Remove<EntityUid>("Target");
            htn.Blackboard.Remove<EntityCoordinates>("TargetCoordinates");
            cleared = true;
        }

        if (TryComp(uid, out NPCRangedCombatComponent? ranged) &&
            ranged.Target == combatTarget)
        {
            ranged.Target = EntityUid.Invalid;
            ranged.TargetInLOS = false;
            ranged.Status = CombatStatus.Unspecified;
            ranged.ShootAccumulator = 0f;
            cleared = true;
        }

        if (TryComp(uid, out NPCMeleeCombatComponent? melee) &&
            melee.Target == combatTarget)
        {
            melee.Target = EntityUid.Invalid;
            melee.Status = CombatStatus.Unspecified;
            cleared = true;
        }

        if (cleared)
            _bench.RecordCount("npc.wave.objective.clear_incidental_combat_target", 1);
    }

    private void SanitizeObjectiveCombatTargets(EntityUid uid, EntityUid objective, EntityUid objectiveBlockerTarget)
    {
        if (TryComp(uid, out NPCRangedCombatComponent? ranged) &&
            ranged.Target != EntityUid.Invalid &&
            ranged.Target != objective &&
            ranged.Target != objectiveBlockerTarget &&
            (!IsCombatTargetCandidate(uid, ranged.Target) ||
             !ShouldObjectiveCombatPreempt(uid, ranged.Target)))
        {
            ranged.Target = EntityUid.Invalid;
            ranged.TargetInLOS = false;
            ranged.Status = CombatStatus.Unspecified;
            ranged.ShootAccumulator = 0f;
            _bench.RecordCount("npc.wave.objective.clear_invalid_ranged_target", 1);
        }

        if (TryComp(uid, out NPCMeleeCombatComponent? melee) &&
            melee.Target != EntityUid.Invalid &&
            melee.Target != objective &&
            melee.Target != objectiveBlockerTarget &&
            (!IsCombatTargetCandidate(uid, melee.Target) ||
             !ShouldObjectiveCombatPreempt(uid, melee.Target)))
        {
            melee.Target = EntityUid.Invalid;
            melee.Status = CombatStatus.Unspecified;
            _bench.RecordCount("npc.wave.objective.clear_invalid_melee_target", 1);
        }
    }

    private bool TryFindNearestLoadoutItem(
        EntityUid uid,
        TransformComponent xform,
        float radius,
        out EntityUid source,
        out TransformComponent sourceXform,
        out float distance)
    {
        source = EntityUid.Invalid;
        sourceXform = default!;
        distance = float.MaxValue;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);

        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            radius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (!IsLoadoutItemCandidate(candidate))
                continue;

            if (!TryComp(candidate, out TransformComponent? candidateXform))
                continue;

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var candidateDistance = (candidateMap.Position - origin.Position).Length();
            if (candidateDistance >= distance)
                continue;

            source = candidate;
            sourceXform = candidateXform;
            distance = candidateDistance;
        }

        return source != EntityUid.Invalid;
    }

    private bool IsLoadoutItemCandidate(EntityUid candidate)
    {
        if (!TryComp(candidate, out ItemComponent? _) ||
            !TryComp(candidate, out TransformComponent? xform))
        {
            return false;
        }

        if (xform.Anchored || xform.MapID == MapId.Nullspace)
            return false;

        return HasComp<GunComponent>(candidate) || HasComp<MeleeWeaponComponent>(candidate);
    }

    private bool TryFindNearestArmedMine(
        EntityUid uid,
        TransformComponent xform,
        float radius,
        out EntityUid mine,
        out TransformComponent mineXform,
        out float distance)
    {
        mine = EntityUid.Invalid;
        mineXform = default!;
        distance = float.MaxValue;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);

        _mineLookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            xform.Coordinates,
            radius,
            _mineLookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var (candidate, _) in _mineLookupBuffer)
        {
            if (!TryComp(candidate, out ItemToggleComponent? toggle) ||
                !toggle.Activated ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var candidateDistance = (candidateMap.Position - origin.Position).Length();
            if (candidateDistance >= distance)
                continue;

            mine = candidate;
            mineXform = candidateXform;
            distance = candidateDistance;
        }

        return mine != EntityUid.Invalid;
    }

    private bool TryFindNearestEnvironmentalHazard(
        EntityUid uid,
        TransformComponent xform,
        float radius,
        out EntityUid hazard,
        out TransformComponent hazardXform,
        out float distance)
    {
        hazard = EntityUid.Invalid;
        hazardXform = default!;
        distance = float.MaxValue;

        var origin = _transform.ToMapCoordinates(xform.Coordinates);

        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            radius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (candidate == uid ||
                !IsEnvironmentalHazardCandidate(candidate) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            var candidateDistance = (candidateMap.Position - origin.Position).Length();
            if (candidateDistance >= distance)
                continue;

            hazard = candidate;
            hazardXform = candidateXform;
            distance = candidateDistance;
        }

        return hazard != EntityUid.Invalid;
    }

    private bool IsEnvironmentalHazardCandidate(EntityUid candidate)
    {
        if (!TryComp(candidate, out TransformComponent? xform) ||
            !xform.Anchored ||
            xform.MapID == MapId.Nullspace)
        {
            return false;
        }

        if (HasComp<DamageContactsComponent>(candidate))
            return true;

        if (HasComp<SlipperyComponent>(candidate))
            return true;

        return TryComp(candidate, out SpeedModifierContactsComponent? slow) &&
               (slow.WalkSpeedModifier <= 0.35f || slow.SprintSpeedModifier <= 0.35f);
    }

    private bool TryBuildHazardDetour(
        EntityUid uid,
        HTNComponent htn,
        TransformComponent xform,
        EntityUid hazard,
        EntityCoordinates hazardCoordinates,
        out EntityCoordinates detourCoordinates)
    {
        detourCoordinates = EntityCoordinates.Invalid;

        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        var hazardMap = _transform.ToMapCoordinates(hazardCoordinates);
        if (npcMap.MapId == MapId.Nullspace || npcMap.MapId != hazardMap.MapId)
            return false;

        if (!TryGetHazardTravelDirection(uid, htn, xform, out var travelDirection))
            travelDirection = hazardMap.Position - npcMap.Position;

        if (travelDirection.LengthSquared() < 0.01f)
        {
            var seed = (uid.GetHashCode() & int.MaxValue) % 8;
            var angle = MathF.Tau * seed / 8f;
            travelDirection = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        travelDirection = Vector2.Normalize(travelDirection);
        var lateralDirection = new Vector2(-travelDirection.Y, travelDirection.X);
        var hazardCenter = hazardMap.Position;
        var boundsMin = hazardCenter;
        var boundsMax = hazardCenter;

        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            MathF.Max(2.75f, _hazardScanRadius * 0.75f),
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Sensors | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (candidate == uid ||
                !IsEnvironmentalHazardCandidate(candidate) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != npcMap.MapId ||
                (candidateMap.Position - hazardCenter).Length() > 2.35f)
            {
                continue;
            }

            boundsMin = Vector2.Min(boundsMin, candidateMap.Position);
            boundsMax = Vector2.Max(boundsMax, candidateMap.Position);
        }

        var clusterCenter = (boundsMin + boundsMax) * 0.5f;
        var clusterSize = boundsMax - boundsMin;
        var lateralExtent =
            0.5f * (MathF.Abs(clusterSize.X * lateralDirection.X) + MathF.Abs(clusterSize.Y * lateralDirection.Y));
        var forwardExtent =
            0.5f * (MathF.Abs(clusterSize.X * travelDirection.X) + MathF.Abs(clusterSize.Y * travelDirection.Y));

        var preferredSide = Vector2.Dot(npcMap.Position - clusterCenter, lateralDirection);
        var preferredSign =
            MathF.Abs(preferredSide) > 0.2f
                ? MathF.Sign(preferredSide)
                : (((hazard.GetHashCode() ^ uid.GetHashCode()) & 1) == 0 ? 1f : -1f);

        var clearanceBase = MathF.Max(1.65f, lateralExtent + 1.45f);
        var leadBase = MathF.Max(1.10f, forwardExtent + 0.90f);
        Span<float> signOrder = stackalloc float[2] { preferredSign, -preferredSign };

        foreach (var sign in signOrder)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var clearance = clearanceBase + attempt * 1.1f;
                var lead = leadBase + attempt * 0.65f;
                var detourWorld = clusterCenter + lateralDirection * sign * clearance + travelDirection * lead;
                if (TryMapPositionToCoordinates(npcMap.MapId, detourWorld, out detourCoordinates))
                    return true;
            }
        }

        var away = npcMap.Position - clusterCenter;
        if (away.LengthSquared() < 0.01f)
            away = lateralDirection * preferredSign;
        else
            away = Vector2.Normalize(away);

        var fallbackWorld = npcMap.Position + away * 2.2f + lateralDirection * preferredSign * 1.8f;
        return TryMapPositionToCoordinates(npcMap.MapId, fallbackWorld, out detourCoordinates);
    }

    private bool TryMapPositionToCoordinates(MapId mapId, Vector2 worldPosition, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        var mapCoordinates = new MapCoordinates(worldPosition, mapId);
        if (!_mapManager.TryFindGridAt(mapCoordinates, out var gridUid, out var grid))
            return false;

        coordinates = new EntityCoordinates(gridUid, _map.WorldToLocal(gridUid, grid, worldPosition));
        return true;
    }

    private bool UpdateObjectiveMotionStallState(
        EntityUid uid,
        TransformComponent xform,
        WaveRuntimeState state,
        float objectiveDistance,
        float objectiveEngageRadius)
    {
        var npcMap = _transform.ToMapCoordinates(xform.Coordinates);
        if (npcMap.MapId == MapId.Nullspace)
        {
            state.ObjectiveLastMapId = MapId.Nullspace;
            state.ObjectiveMotionStallSamples = 0;
            return false;
        }

        if (state.ObjectiveLastMapId != npcMap.MapId)
        {
            state.ObjectiveLastMapId = npcMap.MapId;
            state.ObjectiveLastMapPosition = npcMap.Position;
            state.ObjectiveMotionStallSamples = 0;
            return false;
        }

        var movedDistance = (npcMap.Position - state.ObjectiveLastMapPosition).Length();
        state.ObjectiveLastMapPosition = npcMap.Position;

        if (objectiveDistance <= objectiveEngageRadius + 3f ||
            state.HazardAvoidUntil > _timing.CurTime ||
            !TryComp(uid, out NPCSteeringComponent? steering) ||
            steering.Status != SteeringStatus.Moving ||
            (steering.CurrentPath.Count == 0 && !steering.Coordinates.IsValid(EntityManager)))
        {
            state.ObjectiveMotionStallSamples = 0;
            return false;
        }

        if (movedDistance > 0.18f)
        {
            state.ObjectiveMotionStallSamples = 0;
            return false;
        }

        state.ObjectiveMotionStallSamples++;
        return state.ObjectiveMotionStallSamples >= 5;
    }

    private int GetObjectiveRoutePressure(EntityUid uid, string teamId, WaveRuntimeState state)
    {
        var pressure = GetObjectiveAdaptiveRoutePressure(state);

        if (_teamDirectorStates.TryGetValue(teamId, out var directorState) &&
            directorState.RallyLeader != EntityUid.Invalid &&
            directorState.RallyLeader != uid &&
            _states.TryGetValue(directorState.RallyLeader, out var leaderState))
        {
            pressure = Math.Max(pressure, GetObjectiveAdaptiveRoutePressure(leaderState));
        }

        return pressure;
    }

    private static int GetObjectiveAdaptiveRoutePressure(WaveRuntimeState state)
    {
        if (state.ObjectiveMotionStallSamples < 3)
            return state.ObjectiveNoPathStreak;

        return state.ObjectiveNoPathStreak + Math.Min(4, state.ObjectiveMotionStallSamples - 2);
    }

    private int CountNearbyWaveAllies(EntityUid uid, string teamId, TransformComponent xform, float radius)
    {
        var count = 0;
        var origin = _transform.ToMapCoordinates(xform.Coordinates);

        _lookupBuffer.Clear();
        _lookup.GetEntitiesInRange(
            uid,
            radius,
            _lookupBuffer,
            LookupFlags.Dynamic | LookupFlags.Approximate);

        foreach (var candidate in _lookupBuffer)
        {
            if (candidate == uid ||
                !TryComp(candidate, out HTNComponent? candidateHtn) ||
                !IsWaveRole(candidateHtn) ||
                !TryResolveNpcTeamId(candidate, out var candidateTeamId) ||
                !string.Equals(candidateTeamId, teamId, StringComparison.OrdinalIgnoreCase) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            var candidateMap = _transform.ToMapCoordinates(candidateXform.Coordinates);
            if (candidateMap.MapId != origin.MapId)
                continue;

            count++;
        }

        return count;
    }

    private bool TryGetActiveAggressiveWeather(MapId mapId, out WeatherStatusEffectComponent weatherProto)
    {
        weatherProto = default!;

        if (!_map.TryGetMap(mapId, out var mapUid) ||
            !_weather.TryGetWeatherEffects(mapUid, out var weatherEffects))
        {
            return false;
        }

        foreach (var (uid, weather, status) in weatherEffects)
        {
            var protoId = Prototype(uid)?.ID;
            if (protoId == null || !_aggressiveWeatherIds.Contains(protoId))
                continue;

            if (_weather.IsWeatherEnding((uid, status)))
                continue;

            weatherProto = weather;
            return true;
        }

        return false;
    }

    private bool TryFindNearestRoofedCoordinates(TransformComponent xform, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        if (xform.GridUid == null ||
            !TryComp<MapGridComponent>(xform.GridUid, out var grid))
        {
            return false;
        }

        var gridUid = xform.GridUid.Value;
        var center = _map.LocalToTile(gridUid, grid, xform.Coordinates);

        Vector2i? best = null;
        var bestDistance = int.MaxValue;

        for (var dx = -_shelterSearchRadiusTiles; dx <= _shelterSearchRadiusTiles; dx++)
        {
            for (var dy = -_shelterSearchRadiusTiles; dy <= _shelterSearchRadiusTiles; dy++)
            {
                var candidate = new Vector2i(center.X + dx, center.Y + dy);

                if (!_map.TryGetTileRef(gridUid, grid, candidate, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue;

                if (!IsRoovedTile(gridUid, grid, candidate))
                    continue;

                var distance = dx * dx + dy * dy;
                if (distance >= bestDistance)
                    continue;

                best = candidate;
                bestDistance = distance;
            }
        }

        if (best == null)
            return false;

        coordinates = new EntityCoordinates(gridUid, best.Value + grid.TileSizeHalfVector);
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

    private EntityCoordinates TryGetReturnCoordinates(EntityUid uid, TransformComponent xform)
    {
        if (TryComp(uid, out NPCSteeringComponent? steering) &&
            steering.Coordinates.IsValid(EntityManager))
        {
            return steering.Coordinates;
        }

        return xform.Coordinates;
    }

    private void PruneHazardMemory(WaveRuntimeState state, TimeSpan now)
    {
        if (state.HazardMemory.Count == 0)
            return;

        state.HazardPruneBuffer.Clear();
        foreach (var (mine, expiresAt) in state.HazardMemory)
        {
            if (expiresAt <= now || Deleted(mine))
                state.HazardPruneBuffer.Add(mine);
        }

        foreach (var mine in state.HazardPruneBuffer)
        {
            state.HazardMemory.Remove(mine);
        }
    }

    private sealed class WaveRuntimeState
    {
        public TimeSpan NextUpdateTime = TimeSpan.Zero;
        public TimeSpan NextHazardScanTime = TimeSpan.Zero;
        public TimeSpan NextLoadoutScanTime = TimeSpan.Zero;
        public TimeSpan NextWeatherScanTime = TimeSpan.Zero;
        public TimeSpan NextServiceScanTime = TimeSpan.Zero;
        public TimeSpan NextDeployScanTime = TimeSpan.Zero;
        public TimeSpan NextInfluenceScanTime = TimeSpan.Zero;
        public TimeSpan NextObjectiveScanTime = TimeSpan.Zero;
        public TimeSpan NextCommsScanTime = TimeSpan.Zero;

        public TimeSpan? LoadoutStartTime;
        public bool LoadoutTimeoutReported;

        public readonly Dictionary<EntityUid, TimeSpan> HazardMemory = new();
        public readonly List<EntityUid> HazardPruneBuffer = new();
        public TimeSpan HazardAvoidUntil = TimeSpan.Zero;
        public EntityCoordinates HazardAvoidCoordinates = EntityCoordinates.Invalid;
        public EntityUid HazardFocus = EntityUid.Invalid;

        public bool ShelterActive;
        public TimeSpan ShelterEnteredAt = TimeSpan.Zero;
        public EntityCoordinates ShelterCoordinates = EntityCoordinates.Invalid;
        public EntityCoordinates ShelterReturnCoordinates = EntityCoordinates.Invalid;
        public TimeSpan ShelterCooldownUntil = TimeSpan.Zero;

        public bool ServiceJobActive;
        public TimeSpan ServiceStartedAt = TimeSpan.Zero;
        public EntityUid ServiceMachine = EntityUid.Invalid;
        public EntityUid ServiceSourceItem = EntityUid.Invalid;
        public EntityUid ServiceSourceStorage = EntityUid.Invalid;
        public bool ServiceRestockPending;
        public EntityUid ServicePendingRestockItem = EntityUid.Invalid;
        public TimeSpan ServicePendingRestockUntil = TimeSpan.Zero;

        public bool DeployJobActive;
        public TimeSpan DeployStartedAt = TimeSpan.Zero;
        public TimeSpan DeployResolveAt = TimeSpan.Zero;
        public EntityUid DeployItem = EntityUid.Invalid;
        public EntityCoordinates DeployCoordinates = EntityCoordinates.Invalid;
        public Direction DeployDirection = Direction.North;
        public int DeployCompletedCount;

        public EntityUid InfluenceTargetPoint = EntityUid.Invalid;
        public EntityUid ObjectiveTarget = EntityUid.Invalid;
        public bool ObjectiveNoPathActive;
        public int ObjectiveNoPathStreak;
        public MapId ObjectiveLastMapId = MapId.Nullspace;
        public Vector2 ObjectiveLastMapPosition = Vector2.Zero;
        public int ObjectiveMotionStallSamples;
        public int ObjectivePathRequestsThisTick;
        public int ObjectivePathQueueDepthPeak;
        public WaveDirectorOrder DirectorOrder = WaveDirectorOrder.None;

        public void ResetService()
        {
            ServiceJobActive = false;
            ServiceStartedAt = TimeSpan.Zero;
            ServiceMachine = EntityUid.Invalid;
            ServiceSourceItem = EntityUid.Invalid;
            ServiceSourceStorage = EntityUid.Invalid;
            ServiceRestockPending = false;
            ServicePendingRestockItem = EntityUid.Invalid;
            ServicePendingRestockUntil = TimeSpan.Zero;
        }

        public void ResetDeploy()
        {
            DeployJobActive = false;
            DeployStartedAt = TimeSpan.Zero;
            DeployResolveAt = TimeSpan.Zero;
            DeployItem = EntityUid.Invalid;
            DeployCoordinates = EntityCoordinates.Invalid;
            DeployDirection = Direction.North;
        }
    }

    private sealed class ServiceReservationState
    {
        public EntityUid Owner;
        public TimeSpan ExpiresAt;
    }

    private sealed class TeamDirectorState
    {
        public TimeSpan NextEvaluateTime = TimeSpan.Zero;
        public TimeSpan OrderExpiresAt = TimeSpan.Zero;
        public TimeSpan LastOrderSwitchAt = TimeSpan.Zero;
        public TimeSpan LastPreemptAt = TimeSpan.Zero;
        public TimeSpan BaseThreatHoldUntil = TimeSpan.Zero;
        public WaveDirectorOrder ActiveOrder = WaveDirectorOrder.None;
        public float ActiveOrderScore;

        public bool HasLogistics;
        public bool HasBreacher;
        public bool SupplyShortage;
        public bool HasEnemyObjective;
        public bool HasInfluenceOpportunity;
        public bool BaseUnderThreat;
        public MapId TeamMapId = MapId.Nullspace;
        public Vector2 TeamCenter = Vector2.Zero;
        public float TeamSpreadRadius;
        public int TeamMemberCount;
        public EntityUid RallyLeader = EntityUid.Invalid;

        public EntityUid EnemyObjectiveTarget = EntityUid.Invalid;
        public EntityUid InfluenceTarget = EntityUid.Invalid;
        public EntityUid DefenseThreatTarget = EntityUid.Invalid;
        public EntityUid FriendlyObjectiveAnchor = EntityUid.Invalid;
    }

    private struct TeamDirectorSnapshot
    {
        public MapId TeamMapId;
        public Vector2 Center;
        public float SpreadRadius;
        public bool HasLogistics;
        public bool HasBreacher;
        public bool SupplyShortage;
        public bool HasEnemyObjective;
        public bool HasInfluenceOpportunity;
        public bool BaseUnderThreat;
        public int MemberCount;
        public EntityUid RallyLeader;
        public EntityUid EnemyObjectiveTarget;
        public EntityUid InfluenceTarget;
        public EntityUid DefenseThreatTarget;
        public EntityUid FriendlyObjectiveAnchor;
    }

    private enum WaveDirectorOrder : byte
    {
        None = 0,
        DefendBase = 1,
        PushObjective = 2,
        CaptureInfluence = 3,
        Resupply = 4,
        BreachLane = 5,
        Regroup = 6,
    }

    private enum WaveDirectorRole : byte
    {
        Unknown = 0,
        Assault = 1,
        Breacher = 2,
        Sapper = 3,
        Support = 4,
        Logistics = 5,
        Coordinator = 6,
    }

    private readonly struct ServiceMachineCandidate
    {
        public readonly EntityUid Machine;
        public readonly VendingMachineComponent MachineComp;
        public readonly float Distance;

        public ServiceMachineCandidate(EntityUid machine, VendingMachineComponent machineComp, float distance)
        {
            Machine = machine;
            MachineComp = machineComp;
            Distance = distance;
        }
    }
}
