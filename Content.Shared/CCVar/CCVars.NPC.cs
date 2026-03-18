using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<int> NPCMaxUpdates =
        CVarDef.Create("npc.max_updates", 128);

    public static readonly CVarDef<bool> NPCEnabled = CVarDef.Create("npc.enabled", true);

    /// <summary>
    ///     Should NPCs pathfind when steering. For debug purposes.
    /// </summary>
    public static readonly CVarDef<bool> NPCPathfinding = CVarDef.Create("npc.pathfinding", true);

    /// <summary>
    ///     Enables short-lived route-cache for A* requests (chunk-bucket start + exact end tile).
    /// </summary>
    public static readonly CVarDef<bool> NPCPathRouteCacheEnabled =
        CVarDef.Create("npc.path_route_cache_enabled", true);

    /// <summary>
    ///     TTL for successful cached routes (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCPathRouteCacheTtlSeconds =
        CVarDef.Create("npc.path_route_cache_ttl_seconds", 0.80f);

    /// <summary>
    ///     TTL for cached no-path outcomes (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCPathRouteCacheNoPathTtlSeconds =
        CVarDef.Create("npc.path_route_cache_nopath_ttl_seconds", 0.20f);

    /// <summary>
    ///     Max amount of cached route entries.
    /// </summary>
    public static readonly CVarDef<int> NPCPathRouteCacheMaxEntries =
        CVarDef.Create("npc.path_route_cache_max_entries", 1024);

    /// <summary>
    ///     Minimum delay between steering path requests per NPC (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCSteeringPathRequestCooldownSeconds =
        CVarDef.Create("npc.steering_path_request_cooldown_seconds", 0.10f);

    /// <summary>
    ///     Base additional retry delay after a no-path result (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCSteeringPathNoPathBackoffSeconds =
        CVarDef.Create("npc.steering_path_no_path_backoff_seconds", 0.35f);

    /// <summary>
    ///     Maximum retry backoff after repeated no-path results (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCSteeringPathMaxBackoffSeconds =
        CVarDef.Create("npc.steering_path_max_backoff_seconds", 1.50f);

    /// <summary>
    ///     Time window in seconds used to reset obstacle-failure retry counters.
    /// </summary>
    public static readonly CVarDef<float> NPCSteeringObstacleFailureResetSeconds =
        CVarDef.Create("npc.steering_obstacle_failure_reset_seconds", 2.0f);

    /// <summary>
    ///     Max consecutive obstacle failures before steering gives up with NoPath.
    /// </summary>
    public static readonly CVarDef<int> NPCSteeringObstacleRetryLimit =
        CVarDef.Create("npc.steering_obstacle_retry_limit", 3);

    /// <summary>
    ///     Strength of lateral lane-rotation nudge after an obstacle failure.
    /// </summary>
    public static readonly CVarDef<float> NPCSteeringObstacleLaneRotateWeight =
        CVarDef.Create("npc.steering_obstacle_lane_rotate_weight", 0.65f);

    /// <summary>
    ///     Utility query cache lifetime (seconds). 0 disables cache.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilityCacheTtlSeconds =
        CVarDef.Create("npc.utility_cache_ttl_seconds", 0.20f);

    /// <summary>
    ///     Enables spatial-temporal cache for utility component queries.
    /// </summary>
    public static readonly CVarDef<bool> NPCUtilitySpatialCacheEnabled =
        CVarDef.Create("npc.utility_spatial_cache_enabled", true);

    /// <summary>
    ///     Lifetime of utility spatial cache entries in seconds.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilitySpatialCacheTtlSeconds =
        CVarDef.Create("npc.utility_spatial_cache_ttl_seconds", 0.12f);

    /// <summary>
    ///     Spatial utility cache cell size in world units.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilitySpatialCacheCellSize =
        CVarDef.Create("npc.utility_spatial_cache_cell_size", 6f);

    /// <summary>
    ///     Lifetime of nearby-hostiles subquery cache entries in seconds.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilityHostilesCacheTtlSeconds =
        CVarDef.Create("npc.utility_hostiles_cache_ttl_seconds", 0.00f);

    /// <summary>
    ///     Enables utility LOS cache for target considerations.
    /// </summary>
    public static readonly CVarDef<bool> NPCUtilityLosCacheEnabled =
        CVarDef.Create("npc.utility_los_cache_enabled", true);

    /// <summary>
    ///     Utility LOS cache lifetime in seconds.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilityLosCacheTtlSeconds =
        CVarDef.Create("npc.utility_los_cache_ttl_seconds", 0.15f);

    /// <summary>
    ///     Max movement before LOS cache entry is invalidated.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilityLosCacheMoveThreshold =
        CVarDef.Create("npc.utility_los_cache_move_threshold", 0.75f);

    /// <summary>
    ///     Soft cap of fresh LOS traces per tick for utility considerations.
    /// </summary>
    public static readonly CVarDef<int> NPCUtilityLosBudgetPerTick =
        CVarDef.Create("npc.utility_los_budget_per_tick", 512);

    /// <summary>
    ///     Enables squad shared-target coordination for wave combat utility queries.
    /// </summary>
    public static readonly CVarDef<bool> NPCUtilityWaveCoordinationEnabled =
        CVarDef.Create("npc.utility_wave_coordination_enabled", true);

    /// <summary>
    ///     Lifetime of shared squad target entries in seconds.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilityWaveCoordinationTtlSeconds =
        CVarDef.Create("npc.utility_wave_coordination_ttl_seconds", 1.20f);

    /// <summary>
    ///     Cell size for squad shared-target grouping in world units.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilityWaveCoordinationCellSize =
        CVarDef.Create("npc.utility_wave_coordination_cell_size", 10f);

    /// <summary>
    ///     Score bonus applied to ordered squad target in coordinated combat queries.
    /// </summary>
    public static readonly CVarDef<float> NPCUtilityWaveCoordinationOrderedBonus =
        CVarDef.Create("npc.utility_wave_coordination_ordered_bonus", 0.18f);

    /// <summary>
    ///     Enables adaptive HTN update scheduling based on relevance tiers.
    /// </summary>
    public static readonly CVarDef<bool> NPCAdaptiveSchedulingEnabled =
        CVarDef.Create("npc.adaptive_scheduling_enabled", true);

    /// <summary>
    ///     Radius considered "near players" for full HTN cadence.
    /// </summary>
    public static readonly CVarDef<float> NPCAdaptiveSchedulingNearRange =
        CVarDef.Create("npc.adaptive_scheduling_near_range", 12f);

    /// <summary>
    ///     Radius considered "mid relevance" for medium HTN cadence.
    /// </summary>
    public static readonly CVarDef<float> NPCAdaptiveSchedulingMidRange =
        CVarDef.Create("npc.adaptive_scheduling_mid_range", 28f);

    /// <summary>
    ///     HTN interval for medium relevance entities (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCAdaptiveSchedulingMidIntervalSeconds =
        CVarDef.Create("npc.adaptive_scheduling_mid_interval_seconds", 0.06f);

    /// <summary>
    ///     HTN interval for far/idle entities (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCAdaptiveSchedulingFarIntervalSeconds =
        CVarDef.Create("npc.adaptive_scheduling_far_interval_seconds", 0.16f);

    /// <summary>
    ///     Extra jitter added to adaptive HTN intervals (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCAdaptiveSchedulingJitterSeconds =
        CVarDef.Create("npc.adaptive_scheduling_jitter_seconds", 0.03f);

    /// <summary>
    ///     Action-on-target attempt interval while NPC has target (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCActionOnTargetIntervalSeconds =
        CVarDef.Create("npc.action_on_target_interval_seconds", 0.10f);

    /// <summary>
    ///     Action-on-target attempt interval while NPC has no target (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCActionOnTargetIdleIntervalSeconds =
        CVarDef.Create("npc.action_on_target_idle_interval_seconds", 0.25f);

    /// <summary>
    ///     Extra jitter for action-on-target cadence (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCActionOnTargetJitterSeconds =
        CVarDef.Create("npc.action_on_target_jitter_seconds", 0.02f);

    /// <summary>
    ///     Enables wave-defense capability layer (hazard/loadout/weather/service/deploy/influence).
    /// </summary>
    public static readonly CVarDef<bool> NPCWaveCapabilityEnabled =
        CVarDef.Create("npc.wave_capability_enabled", true);

    /// <summary>
    ///     Base update interval for wave-defense capability layer (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityUpdateIntervalSeconds =
        CVarDef.Create("npc.wave_capability_update_interval_seconds", 0.22f);

    /// <summary>
    ///     Hazard scan cadence for wave-defense capability layer (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityHazardScanIntervalSeconds =
        CVarDef.Create("npc.wave_capability_hazard_scan_interval_seconds", 0.28f);

    /// <summary>
    ///     Hazard scan radius in world units.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityHazardScanRadius =
        CVarDef.Create("npc.wave_capability_hazard_scan_radius", 2.3f);

    /// <summary>
    ///     Short-TTL hazard memory for per-NPC mine awareness (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityHazardMemoryTtlSeconds =
        CVarDef.Create("npc.wave_capability_hazard_memory_ttl_seconds", 2.5f);

    /// <summary>
    ///     Loadout acquire/equip scan cadence (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityLoadoutScanIntervalSeconds =
        CVarDef.Create("npc.wave_capability_loadout_scan_interval_seconds", 0.24f);

    /// <summary>
    ///     Loadout search radius in world units.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityLoadoutSearchRadius =
        CVarDef.Create("npc.wave_capability_loadout_search_radius", 8f);

    /// <summary>
    ///     Max allowed acquire/equip readiness budget before timeout telemetry (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityLoadoutReadyTimeoutSeconds =
        CVarDef.Create("npc.wave_capability_loadout_ready_timeout_seconds", 8f);

    /// <summary>
    ///     Weather shelter logic cadence (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityWeatherScanIntervalSeconds =
        CVarDef.Create("npc.wave_capability_weather_scan_interval_seconds", 0.30f);

    /// <summary>
    ///     Shelter search radius in tiles around NPC.
    /// </summary>
    public static readonly CVarDef<int> NPCWaveCapabilityShelterSearchRadiusTiles =
        CVarDef.Create("npc.wave_capability_shelter_search_radius_tiles", 12);

    /// <summary>
    ///     Time to remain in shelter before forced re-entry evaluation (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityShelterTimeoutSeconds =
        CVarDef.Create("npc.wave_capability_shelter_timeout_seconds", 3.5f);

    /// <summary>
    ///     Cooldown between shelter exit and next shelter re-entry pivot (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityShelterReentryCooldownSeconds =
        CVarDef.Create("npc.wave_capability_shelter_reentry_cooldown_seconds", 1.6f);

    /// <summary>
    ///     Service logistics scan cadence (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityServiceScanIntervalSeconds =
        CVarDef.Create("npc.wave_capability_service_scan_interval_seconds", 0.24f);

    /// <summary>
    ///     Service logistics search radius in world units.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityServiceSearchRadius =
        CVarDef.Create("npc.wave_capability_service_search_radius", 12f);

    /// <summary>
    ///     Reservation TTL for vending-machine service jobs (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityServiceReservationTtlSeconds =
        CVarDef.Create("npc.wave_capability_service_reservation_ttl_seconds", 9f);

    /// <summary>
    ///     Max service job lifetime before timeout abort telemetry (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityServiceJobTimeoutSeconds =
        CVarDef.Create("npc.wave_capability_service_job_timeout_seconds", 18f);

    /// <summary>
    ///     Engineering/deploy scan cadence (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityDeployScanIntervalSeconds =
        CVarDef.Create("npc.wave_capability_deploy_scan_interval_seconds", 0.30f);

    /// <summary>
    ///     Engineering/deploy search radius in world units.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityDeploySearchRadius =
        CVarDef.Create("npc.wave_capability_deploy_search_radius", 10f);

    /// <summary>
    ///     Max deploy job lifetime before timeout abort telemetry (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityDeployJobTimeoutSeconds =
        CVarDef.Create("npc.wave_capability_deploy_job_timeout_seconds", 14f);

    /// <summary>
    ///     Per-NPC cap of successful deploy completions in wave mode.
    /// </summary>
    public static readonly CVarDef<int> NPCWaveCapabilityDeployMaxPerNpc =
        CVarDef.Create("npc.wave_capability_deploy_max_per_npc", 1);

    /// <summary>
    ///     Influence-point behavior scan cadence (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityInfluenceScanIntervalSeconds =
        CVarDef.Create("npc.wave_capability_influence_scan_interval_seconds", 0.30f);

    /// <summary>
    ///     Influence-point search radius in world units.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityInfluenceSearchRadius =
        CVarDef.Create("npc.wave_capability_influence_search_radius", 18f);

    /// <summary>
    ///     Fraction of capture radius at which NPC considers the point held.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityInfluenceHoldRadiusFactor =
        CVarDef.Create("npc.wave_capability_influence_hold_radius_factor", 0.65f);

    /// <summary>
    ///     Objective-assault behavior scan cadence (seconds).
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityObjectiveScanIntervalSeconds =
        CVarDef.Create("npc.wave_capability_objective_scan_interval_seconds", 0.32f);

    /// <summary>
    ///     Enemy objective search radius in world units.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityObjectiveSearchRadius =
        CVarDef.Create("npc.wave_capability_objective_search_radius", 24f);

    /// <summary>
    ///     Fraction of objective interaction range used as hold/engage radius.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveCapabilityObjectiveHoldRadiusFactor =
        CVarDef.Create("npc.wave_capability_objective_hold_radius_factor", 0.65f);

    /// <summary>
    ///     No-path retry count before objective push enters fallback state.
    /// </summary>
    public static readonly CVarDef<int> NPCWaveCapabilityObjectiveNoPathFallbackRetries =
        CVarDef.Create("npc.wave_capability_objective_nopath_fallback_retries", 3);

    /// <summary>
    ///     No-path retry count before objective push is marked unreachable.
    /// </summary>
    public static readonly CVarDef<int> NPCWaveCapabilityObjectiveNoPathUnreachableRetries =
        CVarDef.Create("npc.wave_capability_objective_nopath_unreachable_retries", 6);

    /// <summary>
    ///     Enables team-level collective director order layer for wave NPCs.
    /// </summary>
    public static readonly CVarDef<bool> NPCWaveDirectorEnabled =
        CVarDef.Create("npc.wave_director_enabled", true);

    /// <summary>
    ///     Team director decision cadence in seconds.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveDirectorTickIntervalSeconds =
        CVarDef.Create("npc.wave_director_tick_interval_seconds", 0.35f);

    /// <summary>
    ///     Minimum time a team order is held before non-urgent reassignment.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveDirectorOrderTtlSeconds =
        CVarDef.Create("npc.wave_director_order_ttl_seconds", 1.8f);

    /// <summary>
    ///     Score delta required to switch from current order in non-urgent state.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveDirectorHysteresisScoreDelta =
        CVarDef.Create("npc.wave_director_hysteresis_score_delta", 9f);

    /// <summary>
    ///     Cooldown between non-urgent team order reassignments.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveDirectorReassignCooldownSeconds =
        CVarDef.Create("npc.wave_director_reassign_cooldown_seconds", 1.2f);

    /// <summary>
    ///     Cooldown between urgent preempt order switches.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveDirectorUrgentPreemptCooldownSeconds =
        CVarDef.Create("npc.wave_director_urgent_preempt_cooldown_seconds", 0.8f);

    /// <summary>
    ///     Threat radius around friendly objective used by director defense preempt.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveDirectorDefenseThreatRadius =
        CVarDef.Create("npc.wave_director_defense_threat_radius", 10f);

    /// <summary>
    ///     Time window in seconds to keep defend-base pressure after the last confirmed base threat.
    /// </summary>
    public static readonly CVarDef<float> NPCWaveDirectorDefenseThreatMemorySeconds =
        CVarDef.Create("npc.wave_director_defense_threat_memory_seconds", 10f);

    /// <summary>
    ///     Number of restock-needing machines treated as shortage by director.
    /// </summary>
    public static readonly CVarDef<int> NPCWaveDirectorResupplyShortageThreshold =
        CVarDef.Create("npc.wave_director_resupply_shortage_threshold", 1);

    /// <summary>
    ///     Enables detailed NPC benchmark instrumentation and periodic server logs.
    /// </summary>
    public static readonly CVarDef<bool> NPCBenchmarkEnabled = CVarDef.Create("npc.benchmark_enabled", false);

    /// <summary>
    ///     Interval for periodic NPC benchmark report logs in seconds.
    /// </summary>
    public static readonly CVarDef<float> NPCBenchmarkLogIntervalSeconds =
        CVarDef.Create("npc.benchmark_log_interval_seconds", 5f);

    /// <summary>
    ///     Enables high-cardinality benchmark points (for example utility query prototype ids).
    /// </summary>
    public static readonly CVarDef<bool> NPCBenchmarkDetailed =
        CVarDef.Create("npc.benchmark_detailed", false);
}
