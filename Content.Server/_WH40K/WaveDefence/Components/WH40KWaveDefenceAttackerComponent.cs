using Content.Server._WH40K.GameTicking.Rules;
using Content.Server.NPC.Pathfinding;
using Content.Server._WH40K.WaveDefence.HTN.Operators;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.WaveDefence.Components;

[RegisterComponent, Access(typeof(WH40KWaveDefenceRuleSystem), typeof(WH40KWaveDefencePickLaneTargetOperator), typeof(WH40KWaveDefencePickObjectiveOperator), typeof(WH40KWaveDefencePickPlayerTargetOperator), typeof(WH40KWaveDefenceAISystem), typeof(WH40KWaveDefenceLocomotionSystem), typeof(WH40KWaveDefenceAiDebugOverlaySystem))]
public sealed partial class WH40KWaveDefenceAttackerComponent : Component
{
    [ViewVariables]
    public string LaneId = string.Empty;

    [ViewVariables]
    public string HomeLaneId = string.Empty;

    [ViewVariables]
    public List<string> CandidateLaneIds = new();

    [ViewVariables]
    public List<EntityUid> LanePoints = new();

    [ViewVariables]
    public int LanePointIndex;

    [ViewVariables]
    public int LastReachedLanePointIndex = -1;

    [ViewVariables]
    public int FurthestReachedLanePointIndex = -1;

    [ViewVariables]
    public int LastFallbackAnchorIndex = -1;

    [ViewVariables]
    public int TotalLanePointCount;

    [ViewVariables]
    public float RouteProgressRatio;

    [ViewVariables]
    public bool RouteCompleted;

    [ViewVariables]
    public EntityCoordinates RouteStartCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public float CurrentRouteProgressRatio;

    [ViewVariables]
    public float SharedLaneFrontProgress;

    [ViewVariables]
    public int SwarmBandIndex;

    [ViewVariables]
    public EntityCoordinates ActiveRouteTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public string ActiveRouteTargetLabel = string.Empty;

    [ViewVariables]
    public EntityUid? Objective;

    [ViewVariables]
    public WH40KWaveDefenceLocomotionMode LocomotionMode;

    [ViewVariables]
    public EntityCoordinates LocomotionTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public string LocomotionTargetLabel = string.Empty;

    [ViewVariables]
    public EntityCoordinates StickyObjectiveTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public TimeSpan StickyObjectiveTargetUntil = TimeSpan.Zero;

    [ViewVariables]
    public EntityCoordinates StrategicRouteTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public string StrategicRouteTargetLabel = string.Empty;

    [ViewVariables]
    public int StrategicRouteTopologyVersion;

    [ViewVariables]
    public int CommittedRouteTopologyVersion;

    [ViewVariables]
    public int LastEvaluatedRouteTopologyVersion;

    [ViewVariables]
    public bool HasCommittedRoute;

    [ViewVariables]
    public int CommittedRouteCursor;

    [ViewVariables]
    public float CommittedRouteCost;

    [ViewVariables]
    public float CommittedRouteRemainingCost;

    [ViewVariables]
    public List<MapCoordinates> CommittedRoutePoints = new();

    [ViewVariables]
    public List<float> CommittedRouteCumulativeCosts = new();

    [ViewVariables]
    public HashSet<PathPolyKey> CommittedRoutePolyKeys = new();

    [ViewVariables]
    public bool HasShadowRoute;

    [ViewVariables]
    public float ShadowRouteCost;

    [ViewVariables]
    public int ShadowRouteTopologyVersion;

    [ViewVariables]
    public List<MapCoordinates> ShadowRoutePoints = new();

    [ViewVariables]
    public List<float> ShadowRouteCumulativeCosts = new();

    [ViewVariables]
    public HashSet<PathPolyKey> ShadowRouteAvoidPolys = new();

    [ViewVariables]
    public int ShadowRouteAvoidTopologyVersion;

    [ViewVariables]
    public int ShadowRouteClearanceRetryCount;

    [ViewVariables]
    public string RouteMindDecision = "idle";

    [ViewVariables]
    public TimeSpan RouteCommitUntil = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan RouteSwitchCooldownUntil = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextShadowRouteThinkAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastCommittedRouteAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastShadowRouteAt = TimeSpan.Zero;

    [ViewVariables]
    public float PointArrivalRange = 1.2f;

    [ViewVariables]
    public string? RootTaskOverride;

    [ViewVariables]
    public WH40KWaveSquadRole Role = WH40KWaveSquadRole.Soldier;

    [ViewVariables]
    public WH40KWaveAiProfile AiProfile = WH40KWaveAiProfile.SimpleSwarm;

    [ViewVariables]
    public float LaneCommitSeconds = 14f;

    [ViewVariables]
    public float StallSeconds = 8f;

    [ViewVariables]
    public float CombatStallSeconds = 14f;

    [ViewVariables]
    public float RecoveryCooldownSeconds = 4f;

    [ViewVariables]
    public float FallbackCommitSeconds = 2.5f;

    [ViewVariables]
    public float FallbackStallSeconds = 9f;

    [ViewVariables]
    public float PursuitLeashDistance = 7f;

    [ViewVariables]
    public float CombatDisengageCommitSeconds = 3f;

    [ViewVariables]
    public float VisionRadius = 12f;

    [ViewVariables]
    public float AggroVisionRadius = 16f;

    [ViewVariables]
    public float PlayerMemorySeconds = 5f;

    [ViewVariables]
    public float InvestigationSearchSeconds = 2.25f;

    [ViewVariables]
    public float InvestigationLeashDistance = 4.25f;

    [ViewVariables]
    public float InvestigationStallSeconds = 1.5f;

    [ViewVariables]
    public float ObjectiveMemorySearchSeconds = 1f;

    [ViewVariables]
    public float ObjectiveMemorySearchDistance = 2.35f;

    [ViewVariables]
    public float ObjectiveRelaySearchSeconds = 0.9f;

    [ViewVariables]
    public float ObjectiveRelaySearchDistance = 4.5f;

    [ViewVariables]
    public float PlayerRelayRadius = 4.75f;

    [ViewVariables]
    public float PlayerRelayMemorySeconds = 2.5f;

    [ViewVariables]
    public float PlayerRelayCooldownSeconds = 0.45f;

    [ViewVariables]
    public float ForcedStrategicPlayerOverrideDistance = 2.75f;

    [ViewVariables]
    public float ForcedStrategicObjectiveGuardDistance = 4.5f;

    [ViewVariables]
    public float GeometryRecoveryCommitSeconds = 2.4f;

    [ViewVariables]
    public float GeometryRecoveryStallSeconds = 1.15f;

    [ViewVariables]
    public float GeometryRecoveryProgressDelta = 0.025f;

    [ViewVariables]
    public float PreparedLanePlanSeconds = 0.9f;

    [ViewVariables]
    public float BodyClearanceRadius = 0.42f;

    [ViewVariables]
    public float BodyClearanceDiameter = 0.84f;

    [ViewVariables]
    public TimeSpan BodyClearanceCachedAt = TimeSpan.Zero;

    [ViewVariables]
    public EntityCoordinates ClearanceDebugTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public EntityCoordinates ClearanceDebugSample = EntityCoordinates.Invalid;

    [ViewVariables]
    public string ClearanceDebugLabel = "none";

    [ViewVariables]
    public string ClearanceDebugReason = "none";

    [ViewVariables]
    public string ClearanceDebugBlockerLabel = string.Empty;

    [ViewVariables]
    public TimeSpan ClearanceDebugUpdatedAt = TimeSpan.Zero;

    [ViewVariables]
    public EntityCoordinates DynamicClearanceDebugTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public EntityCoordinates DynamicClearanceDebugSample = EntityCoordinates.Invalid;

    [ViewVariables]
    public string DynamicClearanceDebugLabel = "none";

    [ViewVariables]
    public string DynamicClearanceDebugReason = "none";

    [ViewVariables]
    public string DynamicClearanceDebugBlockerLabel = string.Empty;

    [ViewVariables]
    public TimeSpan DynamicClearanceDebugUpdatedAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan DynamicOccupancyHoldUntil = TimeSpan.Zero;

    [ViewVariables]
    public EntityUid VisiblePlayer = EntityUid.Invalid;

    [ViewVariables]
    public EntityCoordinates VisiblePlayerCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public TimeSpan VisiblePlayerUntil = TimeSpan.Zero;

    [ViewVariables]
    public EntityUid RememberedPlayer = EntityUid.Invalid;

    [ViewVariables]
    public EntityCoordinates RememberedPlayerCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public TimeSpan RememberedPlayerUntil = TimeSpan.Zero;

    [ViewVariables]
    public WH40KWaveDefencePlayerContactSource RememberedPlayerSource;

    [ViewVariables]
    public TimeSpan RememberedPlayerReceivedAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastPlayerRelayAt = TimeSpan.Zero;

    [ViewVariables]
    public int PerceptionEpoch;

    [ViewVariables]
    public int LastAcceptedPerceptionEpoch;

    [ViewVariables]
    public int PerceptionRequestEpoch;

    [ViewVariables]
    public int PendingPerceptionRequestEpoch;

    [ViewVariables]
    public int LastAppliedPerceptionRequestEpoch;

    [ViewVariables]
    public string PerceptionStateLabel = "none";

    [ViewVariables]
    public WH40KWaveDefencePlayerContactMode PlayerContactMode;

    [ViewVariables]
    public string PlayerContactPolicyLabel = "none";

    [ViewVariables]
    public bool PlayerContactShouldOverrideObjective;

    [ViewVariables]
    public TimeSpan LaneCommitUntil = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastLaneChangeAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastProgressAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextRecoveryAttemptAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextTacticalThinkAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextDeliberationAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastDeliberationAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextLocomotionThinkAt = TimeSpan.Zero;

    [ViewVariables]
    public EntityCoordinates DesiredTargetProposal = EntityCoordinates.Invalid;

    [ViewVariables]
    public string DesiredTargetProposalLabel = string.Empty;

    [ViewVariables]
    public EntityUid CombatFocusTarget = EntityUid.Invalid;

    [ViewVariables]
    public EntityCoordinates CombatFocusCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public string CombatFocusLabel = string.Empty;

    [ViewVariables]
    public EntityUid InvestigationTarget = EntityUid.Invalid;

    [ViewVariables]
    public EntityCoordinates InvestigationCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public string InvestigationLabel = string.Empty;

    [ViewVariables]
    public EntityCoordinates InvestigationAnchorCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public TimeSpan InvestigationAnchorSetAt = TimeSpan.Zero;

    [ViewVariables]
    public float LastInvestigationDistance = float.MaxValue;

    [ViewVariables]
    public TimeSpan LastInvestigationProgressAt = TimeSpan.Zero;

    [ViewVariables]
    public EntityCoordinates MovementTargetDirective = EntityCoordinates.Invalid;

    [ViewVariables]
    public string MovementTargetDirectiveLabel = string.Empty;

    [ViewVariables]
    public EntityCoordinates GeometryRecoveryTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public string GeometryRecoveryLabel = string.Empty;

    [ViewVariables]
    public TimeSpan GeometryRecoveryUntil = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan GeometryRecoveryStartedAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan GeometryRecoveryLastProgressAt = TimeSpan.Zero;

    [ViewVariables]
    public float GeometryRecoveryStartProgress;

    [ViewVariables]
    public float GeometryRecoveryBestDistance = float.MaxValue;

    [ViewVariables]
    public int GeometryRecoveryLanePointIndex = -1;

    [ViewVariables]
    public EntityCoordinates PreparedLaneTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public string PreparedLaneTargetLabel = string.Empty;

    [ViewVariables]
    public EntityCoordinates PreparedLaneAlternateTargetA = EntityCoordinates.Invalid;

    [ViewVariables]
    public string PreparedLaneAlternateLabelA = string.Empty;

    [ViewVariables]
    public EntityCoordinates PreparedLaneAlternateTargetB = EntityCoordinates.Invalid;

    [ViewVariables]
    public string PreparedLaneAlternateLabelB = string.Empty;

    [ViewVariables]
    public TimeSpan PreparedLanePlanUntil = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan PreparedLanePlanBuiltAt = TimeSpan.Zero;

    [ViewVariables]
    public int PreparedLanePointIndex = -1;

    [ViewVariables]
    public float PreparedLanePlanProgress;

    [ViewVariables]
    public List<EntityCoordinates> LocalLaneCorridorPoints = new();

    [ViewVariables]
    public TimeSpan LocalLaneCorridorUntil = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LocalLaneCorridorBuiltAt = TimeSpan.Zero;

    [ViewVariables]
    public int LocalLaneCorridorPointIndex = -1;

    [ViewVariables]
    public TimeSpan LocalLaneCorridorRetryAt = TimeSpan.Zero;

    [ViewVariables]
    public int LocalLaneCorridorRetryPointIndex = -1;

    [ViewVariables]
    public string LocalLaneCorridorRetryLaneId = string.Empty;

    [ViewVariables]
    public int LocalLaneCorridorCursor;

    [ViewVariables]
    public float LocalLaneCorridorGoalProgress;

    [ViewVariables]
    public string LocalLaneCorridorLabel = string.Empty;

    [ViewVariables]
    public int NavigationEpoch;

    [ViewVariables]
    public int LastAcceptedNavigationEpoch;

    [ViewVariables]
    public int NavigationRequestEpoch;

    [ViewVariables]
    public int PendingNavigationRequestEpoch;

    [ViewVariables]
    public int LastAppliedNavigationRequestEpoch;

    [ViewVariables]
    public string NavigationStateLabel = "none";

    [ViewVariables]
    public float BestProgressScore = float.MinValue;

    [ViewVariables]
    public int RecoveryLevel;

    [ViewVariables]
    public int RecoveryAttempts;

    [ViewVariables]
    public int NoPathCount;

    [ViewVariables]
    public int LaneRerouteCount;

    [ViewVariables]
    public int FallbackCount;

    [ViewVariables]
    public bool BaseNavInteract;

    [ViewVariables]
    public bool BaseNavPry;

    [ViewVariables]
    public bool BaseNavSmash;

    [ViewVariables]
    public bool BaseNavClimb;

    [ViewVariables]
    public bool CanInteract;

    [ViewVariables]
    public bool CanPry;

    [ViewVariables]
    public bool CanSmash;

    [ViewVariables]
    public bool CanClimb;

    [ViewVariables]
    public EntityCoordinates ForcedTarget = EntityCoordinates.Invalid;

    [ViewVariables]
    public TimeSpan ForcedTargetUntil = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan ForcedTargetCommitUntil = TimeSpan.Zero;

    [ViewVariables]
    public string ForcedTargetLabel = string.Empty;

    [ViewVariables]
    public WH40KWaveDefenceForcedTargetKind ForcedTargetKind = WH40KWaveDefenceForcedTargetKind.None;

    [ViewVariables]
    public float LastForcedTargetDistance = float.MaxValue;

    [ViewVariables]
    public TimeSpan LastForcedTargetProgressAt = TimeSpan.Zero;

    [ViewVariables]
    public WH40KWaveDefenceAttackerIntent Intent = WH40KWaveDefenceAttackerIntent.Advance;

    [ViewVariables]
    public int DecisionEpoch;

    [ViewVariables]
    public string DecisionReason = "uninitialized";

    [ViewVariables]
    public string DecisionPriority = "idle";

    [ViewVariables]
    public EntityUid CombatAnchorTarget = EntityUid.Invalid;

    [ViewVariables]
    public EntityCoordinates CombatAnchorCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public TimeSpan CombatAnchorSetAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan CombatDisengageCommitUntil = TimeSpan.Zero;

    [ViewVariables]
    public float BestAttackRangeDistance = float.MaxValue;

    [ViewVariables]
    public TimeSpan LastAttackRangeImprovementAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastSuccessfulDamageDealtAt = TimeSpan.Zero;

    [ViewVariables]
    public EntityUid ActiveSiegeBlocker = EntityUid.Invalid;

    [ViewVariables]
    public string ActiveSiegeBlockerLabel = string.Empty;

    [ViewVariables]
    public string DebugState = "Uninitialized";

    [ViewVariables]
    public WH40KWaveDefenceAttackerIntent LastLoggedIntent = WH40KWaveDefenceAttackerIntent.Advance;

    [ViewVariables]
    public TimeSpan LastIntentChangeAt = TimeSpan.Zero;

    [ViewVariables]
    public string LastLoggedTargetLabel = string.Empty;

    [ViewVariables]
    public EntityUid LastLoggedTargetEntity = EntityUid.Invalid;

    [ViewVariables]
    public TimeSpan LastTargetChangeAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastTargetPushAt = TimeSpan.Zero;

    [ViewVariables]
    public string LastTargetPushReason = string.Empty;

    [ViewVariables]
    public string LastTargetPushLabel = string.Empty;

    [ViewVariables]
    public EntityCoordinates LastTargetPushCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public string LastLoggedSteeringStatus = string.Empty;

    [ViewVariables]
    public TimeSpan LastSteeringChangeAt = TimeSpan.Zero;

    [ViewVariables]
    public bool LastLoggedPlanning;

    [ViewVariables]
    public bool LastLoggedHasPlan;

    [ViewVariables]
    public TimeSpan LastPlanningStateChangeAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastLoggedPlanlessAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastLoggedNoPathAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastLoggedStallAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastLoggedPlanningDelayAt = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan LastLoggedReactionDelayAt = TimeSpan.Zero;

    [ViewVariables]
    public EntityUid LastLoggedRememberedPlayer = EntityUid.Invalid;

    [ViewVariables]
    public bool LastLoggedHadMemory;

    [ViewVariables]
    public WH40KWaveDefencePlayerContactSource LastLoggedRememberedPlayerSource;

    [ViewVariables]
    public TimeSpan LastMemoryChangeAt = TimeSpan.Zero;

    [ViewVariables]
    public bool RuntimeInitialized;
}

public enum WH40KWaveDefenceAttackerIntent : byte
{
    Advance = 0,
    Fallback = 1,
    Reroute = 2,
    DirectObjective = 3,
    SiegeObjective = 4,
    Disengage = 5,
}

public enum WH40KWaveDefenceLocomotionMode : byte
{
    None = 0,
    Route = 1,
    Objective = 2,
}

public enum WH40KWaveDefencePlayerContactSource : byte
{
    None = 0,
    DirectSight = 1,
    AllyRelay = 2,
}

public enum WH40KWaveDefencePlayerContactMode : byte
{
    None = 0,
    VisibleCombat = 1,
    InvestigateMemory = 2,
    PassiveMemory = 3,
}

public enum WH40KWaveDefenceForcedTargetKind : byte
{
    None = 0,
    Fallback = 1,
    Breach = 2,
    DirectObjective = 3,
    DisengageToLane = 4,
}
