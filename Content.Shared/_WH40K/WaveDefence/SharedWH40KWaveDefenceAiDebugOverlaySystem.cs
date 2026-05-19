using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.WaveDefence;

public abstract class SharedWH40KWaveDefenceAiDebugOverlaySystem : EntitySystem
{
    protected const float LocalViewRange = 28f;
    protected static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(0.25f);
}

[Serializable, NetSerializable]
public sealed class WH40KWaveDefenceAiDebugOverlayDisableMessage : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class WH40KWaveDefenceAiDebugOverlayMessage : EntityEventArgs
{
    public WH40KWaveDefenceAiDebugEntry[] Entries { get; }

    public WH40KWaveDefenceAiDebugOverlayMessage(WH40KWaveDefenceAiDebugEntry[] entries)
    {
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public enum WH40KWaveDefenceAiDebugTargetKind : byte
{
    None = 0,
    Objective = 1,
    VisiblePlayer = 2,
    RememberedPlayer = 3,
    LanePoint = 4,
    ForcedPoint = 5,
    Unknown = 6,
}

[Serializable, NetSerializable]
public readonly record struct WH40KWaveDefenceAiDebugEntry(
    string Label,
    MapCoordinates NpcPosition,
    float VisionRadius,
    float AggroVisionRadius,
    MapCoordinates ObjectivePosition,
    bool HasObjectivePosition,
    MapCoordinates CurrentTargetPosition,
    bool HasCurrentTargetPosition,
    MapCoordinates RememberedTargetPosition,
    bool HasRememberedTargetPosition,
    float MemoryRemainingSeconds,
    WH40KWaveDefenceAiDebugTargetKind TargetKind,
    bool HasLineOfSightToPlayer,
    bool NoPath,
    bool Engaged,
    int RecoveryLevel,
    string LaneId,
    string Intent,
    int CurrentLanePointIndex,
    int LastReachedLanePointIndex,
    int FurthestReachedLanePointIndex,
    int TotalLanePointCount,
    float RouteProgressRatio,
    bool RouteCompleted,
    string SiegeBlockerLabel,
    bool HasSiegeBlocker,
    string CurrentLanePointId,
    WH40KWaveLanePointType CurrentLanePointType,
    bool HasCurrentLanePoint,
    string LastReachedLanePointId,
    WH40KWaveLanePointType LastReachedLanePointType,
    bool HasLastReachedLanePoint,
    string RootTask,
    string CurrentTask,
    string SteeringStatus,
    string BrainOwner,
    string CombatOwner,
    string MovementOwner,
    string MemoryOwner,
    string RecoveryOwner,
    string EpochSummary,
    string DebugState,
    float BodyClearanceRadius,
    float BodyClearanceDiameter,
    string ClearanceDebugLabel,
    string ClearanceDebugReason,
    string ClearanceDebugBlockerLabel,
    MapCoordinates ClearanceDebugSamplePosition,
    bool HasClearanceDebugSamplePosition,
    string DynamicClearanceDebugLabel,
    string DynamicClearanceDebugReason,
    string DynamicClearanceDebugBlockerLabel,
    MapCoordinates DynamicClearanceDebugSamplePosition,
    bool HasDynamicClearanceDebugSamplePosition,
    bool HasCommittedRoute,
    float CommittedRouteCost,
    float CommittedRouteRemainingCost,
    int CommittedRouteTopologyVersion,
    MapCoordinates[] CommittedRoutePoints,
    float[] CommittedRouteCumulativeCosts,
    bool HasShadowRoute,
    float ShadowRouteCost,
    int ShadowRouteTopologyVersion,
    MapCoordinates[] ShadowRoutePoints,
    float[] ShadowRouteCumulativeCosts,
    string RouteMindDecision);
