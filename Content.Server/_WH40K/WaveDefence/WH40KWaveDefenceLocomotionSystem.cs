using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Destructible;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Server._WH40K.WaveDefence.HTN;
using Content.Shared.Climbing.Components;
using Content.Shared.Damage.Components;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Content.Shared._WH40K.WaveDefence;
using Content.Shared.Doors.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WaveDefence;

/// <summary>
/// Keeps SimpleSwarm attackers moving continuously between tactical AI refreshes.
/// The main AI still owns lane choice, intent, recovery, and combat decisions,
/// while this system continuously advances a short-horizon movement target.
/// </summary>
public sealed class WH40KWaveDefenceLocomotionSystem : EntitySystem
{
    private const string MovementRangeKey = "MovementRange";
    private const string MeleeRangeKey = "MeleeRange";
    private const float LocomotionThinkIntervalSeconds = 0.05f;
    private const float HardRetargetDistance = 1.15f;
    private const float StickyObjectiveSeconds = 1.75f;
    private const float StickyObjectiveReuseDistance = 2.65f;
    private static readonly float[] SwarmBandOffsets = [-0.85f, -0.4f, 0f, 0.4f, 0.85f];
    private const float SwarmLeadProgress = 0.06f;
    private const float SwarmFrontSlack = 0.03f;
    private const float SwarmCatchupLimit = 0.18f;
    private const float SwarmProgressEpsilon = 0.015f;
    private const float SwarmFrontAssistSlack = 0.04f;
    private const float SwarmFrontAssistLead = 0.08f;
    private const float SwarmMinimumLeadDistance = 3.0f;
    private const float SimpleSwarmFinalPointProgressHandoff = 0.92f;
    private const float SimpleSwarmFinalPointFrontHandoff = 0.98f;
    private const float SimpleSwarmFinalPointArrivalBonus = 0.85f;
    private const float RouteMindLeadDistance = 3.1f;
    private const float RouteMindNodeReachDistance = 0.85f;
    private const float RouteMindRefreshSeconds = 0.90f;
    private const float RouteMindObstacleRefreshSeconds = 0.28f;
    private const float RouteMindSwitchImprovementRatio = 0.18f;
    private const float RouteMindSwitchMinimumCost = 1.35f;
    private const float RouteMindSwitchPenalty = 0.65f;
    private const float RouteMindCommitSeconds = 1.4f;
    private const float RouteMindSwitchCooldownSeconds = 0.9f;
    private const float SimpleSwarmShadowAnchorDistance = 4.25f;
    private const float SimpleSwarmShadowAnchorMinimumDistance = 1.1f;
    private const float SimpleSwarmShadowAnchorProbeStep = 0.58f;
    private const float SimpleSwarmShadowAnchorMinimumAdvance = 0.62f;
    private const float SimpleSwarmDirectClearanceProbeDistance = 9.5f;
    private const float RouteMindObstacleCommitSeconds = 1.1f;
    private const int ShadowRouteClearanceRetryLimit = 6;
    private const float ShadowRoutePortalClearanceSlack = 0.02f;
    private const float ShadowRoutePointMergeDistance = 0.08f;
    private const float SharedShadowAvoidanceSeconds = 7.5f;
    private const float ShadowAvoidanceSizeBucketScale = 20f;
    private const float ShadowReservationAcquireDistance = 2.75f;
    private const float ShadowReservationHoldSeconds = 0.85f;
    private const float ShadowReservationQuantization = 0.25f;
    private const float GateTraversalActivationSlack = 0.65f;
    private const float GateTraversalAnchorSlack = 0.55f;
    private const float GateTraversalMinDepth = 1.1f;
    private const float GateTraversalPassThreshold = 0.28f;
    private const float GateTraversalLateralScale = 1.35f;
    private const float GateTraversalExpandedLateralScale = 1.9f;
    private const float ObjectiveRouteHandoffSlack = 1.15f;
    private const float ObjectiveRouteHandoffMinimumCost = 2.2f;
    private const float ObjectiveSteeringRangeScale = 0.75f;
    private const float ObjectiveSteeringMinimumRange = 0.45f;
    private const float ObjectiveSteeringMaximumRange = 0.8f;
    private const float RouteSteeringPathRange = 0.2f;
    private const float RouteSteeringCrossRange = 0.24f;
    private const float RouteSteeringBreachRange = 0.32f;
    private const float RouteSteeringDefaultRange = 0.28f;
    private const float LaneTraversalCrowdAvoidanceRadius = 0.8f;
    private const float LaneTraversalCrowdHardBlockRadius = 0.42f;
    private const float LaneTraversalStallAdvanceSeconds = 1.35f;
    private const float LaneTraversalStallProgressSlack = 0.035f;
    private const float LaneTraversalStallDistanceSlack = 0.9f;
    private const float LaneTraversalExpandedGateSlack = 0.55f;
    private const float LaneRecoveryDistinctDistance = 0.7f;
    private const float LaneRecoveryEscapeProgressStep = 0.035f;
    private const float LaneRecoveryEscapeLeadSlack = 0.16f;
    private const float LaneRecoveryEscapeWidthScale = 1.55f;
    private const float LaneRecoveryEscapeForwardScale = 0.5f;
    private const float PreparedLanePlanProgressStep = 0.04f;
    private const float PreparedLanePlanLeadSlack = 0.12f;
    private const float PreparedLanePlanWidthScale = 1.2f;
    private const float PreparedLanePlanForwardScale = 0.4f;
    private const float PreparedLanePlanDistinctDistance = 0.55f;
    private const float LocalLaneCorridorPathSeconds = 1.1f;
    private const float LocalLaneCorridorAdvanceDistance = 0.48f;
    private const float LocalLaneCorridorDriftDistance = 2.2f;
    private const float LocalLaneCorridorDirectTargetDistance = 3.6f;
    private const float LocalLaneCorridorNodeSpacing = 0.58f;
    private const float LocalLaneCorridorGoalSlack = 1.15f;
    private const float LocalLaneCorridorLookahead = 4.6f;
    private const float LocalLaneCorridorOffsetStep = 0.34f;
    private const float LocalLaneCorridorOutsidePenalty = 1.2f;
    private const float LocalLaneCorridorOffsetPenalty = 0.42f;
    private const float LocalLaneCorridorTurnPenalty = 0.18f;
    private const float LocalLaneCorridorEdgeMaxDistance = 1.9f;
    private const float LocalLaneCorridorMinimumProgress = 0.01f;
    private const int LocalLaneCorridorMaxLayerSkip = 2;
    private const float LocalLaneCorridorSegmentWidthThreshold = 1.95f;
    private const float LocalLaneCorridorGateWidthThreshold = 1.65f;
    private const float LocalLaneCorridorRetryCooldownSeconds = 0.45f;
    private const float BodyClearanceCacheSeconds = 3.5f;
    private const float DefaultBodyClearanceRadius = 0.42f;
    private const float MinimumBodyClearanceMargin = 0.04f;
    private const float PreferredBodyClearanceMargin = 0.2f;
    private const float BodyClearancePenaltyScale = 8f;
    private const float BodyClearanceBonusScale = 1.5f;
    private const float PhysicalClearanceSampleStep = 0.42f;
    private const float PhysicalClearanceMarginScale = 0.9f;
    private const int PhysicalClearanceCollisionMask = (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable);
    private const float LaneRecoverySearchScaleStep = 0.18f;
    private const float LaneRecoverySearchScaleMaxBonus = 1.35f;
    private const float LaneBlockerCandidateBonus = 1.75f;
    private const float LaneBlockerForwardOffset = 0.8f;
    private const float LaneBlockerLateralOffset = 1.1f;
    private const CollisionGroup LaneBlockerRayMask = CollisionGroup.MobMask | CollisionGroup.InteractImpassable;

    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly PathfindingSystem _pathfinding = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly WH40KWaveDefenceNavigationSchedulerSystem _navigationScheduler = default!;
    [Dependency] private readonly WH40KWaveDefenceObjectiveNavigationSystem _objectiveNavigation = default!;
    private readonly HashSet<EntityUid> _clearanceIntersecting = new();
    private readonly Dictionary<SharedShadowChokeKey, SharedShadowAvoidanceMemory> _sharedShadowAvoidances = new();
    private readonly Dictionary<ShadowReservationKey, ShadowReservationState> _shadowReservations = new();

    private readonly record struct SharedShadowChokeKey(string LaneId, string StrategicLabel, int TopologyVersion, int SizeBucket);

    private sealed class SharedShadowAvoidanceMemory
    {
        public HashSet<PathPolyKey> AvoidPolys = new();
        public TimeSpan ExpiresAt = TimeSpan.Zero;
        public string Reason = string.Empty;
    }

    private readonly record struct ShadowReservationKey(string LaneId, MapId MapId, int SizeBucket, int X, int Y);

    private sealed class ShadowReservationState
    {
        public EntityUid Holder = EntityUid.Invalid;
        public TimeSpan ExpiresAt = TimeSpan.Zero;
        public EntityCoordinates Target = EntityCoordinates.Invalid;
        public string Label = string.Empty;
    }

    private readonly struct LocalLaneCorridorNode
    {
        public readonly EntityCoordinates Coordinates;
        public readonly float Progress;
        public readonly float LateralOffset;
        public readonly float SegmentWidth;
        public readonly int SliceIndex;
        public readonly float Bias;

        public LocalLaneCorridorNode(
            EntityCoordinates coordinates,
            float progress,
            float lateralOffset,
            float segmentWidth,
            int sliceIndex,
            float bias)
        {
            Coordinates = coordinates;
            Progress = progress;
            LateralOffset = lateralOffset;
            SegmentWidth = segmentWidth;
            SliceIndex = sliceIndex;
            Bias = bias;
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(WH40KWaveDefenceAISystem));
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
    }

    public override void Shutdown()
    {
        base.Shutdown();
    }

    public void DeactivateAttackerRuntime(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker)
    {
        _navigationScheduler.CancelEvaluation(uid);
        attacker.PendingNavigationRequestEpoch = 0;
        attacker.NavigationRequestEpoch = 0;
        attacker.LastAppliedNavigationRequestEpoch = 0;
        attacker.LastAcceptedNavigationEpoch = 0;
        attacker.NavigationEpoch = 0;
        attacker.NavigationStateLabel = "inactive";
        attacker.NextShadowRouteThinkAt = TimeSpan.Zero;
        attacker.LastCommittedRouteAt = TimeSpan.Zero;
        attacker.LastShadowRouteAt = TimeSpan.Zero;
        attacker.RouteMindDecision = "inactive";
        attacker.LocomotionMode = WH40KWaveDefenceLocomotionMode.None;
        attacker.ActiveRouteTarget = EntityCoordinates.Invalid;
        attacker.ActiveRouteTargetLabel = string.Empty;
        attacker.ActiveSiegeBlocker = EntityUid.Invalid;
        attacker.ActiveSiegeBlockerLabel = string.Empty;
        ClearLocomotionTarget(attacker, clearStickyObjective: true);
        ResetRouteMind(attacker, clearStrategic: true);
        ClearShadowRouteAvoidance(attacker);
        ClearLocalLaneCorridor(attacker);
        ClearPreparedLanePlan(attacker);
        ReleaseShadowReservations(uid);
        ClearDynamicClearanceDebug(attacker);

        if (TryComp(uid, out NPCSteeringComponent? steering))
            _steering.Unregister(uid, steering);
    }

    public bool TryRecoverComplexGeometry(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label,
        out bool advancedLanePoint)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;
        advancedLanePoint = false;

        if (attacker.AiProfile != WH40KWaveAiProfile.SimpleSwarm ||
            attacker.RouteCompleted ||
            attacker.LanePointIndex < 0 ||
            attacker.LanePointIndex >= attacker.LanePoints.Count)
        {
            return false;
        }

        var previousPointIndex = attacker.LanePointIndex;
        UpdateSimpleSwarmRouteProgress(uid, attacker, origin);
        advancedLanePoint = attacker.LanePointIndex != previousPointIndex;
        var blockedTarget = ResolveCurrentRecoveryBlockedTarget(attacker);
        var blockedPoint = TryGetLanePointCoordinates(attacker, attacker.LanePointIndex, out var pointCoordinates)
            ? pointCoordinates
            : EntityCoordinates.Invalid;

        ClearLocalLaneCorridor(attacker);
        if (TryBuildLocalLaneCorridor(uid, attacker, origin, out target, out label) &&
            IsMeaningfullyDistinctRecoveryTarget(attacker, target, blockedTarget, blockedPoint))
        {
            return true;
        }

        ClearLocalLaneCorridor(attacker);
        if (TryUsePreparedLaneAlternateTarget(attacker, blockedTarget, blockedPoint, out target, out label))
            return true;

        if (TryResolveLaneTraversalTarget(uid, attacker, origin, 0.35f, out target, out label) &&
            IsMeaningfullyDistinctRecoveryTarget(attacker, target, blockedTarget, blockedPoint))
        {
            return true;
        }

        if (TryResolveSameLaneEscapeTarget(uid, attacker, origin, blockedTarget, blockedPoint, out target, out label))
            return true;

        if (TryResolveSimpleSwarmRouteTarget(uid, attacker, origin, out target, out label) &&
            (advancedLanePoint || IsMeaningfullyDistinctRecoveryTarget(attacker, target, blockedTarget, blockedPoint)))
        {
            return true;
        }

        return advancedLanePoint;
    }

    private EntityCoordinates ResolveCurrentRecoveryBlockedTarget(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (attacker.MovementTargetDirective.IsValid(EntityManager))
            return attacker.MovementTargetDirective;

        if (attacker.LocomotionTarget.IsValid(EntityManager))
            return attacker.LocomotionTarget;

        if (attacker.ActiveRouteTarget.IsValid(EntityManager))
            return attacker.ActiveRouteTarget;

        if (attacker.StrategicRouteTarget.IsValid(EntityManager))
            return attacker.StrategicRouteTarget;

        return EntityCoordinates.Invalid;
    }

    private bool TryResolveSameLaneEscapeTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates blockedTarget,
        EntityCoordinates blockedPoint,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (!TryBuildRouteGeometry(attacker, out var vertices, out var pointVertices, out var totalLength) ||
            totalLength <= 0.05f)
        {
            return false;
        }

        var currentProgress = Math.Clamp(attacker.CurrentRouteProgressRatio, 0f, 0.999f);
        var laneFront = Math.Max(GetSharedLaneFrontProgress(uid, attacker), currentProgress);
        var pointProgress = currentProgress + 0.01f;
        if (attacker.LanePointIndex >= 0 &&
            attacker.LanePointIndex < attacker.LanePoints.Count &&
            TryGetPointProgressRatio(attacker.LanePointIndex, pointVertices, vertices, totalLength, out var resolvedPointProgress))
        {
            pointProgress = resolvedPointProgress;
        }

        var sampleProgresses = new[]
        {
            Math.Clamp(currentProgress + 0.015f, currentProgress + 0.01f, 0.999f),
            Math.Clamp(pointProgress - LaneRecoveryEscapeProgressStep * 2f, currentProgress + 0.01f, 0.999f),
            Math.Clamp(pointProgress - LaneRecoveryEscapeProgressStep, currentProgress + 0.01f, 0.999f),
            Math.Clamp(pointProgress, currentProgress + 0.01f, 0.999f),
            Math.Clamp(pointProgress + LaneRecoveryEscapeProgressStep, currentProgress + 0.01f, 0.999f),
            Math.Clamp(pointProgress + LaneRecoveryEscapeProgressStep * 2f, currentProgress + 0.01f, 0.999f),
            Math.Clamp(Math.Min(pointProgress + LaneRecoveryEscapeLeadSlack, laneFront + LaneRecoveryEscapeLeadSlack), currentProgress + 0.01f, 0.999f),
        };

        var bestScore = float.MinValue;
        for (var i = 0; i < sampleProgresses.Length; i++)
        {
            var sampleProgress = sampleProgresses[i];
            if (i > 0 && MathF.Abs(sampleProgress - sampleProgresses[i - 1]) <= 0.005f)
                continue;

            if (!TryResolveProgressCoordinate(
                    attacker,
                    vertices,
                    totalLength,
                    sampleProgress,
                    out var baseTarget,
                    out var segmentDirection,
                    out var segmentWidth))
            {
                continue;
            }

            if (!TryResolveRecoveryEscapeBandTarget(
                    uid,
                    attacker,
                    origin,
                    baseTarget,
                    segmentDirection,
                    segmentWidth,
                    out var candidate))
            {
                continue;
            }

            if (!IsMeaningfullyDistinctRecoveryTarget(attacker, candidate, blockedTarget, blockedPoint))
                continue;

            var score = sampleProgress * 12f;
            if (origin.TryDistance(EntityManager, candidate, out var travelDistance))
                score -= travelDistance * 0.2f;

            if (score <= bestScore)
                continue;

            bestScore = score;
            target = candidate;
        }

        if (!target.IsValid(EntityManager))
            return false;

        label = $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}:escape";
        return true;
    }

    private bool TryResolveRecoveryEscapeBandTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates baseTarget,
        Vector2 direction,
        float segmentWidth,
        out EntityCoordinates target)
    {
        target = EntityCoordinates.Invalid;
        var mapTarget = _transform.ToMapCoordinates(baseTarget);
        if (mapTarget.MapId == MapId.Nullspace)
            return false;

        if (direction.LengthSquared() <= 0.001f)
            direction = Vector2.UnitX;

        var perpendicular = new Vector2(-direction.Y, direction.X);
        var bandScale = Math.Clamp(segmentWidth * LaneRecoveryEscapeWidthScale * 0.35f, 0.35f, 1.45f);
        var preferredOffset = SwarmBandOffsets[Math.Clamp(attacker.SwarmBandIndex, 0, SwarmBandOffsets.Length - 1)] * bandScale;
        var candidateOffsets = new[]
        {
            preferredOffset,
            preferredOffset * 0.5f,
            0f,
            bandScale,
            -bandScale,
            bandScale * 0.65f,
            bandScale * -0.65f,
            preferredOffset * -0.5f,
            preferredOffset * -1f,
        };
        var forwardOffsets = new[]
        {
            0f,
            LaneRecoveryEscapeForwardScale,
            -LaneRecoveryEscapeForwardScale * 0.55f,
            LaneRecoveryEscapeForwardScale * 1.3f,
        };

        var bestScore = float.MinValue;
        foreach (var lateral in candidateOffsets)
        {
            foreach (var forward in forwardOffsets)
            {
                var candidatePosition = mapTarget.Position + perpendicular * lateral + direction * forward;
                var candidate = _transform.ToCoordinates(
                    baseTarget.EntityId,
                    new MapCoordinates(candidatePosition, mapTarget.MapId));

                if (_pathfinding.GetPoly(candidate) == null ||
                    HasHardLaneCrowding(uid, attacker, origin, candidate))
                {
                    continue;
                }

                var score = EvaluateLaneCandidateClearance(uid, attacker, origin, candidate);
                score -= MathF.Abs(lateral) * 0.35f;
                if (origin.TryDistance(EntityManager, candidate, out var distance))
                    score -= distance * 0.08f;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                target = candidate;
            }
        }

        return target.IsValid(EntityManager);
    }

    private bool IsMeaningfullyDistinctRecoveryTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates candidate,
        EntityCoordinates blockedTarget,
        EntityCoordinates blockedPoint)
    {
        if (!candidate.IsValid(EntityManager))
            return false;

        if (SameCoordinates(candidate, blockedTarget) ||
            SameCoordinates(candidate, blockedPoint) ||
            SameCoordinates(candidate, attacker.ActiveRouteTarget) ||
            SameCoordinates(candidate, attacker.StrategicRouteTarget))
        {
            return false;
        }

        return IsCoordinateDistinct(candidate, blockedTarget, LaneRecoveryDistinctDistance) &&
               IsCoordinateDistinct(candidate, blockedPoint, LaneRecoveryDistinctDistance * 0.85f);
    }

    private bool IsCoordinateDistinct(EntityCoordinates candidate, EntityCoordinates reference, float minimumDistance)
    {
        if (!candidate.IsValid(EntityManager) || !reference.IsValid(EntityManager))
            return true;

        if (candidate.EntityId != reference.EntityId)
            return true;

        return (candidate.Position - reference.Position).LengthSquared() >= minimumDistance * minimumDistance;
    }

    private bool TryResolveContinuousLanePlannerTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        float movementRange,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (attacker.AiProfile != WH40KWaveAiProfile.SimpleSwarm ||
            attacker.RouteCompleted ||
            attacker.LanePointIndex < 0 ||
            attacker.LanePointIndex >= attacker.LanePoints.Count)
        {
            ClearLocalLaneCorridor(attacker);
            ClearPreparedLanePlan(attacker);
            return false;
        }

        var useCorridorPlanner = ShouldUseLocalLaneCorridorPlanner(attacker);
        if (useCorridorPlanner)
        {
            if (TryResolveLocalLaneCorridorTarget(uid, attacker, origin, out target, out label))
                return true;
        }
        else
        {
            ClearLocalLaneCorridor(attacker);

            if (CanReusePreparedLanePlan(attacker))
            {
                target = attacker.PreparedLaneTarget;
                label = attacker.PreparedLaneTargetLabel;
                return true;
            }

            if (TryBuildPreparedLanePlan(uid, attacker, origin, out target, out label))
                return true;
        }

        if (TryResolveLaneTraversalTarget(uid, attacker, origin, movementRange, out target, out label))
        {
            StorePreparedLanePlan(
                attacker,
                target,
                label,
                EntityCoordinates.Invalid,
                string.Empty,
                EntityCoordinates.Invalid,
                string.Empty,
                attacker.CurrentRouteProgressRatio);
            return true;
        }

        ClearPreparedLanePlan(attacker);
        return false;
    }

    private bool TryResolveLocalLaneCorridorTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (TryReuseLocalLaneCorridorTarget(uid, attacker, origin, out target, out label))
            return true;

        if (attacker.LocalLaneCorridorRetryAt != TimeSpan.Zero &&
            _timing.CurTime < attacker.LocalLaneCorridorRetryAt &&
            attacker.LocalLaneCorridorRetryPointIndex == attacker.LanePointIndex &&
            attacker.LocalLaneCorridorRetryLaneId == attacker.LaneId)
        {
            return false;
        }

        if (TryBuildLocalLaneCorridor(uid, attacker, origin, out target, out label))
        {
            attacker.LocalLaneCorridorRetryAt = TimeSpan.Zero;
            attacker.LocalLaneCorridorRetryPointIndex = -1;
            attacker.LocalLaneCorridorRetryLaneId = string.Empty;
            return true;
        }

        attacker.LocalLaneCorridorRetryAt = _timing.CurTime + TimeSpan.FromSeconds(LocalLaneCorridorRetryCooldownSeconds);
        attacker.LocalLaneCorridorRetryPointIndex = attacker.LanePointIndex;
        attacker.LocalLaneCorridorRetryLaneId = attacker.LaneId;
        ClearLocalLaneCorridor(attacker);
        return false;
    }

    private bool ShouldUseLocalLaneCorridorPlanner(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (attacker.NoPathCount > 0 ||
            attacker.RecoveryLevel > 0 ||
            attacker.RecoveryAttempts > 0 ||
            attacker.GeometryRecoveryUntil > _timing.CurTime)
        {
            return true;
        }

        if (attacker.LanePointIndex < 0 || attacker.LanePointIndex >= attacker.LanePoints.Count)
            return false;

        var pointUid = attacker.LanePoints[attacker.LanePointIndex];
        if (!TryComp<WH40KWaveLanePointComponent>(pointUid, out var point))
            return false;

        if (point.PointType is WH40KWaveLanePointType.Breach or WH40KWaveLanePointType.Siege)
            return true;

        if (attacker.LastProgressAt != TimeSpan.Zero &&
            _timing.CurTime - attacker.LastProgressAt <= TimeSpan.FromSeconds(0.55f))
        {
            return false;
        }

        var segmentWidth = ResolveSegmentWidth(attacker, attacker.LanePointIndex);
        if (segmentWidth <= Math.Max(LocalLaneCorridorSegmentWidthThreshold, attacker.BodyClearanceDiameter + 0.85f))
            return true;

        if (point.ProgressGateWidth > 0.05f &&
            point.ProgressGateWidth <= Math.Max(LocalLaneCorridorGateWidthThreshold, attacker.BodyClearanceDiameter + 0.65f))
        {
            return true;
        }

        return false;
    }

    private bool TryReuseLocalLaneCorridorTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (attacker.LocalLaneCorridorUntil == TimeSpan.Zero ||
            _timing.CurTime >= attacker.LocalLaneCorridorUntil ||
            attacker.LocalLaneCorridorPointIndex != attacker.LanePointIndex ||
            attacker.LocalLaneCorridorPoints.Count == 0)
        {
            return false;
        }

        while (attacker.LocalLaneCorridorCursor < attacker.LocalLaneCorridorPoints.Count)
        {
            var current = attacker.LocalLaneCorridorPoints[attacker.LocalLaneCorridorCursor];
            if (!current.IsValid(EntityManager) || _pathfinding.GetPoly(current) == null)
                return false;

            if (origin.TryDistance(EntityManager, current, out var distance) &&
                distance <= LocalLaneCorridorAdvanceDistance)
            {
                attacker.LocalLaneCorridorCursor++;
                continue;
            }

            break;
        }

        if (attacker.LocalLaneCorridorCursor >= attacker.LocalLaneCorridorPoints.Count)
            return false;

        attacker.LocalLaneCorridorCursor = ResolveBestLocalLaneCorridorCursor(uid, attacker, origin);

        target = attacker.LocalLaneCorridorPoints[attacker.LocalLaneCorridorCursor];
        label = string.IsNullOrWhiteSpace(attacker.LocalLaneCorridorLabel)
            ? $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}:path"
            : attacker.LocalLaneCorridorLabel;

        if (origin.TryDistance(EntityManager, target, out var driftDistance) &&
            driftDistance > LocalLaneCorridorDriftDistance)
        {
            return false;
        }

        return HasSufficientPhysicalClearanceAlongPath(uid, attacker, origin, target);
    }

    private bool TryBuildLocalLaneCorridor(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (!TryBuildRouteGeometry(attacker, out var vertices, out var pointVertices, out var totalLength) ||
            totalLength <= 0.05f)
        {
            ClearLocalLaneCorridor(attacker);
            return false;
        }

        var currentProgress = Math.Clamp(attacker.CurrentRouteProgressRatio, 0f, 0.999f);
        var laneFront = Math.Max(GetSharedLaneFrontProgress(uid, attacker), currentProgress);
        var pointProgress = currentProgress + LocalLaneCorridorMinimumProgress;
        if (TryGetPointProgressRatio(attacker.LanePointIndex, pointVertices, vertices, totalLength, out var resolvedPointProgress))
            pointProgress = Math.Max(pointProgress, resolvedPointProgress);

        var currentDistance = currentProgress * totalLength;
        var pointDistance = pointProgress * totalLength;
        var lookaheadDistance = Math.Clamp(
            Math.Max(LocalLaneCorridorLookahead, attacker.BodyClearanceDiameter * 4.5f),
            LocalLaneCorridorLookahead,
            6.6f);
        var goalDistance = Math.Min(
            totalLength * 0.999f,
            Math.Max(
                currentDistance + lookaheadDistance,
                Math.Max(pointDistance + LocalLaneCorridorGoalSlack, laneFront * totalLength + 0.65f)));
        if (goalDistance <= currentDistance + LocalLaneCorridorGoalSlack * 0.5f)
            goalDistance = Math.Min(totalLength * 0.999f, currentDistance + LocalLaneCorridorLookahead);

        if (_pathfinding.GetPoly(origin) == null)
        {
            ClearLocalLaneCorridor(attacker);
            return false;
        }

        if (!TrySolveLocalLaneCorridorGraph(
                uid,
                attacker,
                origin,
                vertices,
                totalLength,
                currentDistance,
                goalDistance,
                out var path,
                out var goalProgress))
        {
            ClearLocalLaneCorridor(attacker);
            return false;
        }

        if (path.Count == 0)
        {
            ClearLocalLaneCorridor(attacker);
            return false;
        }

        ClearPreparedLanePlan(attacker);
        StoreLocalLaneCorridor(attacker, path, goalProgress);
        return TryReuseLocalLaneCorridorTarget(uid, attacker, origin, out target, out label);
    }

    private int ResolveBestLocalLaneCorridorCursor(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        var bestCursor = Math.Clamp(attacker.LocalLaneCorridorCursor, 0, attacker.LocalLaneCorridorPoints.Count - 1);
        for (var i = bestCursor; i < attacker.LocalLaneCorridorPoints.Count; i++)
        {
            var candidate = attacker.LocalLaneCorridorPoints[i];
            if (!candidate.IsValid(EntityManager) ||
                _pathfinding.GetPoly(candidate) == null ||
                !origin.TryDistance(EntityManager, candidate, out var distance) ||
                distance > LocalLaneCorridorDirectTargetDistance ||
                !HasSufficientPhysicalClearanceAlongPath(uid, attacker, origin, candidate))
            {
                break;
            }

            bestCursor = i;
        }

        return bestCursor;
    }

    private bool TrySolveLocalLaneCorridorGraph(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        List<EntityCoordinates> vertices,
        float totalLength,
        float currentDistance,
        float goalDistance,
        out List<EntityCoordinates> path,
        out float goalProgress)
    {
        path = new List<EntityCoordinates>();
        goalProgress = 0f;

        var layers = new List<List<LocalLaneCorridorNode>>();
        var sliceCount = Math.Max(2, (int) MathF.Ceiling((goalDistance - currentDistance) / LocalLaneCorridorNodeSpacing));
        for (var sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
        {
            var sliceDistance = Math.Clamp(
                currentDistance + LocalLaneCorridorNodeSpacing * (sliceIndex + 1),
                currentDistance + LocalLaneCorridorMinimumProgress,
                goalDistance);
            var sliceProgress = Math.Clamp(sliceDistance / totalLength, 0f, 0.999f);

            if (!TryResolveProgressCoordinate(
                    attacker,
                    vertices,
                    totalLength,
                    sliceProgress,
                    out var baseTarget,
                    out var segmentDirection,
                    out var segmentWidth))
            {
                continue;
            }

            var layer = BuildLocalLaneCorridorLayer(
                uid,
                attacker,
                origin,
                baseTarget,
                segmentDirection,
                segmentWidth,
                sliceProgress,
                layers.Count);
            if (layer.Count > 0)
                layers.Add(layer);
        }

        if (layers.Count == 0)
            return false;

        var costs = new List<float[]>(layers.Count);
        var previousLayer = new List<int[]>(layers.Count);
        var previousNode = new List<int[]>(layers.Count);
        for (var i = 0; i < layers.Count; i++)
        {
            costs.Add(new float[layers[i].Count]);
            previousLayer.Add(new int[layers[i].Count]);
            previousNode.Add(new int[layers[i].Count]);
            for (var j = 0; j < layers[i].Count; j++)
            {
                costs[i][j] = float.MaxValue;
                previousLayer[i][j] = -1;
                previousNode[i][j] = -1;
            }
        }

        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            for (var nodeIndex = 0; nodeIndex < layers[layerIndex].Count; nodeIndex++)
            {
                var node = layers[layerIndex][nodeIndex];
                if (TryScoreLocalLaneCorridorEdge(uid, attacker, origin, node.Coordinates, out var originCost))
                {
                    var totalCost = originCost + node.Bias;
                    if (totalCost < costs[layerIndex][nodeIndex])
                    {
                        costs[layerIndex][nodeIndex] = totalCost;
                        previousLayer[layerIndex][nodeIndex] = -2;
                        previousNode[layerIndex][nodeIndex] = -1;
                    }
                }

                for (var priorLayerIndex = Math.Max(0, layerIndex - LocalLaneCorridorMaxLayerSkip); priorLayerIndex < layerIndex; priorLayerIndex++)
                {
                    for (var priorNodeIndex = 0; priorNodeIndex < layers[priorLayerIndex].Count; priorNodeIndex++)
                    {
                        var priorCost = costs[priorLayerIndex][priorNodeIndex];
                        if (priorCost == float.MaxValue)
                            continue;

                        var prior = layers[priorLayerIndex][priorNodeIndex];
                        if (!TryScoreLocalLaneCorridorEdge(uid, attacker, prior.Coordinates, node.Coordinates, out var edgeCost))
                            continue;

                        var totalCost = priorCost +
                                        edgeCost +
                                        node.Bias +
                                        MathF.Abs(node.LateralOffset - prior.LateralOffset) * LocalLaneCorridorTurnPenalty;
                        if (totalCost >= costs[layerIndex][nodeIndex])
                            continue;

                        costs[layerIndex][nodeIndex] = totalCost;
                        previousLayer[layerIndex][nodeIndex] = priorLayerIndex;
                        previousNode[layerIndex][nodeIndex] = priorNodeIndex;
                    }
                }
            }
        }

        var bestLayer = -1;
        var bestNode = -1;
        var bestProgress = float.MinValue;
        var bestCost = float.MaxValue;
        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            for (var nodeIndex = 0; nodeIndex < layers[layerIndex].Count; nodeIndex++)
            {
                var cost = costs[layerIndex][nodeIndex];
                if (cost == float.MaxValue)
                    continue;

                var node = layers[layerIndex][nodeIndex];
                if (node.Progress > bestProgress + 0.001f ||
                    MathF.Abs(node.Progress - bestProgress) <= 0.001f && cost < bestCost)
                {
                    bestLayer = layerIndex;
                    bestNode = nodeIndex;
                    bestProgress = node.Progress;
                    bestCost = cost;
                }
            }
        }

        if (bestLayer == -1 || bestNode == -1)
            return false;

        var reversePath = new List<EntityCoordinates>(layers.Count);
        var cursorLayer = bestLayer;
        var cursorNode = bestNode;
        while (cursorLayer >= 0 && cursorNode >= 0)
        {
            reversePath.Add(layers[cursorLayer][cursorNode].Coordinates);
            var nextLayer = previousLayer[cursorLayer][cursorNode];
            var nextNode = previousNode[cursorLayer][cursorNode];
            if (nextLayer == -2)
                break;

            cursorLayer = nextLayer;
            cursorNode = nextNode;
        }

        reversePath.Reverse();
        foreach (var coordinates in reversePath)
        {
            if (path.Count == 0)
            {
                if (!origin.TryDistance(EntityManager, coordinates, out var distance) ||
                    distance > LocalLaneCorridorAdvanceDistance * 0.6f)
                {
                    path.Add(coordinates);
                }

                continue;
            }

            if (path[^1].TryDistance(EntityManager, coordinates, out var distanceFromPrevious) &&
                distanceFromPrevious < 0.18f)
            {
                continue;
            }

            path.Add(coordinates);
        }

        goalProgress = bestProgress;
        return path.Count > 0;
    }

    private List<LocalLaneCorridorNode> BuildLocalLaneCorridorLayer(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates baseTarget,
        Vector2 direction,
        float segmentWidth,
        float progress,
        int sliceIndex)
    {
        var results = new List<LocalLaneCorridorNode>(18);
        var mapTarget = _transform.ToMapCoordinates(baseTarget);
        if (mapTarget.MapId == MapId.Nullspace)
            return results;

        if (direction.LengthSquared() <= 0.001f)
            direction = Vector2.UnitX;

        var searchScale = ResolveLaneSearchScale(attacker);
        var bodyRadius = ResolveBodyClearanceRadius(uid, attacker);
        var corridorHalfWidth = Math.Max(segmentWidth * 0.55f * searchScale, bodyRadius + 0.32f);
        var maxOffset = Math.Clamp(
            Math.Max(corridorHalfWidth * 1.45f, bodyRadius + 1.05f),
            bodyRadius + 0.45f,
            3.2f);
        var preferredOffset = SwarmBandOffsets[Math.Clamp(attacker.SwarmBandIndex, 0, SwarmBandOffsets.Length - 1)] *
                              Math.Min(corridorHalfWidth * 0.7f, 0.95f);
        var offsets = BuildLocalLaneCorridorOffsets(preferredOffset, maxOffset, attacker.BodyClearanceDiameter);
        var perpendicular = new Vector2(-direction.Y, direction.X);

        foreach (var lateral in offsets)
        {
            var candidatePosition = mapTarget.Position + perpendicular * lateral;
            var candidate = _transform.ToCoordinates(
                baseTarget.EntityId,
                new MapCoordinates(candidatePosition, mapTarget.MapId));

            if (_pathfinding.GetPoly(candidate) == null ||
                !HasSufficientPhysicalClearanceAtPoint(uid, attacker, mapTarget.MapId, candidatePosition))
            {
                continue;
            }

            var crowdPenalty = GetLaneCrowdPenalty(uid, attacker, candidate);
            if (crowdPenalty == float.MinValue)
                continue;

            var penalty = MathF.Abs(lateral - preferredOffset) * LocalLaneCorridorOffsetPenalty;
            var outside = MathF.Max(0f, MathF.Abs(lateral) - segmentWidth * 0.5f);
            penalty += outside * LocalLaneCorridorOutsidePenalty;
            penalty += crowdPenalty;

            if (origin.TryDistance(EntityManager, candidate, out var distanceFromOrigin))
                penalty += distanceFromOrigin * 0.03f;

            results.Add(new LocalLaneCorridorNode(candidate, progress, lateral, segmentWidth, sliceIndex, penalty));
        }

        return results;
    }

    private List<float> BuildLocalLaneCorridorOffsets(float preferredOffset, float maxOffset, float bodyDiameter)
    {
        var offsets = new List<float>();
        var step = Math.Max(LocalLaneCorridorOffsetStep, MathF.Max(0.22f, bodyDiameter * 0.45f));
        var steps = Math.Max(1, (int) MathF.Ceiling(maxOffset / step));
        for (var i = -steps; i <= steps; i++)
        {
            var offset = Math.Clamp(i * step, -maxOffset, maxOffset);
            if (!offsets.Exists(existing => MathF.Abs(existing - offset) <= 0.05f))
                offsets.Add(offset);
        }

        if (!offsets.Exists(existing => MathF.Abs(existing - preferredOffset) <= 0.05f))
            offsets.Add(Math.Clamp(preferredOffset, -maxOffset, maxOffset));

        offsets.Sort((left, right) =>
        {
            var leftScore = MathF.Abs(left - preferredOffset);
            var rightScore = MathF.Abs(right - preferredOffset);
            return leftScore.CompareTo(rightScore);
        });

        return offsets;
    }

    private bool TryScoreLocalLaneCorridorEdge(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates start,
        EntityCoordinates end,
        out float cost)
    {
        cost = float.MaxValue;
        if (!start.IsValid(EntityManager) ||
            !end.IsValid(EntityManager) ||
            !start.TryDistance(EntityManager, end, out var distance) ||
            distance > LocalLaneCorridorEdgeMaxDistance ||
            !HasSufficientPhysicalClearanceAlongPath(uid, attacker, start, end))
        {
            return false;
        }

        cost = distance;
        return true;
    }

    private float GetLaneCrowdPenalty(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates candidate)
    {
        var candidateMap = _transform.ToMapCoordinates(candidate);
        if (candidateMap.MapId == MapId.Nullspace)
            return float.MinValue;

        var penalty = 0f;
        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, TransformComponent>();
        while (query.MoveNext(out var otherUid, out var other, out var xform))
        {
            if (otherUid == uid ||
                !other.RuntimeInitialized ||
                xform.MapID != candidateMap.MapId ||
                !string.Equals(other.LaneId, attacker.LaneId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!xform.Coordinates.TryDistance(EntityManager, candidate, out var distance) ||
                distance > LaneTraversalCrowdAvoidanceRadius)
            {
                continue;
            }

            if (distance < LaneTraversalCrowdHardBlockRadius)
                return float.MinValue;

            penalty += (LaneTraversalCrowdAvoidanceRadius - distance) * 4f;
        }

        return penalty;
    }

    private void StoreLocalLaneCorridor(
        WH40KWaveDefenceAttackerComponent attacker,
        List<EntityCoordinates> path,
        float goalProgress)
    {
        attacker.LocalLaneCorridorPoints.Clear();
        attacker.LocalLaneCorridorPoints.AddRange(path);
        attacker.LocalLaneCorridorBuiltAt = _timing.CurTime;
        attacker.LocalLaneCorridorUntil = _timing.CurTime + TimeSpan.FromSeconds(LocalLaneCorridorPathSeconds);
        attacker.LocalLaneCorridorPointIndex = attacker.LanePointIndex;
        attacker.LocalLaneCorridorRetryAt = TimeSpan.Zero;
        attacker.LocalLaneCorridorRetryPointIndex = -1;
        attacker.LocalLaneCorridorRetryLaneId = string.Empty;
        attacker.LocalLaneCorridorCursor = 0;
        attacker.LocalLaneCorridorGoalProgress = goalProgress;
        attacker.LocalLaneCorridorLabel = $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}:path";
    }

    private void ClearLocalLaneCorridor(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.LocalLaneCorridorPoints.Clear();
        attacker.LocalLaneCorridorUntil = TimeSpan.Zero;
        attacker.LocalLaneCorridorBuiltAt = TimeSpan.Zero;
        attacker.LocalLaneCorridorPointIndex = -1;
        attacker.LocalLaneCorridorCursor = 0;
        attacker.LocalLaneCorridorGoalProgress = 0f;
        attacker.LocalLaneCorridorLabel = string.Empty;
    }

    private bool CanReusePreparedLanePlan(WH40KWaveDefenceAttackerComponent attacker)
    {
        return attacker.PreparedLanePlanUntil != TimeSpan.Zero &&
               _timing.CurTime < attacker.PreparedLanePlanUntil &&
               attacker.PreparedLanePointIndex == attacker.LanePointIndex &&
               attacker.PreparedLaneTarget.IsValid(EntityManager) &&
               _pathfinding.GetPoly(attacker.PreparedLaneTarget) != null;
    }

    private bool TryBuildPreparedLanePlan(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (!TryBuildRouteGeometry(attacker, out var vertices, out var pointVertices, out var totalLength) ||
            totalLength <= 0.05f)
        {
            ClearPreparedLanePlan(attacker);
            return false;
        }

        var currentProgress = Math.Clamp(attacker.CurrentRouteProgressRatio, 0f, 0.999f);
        var laneFront = Math.Max(GetSharedLaneFrontProgress(uid, attacker), currentProgress);
        var leadProgress = Math.Clamp(
            SwarmMinimumLeadDistance / totalLength,
            SwarmLeadProgress,
            Math.Max(SwarmLeadProgress, SwarmCatchupLimit - 0.02f));
        var targetProgress = Math.Clamp(
            Math.Max(
                currentProgress + leadProgress,
                Math.Min(laneFront + SwarmFrontSlack, currentProgress + SwarmCatchupLimit)),
            currentProgress + 0.01f,
            0.999f);

        var pointProgress = targetProgress;
        if (TryGetPointProgressRatio(attacker.LanePointIndex, pointVertices, vertices, totalLength, out var resolvedPointProgress))
            pointProgress = resolvedPointProgress;

        var sampleProgresses = new[]
        {
            Math.Clamp(currentProgress + 0.015f, currentProgress + 0.01f, 0.999f),
            Math.Clamp(currentProgress + leadProgress * 0.5f, currentProgress + 0.01f, 0.999f),
            targetProgress,
            Math.Clamp(pointProgress, currentProgress + 0.01f, 0.999f),
            Math.Clamp(pointProgress + PreparedLanePlanProgressStep, currentProgress + 0.01f, 0.999f),
            Math.Clamp(Math.Min(pointProgress + PreparedLanePlanLeadSlack, laneFront + PreparedLanePlanLeadSlack), currentProgress + 0.01f, 0.999f),
        };

        var candidates = new List<(EntityCoordinates Target, float Score, string Label)>(24);
        for (var i = 0; i < sampleProgresses.Length; i++)
        {
            var sampleProgress = sampleProgresses[i];
            if (i > 0 && MathF.Abs(sampleProgress - sampleProgresses[i - 1]) <= 0.005f)
                continue;

            if (!TryResolveProgressCoordinate(
                    attacker,
                    vertices,
                    totalLength,
                    sampleProgress,
                    out var baseTarget,
                    out var segmentDirection,
                    out var segmentWidth))
            {
                continue;
            }

            CollectPreparedLanePlanCandidates(
                uid,
                attacker,
                origin,
                baseTarget,
                segmentDirection,
                segmentWidth,
                sampleProgress,
                candidates);
        }

        if (candidates.Count == 0)
        {
            ClearPreparedLanePlan(attacker);
            return false;
        }

        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        var distinct = SelectDistinctPreparedLaneTargets(candidates);
        if (distinct.Count == 0)
        {
            ClearPreparedLanePlan(attacker);
            return false;
        }

        target = distinct[0].Target;
        label = distinct[0].Label;
        var alternateA = distinct.Count > 1 ? distinct[1].Target : EntityCoordinates.Invalid;
        var alternateALabel = distinct.Count > 1 ? distinct[1].Label : string.Empty;
        var alternateB = distinct.Count > 2 ? distinct[2].Target : EntityCoordinates.Invalid;
        var alternateBLabel = distinct.Count > 2 ? distinct[2].Label : string.Empty;
        StorePreparedLanePlan(attacker, target, label, alternateA, alternateALabel, alternateB, alternateBLabel, currentProgress);
        return true;
    }

    private void CollectPreparedLanePlanCandidates(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates baseTarget,
        Vector2 direction,
        float segmentWidth,
        float sampleProgress,
        List<(EntityCoordinates Target, float Score, string Label)> candidates)
    {
        var mapTarget = _transform.ToMapCoordinates(baseTarget);
        if (mapTarget.MapId == MapId.Nullspace)
            return;

        if (direction.LengthSquared() <= 0.001f)
            direction = Vector2.UnitX;

        var searchScale = ResolveLaneSearchScale(attacker);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var bandScale = Math.Clamp(segmentWidth * PreparedLanePlanWidthScale * 0.35f * searchScale, 0.25f, 2.2f);
        var preferredOffset = SwarmBandOffsets[Math.Clamp(attacker.SwarmBandIndex, 0, SwarmBandOffsets.Length - 1)] * bandScale;
        var candidateOffsets = MathF.Abs(preferredOffset) <= 0.05f
            ? new[] { 0f, bandScale * 0.35f, bandScale * -0.35f, bandScale * 0.7f, bandScale * -0.7f, bandScale, -bandScale, bandScale * 1.3f, bandScale * -1.3f }
            : new[] { preferredOffset, preferredOffset * 0.5f, 0f, preferredOffset * -0.35f, preferredOffset * -0.7f, preferredOffset * 1.15f };
        var forwardScale = PreparedLanePlanForwardScale * MathF.Max(1f, searchScale);
        var forwardOffsets = new[] { 0f, forwardScale, -forwardScale * 0.35f, forwardScale * 1.25f, forwardScale * 1.75f };
        var defaultLabel = $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}:plan";

        foreach (var lateral in candidateOffsets)
        {
            foreach (var forward in forwardOffsets)
            {
                var candidatePosition = mapTarget.Position + perpendicular * lateral + direction * forward;
                var candidate = _transform.ToCoordinates(
                    baseTarget.EntityId,
                    new MapCoordinates(candidatePosition, mapTarget.MapId));

                if (_pathfinding.GetPoly(candidate) == null ||
                    HasHardLaneCrowding(uid, attacker, origin, candidate))
                {
                    continue;
                }

                var score = sampleProgress * 14f;
                score += EvaluateLaneCandidateClearance(uid, attacker, origin, candidate, segmentWidth * 0.5f, lateral);
                score -= MathF.Abs(lateral - preferredOffset) * 0.35f;
                if (forward < 0f)
                    score -= 0.8f;
                else
                    score += forward * 1.8f;

                if (origin.TryDistance(EntityManager, candidate, out var distance))
                    score -= distance * 0.12f;

                candidates.Add((candidate, score, defaultLabel));

                if (TryResolveLaneBlockerTarget(uid, attacker, origin, candidate, direction, out var breachTarget, out var breachLabel))
                    candidates.Add((breachTarget, score + LaneBlockerCandidateBonus, breachLabel));
            }
        }
    }

    private List<(EntityCoordinates Target, string Label)> SelectDistinctPreparedLaneTargets(List<(EntityCoordinates Target, float Score, string Label)> candidates)
    {
        var results = new List<(EntityCoordinates Target, string Label)>(3);
        foreach (var candidate in candidates)
        {
            if (!candidate.Target.IsValid(EntityManager))
                continue;

            var distinct = true;
            foreach (var existing in results)
            {
                if (!IsCoordinateDistinct(candidate.Target, existing.Target, PreparedLanePlanDistinctDistance))
                {
                    distinct = false;
                    break;
                }
            }

            if (!distinct)
                continue;

            results.Add((candidate.Target, candidate.Label));
            if (results.Count >= 3)
                break;
        }

        return results;
    }

    private void StorePreparedLanePlan(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        string label,
        EntityCoordinates alternateA,
        string alternateALabel,
        EntityCoordinates alternateB,
        string alternateBLabel,
        float progress)
    {
        attacker.PreparedLaneTarget = target;
        attacker.PreparedLaneTargetLabel = label;
        attacker.PreparedLaneAlternateTargetA = alternateA;
        attacker.PreparedLaneAlternateLabelA = alternateALabel;
        attacker.PreparedLaneAlternateTargetB = alternateB;
        attacker.PreparedLaneAlternateLabelB = alternateBLabel;
        attacker.PreparedLanePlanBuiltAt = _timing.CurTime;
        attacker.PreparedLanePlanUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0.3f, attacker.PreparedLanePlanSeconds));
        attacker.PreparedLanePointIndex = attacker.LanePointIndex;
        attacker.PreparedLanePlanProgress = progress;
    }

    private void ClearPreparedLanePlan(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.PreparedLaneTarget = EntityCoordinates.Invalid;
        attacker.PreparedLaneTargetLabel = string.Empty;
        attacker.PreparedLaneAlternateTargetA = EntityCoordinates.Invalid;
        attacker.PreparedLaneAlternateLabelA = string.Empty;
        attacker.PreparedLaneAlternateTargetB = EntityCoordinates.Invalid;
        attacker.PreparedLaneAlternateLabelB = string.Empty;
        attacker.PreparedLanePlanBuiltAt = TimeSpan.Zero;
        attacker.PreparedLanePlanUntil = TimeSpan.Zero;
        attacker.PreparedLanePointIndex = -1;
        attacker.PreparedLanePlanProgress = 0f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        PruneSharedShadowAvoidances();
        PruneShadowReservations();

        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, HTNComponent, ActiveNPCComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var attacker, out var htn, out _, out var xform))
        {
            if (xform.MapID == MapId.Nullspace ||
                !attacker.RuntimeInitialized ||
                attacker.AiProfile != WH40KWaveAiProfile.SimpleSwarm ||
                _timing.CurTime < attacker.NextLocomotionThinkAt)
            {
                continue;
            }

            attacker.NextLocomotionThinkAt = _timing.CurTime + GetLocomotionThinkDelay(uid);
            ClearDynamicClearanceDebug(attacker);

            if (!ShouldControlLocomotion(uid, attacker, htn))
            {
                _navigationScheduler.CancelEvaluation(uid);
                attacker.PendingNavigationRequestEpoch = 0;
                attacker.RouteMindDecision = "paused:tactical-target";
                ClearLocomotionTarget(attacker, clearStickyObjective: false);
                continue;
            }

            UpdateSimpleSwarmRouteProgress(uid, attacker, xform.Coordinates);
            RefreshProgressScore(attacker, xform.Coordinates);

            if (!TryResolveLocomotionTarget(uid, attacker, htn, xform.Coordinates, out var target, out var label, out var mode))
            {
                ClearLocomotionTarget(attacker, clearStickyObjective: false);
                continue;
            }

            ApplyLocomotionTarget(uid, attacker, htn, target, label, mode);
        }
    }

    private bool ShouldControlLocomotion(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, HTNComponent htn)
    {
        if (!attacker.DesiredTargetProposal.IsValid(EntityManager))
            return false;

        var label = attacker.DesiredTargetProposalLabel;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        if (label.StartsWith("forced:", StringComparison.Ordinal))
            return IsStrategicForcedLocomotionTarget(attacker);

        if (label.StartsWith("player:", StringComparison.Ordinal) ||
            label.StartsWith("memory:", StringComparison.Ordinal) ||
            label.StartsWith("investigate:", StringComparison.Ordinal))
        {
            return false;
        }

        return label.StartsWith("lane:", StringComparison.Ordinal) ||
               label.StartsWith("objective:", StringComparison.Ordinal);
    }

    private bool TryResolveLocomotionTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label,
        out WH40KWaveDefenceLocomotionMode mode)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;
        mode = WH40KWaveDefenceLocomotionMode.None;

        var range = htn.Blackboard.GetValueOrDefault<float>(MovementRangeKey, EntityManager);
        if (range <= 0f)
            range = 0.2f;

        var meleeRange = htn.Blackboard.GetValueOrDefault<float>(MeleeRangeKey, EntityManager);
        if (meleeRange <= 0f)
            meleeRange = 1f;

        if (!TryResolveStrategicRouteTarget(uid, attacker, origin, meleeRange, out var strategicTarget, out var strategicLabel, out mode))
            return false;

        ReleaseShadowReservations(uid);
        var topologyVersion = GetRouteTopologyVersion(strategicTarget);
        SyncStrategicRouteState(uid, attacker, strategicTarget, strategicLabel, topologyVersion);
        var forcedStrategic = IsStrategicForcedLocomotionTarget(attacker) &&
                              string.Equals(strategicLabel, attacker.DesiredTargetProposalLabel, StringComparison.Ordinal);
        var simpleSwarmRouteMode = attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm &&
                                   mode == WH40KWaveDefenceLocomotionMode.Route &&
                                   !forcedStrategic;
        var simpleSwarmShadowEscalation = simpleSwarmRouteMode &&
                                          ShouldEscalateSimpleSwarmToShadowRoute(
                                              uid,
                                              attacker,
                                              origin,
                                              strategicTarget,
                                              strategicLabel);
        var useRouteMind = !simpleSwarmRouteMode || simpleSwarmShadowEscalation;

        if (useRouteMind)
        {
            UpdateCommittedRouteProgress(attacker, origin);
            var flags = _pathfinding.GetFlags(htn.Blackboard);
            EvaluateRouteMind(uid, attacker, strategicTarget, strategicLabel, mode, origin, flags, range, topologyVersion);
        }
        else
        {
            _navigationScheduler.CancelEvaluation(uid);
            attacker.PendingNavigationRequestEpoch = 0;
            attacker.RouteMindDecision = "simple-swarm:primitive";
        }

        if (forcedStrategic)
        {
            if (attacker.HasCommittedRoute &&
                TryResolveCommittedRouteAnchor(attacker, origin, out target, out label))
            {
                if (mode == WH40KWaveDefenceLocomotionMode.Route)
                {
                    attacker.ActiveRouteTarget = target;
                    attacker.ActiveRouteTargetLabel = label;
                }

                attacker.RouteMindDecision = mode == WH40KWaveDefenceLocomotionMode.Objective
                    ? "forced-objective-route"
                    : "forced-breach-route";
                return true;
            }

            target = strategicTarget;
            label = strategicLabel;
            if (mode == WH40KWaveDefenceLocomotionMode.Route)
            {
                attacker.ActiveRouteTarget = target;
                attacker.ActiveRouteTargetLabel = label;
            }

            attacker.RouteMindDecision = mode == WH40KWaveDefenceLocomotionMode.Objective
                ? "forced-objective-direct"
                : "forced-breach-direct";
            return target.IsValid(EntityManager);
        }

        var objectiveAssaultHandoff = mode == WH40KWaveDefenceLocomotionMode.Objective &&
                                      ShouldUseObjectiveAssaultHandoff(attacker, origin, strategicTarget, meleeRange);

        if (objectiveAssaultHandoff &&
            TryResolveObjectiveLocomotionTarget(uid, attacker, origin, meleeRange, out target, out label))
        {
            attacker.RouteMindDecision = attacker.HasCommittedRoute
                ? "objective-handoff"
                : "objective-direct";
            return true;
        }

        if (simpleSwarmShadowEscalation &&
            attacker.HasCommittedRoute &&
            TryResolveSimpleSwarmCommittedShadowAnchor(uid, attacker, origin, out target, out label))
        {
            if (TryResolveSimpleSwarmShadowHoldTarget(uid, attacker, origin, target, strategicTarget, strategicLabel, out var holdTarget, out var holdLabel))
            {
                attacker.RouteMindDecision = "simple-swarm:shadow-hold";
                attacker.ActiveRouteTarget = holdTarget;
                attacker.ActiveRouteTargetLabel = holdLabel;
                attacker.ActiveSiegeBlocker = EntityUid.Invalid;
                attacker.ActiveSiegeBlockerLabel = string.Empty;
                target = holdTarget;
                label = holdLabel;
                return true;
            }

            attacker.RouteMindDecision = "simple-swarm:shadow-commit";
            attacker.ActiveRouteTarget = target;
            attacker.ActiveRouteTargetLabel = label;
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            return true;
        }

        if (simpleSwarmRouteMode)
        {
            target = strategicTarget;
            label = strategicLabel;
            attacker.ActiveRouteTarget = target;
            attacker.ActiveRouteTargetLabel = label;
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            attacker.RouteMindDecision = simpleSwarmShadowEscalation
                ? attacker.HasCommittedRoute
                    ? "simple-swarm:shadow-hold"
                    : "simple-swarm:shadow-pending"
                : "simple-swarm:primitive";
            return target.IsValid(EntityManager);
        }

        if (mode == WH40KWaveDefenceLocomotionMode.Route &&
            TryResolveContinuousLanePlannerTarget(uid, attacker, origin, range, out target, out label))
        {
            attacker.RouteMindDecision = label.Contains(":path", StringComparison.Ordinal)
                ? "local-plan:corridor"
                : label.Contains(":breach", StringComparison.Ordinal)
                    ? "local-plan:breach"
                    : label.Contains(":cross", StringComparison.Ordinal)
                        ? "local-plan:traversal"
                        : attacker.HasCommittedRoute
                            ? "local-plan:prepared"
                            : "local-plan:precommit";
            attacker.ActiveRouteTarget = target;
            attacker.ActiveRouteTargetLabel = label;
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            return true;
        }

        if (!objectiveAssaultHandoff &&
            attacker.AiProfile != WH40KWaveAiProfile.SimpleSwarm &&
            attacker.HasCommittedRoute &&
            TryResolveCommittedRouteAnchor(attacker, origin, out target, out label))
        {
            if (mode == WH40KWaveDefenceLocomotionMode.Route)
            {
                attacker.ActiveRouteTarget = target;
                attacker.ActiveRouteTargetLabel = label;
                attacker.ActiveSiegeBlocker = EntityUid.Invalid;
                attacker.ActiveSiegeBlockerLabel = string.Empty;
            }

            return true;
        }

        target = strategicTarget;
        label = strategicLabel;
        attacker.RouteMindDecision = attacker.HasCommittedRoute
            ? "fallback:strategic"
            : "fallback:precommit-strategic";
        return target.IsValid(EntityManager);
    }

    private bool ShouldUseSimpleSwarmShadowEscalation(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (attacker.AiProfile != WH40KWaveAiProfile.SimpleSwarm ||
            attacker.RouteCompleted)
        {
            return false;
        }

        if (attacker.NoPathCount >= 2 ||
            attacker.RecoveryAttempts >= 2 ||
            attacker.RecoveryLevel >= 1 ||
            attacker.GeometryRecoveryUntil > _timing.CurTime)
        {
            return true;
        }

        if (attacker.LastProgressAt != TimeSpan.Zero &&
            _timing.CurTime - attacker.LastProgressAt >= TimeSpan.FromSeconds(Math.Max(1.35f, attacker.StallSeconds * 0.5f)))
        {
            return true;
        }

        return false;
    }

    private bool ShouldEscalateSimpleSwarmToShadowRoute(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates strategicTarget,
        string strategicLabel)
    {
        if (ShouldUseSimpleSwarmShadowEscalation(attacker))
        {
            SetClearanceDebug(attacker, strategicTarget, strategicLabel, "stalled-or-recovery", EntityUid.Invalid, EntityCoordinates.Invalid);
            return true;
        }

        var steering = CompOrNull<NPCSteeringComponent>(uid);
        if (steering?.Status == SteeringStatus.NoPath)
        {
            SetClearanceDebug(attacker, strategicTarget, strategicLabel, "steering:nopath", steering.ActiveObstacle, EntityCoordinates.Invalid);
            return true;
        }

        if (!origin.TryDistance(EntityManager, strategicTarget, out var distance) ||
            distance > SimpleSwarmDirectClearanceProbeDistance)
        {
            SetClearanceDebug(attacker, strategicTarget, strategicLabel, $"deferred:{distance:0.0}", EntityUid.Invalid, EntityCoordinates.Invalid);
            return false;
        }

        if (TryGetPhysicalClearanceFailureAlongPath(
                uid,
                attacker,
                origin,
                strategicTarget,
                out var reason,
                out var blocker,
                out var sample))
        {
            SetClearanceDebug(attacker, strategicTarget, strategicLabel, reason, blocker, sample);
            return true;
        }

        SetClearanceDebug(attacker, strategicTarget, strategicLabel, "clear", EntityUid.Invalid, EntityCoordinates.Invalid);
        return false;
    }

    private void SetClearanceDebug(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        string label,
        string reason,
        EntityUid blocker,
        EntityCoordinates sample)
    {
        attacker.ClearanceDebugTarget = target;
        attacker.ClearanceDebugSample = sample;
        attacker.ClearanceDebugLabel = string.IsNullOrWhiteSpace(label) ? "<none>" : label;
        attacker.ClearanceDebugReason = string.IsNullOrWhiteSpace(reason) ? "none" : reason;
        attacker.ClearanceDebugBlockerLabel = blocker.IsValid()
            ? ToPrettyString(blocker)
            : string.Empty;
        attacker.ClearanceDebugUpdatedAt = _timing.CurTime;
    }

    private void SetDynamicClearanceDebug(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        string label,
        string reason,
        EntityUid blocker,
        EntityCoordinates sample)
    {
        attacker.DynamicClearanceDebugTarget = target;
        attacker.DynamicClearanceDebugSample = sample;
        attacker.DynamicClearanceDebugLabel = string.IsNullOrWhiteSpace(label) ? "<none>" : label;
        attacker.DynamicClearanceDebugReason = string.IsNullOrWhiteSpace(reason) ? "none" : reason;
        attacker.DynamicClearanceDebugBlockerLabel = blocker.IsValid()
            ? ToPrettyString(blocker)
            : string.Empty;
        attacker.DynamicClearanceDebugUpdatedAt = _timing.CurTime;
    }

    private void ClearDynamicClearanceDebug(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.DynamicClearanceDebugTarget = EntityCoordinates.Invalid;
        attacker.DynamicClearanceDebugSample = EntityCoordinates.Invalid;
        attacker.DynamicClearanceDebugLabel = "none";
        attacker.DynamicClearanceDebugReason = "clear";
        attacker.DynamicClearanceDebugBlockerLabel = string.Empty;
        attacker.DynamicClearanceDebugUpdatedAt = _timing.CurTime;
    }

    private bool TryResolveStrategicRouteTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        float meleeRange,
        out EntityCoordinates target,
        out string label,
        out WH40KWaveDefenceLocomotionMode mode)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;
        mode = WH40KWaveDefenceLocomotionMode.None;

        if (TryResolveForcedStrategicTarget(attacker, out target, out label, out mode))
            return true;

        if (!attacker.RouteCompleted &&
            TryGetStrategicLanePointCoordinates(attacker, out var strategicPointIndex, out target))
        {
            if (TryResolveStrategicLaneTargetOverride(uid, attacker, origin, strategicPointIndex, out var traversalTarget))
                target = traversalTarget;

            label = $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, strategicPointIndex)}";
            mode = WH40KWaveDefenceLocomotionMode.Route;
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            return true;
        }

        if (TryResolveObjectiveLocomotionTarget(uid, attacker, origin, meleeRange, out target, out label))
        {
            mode = WH40KWaveDefenceLocomotionMode.Objective;
            return true;
        }

        return false;
    }

    private bool TryUsePreparedLaneAlternateTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates blockedTarget,
        EntityCoordinates blockedPoint,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (attacker.PreparedLanePlanUntil == TimeSpan.Zero ||
            _timing.CurTime >= attacker.PreparedLanePlanUntil ||
            attacker.PreparedLanePointIndex != attacker.LanePointIndex)
        {
            return false;
        }

        if (attacker.PreparedLaneAlternateTargetA.IsValid(EntityManager) &&
            _pathfinding.GetPoly(attacker.PreparedLaneAlternateTargetA) != null &&
            IsMeaningfullyDistinctRecoveryTarget(attacker, attacker.PreparedLaneAlternateTargetA, blockedTarget, blockedPoint))
        {
            target = attacker.PreparedLaneAlternateTargetA;
            label = string.IsNullOrWhiteSpace(attacker.PreparedLaneAlternateLabelA)
                ? $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}:alt-a"
                : attacker.PreparedLaneAlternateLabelA;
            return true;
        }

        if (attacker.PreparedLaneAlternateTargetB.IsValid(EntityManager) &&
            _pathfinding.GetPoly(attacker.PreparedLaneAlternateTargetB) != null &&
            IsMeaningfullyDistinctRecoveryTarget(attacker, attacker.PreparedLaneAlternateTargetB, blockedTarget, blockedPoint))
        {
            target = attacker.PreparedLaneAlternateTargetB;
            label = string.IsNullOrWhiteSpace(attacker.PreparedLaneAlternateLabelB)
                ? $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}:alt-b"
                : attacker.PreparedLaneAlternateLabelB;
            return true;
        }

        return false;
    }

    private bool TryResolveForcedStrategicTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityCoordinates target,
        out string label,
        out WH40KWaveDefenceLocomotionMode mode)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;
        mode = WH40KWaveDefenceLocomotionMode.None;

        if (!IsStrategicForcedLocomotionTarget(attacker))
            return false;

        target = attacker.ForcedTarget;
        label = attacker.DesiredTargetProposalLabel;
        mode = attacker.ForcedTargetKind == WH40KWaveDefenceForcedTargetKind.DirectObjective
            ? WH40KWaveDefenceLocomotionMode.Objective
            : WH40KWaveDefenceLocomotionMode.Route;
        return target.IsValid(EntityManager);
    }

    private bool IsStrategicForcedLocomotionTarget(WH40KWaveDefenceAttackerComponent attacker)
    {
        return attacker.ForcedTarget.IsValid(EntityManager) &&
               attacker.ForcedTargetKind is WH40KWaveDefenceForcedTargetKind.Breach or
                   WH40KWaveDefenceForcedTargetKind.DirectObjective;
    }

    private void SyncStrategicRouteState(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates strategicTarget,
        string strategicLabel,
        int topologyVersion)
    {
        var changed = !SameCoordinates(attacker.StrategicRouteTarget, strategicTarget) ||
                      !string.Equals(attacker.StrategicRouteTargetLabel, strategicLabel, StringComparison.Ordinal);

        attacker.StrategicRouteTarget = strategicTarget;
        attacker.StrategicRouteTargetLabel = strategicLabel;
        attacker.StrategicRouteTopologyVersion = topologyVersion;

        if (!changed)
            return;

        _navigationScheduler.CancelEvaluation(uid);
        attacker.PendingNavigationRequestEpoch = 0;
        ResetRouteMind(attacker, clearStrategic: false);
        ClearShadowRouteAvoidance(attacker);
        attacker.DynamicOccupancyHoldUntil = TimeSpan.Zero;
        attacker.NextShadowRouteThinkAt = TimeSpan.Zero;
        attacker.RouteMindDecision = "route-mind:retarget";
        ClearPreparedLanePlan(attacker);
    }

    private bool ShouldUseObjectiveAssaultHandoff(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates strategicTarget,
        float meleeRange)
    {
        if (!attacker.HasCommittedRoute)
            return true;

        var routeCutoff = MathF.Max(ObjectiveRouteHandoffMinimumCost, meleeRange + ObjectiveRouteHandoffSlack);
        if (attacker.CommittedRouteRemainingCost <= routeCutoff)
            return true;

        return origin.TryDistance(EntityManager, strategicTarget, out var targetDistance) &&
               targetDistance <= routeCutoff;
    }

    private void EvaluateRouteMind(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates strategicTarget,
        string strategicLabel,
        WH40KWaveDefenceLocomotionMode mode,
        EntityCoordinates origin,
        PathFlags flags,
        float range,
        int topologyVersion)
    {
        if (ShouldUseDirectMove(uid, strategicTarget) ||
            _pathfinding.GetPoly(origin) == null ||
            _pathfinding.GetPoly(strategicTarget) == null)
        {
            _navigationScheduler.CancelEvaluation(uid);
            attacker.PendingNavigationRequestEpoch = 0;
            ResetCommittedRoute(attacker);
            ResetShadowRoute(attacker);
            attacker.RouteMindDecision = "route-mind:direct";
            return;
        }

        var now = _timing.CurTime;
        var steering = CompOrNull<NPCSteeringComponent>(uid);
        if (HasObstacleCommit(attacker, steering))
            attacker.RouteCommitUntil = Max(attacker.RouteCommitUntil, now + TimeSpan.FromSeconds(RouteMindObstacleCommitSeconds));

        TryConsumeRouteEvaluation(uid, attacker, topologyVersion);

        var topologyChanged = topologyVersion != attacker.LastEvaluatedRouteTopologyVersion ||
                              topologyVersion != attacker.CommittedRouteTopologyVersion;
        if (topologyChanged &&
            attacker.ShadowRouteAvoidTopologyVersion != 0 &&
            attacker.ShadowRouteAvoidTopologyVersion != topologyVersion)
        {
            ClearShadowRouteAvoidance(attacker);
        }

        ApplySharedShadowAvoidance(attacker, strategicLabel, topologyVersion);
        if (attacker.HasCommittedRoute &&
            attacker.ShadowRouteAvoidPolys.Count > 0 &&
            attacker.CommittedRoutePolyKeys.Overlaps(attacker.ShadowRouteAvoidPolys))
        {
            ResetCommittedRoute(attacker);
            attacker.RouteMindDecision = "shadow:invalidate-shared";
        }

        var needsRefresh = !attacker.HasCommittedRoute ||
                           attacker.NextShadowRouteThinkAt == TimeSpan.Zero ||
                           now >= attacker.NextShadowRouteThinkAt ||
                           topologyChanged ||
                           steering?.Status == SteeringStatus.NoPath ||
                           HasObstacleCommit(attacker, steering);

        if (!needsRefresh)
        {
            attacker.RouteMindDecision = "route-mind:waiting";
            return;
        }

        if (!_navigationScheduler.ShouldRequestEvaluation(uid, origin, strategicTarget, strategicLabel, mode, flags, range, topologyVersion, attacker.ShadowRouteAvoidPolys))
        {
            attacker.RouteMindDecision = "route-mind:evaluating";
            return;
        }

        QueueShadowRouteEvaluation(uid, attacker, origin, strategicTarget, strategicLabel, mode, flags, range, topologyVersion);
    }

    private void TryConsumeRouteEvaluation(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        int topologyVersion)
    {
        if (!_navigationScheduler.TryConsumeResult(uid, out var result))
            return;

        if (result.RequestEpoch != attacker.PendingNavigationRequestEpoch)
            return;

        attacker.LastAppliedNavigationRequestEpoch = result.RequestEpoch;
        attacker.PendingNavigationRequestEpoch = 0;

        var now = _timing.CurTime;
        MarkNavigationState(attacker, result.Label);

        if (!SameCoordinates(attacker.StrategicRouteTarget, result.StrategicTarget) ||
            !string.Equals(attacker.StrategicRouteTargetLabel, result.StrategicLabel, StringComparison.Ordinal) ||
            topologyVersion != result.TopologyVersion)
        {
            attacker.RouteMindDecision = "shadow:discard:stale";
            attacker.NextShadowRouteThinkAt = now + TimeSpan.FromSeconds(RouteMindObstacleRefreshSeconds);
            return;
        }

        attacker.LastEvaluatedRouteTopologyVersion = result.TopologyVersion;
        attacker.LastShadowRouteAt = now;

        if (attacker.ShadowRouteAvoidTopologyVersion != 0 &&
            attacker.ShadowRouteAvoidTopologyVersion != result.TopologyVersion)
        {
            ClearShadowRouteAvoidance(attacker);
        }

        if (result.PathResult != PathResult.Path || result.Path.Count == 0)
        {
            attacker.HasShadowRoute = false;
            attacker.ShadowRouteCost = 0f;
            attacker.ShadowRouteTopologyVersion = result.TopologyVersion;
            attacker.ShadowRoutePoints.Clear();
            attacker.ShadowRouteCumulativeCosts.Clear();
            attacker.RouteMindDecision = $"shadow:{result.PathResult}";
            attacker.NextShadowRouteThinkAt = now + TimeSpan.FromSeconds(RouteMindObstacleRefreshSeconds);
            return;
        }

        var shadowPoints = BuildRoutePoints(result.Path, result.Origin, result.StrategicTarget);
        var shadowCosts = BuildLinearCosts(shadowPoints);
        var shadowCost = shadowCosts.Count > 0 ? shadowCosts[^1] : 0f;
        var shadowPolyKeys = BuildPathPolyKeySet(result.Path);

        if (!TryValidateShadowRouteClearance(
                uid,
                attacker,
                result.Path,
                shadowPoints,
                result.StrategicTarget,
                result.StrategicLabel,
                out var rejectionReason,
                out var rejectionBlocker,
                out var rejectionSample,
                out var avoidPolys))
        {
            RememberSharedShadowAvoidance(attacker, result.StrategicLabel, result.TopologyVersion, avoidPolys, rejectionReason);
            var steering = CompOrNull<NPCSteeringComponent>(uid);
            var overlapsRejectedChoke = attacker.HasCommittedRoute &&
                                        avoidPolys.Count > 0 &&
                                        attacker.CommittedRoutePolyKeys.Overlaps(avoidPolys);
            var preserveCommittedRoute = attacker.HasCommittedRoute &&
                                         !overlapsRejectedChoke &&
                                         steering?.Status != SteeringStatus.NoPath &&
                                         (attacker.LastProgressAt == TimeSpan.Zero ||
                                          now - attacker.LastProgressAt < TimeSpan.FromSeconds(Math.Max(0.8f, attacker.StallSeconds * 0.25f)));
            if (!preserveCommittedRoute)
                ResetCommittedRoute(attacker);

            ResetShadowRoute(attacker);
            SetClearanceDebug(attacker, result.StrategicTarget, $"{result.StrategicLabel}:shadow", rejectionReason, rejectionBlocker, rejectionSample);

            if (TryQueueShadowRouteClearanceRetry(uid, attacker, result, avoidPolys, rejectionReason))
                return;

            attacker.RouteMindDecision = $"shadow:reject-clearance:{rejectionReason}";
            attacker.NextShadowRouteThinkAt = now + TimeSpan.FromSeconds(RouteMindObstacleRefreshSeconds);
            return;
        }

        attacker.HasShadowRoute = true;
        attacker.ShadowRouteCost = shadowCost;
        attacker.ShadowRouteTopologyVersion = result.TopologyVersion;
        attacker.ShadowRoutePoints = shadowPoints;
        attacker.ShadowRouteCumulativeCosts = shadowCosts;

        if (ShouldAcceptShadowRoute(uid, attacker, shadowCost, out var decision))
        {
            CommitRoute(uid, attacker, shadowPoints, shadowCosts, shadowPolyKeys, shadowCost, result.TopologyVersion, decision);
        }
        else
        {
            attacker.RouteMindDecision = decision;
            attacker.NextShadowRouteThinkAt = now + TimeSpan.FromSeconds(GetRouteMindRefreshDelay(CompOrNull<NPCSteeringComponent>(uid)));
        }
    }

    private bool ShouldAcceptShadowRoute(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        float shadowCost,
        out string decision)
    {
        decision = "shadow:accept:first";
        if (!attacker.HasCommittedRoute || attacker.CommittedRoutePoints.Count == 0)
            return true;

        var now = _timing.CurTime;
        var steering = CompOrNull<NPCSteeringComponent>(uid);
        var currentCost = Math.Max(0f, attacker.CommittedRouteRemainingCost > 0f
            ? attacker.CommittedRouteRemainingCost
            : attacker.CommittedRouteCost);
        var improvement = currentCost - shadowCost;
        var minimumGain = Math.Max(RouteMindSwitchMinimumCost, currentCost * RouteMindSwitchImprovementRatio) + RouteMindSwitchPenalty;

        if (now < attacker.RouteSwitchCooldownUntil)
        {
            decision = "shadow:hold-cooldown";
            return false;
        }

        if (HasObstacleCommit(attacker, steering))
            minimumGain = Math.Max(minimumGain * 1.75f, RouteMindSwitchMinimumCost * 2f);

        if (now < attacker.RouteCommitUntil)
        {
            decision = "shadow:hold-commit";
            return false;
        }

        if (improvement < minimumGain)
        {
            decision = $"shadow:hold:{improvement:0.0}<{minimumGain:0.0}";
            return false;
        }

        decision = $"shadow:accept:{improvement:0.0}";
        return true;
    }

    private void CommitRoute(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        List<MapCoordinates> routePoints,
        List<float> cumulativeCosts,
        HashSet<PathPolyKey> routePolyKeys,
        float routeCost,
        int topologyVersion,
        string decision)
    {
        attacker.HasCommittedRoute = true;
        attacker.CommittedRouteCursor = 0;
        attacker.CommittedRouteCost = routeCost;
        attacker.CommittedRouteRemainingCost = routeCost;
        attacker.CommittedRoutePoints = routePoints;
        attacker.CommittedRouteCumulativeCosts = cumulativeCosts;
        attacker.CommittedRoutePolyKeys = routePolyKeys;
        attacker.CommittedRouteTopologyVersion = topologyVersion;
        attacker.LastCommittedRouteAt = _timing.CurTime;
        attacker.RouteCommitUntil = _timing.CurTime + TimeSpan.FromSeconds(RouteMindCommitSeconds);
        attacker.RouteSwitchCooldownUntil = _timing.CurTime + TimeSpan.FromSeconds(RouteMindSwitchCooldownSeconds);
        attacker.ShadowRouteClearanceRetryCount = 0;
        attacker.RouteMindDecision = decision;
        attacker.NextShadowRouteThinkAt = _timing.CurTime + TimeSpan.FromSeconds(GetRouteMindRefreshDelay(CompOrNull<NPCSteeringComponent>(uid)));

        _steering.ForceRepath(uid, cancelObstacleHandling: true);
    }

    private HashSet<PathPolyKey> BuildPathPolyKeySet(IReadOnlyList<PathPoly> path)
    {
        var keys = new HashSet<PathPolyKey>();
        foreach (var poly in path)
        {
            keys.Add(PathPolyKey.FromPoly(poly));
        }

        return keys;
    }

    private void QueueShadowRouteEvaluation(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates strategicTarget,
        string strategicLabel,
        WH40KWaveDefenceLocomotionMode mode,
        PathFlags flags,
        float range,
        int topologyVersion)
    {
        var requestEpoch = ++attacker.NavigationRequestEpoch;
        attacker.PendingNavigationRequestEpoch = requestEpoch;
        ApplySharedShadowAvoidance(attacker, strategicLabel, topologyVersion);
        _navigationScheduler.RequestEvaluation(
            uid,
            requestEpoch,
            origin,
            strategicTarget,
            strategicLabel,
            mode,
            flags,
            range,
            topologyVersion,
            attacker.ShadowRouteAvoidPolys);
        attacker.RouteMindDecision = "route-mind:evaluating";
        MarkNavigationState(attacker, $"navigation-request:{strategicLabel}");
        attacker.NextShadowRouteThinkAt = _timing.CurTime + TimeSpan.FromSeconds(GetRouteMindRefreshDelay(CompOrNull<NPCSteeringComponent>(uid)));
    }

    private bool TryQueueShadowRouteClearanceRetry(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        WH40KWaveDefenceNavigationSchedulerSystem.NavigationResult result,
        IReadOnlyCollection<PathPolyKey> avoidPolys,
        string rejectionReason)
    {
        if (avoidPolys.Count == 0 ||
            attacker.ShadowRouteClearanceRetryCount >= ShadowRouteClearanceRetryLimit)
        {
            return false;
        }

        var merged = false;
        foreach (var avoid in avoidPolys)
        {
            merged |= attacker.ShadowRouteAvoidPolys.Add(avoid);
        }

        if (!merged)
            return false;

        attacker.ShadowRouteAvoidTopologyVersion = result.TopologyVersion;
        attacker.ShadowRouteClearanceRetryCount++;
        RememberSharedShadowAvoidance(attacker, result.StrategicLabel, result.TopologyVersion, avoidPolys, rejectionReason);
        var requestEpoch = ++attacker.NavigationRequestEpoch;
        attacker.PendingNavigationRequestEpoch = requestEpoch;
        _navigationScheduler.RequestEvaluation(
            uid,
            requestEpoch,
            result.Origin,
            result.StrategicTarget,
            result.StrategicLabel,
            result.Mode,
            result.Flags,
            result.Range,
            result.TopologyVersion,
            attacker.ShadowRouteAvoidPolys);
        attacker.RouteMindDecision = $"shadow:retry-clearance:{attacker.ShadowRouteClearanceRetryCount}:{rejectionReason}";
        MarkNavigationState(attacker, $"navigation-retry:{result.StrategicLabel}");
        attacker.NextShadowRouteThinkAt = _timing.CurTime + TimeSpan.FromSeconds(RouteMindObstacleRefreshSeconds);
        return true;
    }

    private void UpdateCommittedRouteProgress(WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates origin)
    {
        if (!attacker.HasCommittedRoute ||
            attacker.CommittedRoutePoints.Count == 0 ||
            attacker.CommittedRouteCumulativeCosts.Count != attacker.CommittedRoutePoints.Count)
        {
            attacker.CommittedRouteRemainingCost = 0f;
            attacker.CommittedRouteCursor = 0;
            return;
        }

        if (!TryProjectRouteProgress(attacker.CommittedRoutePoints, attacker.CommittedRouteCumulativeCosts, origin, out var segmentIndex, out _, out var remainingCost))
        {
            attacker.CommittedRouteRemainingCost = attacker.CommittedRouteCost;
            return;
        }

        attacker.CommittedRouteCursor = segmentIndex;
        attacker.CommittedRouteRemainingCost = Math.Clamp(remainingCost, 0f, attacker.CommittedRouteCost);
    }

    private bool TryResolveCommittedRouteAnchor(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (!attacker.HasCommittedRoute ||
            attacker.CommittedRoutePoints.Count == 0 ||
            !TryResolveRouteLeadPoint(attacker.CommittedRoutePoints, origin, RouteMindLeadDistance, out var leadPoint))
        {
            return false;
        }

        var referenceUid = _transform.GetGrid(origin) ?? _transform.GetGrid(attacker.StrategicRouteTarget);
        target = referenceUid != null
            ? _transform.ToCoordinates(referenceUid.Value, leadPoint)
            : _transform.ToCoordinates(leadPoint);

        if (!target.IsValid(EntityManager))
            return false;

        label = $"{attacker.StrategicRouteTargetLabel}:rt {attacker.CommittedRouteRemainingCost:0.0}";
        return true;
    }

    private bool TryResolveSimpleSwarmCommittedShadowAnchor(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (!attacker.HasCommittedRoute ||
            attacker.CommittedRoutePoints.Count == 0)
        {
            return false;
        }

        if (!TryProjectRouteProgress(
                attacker.CommittedRoutePoints,
                attacker.CommittedRouteCumulativeCosts,
                origin,
                out var segmentIndex,
                out var segmentProgress,
                out _))
        {
            return false;
        }

        var currentPoint = attacker.CommittedRoutePoints[Math.Clamp(segmentIndex, 0, attacker.CommittedRoutePoints.Count - 1)];
        var currentMapPosition = currentPoint.Position;
        if (segmentIndex < attacker.CommittedRoutePoints.Count - 1)
        {
            var nextPoint = attacker.CommittedRoutePoints[segmentIndex + 1];
            if (nextPoint.MapId == currentPoint.MapId)
                currentMapPosition = currentPoint.Position + (nextPoint.Position - currentPoint.Position) * segmentProgress;
        }

        var referenceUid = _transform.GetGrid(origin) ?? _transform.GetGrid(attacker.StrategicRouteTarget);
        var furthestValid = MapCoordinates.Nullspace;
        var furthestDistance = 0f;
        var traveled = 0f;

        for (var i = segmentIndex; i < attacker.CommittedRoutePoints.Count - 1; i++)
        {
            var start = i == segmentIndex
                ? currentMapPosition
                : attacker.CommittedRoutePoints[i].Position;
            var end = attacker.CommittedRoutePoints[i + 1].Position;
            var segment = end - start;
            var length = segment.Length();
            if (length <= 0.001f)
                continue;

            var steps = Math.Max(1, (int) MathF.Ceiling(length / SimpleSwarmShadowAnchorProbeStep));
            for (var step = 1; step <= steps; step++)
            {
                var localDistance = length * (step / (float) steps);
                var routeDistance = traveled + localDistance;
                if (routeDistance > SimpleSwarmShadowAnchorDistance)
                    break;

                var candidatePoint = new MapCoordinates(start + Vector2.Normalize(segment) * localDistance, attacker.CommittedRoutePoints[i].MapId);
                var candidate = referenceUid != null
                    ? _transform.ToCoordinates(referenceUid.Value, candidatePoint)
                    : _transform.ToCoordinates(candidatePoint);
                if (!candidate.IsValid(EntityManager))
                    continue;

                if (TryGetPhysicalClearanceFailureAlongPath(uid, attacker, origin, candidate, out _, out _, out _))
                {
                    if (furthestValid.MapId != MapId.Nullspace)
                        goto resolve_anchor;

                    break;
                }

                furthestValid = candidatePoint;
                furthestDistance = routeDistance;
            }

            traveled += length;
            if (traveled >= SimpleSwarmShadowAnchorDistance)
                break;
        }

resolve_anchor:
        if (furthestValid.MapId == MapId.Nullspace)
            return false;

        target = referenceUid != null
            ? _transform.ToCoordinates(referenceUid.Value, furthestValid)
            : _transform.ToCoordinates(furthestValid);

        if (!target.IsValid(EntityManager))
            return false;

        if (furthestDistance < SimpleSwarmShadowAnchorMinimumAdvance &&
            attacker.CommittedRoutePoints.Count > segmentIndex + 1)
        {
            var fallback = attacker.CommittedRoutePoints[Math.Min(attacker.CommittedRoutePoints.Count - 1, segmentIndex + 1)];
            var fallbackTarget = referenceUid != null
                ? _transform.ToCoordinates(referenceUid.Value, fallback)
                : _transform.ToCoordinates(fallback);

            if (fallbackTarget.IsValid(EntityManager) &&
                !TryGetPhysicalClearanceFailureAlongPath(uid, attacker, origin, fallbackTarget, out _, out _, out _))
            {
                target = fallbackTarget;
            }
        }

        label = $"{attacker.StrategicRouteTargetLabel}:shadow";
        return true;
    }

    private bool TryResolveSimpleSwarmShadowHoldTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates shadowTarget,
        EntityCoordinates strategicTarget,
        string strategicLabel,
        out EntityCoordinates holdTarget,
        out string holdLabel)
    {
        holdTarget = EntityCoordinates.Invalid;
        holdLabel = string.Empty;

        if (!origin.TryDistance(EntityManager, shadowTarget, out var distanceToShadow) ||
            distanceToShadow > ShadowReservationAcquireDistance)
        {
            return false;
        }

        if (!TryGetShadowReservationKey(attacker, shadowTarget, out var key))
            return false;

        if (_shadowReservations.TryGetValue(key, out var reservation))
        {
            if (reservation.ExpiresAt <= _timing.CurTime ||
                Deleted(reservation.Holder))
            {
                _shadowReservations.Remove(key);
                reservation = null;
            }
        }

        if (reservation == null)
        {
            _shadowReservations[key] = new ShadowReservationState
            {
                Holder = uid,
                ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(ShadowReservationHoldSeconds),
                Target = shadowTarget,
                Label = strategicLabel
            };

            attacker.DynamicOccupancyHoldUntil = TimeSpan.Zero;
            SetDynamicClearanceDebug(attacker, strategicTarget, $"{strategicLabel}:shadow", "reservation:self", EntityUid.Invalid, shadowTarget);
            return false;
        }

        if (reservation.Holder == uid)
        {
            reservation.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(ShadowReservationHoldSeconds);
            reservation.Target = shadowTarget;
            reservation.Label = strategicLabel;
            attacker.DynamicOccupancyHoldUntil = TimeSpan.Zero;
            SetDynamicClearanceDebug(attacker, strategicTarget, $"{strategicLabel}:shadow", "reservation:self", EntityUid.Invalid, shadowTarget);
            return false;
        }

        holdTarget = origin;
        holdLabel = $"{strategicLabel}:shadow-hold";
        attacker.DynamicOccupancyHoldUntil = _timing.CurTime + TimeSpan.FromSeconds(ShadowReservationHoldSeconds);
        attacker.LastProgressAt = _timing.CurTime;
        attacker.LastForcedTargetProgressAt = _timing.CurTime;
        SetDynamicClearanceDebug(attacker, strategicTarget, $"{strategicLabel}:shadow", "crowded-reserved", reservation.Holder, shadowTarget);
        return true;
    }

    private int GetRouteTopologyVersion(EntityCoordinates strategicTarget)
    {
        var gridUid = _transform.GetGrid(strategicTarget);
        return TryComp<GridPathfindingComponent>(gridUid, out var gridPathfinding)
            ? gridPathfinding.TopologyVersion
            : 0;
    }

    private static float GetRouteMindRefreshDelay(NPCSteeringComponent? steering)
    {
        if (steering == null)
            return RouteMindRefreshSeconds;

        return steering.Status == SteeringStatus.NoPath ||
               steering.ActionableObstacle ||
               steering.ActiveObstacle.IsValid() ||
               steering.DoAfterId != null
            ? RouteMindObstacleRefreshSeconds
            : RouteMindRefreshSeconds;
    }

    private static bool HasObstacleCommit(WH40KWaveDefenceAttackerComponent attacker, NPCSteeringComponent? steering)
    {
        if (steering == null)
            return false;

        return steering.ActionableObstacle ||
               steering.ActiveObstacle.IsValid() ||
               steering.DoAfterId != null ||
               attacker.ActiveSiegeBlocker.IsValid();
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b)
    {
        return a >= b ? a : b;
    }

    private bool TryGetShadowReservationKey(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        out ShadowReservationKey key)
    {
        key = default;
        var mapTarget = _transform.ToMapCoordinates(target);
        if (mapTarget.MapId == MapId.Nullspace ||
            string.IsNullOrWhiteSpace(attacker.LaneId))
        {
            return false;
        }

        var sizeBucket = ResolveShadowSizeBucket(attacker);
        var quantizedX = (int) MathF.Round(mapTarget.Position.X / ShadowReservationQuantization);
        var quantizedY = (int) MathF.Round(mapTarget.Position.Y / ShadowReservationQuantization);
        key = new ShadowReservationKey(attacker.LaneId, mapTarget.MapId, sizeBucket, quantizedX, quantizedY);
        return true;
    }

    private int ResolveShadowSizeBucket(WH40KWaveDefenceAttackerComponent attacker)
    {
        return Math.Max(1, (int) MathF.Round(Math.Max(0.1f, attacker.BodyClearanceDiameter) * ShadowAvoidanceSizeBucketScale));
    }

    private void ReleaseShadowReservations(EntityUid uid)
    {
        var released = new List<ShadowReservationKey>();
        foreach (var (key, reservation) in _shadowReservations)
        {
            if (reservation.Holder == uid)
                released.Add(key);
        }

        foreach (var key in released)
        {
            _shadowReservations.Remove(key);
        }
    }

    private void PruneShadowReservations()
    {
        var expired = new List<ShadowReservationKey>();
        foreach (var (key, reservation) in _shadowReservations)
        {
            if (reservation.ExpiresAt <= _timing.CurTime ||
                Deleted(reservation.Holder))
            {
                expired.Add(key);
            }
        }

        foreach (var key in expired)
        {
            _shadowReservations.Remove(key);
        }
    }

    private SharedShadowChokeKey BuildSharedShadowChokeKey(
        WH40KWaveDefenceAttackerComponent attacker,
        string strategicLabel,
        int topologyVersion)
    {
        return new SharedShadowChokeKey(
            string.IsNullOrWhiteSpace(attacker.LaneId) ? "<none>" : attacker.LaneId,
            strategicLabel,
            topologyVersion,
            ResolveShadowSizeBucket(attacker));
    }

    private void ApplySharedShadowAvoidance(
        WH40KWaveDefenceAttackerComponent attacker,
        string strategicLabel,
        int topologyVersion)
    {
        if (string.IsNullOrWhiteSpace(strategicLabel) ||
            topologyVersion == 0)
        {
            return;
        }

        var key = BuildSharedShadowChokeKey(attacker, strategicLabel, topologyVersion);
        if (!_sharedShadowAvoidances.TryGetValue(key, out var memory))
            return;

        if (memory.ExpiresAt <= _timing.CurTime)
        {
            _sharedShadowAvoidances.Remove(key);
            return;
        }

        if (attacker.ShadowRouteAvoidTopologyVersion != 0 &&
            attacker.ShadowRouteAvoidTopologyVersion != topologyVersion)
        {
            ClearShadowRouteAvoidance(attacker);
        }

        attacker.ShadowRouteAvoidTopologyVersion = topologyVersion;
        foreach (var avoid in memory.AvoidPolys)
        {
            attacker.ShadowRouteAvoidPolys.Add(avoid);
        }
    }

    private void RememberSharedShadowAvoidance(
        WH40KWaveDefenceAttackerComponent attacker,
        string strategicLabel,
        int topologyVersion,
        IReadOnlyCollection<PathPolyKey> avoidPolys,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(strategicLabel) ||
            topologyVersion == 0 ||
            avoidPolys.Count == 0)
        {
            return;
        }

        var key = BuildSharedShadowChokeKey(attacker, strategicLabel, topologyVersion);
        if (!_sharedShadowAvoidances.TryGetValue(key, out var memory))
        {
            memory = new SharedShadowAvoidanceMemory();
            _sharedShadowAvoidances[key] = memory;
        }

        memory.AvoidPolys.Clear();
        foreach (var avoid in avoidPolys)
        {
            memory.AvoidPolys.Add(avoid);
        }

        memory.Reason = reason;
        memory.ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(SharedShadowAvoidanceSeconds);
    }

    private void PruneSharedShadowAvoidances()
    {
        var expired = new List<SharedShadowChokeKey>();
        foreach (var (key, memory) in _sharedShadowAvoidances)
        {
            if (memory.ExpiresAt <= _timing.CurTime)
                expired.Add(key);
        }

        foreach (var key in expired)
        {
            _sharedShadowAvoidances.Remove(key);
        }
    }

    private void ResetRouteMind(WH40KWaveDefenceAttackerComponent attacker, bool clearStrategic)
    {
        ResetCommittedRoute(attacker);
        ResetShadowRoute(attacker);
        attacker.RouteCommitUntil = TimeSpan.Zero;
        attacker.RouteSwitchCooldownUntil = TimeSpan.Zero;
        attacker.LastEvaluatedRouteTopologyVersion = 0;

        if (!clearStrategic)
            return;

        attacker.StrategicRouteTarget = EntityCoordinates.Invalid;
        attacker.StrategicRouteTargetLabel = string.Empty;
        attacker.StrategicRouteTopologyVersion = 0;
    }

    private static void ResetCommittedRoute(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.HasCommittedRoute = false;
        attacker.CommittedRouteCursor = 0;
        attacker.CommittedRouteCost = 0f;
        attacker.CommittedRouteRemainingCost = 0f;
        attacker.CommittedRouteTopologyVersion = 0;
        attacker.CommittedRoutePoints.Clear();
        attacker.CommittedRouteCumulativeCosts.Clear();
        attacker.CommittedRoutePolyKeys.Clear();
    }

    private static void ResetShadowRoute(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.HasShadowRoute = false;
        attacker.ShadowRouteCost = 0f;
        attacker.ShadowRouteTopologyVersion = 0;
        attacker.ShadowRoutePoints.Clear();
        attacker.ShadowRouteCumulativeCosts.Clear();
    }

    private static void ClearShadowRouteAvoidance(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.ShadowRouteAvoidPolys.Clear();
        attacker.ShadowRouteAvoidTopologyVersion = 0;
        attacker.ShadowRouteClearanceRetryCount = 0;
    }

    private List<MapCoordinates> BuildRoutePoints(
        IReadOnlyList<PathPoly> path,
        EntityCoordinates origin,
        EntityCoordinates strategicTarget)
    {
        var points = new List<MapCoordinates>(path.Count + 2);
        var originMap = _transform.ToMapCoordinates(origin);
        if (originMap.MapId != MapId.Nullspace)
            AddDistinctRoutePoint(points, originMap);

        if (path.Count == 0)
        {
            AddDistinctRoutePoint(points, _transform.ToMapCoordinates(strategicTarget));
            return points;
        }

        for (var i = 0; i < path.Count - 1; i++)
        {
            if (TryResolvePathPortal(path[i], path[i + 1], out var portalPoint, out _))
                AddDistinctRoutePoint(points, portalPoint);
            else
                AddDistinctRoutePoint(points, _transform.ToMapCoordinates(path[i + 1].Coordinates));
        }

        AddDistinctRoutePoint(points, _transform.ToMapCoordinates(strategicTarget));
        if (points.Count == 0)
            points.Add(_transform.ToMapCoordinates(path[^1].Coordinates));

        return points;
    }

    private void AddDistinctRoutePoint(List<MapCoordinates> points, MapCoordinates candidate)
    {
        if (candidate.MapId == MapId.Nullspace)
            return;

        if (points.Count == 0)
        {
            points.Add(candidate);
            return;
        }

        var current = points[^1];
        if (current.MapId == candidate.MapId &&
            Vector2.DistanceSquared(current.Position, candidate.Position) <= ShadowRoutePointMergeDistance * ShadowRoutePointMergeDistance)
        {
            return;
        }

        points.Add(candidate);
    }

    private bool TryResolvePathPortal(
        PathPoly current,
        PathPoly next,
        out MapCoordinates portal,
        out float portalSpan)
    {
        portal = MapCoordinates.Nullspace;
        portalSpan = 0f;

        if (current.GraphUid != next.GraphUid)
            return false;

        var left = MathF.Max(current.Box.Left, next.Box.Left);
        var right = MathF.Min(current.Box.Right, next.Box.Right);
        var bottom = MathF.Max(current.Box.Bottom, next.Box.Bottom);
        var top = MathF.Min(current.Box.Top, next.Box.Top);

        if (right < left - 0.001f || top < bottom - 0.001f)
            return false;

        var width = MathF.Max(0f, right - left);
        var height = MathF.Max(0f, top - bottom);
        portalSpan = MathF.Max(width, height);
        var localCenter = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
        portal = _transform.ToMapCoordinates(new EntityCoordinates(current.GraphUid, localCenter));
        return portal.MapId != MapId.Nullspace;
    }

    private bool TryValidateShadowRouteClearance(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        IReadOnlyList<PathPoly> path,
        IReadOnlyList<MapCoordinates> routePoints,
        EntityCoordinates strategicTarget,
        string strategicLabel,
        out string reason,
        out EntityUid blocker,
        out EntityCoordinates sample,
        out HashSet<PathPolyKey> avoidPolys)
    {
        avoidPolys = new HashSet<PathPolyKey>();
        blocker = EntityUid.Invalid;
        sample = EntityCoordinates.Invalid;

        if (path.Count == 0 || routePoints.Count == 0)
        {
            reason = "shadow-empty";
            return false;
        }

        var requiredSpan = ResolveBodyClearanceRadius(uid, attacker) * 2f + MinimumBodyClearanceMargin * 2f;
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (!TryResolvePathPortal(path[i], path[i + 1], out var portal, out var portalSpan))
                continue;

            if (portalSpan + ShadowRoutePortalClearanceSlack >= requiredSpan)
                continue;

            avoidPolys.Add(PathPolyKey.FromPoly(path[i]));
            avoidPolys.Add(PathPolyKey.FromPoly(path[i + 1]));
            sample = _transform.ToCoordinates(portal);
            reason = $"shadow-portal:{i + 1}/{path.Count - 1}:{portalSpan:0.00}<{requiredSpan:0.00}";
            return false;
        }

        for (var i = 0; i < routePoints.Count - 1; i++)
        {
            var from = _transform.ToCoordinates(routePoints[i]);
            var to = _transform.ToCoordinates(routePoints[i + 1]);
            if (!from.IsValid(EntityManager) || !to.IsValid(EntityManager))
                continue;

            if (!TryGetPhysicalClearanceFailureAlongPath(uid, attacker, from, to, out var segmentReason, out blocker, out sample))
                continue;

            var polyIndex = Math.Clamp(i, 0, path.Count - 1);
            avoidPolys.Add(PathPolyKey.FromPoly(path[polyIndex]));
            if (polyIndex > 0)
                avoidPolys.Add(PathPolyKey.FromPoly(path[polyIndex - 1]));
            if (polyIndex + 1 < path.Count)
                avoidPolys.Add(PathPolyKey.FromPoly(path[polyIndex + 1]));

            reason = $"shadow-segment:{i + 1}/{routePoints.Count - 1}:{segmentReason}";
            return false;
        }

        SetClearanceDebug(attacker, strategicTarget, $"{strategicLabel}:shadow", "route-clear", EntityUid.Invalid, EntityCoordinates.Invalid);
        reason = "route-clear";
        return true;
    }

    private bool TryProjectRouteProgress(
        IReadOnlyList<MapCoordinates> routePoints,
        IReadOnlyList<float> cumulativeCosts,
        EntityCoordinates origin,
        out int segmentIndex,
        out float segmentProgress,
        out float remainingCost)
    {
        segmentIndex = 0;
        segmentProgress = 0f;
        remainingCost = 0f;

        if (routePoints.Count == 0 || cumulativeCosts.Count != routePoints.Count)
            return false;

        var originMap = _transform.ToMapCoordinates(origin);
        if (originMap.MapId == MapId.Nullspace)
            return false;

        if (routePoints.Count == 1)
        {
            remainingCost = 0f;
            return true;
        }

        var bestDistance = float.MaxValue;
        var bestIndex = 0;
        var bestProgress = 0f;

        for (var i = 0; i < routePoints.Count - 1; i++)
        {
            var start = routePoints[i];
            var end = routePoints[i + 1];
            if (start.MapId == MapId.Nullspace || start.MapId != originMap.MapId || start.MapId != end.MapId)
                continue;

            var segment = end.Position - start.Position;
            var segmentLengthSquared = segment.LengthSquared();
            if (segmentLengthSquared <= 0.0001f)
                continue;

            var progress = Math.Clamp(Vector2.Dot(originMap.Position - start.Position, segment) / segmentLengthSquared, 0f, 1f);
            var closest = start.Position + segment * progress;
            var distance = Vector2.Distance(originMap.Position, closest);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
            bestProgress = progress;
        }

        segmentIndex = bestIndex;
        segmentProgress = bestProgress;

        var startCost = cumulativeCosts[bestIndex];
        var endCost = cumulativeCosts[Math.Min(bestIndex + 1, cumulativeCosts.Count - 1)];
        var traversedCost = startCost + (endCost - startCost) * bestProgress;
        var totalCost = cumulativeCosts[^1];
        remainingCost = Math.Max(0f, totalCost - traversedCost);
        return true;
    }

    private bool TryResolveRouteLeadPoint(
        IReadOnlyList<MapCoordinates> routePoints,
        EntityCoordinates origin,
        float leadDistance,
        out MapCoordinates point)
    {
        point = MapCoordinates.Nullspace;
        if (routePoints.Count == 0)
            return false;

        if (routePoints.Count == 1)
        {
            point = routePoints[0];
            return true;
        }

        if (!TryProjectRouteProgress(routePoints, BuildLinearCosts(routePoints), origin, out var segmentIndex, out var segmentProgress, out _))
        {
            point = routePoints[^1];
            return true;
        }

        var currentStart = routePoints[segmentIndex];
        var currentEnd = routePoints[Math.Min(segmentIndex + 1, routePoints.Count - 1)];
        if (currentStart.MapId == MapId.Nullspace || currentStart.MapId != currentEnd.MapId)
        {
            point = routePoints[^1];
            return true;
        }

        var segmentVector = currentEnd.Position - currentStart.Position;
        var segmentLength = segmentVector.Length();
        if (segmentLength <= 0.001f)
        {
            point = routePoints[^1];
            return true;
        }

        var currentPosition = currentStart.Position + segmentVector * segmentProgress;
        var remaining = leadDistance;
        var currentIndex = segmentIndex;

        while (currentIndex < routePoints.Count - 1)
        {
            var start = currentIndex == segmentIndex ? currentPosition : routePoints[currentIndex].Position;
            var end = routePoints[currentIndex + 1].Position;
            var segment = end - start;
            var length = segment.Length();
            if (length <= 0.001f)
            {
                currentIndex++;
                continue;
            }

            if (remaining <= length)
            {
                point = new MapCoordinates(start + Vector2.Normalize(segment) * remaining, routePoints[currentIndex].MapId);
                return true;
            }

            remaining -= length;
            currentIndex++;
        }

        point = routePoints[^1];
        return true;
    }

    private static List<float> BuildLinearCosts(IReadOnlyList<MapCoordinates> routePoints)
    {
        var costs = new List<float>(routePoints.Count);
        if (routePoints.Count == 0)
            return costs;

        costs.Add(0f);
        var total = 0f;
        for (var i = 1; i < routePoints.Count; i++)
        {
            if (routePoints[i - 1].MapId == routePoints[i].MapId)
                total += Vector2.Distance(routePoints[i - 1].Position, routePoints[i].Position);

            costs.Add(total);
        }

        return costs;
    }

    private bool TryResolveStrategicLaneTargetOverride(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        int strategicPointIndex,
        out EntityCoordinates target)
    {
        target = EntityCoordinates.Invalid;

        if (strategicPointIndex < attacker.LanePointIndex)
            return false;

        return TryResolveLaneTraversalTarget(uid, attacker, origin, 0.2f, out target, out _);
    }

    private bool TryResolveLaneTraversalTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        float movementRange,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (attacker.RouteCompleted ||
            attacker.LanePointIndex < 0 ||
            attacker.LanePointIndex >= attacker.LanePoints.Count ||
            !TryResolveLaneTraversalGeometry(uid, attacker, attacker.LanePointIndex, origin, movementRange, out var pointUid, out var pointCoordinates, out var exitDirection, out var gateWidth, out var anchorDepth))
        {
            return false;
        }

        if (!origin.TryDistance(EntityManager, pointCoordinates, out var pointDistance))
            return false;

        var activationDistance = ResolvePointArrivalRange(attacker, pointUid) + anchorDepth + GateTraversalActivationSlack;
        if (pointDistance > activationDistance)
            return false;

        var pointMap = _transform.ToMapCoordinates(pointCoordinates);
        if (pointMap.MapId == MapId.Nullspace)
            return false;

        var referenceUid = _transform.GetGrid(origin) ?? _transform.GetGrid(pointCoordinates) ?? pointCoordinates.EntityId;
        if (!TryResolveAdaptiveLaneTraversalTarget(
                uid,
                attacker,
                origin,
                referenceUid,
                pointMap,
                exitDirection,
                gateWidth,
                anchorDepth,
                out target))
        {
            return false;
        }

        label = $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}:cross";
        if (TryResolveLaneBlockerTarget(uid, attacker, origin, target, exitDirection, out var breachTarget, out var breachLabel))
        {
            target = breachTarget;
            label = breachLabel;
        }

        return true;
    }

    private bool TryResolveObjectiveLocomotionTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        float meleeRange,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        if (attacker.Objective is not { } objective || Deleted(objective))
            return false;

        if (!_objectiveNavigation.TryResolveObjectiveMeleeTarget(uid, origin, objective, meleeRange, out var assaultTarget, out var blocker))
            return false;

        attacker.ActiveSiegeBlocker = blocker;
        attacker.ActiveSiegeBlockerLabel = blocker.IsValid() ? $"objective-blocker:{ToPrettyString(blocker)}" : string.Empty;

        if (!blocker.IsValid() &&
            CanReuseStickyObjectiveTarget(attacker, assaultTarget, Transform(objective).Coordinates, meleeRange))
        {
            target = attacker.StickyObjectiveTarget;
        }
        else if (!blocker.IsValid() &&
                 _objectiveNavigation.TryResolveSwarmAttackSlotTarget(uid, origin, assaultTarget, Transform(objective).Coordinates, meleeRange, out var slottedTarget))
        {
            target = slottedTarget;
            attacker.StickyObjectiveTarget = slottedTarget;
            attacker.StickyObjectiveTargetUntil = _timing.CurTime + TimeSpan.FromSeconds(StickyObjectiveSeconds);
        }
        else
        {
            target = assaultTarget;
            attacker.StickyObjectiveTarget = assaultTarget;
            attacker.StickyObjectiveTargetUntil = _timing.CurTime + TimeSpan.FromSeconds(StickyObjectiveSeconds);
        }

        label = $"objective:{ToPrettyString(objective)}";
        return true;
    }

    private bool CanReuseStickyObjectiveTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates assaultTarget,
        EntityCoordinates objectiveCoordinates,
        float meleeRange)
    {
        if (attacker.StickyObjectiveTargetUntil == TimeSpan.Zero ||
            _timing.CurTime >= attacker.StickyObjectiveTargetUntil ||
            !attacker.StickyObjectiveTarget.IsValid(EntityManager) ||
            _pathfinding.GetPoly(attacker.StickyObjectiveTarget) == null)
        {
            return false;
        }

        if (!attacker.StickyObjectiveTarget.TryDistance(EntityManager, objectiveCoordinates, out var objectiveDistance) ||
            objectiveDistance > meleeRange + 0.05f)
        {
            return false;
        }

        return SameCoordinates(attacker.StickyObjectiveTarget, assaultTarget) ||
               attacker.StickyObjectiveTarget.TryDistance(EntityManager, assaultTarget, out var distance) &&
               distance <= StickyObjectiveReuseDistance;
    }

    private void ApplyLocomotionTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        EntityCoordinates target,
        string label,
        WH40KWaveDefenceLocomotionMode mode)
    {
        attacker.LocomotionMode = mode;
        attacker.LocomotionTarget = target;
        attacker.LocomotionTargetLabel = label;
        attacker.MovementTargetDirective = target;
        attacker.MovementTargetDirectiveLabel = label;
        MarkNavigationState(attacker, label);
        attacker.LastTargetPushAt = _timing.CurTime;
        attacker.LastTargetPushReason = mode == WH40KWaveDefenceLocomotionMode.Route
            ? "locomotion-route-follow"
            : "locomotion-objective-follow";
        attacker.LastTargetPushLabel = label;
        attacker.LastTargetPushCoordinates = target;
        attacker.LastLoggedReactionDelayAt = TimeSpan.Zero;

        var targetKey = mode == WH40KWaveDefenceLocomotionMode.Objective
            ? WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates
            : WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates;
        _npc.SetBlackboard(uid, targetKey, target, htn);
        var steeringRangeOverride = mode == WH40KWaveDefenceLocomotionMode.Objective
            ? ResolveObjectiveSteeringRange(htn)
            : mode == WH40KWaveDefenceLocomotionMode.Route
                ? ResolveRouteSteeringRange(label)
            : (float?) null;
        ApplySteeringTarget(uid, htn, target, steeringRangeOverride);

        if (mode != WH40KWaveDefenceLocomotionMode.Route)
        {
            attacker.ActiveRouteTarget = EntityCoordinates.Invalid;
            attacker.ActiveRouteTargetLabel = string.Empty;
            ClearPreparedLanePlan(attacker);
        }
    }

    private void ApplySteeringTarget(EntityUid uid, HTNComponent htn, EntityCoordinates target, float? rangeOverride = null)
    {
        var range = rangeOverride ?? htn.Blackboard.GetValueOrDefault<float>(MovementRangeKey, EntityManager);
        if (range <= 0f)
            range = 0.2f;

        var flags = _pathfinding.GetFlags(htn.Blackboard);
        var directMove = ShouldUseDirectMove(uid, target);

        if (!TryComp(uid, out NPCSteeringComponent? steering))
        {
            steering = _steering.Register(uid, target);
            ConfigureSteering(steering, range, flags, directMove);
            return;
        }

        var hardRetarget = steering.Status == SteeringStatus.NoPath ||
                           steering.DirectMove != directMove ||
                           steering.Coordinates.EntityId != target.EntityId ||
                           HasLargeCoordinateChange(steering.Coordinates, target);

        if (hardRetarget)
        {
            _steering.Register(uid, target, steering);
            ConfigureSteering(steering, range, flags, directMove);
            return;
        }

        steering.Coordinates = target;
        steering.Range = range;
        steering.Flags = flags;
        steering.ArriveOnLineOfSight = false;
        steering.InRangeMaxSpeed ??= 0.03f;
        steering.DirectMove = directMove;

        if (steering.Status != SteeringStatus.Moving)
            steering.Status = SteeringStatus.Moving;
    }

    private float ResolveObjectiveSteeringRange(HTNComponent htn)
    {
        var movementRange = htn.Blackboard.GetValueOrDefault<float>(MovementRangeKey, EntityManager);
        if (movementRange <= 0f)
            movementRange = 1.5f;

        var meleeRange = htn.Blackboard.GetValueOrDefault<float>(MeleeRangeKey, EntityManager);
        if (meleeRange <= 0f)
            meleeRange = 1f;

        var tightenedRange = Math.Clamp(meleeRange * ObjectiveSteeringRangeScale, ObjectiveSteeringMinimumRange, ObjectiveSteeringMaximumRange);
        return Math.Min(movementRange, tightenedRange);
    }

    private float ResolveRouteSteeringRange(string label)
    {
        if (label.Contains(":path", StringComparison.Ordinal))
            return RouteSteeringPathRange;

        if (label.Contains(":cross", StringComparison.Ordinal))
            return RouteSteeringCrossRange;

        if (label.Contains(":breach", StringComparison.Ordinal))
            return RouteSteeringBreachRange;

        return RouteSteeringDefaultRange;
    }

    private bool ShouldUseDirectMove(EntityUid uid, EntityCoordinates target)
    {
        var ourMap = Transform(uid).MapID;
        var targetMap = _transform.ToMapCoordinates(target).MapId;

        if (ourMap == MapId.Nullspace || targetMap == MapId.Nullspace || ourMap != targetMap)
            return true;

        var ourGrid = Transform(uid).GridUid;
        var targetGrid = _transform.GetGrid(target);
        return ourGrid == null || targetGrid == null || ourGrid != targetGrid;
    }

    private static void ConfigureSteering(
        NPCSteeringComponent steering,
        float range,
        PathFlags flags,
        bool directMove)
    {
        steering.Range = range;
        steering.Flags = flags;
        steering.ArriveOnLineOfSight = false;
        steering.InRangeMaxSpeed ??= 0.03f;
        steering.DirectMove = directMove;
    }

    private void RefreshProgressScore(WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates origin)
    {
        var score = attacker.CurrentRouteProgressRatio * 10000f;
        if (attacker.RouteCompleted &&
            attacker.Objective is { } objective &&
            !Deleted(objective) &&
            origin.TryDistance(EntityManager, Transform(objective).Coordinates, out var objectiveDistance))
        {
            score = attacker.TotalLanePointCount * 1000f + 500f - objectiveDistance;
        }

        if (score <= attacker.BestProgressScore + 0.15f)
            return;

        attacker.BestProgressScore = score;
        attacker.LastProgressAt = _timing.CurTime;
    }

    private bool ShouldAdvanceLanePoint(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        float currentProgress,
        float frontProgress,
        EntityUid pointUid,
        float pointProgress,
        float totalLength)
    {
        var epsilon = ResolvePointProgressEpsilon(attacker, attacker.LanePointIndex, totalLength);
        if (currentProgress >= pointProgress + epsilon)
            return true;

        if (TryResolveLaneTraversalGeometry(uid, attacker, attacker.LanePointIndex, origin, 0.2f, out _, out var pointCoordinates, out var exitDirection, out var gateWidth, out var anchorDepth) &&
            HasCrossedLaneGate(origin, pointCoordinates, exitDirection, gateWidth, Math.Max(GateTraversalPassThreshold, anchorDepth * 0.3f)))
        {
            return true;
        }

        if (currentProgress + epsilon >= pointProgress)
            return true;

        var frontPassedGate = frontProgress >= pointProgress + SwarmFrontAssistLead || frontProgress >= 0.999f;
        var almostAtGate = currentProgress + Math.Max(epsilon, SwarmFrontAssistSlack) >= pointProgress;
        var closeEnoughToGate =
            TryComp(pointUid, out TransformComponent? pointXform) &&
            origin.TryDistance(EntityManager, pointXform.Coordinates, out var gateDistance) &&
            gateDistance <= ResolvePointArrivalRange(attacker, pointUid) + 0.45f;

        if (ShouldForceAdvanceStalledLanePoint(
                uid,
                attacker,
                origin,
                currentProgress,
                pointProgress,
                pointUid,
                epsilon))
        {
            return true;
        }

        return frontPassedGate && (almostAtGate || closeEnoughToGate);
    }

    private void UpdateSimpleSwarmRouteProgress(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates origin)
    {
        while (attacker.LanePointIndex < attacker.LanePoints.Count && Deleted(attacker.LanePoints[attacker.LanePointIndex]))
        {
            MarkLanePointReached(attacker, attacker.LanePointIndex);
        }

        if (!attacker.RouteStartCoordinates.IsValid(EntityManager))
            attacker.RouteStartCoordinates = origin;

        if (!TryBuildRouteGeometry(attacker, out var vertices, out var pointVertices, out var totalLength))
        {
            attacker.CurrentRouteProgressRatio = attacker.TotalLanePointCount == 0 ? 1f : 0f;
            attacker.RouteProgressRatio = attacker.CurrentRouteProgressRatio;
            attacker.RouteCompleted = attacker.TotalLanePointCount == 0;
            return;
        }

        var currentProgress = ComputeSimpleSwarmProgress(attacker, vertices, totalLength, origin);
        attacker.CurrentRouteProgressRatio = currentProgress;
        attacker.RouteProgressRatio = Math.Max(attacker.RouteProgressRatio, currentProgress);
        var frontProgress = Math.Max(GetSharedLaneFrontProgress(uid, attacker), currentProgress);
        attacker.SharedLaneFrontProgress = frontProgress;

        while (attacker.LanePointIndex < attacker.LanePoints.Count)
        {
            var pointUid = attacker.LanePoints[attacker.LanePointIndex];
            if (Deleted(pointUid))
            {
                MarkLanePointReached(attacker, attacker.LanePointIndex);
                continue;
            }

            if (!TryGetPointProgressRatio(attacker.LanePointIndex, pointVertices, vertices, totalLength, out var pointProgress))
                break;

            if (!ShouldAdvanceLanePoint(uid, attacker, origin, currentProgress, frontProgress, pointUid, pointProgress, totalLength))
                break;

            MarkLanePointReached(attacker, attacker.LanePointIndex);
        }

        TryPromoteFinalLanePointToObjective(attacker, origin, currentProgress, frontProgress);

        attacker.RouteCompleted = attacker.TotalLanePointCount == 0 || attacker.LanePointIndex >= attacker.TotalLanePointCount;
        if (attacker.RouteCompleted)
        {
            attacker.CurrentRouteProgressRatio = 1f;
            attacker.RouteProgressRatio = 1f;
        }

    }

    private bool TryResolveSimpleSwarmRouteTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label)
    {
        label = string.Empty;
        target = EntityCoordinates.Invalid;

        if (attacker.LanePointIndex >= attacker.LanePoints.Count)
            return false;

        if (!TryBuildRouteGeometry(attacker, out var vertices, out _, out var totalLength) || totalLength <= 0.05f)
            return false;

        var currentProgress = Math.Clamp(attacker.CurrentRouteProgressRatio, 0f, 0.999f);
        var laneFront = GetSharedLaneFrontProgress(uid, attacker);
        attacker.SharedLaneFrontProgress = laneFront;
        var leadProgress = Math.Clamp(
            SwarmMinimumLeadDistance / totalLength,
            SwarmLeadProgress,
            Math.Max(SwarmLeadProgress, SwarmCatchupLimit - 0.02f));

        var targetProgress = Math.Clamp(
            Math.Max(
                currentProgress + leadProgress,
                Math.Min(laneFront + SwarmFrontSlack, currentProgress + SwarmCatchupLimit)),
            currentProgress + 0.01f,
            0.999f);

        if (!TryResolveProgressCoordinate(
                attacker,
                vertices,
                totalLength,
                targetProgress,
                out var baseTarget,
                out var segmentDirection,
                out var segmentWidth))
        {
            return false;
        }

        if (!TryResolveSwarmBandTarget(uid, attacker, origin, baseTarget, segmentDirection, segmentWidth, out target))
            target = baseTarget;

        attacker.ActiveRouteTarget = target;
        label = $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}";
        attacker.ActiveRouteTargetLabel = label;
        return true;
    }

    private float GetSharedLaneFrontProgress(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker)
    {
        if (string.IsNullOrWhiteSpace(attacker.LaneId))
            return attacker.CurrentRouteProgressRatio;

        var ownMap = _transform.ToMapCoordinates(attacker.RouteStartCoordinates).MapId;
        var front = attacker.CurrentRouteProgressRatio;
        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, TransformComponent>();
        while (query.MoveNext(out var otherUid, out var other, out var xform))
        {
            if (otherUid == uid ||
                !other.RuntimeInitialized ||
                !HasComp<ActiveNPCComponent>(otherUid) ||
                other.AiProfile != WH40KWaveAiProfile.SimpleSwarm ||
                xform.MapID == MapId.Nullspace ||
                xform.MapID != ownMap ||
                !string.Equals(other.LaneId, attacker.LaneId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            front = Math.Max(front, other.CurrentRouteProgressRatio);
        }

        return Math.Clamp(front, 0f, 1f);
    }

    private bool TryBuildRouteGeometry(
        WH40KWaveDefenceAttackerComponent attacker,
        out List<EntityCoordinates> vertices,
        out List<(int PointIndex, int VertexIndex)> pointVertices,
        out float totalLength)
    {
        vertices = new List<EntityCoordinates>(attacker.LanePoints.Count + 1);
        pointVertices = new List<(int PointIndex, int VertexIndex)>(attacker.LanePoints.Count);
        totalLength = 0f;

        if (!attacker.RouteStartCoordinates.IsValid(EntityManager))
            return false;

        vertices.Add(attacker.RouteStartCoordinates);

        for (var i = 0; i < attacker.LanePoints.Count; i++)
        {
            var pointUid = attacker.LanePoints[i];
            if (Deleted(pointUid) || !TryComp(pointUid, out TransformComponent? xform))
                continue;

            vertices.Add(xform.Coordinates);
            pointVertices.Add((i, vertices.Count - 1));
        }

        if (vertices.Count <= 1)
            return false;

        for (var i = 0; i < vertices.Count - 1; i++)
        {
            var start = _transform.ToMapCoordinates(vertices[i]);
            var end = _transform.ToMapCoordinates(vertices[i + 1]);
            if (start.MapId == MapId.Nullspace || start.MapId != end.MapId)
                continue;

            totalLength += Vector2.Distance(start.Position, end.Position);
        }

        return totalLength > 0.05f;
    }

    private float ComputeSimpleSwarmProgress(
        WH40KWaveDefenceAttackerComponent attacker,
        List<EntityCoordinates> vertices,
        float totalLength,
        EntityCoordinates origin)
    {
        if (vertices.Count <= 1 || totalLength <= 0.05f)
            return 0f;

        var originMap = _transform.ToMapCoordinates(origin);
        if (originMap.MapId == MapId.Nullspace)
            return 0f;

        var bestDistance = float.MaxValue;
        var bestProgress = 0f;
        var startSegment = Math.Clamp(attacker.LastReachedLanePointIndex, 0, Math.Max(0, vertices.Count - 2));
        var endSegment = Math.Clamp(attacker.LanePointIndex + 1, 0, Math.Max(0, vertices.Count - 2));
        var cumulative = 0f;

        for (var i = 0; i < vertices.Count - 1; i++)
        {
            var startMap = _transform.ToMapCoordinates(vertices[i]);
            var endMap = _transform.ToMapCoordinates(vertices[i + 1]);
            if (startMap.MapId == MapId.Nullspace || startMap.MapId != endMap.MapId)
                continue;

            var segment = endMap.Position - startMap.Position;
            var segmentLength = segment.Length();
            if (segmentLength <= 0.001f)
            {
                cumulative += segmentLength;
                continue;
            }

            if (i < startSegment || i > endSegment)
            {
                cumulative += segmentLength;
                continue;
            }

            var projection = Vector2.Dot(originMap.Position - startMap.Position, segment) / segment.LengthSquared();
            var clampedProjection = Math.Clamp(projection, 0f, 1f);
            var closest = startMap.Position + segment * clampedProjection;
            var distance = Vector2.Distance(originMap.Position, closest);
            var width = ResolveSegmentWidth(attacker, i);
            if (distance > width)
            {
                cumulative += segmentLength;
                continue;
            }

            var score = distance + MathF.Abs(i - attacker.LanePointIndex) * 0.35f;
            if (score < bestDistance)
            {
                bestDistance = score;
                bestProgress = (cumulative + segmentLength * clampedProjection) / totalLength;
            }

            cumulative += segmentLength;
        }

        if (bestDistance == float.MaxValue)
            return Math.Clamp(ComputeRouteProgressRatio(attacker, origin), 0f, 0.999f);

        return Math.Clamp(bestProgress, 0f, 0.999f);
    }

    private bool TryGetPointProgressRatio(
        int pointIndex,
        List<(int PointIndex, int VertexIndex)> pointVertices,
        List<EntityCoordinates> vertices,
        float totalLength,
        out float ratio)
    {
        ratio = 0f;
        if (totalLength <= 0.05f)
            return false;

        var entryIndex = pointVertices.FindIndex(entry => entry.PointIndex == pointIndex);
        if (entryIndex == -1)
            return false;

        var vertexIndex = pointVertices[entryIndex].VertexIndex;
        var cumulative = 0f;
        for (var i = 0; i < vertexIndex; i++)
        {
            var start = _transform.ToMapCoordinates(vertices[i]);
            var end = _transform.ToMapCoordinates(vertices[i + 1]);
            if (start.MapId == MapId.Nullspace || start.MapId != end.MapId)
                continue;

            cumulative += Vector2.Distance(start.Position, end.Position);
        }

        ratio = Math.Clamp(cumulative / totalLength, 0f, 1f);
        return true;
    }

    private float ResolvePointProgressEpsilon(WH40KWaveDefenceAttackerComponent attacker, int pointIndex, float totalLength)
    {
        if (totalLength <= 0.05f)
            return SwarmProgressEpsilon;

        var pointUid = attacker.LanePoints[pointIndex];
        if (TryComp<WH40KWaveLanePointComponent>(pointUid, out var point))
        {
            var width = point.ProgressGateWidth > 0.05f
                ? point.ProgressGateWidth
                : point.ArrivalRange > 0.05f
                    ? point.ArrivalRange
                    : attacker.PointArrivalRange;
            return Math.Max(SwarmProgressEpsilon, width / totalLength);
        }

        return SwarmProgressEpsilon;
    }

    private float ResolveSegmentWidth(WH40KWaveDefenceAttackerComponent attacker, int segmentIndex)
    {
        var pointIndex = Math.Clamp(segmentIndex, 0, attacker.LanePoints.Count - 1);
        if (pointIndex >= 0 &&
            pointIndex < attacker.LanePoints.Count &&
            TryComp<WH40KWaveLanePointComponent>(attacker.LanePoints[pointIndex], out var point))
        {
            if (point.SegmentWidth > 0.05f)
                return point.SegmentWidth;

            if (point.ArrivalRange > 0.05f)
                return Math.Max(1.1f, point.ArrivalRange * 1.8f);
        }

        return 2.1f;
    }

    private bool TryResolveProgressCoordinate(
        WH40KWaveDefenceAttackerComponent attacker,
        List<EntityCoordinates> vertices,
        float totalLength,
        float progress,
        out EntityCoordinates coordinates,
        out Vector2 direction,
        out float segmentWidth)
    {
        coordinates = EntityCoordinates.Invalid;
        direction = Vector2.UnitX;
        segmentWidth = 1.5f;

        if (vertices.Count <= 1 || totalLength <= 0.05f)
            return false;

        var targetDistance = Math.Clamp(progress, 0f, 1f) * totalLength;
        var cumulative = 0f;
        for (var i = 0; i < vertices.Count - 1; i++)
        {
            var start = _transform.ToMapCoordinates(vertices[i]);
            var end = _transform.ToMapCoordinates(vertices[i + 1]);
            if (start.MapId == MapId.Nullspace || start.MapId != end.MapId)
                continue;

            var segmentVector = end.Position - start.Position;
            var segmentLength = segmentVector.Length();
            if (segmentLength <= 0.001f)
                continue;

            if (targetDistance > cumulative + segmentLength && i < vertices.Count - 2)
            {
                cumulative += segmentLength;
                continue;
            }

            var remaining = Math.Clamp(targetDistance - cumulative, 0f, segmentLength);
            var position = start.Position + segmentVector / segmentLength * remaining;
            coordinates = _transform.ToCoordinates(
                vertices[i].EntityId,
                new MapCoordinates(position, start.MapId));
            direction = Vector2.Normalize(segmentVector);
            segmentWidth = ResolveSegmentWidth(attacker, i);
            return true;
        }

        coordinates = vertices[^1];
        return true;
    }

    private bool TryResolveSwarmBandTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates baseTarget,
        Vector2 direction,
        float segmentWidth,
        out EntityCoordinates target)
    {
        target = baseTarget;
        var mapTarget = _transform.ToMapCoordinates(baseTarget);
        if (mapTarget.MapId == MapId.Nullspace)
            return false;

        if (direction.LengthSquared() <= 0.001f)
            direction = Vector2.UnitX;

        var perpendicular = new Vector2(-direction.Y, direction.X);
        var bandScale = Math.Clamp(segmentWidth * 0.35f, 0.2f, 0.95f);
        var preferredOffset = SwarmBandOffsets[Math.Clamp(attacker.SwarmBandIndex, 0, SwarmBandOffsets.Length - 1)] * bandScale;
        var candidateOffsets = BuildSwarmBandCandidateOffsets(preferredOffset, bandScale, segmentWidth);
        var forwardOffsets = new[] { 0f, 0.25f, -0.2f };

        foreach (var lateral in candidateOffsets)
        {
            foreach (var forward in forwardOffsets)
            {
                var candidatePosition = mapTarget.Position + perpendicular * lateral + direction * forward;
                var candidate = _transform.ToCoordinates(
                    baseTarget.EntityId,
                    new MapCoordinates(candidatePosition, mapTarget.MapId));

                if (_pathfinding.GetPoly(candidate) == null)
                    continue;

                if (HasHardLaneCrowding(uid, attacker, origin, candidate) ||
                    EvaluateBodyClearanceScore(uid, attacker, segmentWidth * 0.5f, lateral) == float.MinValue)
                    continue;

                target = candidate;
                return true;
            }
        }

        return _pathfinding.GetPoly(baseTarget) != null;
    }

    private static float[] BuildSwarmBandCandidateOffsets(float preferredOffset, float bandScale, float segmentWidth)
    {
        if (MathF.Abs(preferredOffset) <= 0.05f)
        {
            if (segmentWidth >= 1.8f)
            {
                return new[]
                {
                    bandScale * 0.65f,
                    bandScale * -0.65f,
                    0f,
                    bandScale * 0.35f,
                    bandScale * -0.35f,
                };
            }

            return new[]
            {
                0f,
                bandScale * 0.35f,
                bandScale * -0.35f,
            };
        }

        return new[]
        {
            preferredOffset,
            preferredOffset * 0.5f,
            0f,
            preferredOffset * -0.5f,
        };
    }

    private void MarkLanePointReached(WH40KWaveDefenceAttackerComponent attacker, int pointIndex)
    {
        attacker.LastReachedLanePointIndex = Math.Max(attacker.LastReachedLanePointIndex, pointIndex);
        attacker.FurthestReachedLanePointIndex = Math.Max(attacker.FurthestReachedLanePointIndex, pointIndex);

        if (IsFallbackAnchor(attacker, pointIndex))
            attacker.LastFallbackAnchorIndex = pointIndex;

        attacker.LanePointIndex = pointIndex + 1;
    }

    private bool TryPromoteFinalLanePointToObjective(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        float currentProgress,
        float frontProgress)
    {
        if (!IsSimpleSwarmFinalObjectiveHandoffReady(attacker, origin, currentProgress, frontProgress))
            return false;

        MarkLanePointReached(attacker, attacker.TotalLanePointCount - 1);
        return true;
    }

    private bool IsSimpleSwarmFinalObjectiveHandoffReady(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        float currentProgress,
        float frontProgress)
    {
        if (attacker.RouteCompleted ||
            attacker.Objective is not { } objective ||
            Deleted(objective) ||
            attacker.TotalLanePointCount <= 0)
        {
            return false;
        }

        var finalPointIndex = attacker.TotalLanePointCount - 1;
        if (attacker.LanePointIndex != finalPointIndex ||
            currentProgress < SimpleSwarmFinalPointProgressHandoff ||
            frontProgress < SimpleSwarmFinalPointFrontHandoff ||
            !TryGetLanePointCoordinates(attacker, finalPointIndex, out var finalCoordinates) ||
            !origin.TryDistance(EntityManager, finalCoordinates, out var finalDistance))
        {
            return false;
        }

        var handoffRange = ResolvePointArrivalRange(attacker, attacker.LanePoints[finalPointIndex]) +
                           SimpleSwarmFinalPointArrivalBonus;
        return finalDistance <= handoffRange;
    }

    private float ResolvePointArrivalRange(WH40KWaveDefenceAttackerComponent attacker, EntityUid pointUid)
    {
        if (TryComp<WH40KWaveLanePointComponent>(pointUid, out var point) && point.ArrivalRange > 0.05f)
            return point.ArrivalRange;

        return attacker.PointArrivalRange;
    }

    private bool TryResolveLaneTraversalGeometry(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        int pointIndex,
        EntityCoordinates origin,
        float movementRange,
        out EntityUid pointUid,
        out EntityCoordinates pointCoordinates,
        out Vector2 exitDirection,
        out float gateWidth,
        out float anchorDepth)
    {
        pointUid = EntityUid.Invalid;
        pointCoordinates = EntityCoordinates.Invalid;
        exitDirection = Vector2.UnitX;
        gateWidth = 1.2f;
        anchorDepth = GateTraversalMinDepth;

        if (!TryGetLanePointCoordinates(attacker, pointIndex, out pointCoordinates))
            return false;

        pointUid = attacker.LanePoints[pointIndex];
        gateWidth = ResolveTraversalGateWidth(attacker, pointIndex, pointUid);
        anchorDepth = ResolveTraversalAnchorDepth(attacker, pointIndex, pointUid, movementRange);

        return TryResolveLaneTraversalForwardDirection(uid, attacker, pointIndex, origin, pointCoordinates, out exitDirection);
    }

    private bool TryResolveLaneTraversalForwardDirection(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        int pointIndex,
        EntityCoordinates origin,
        EntityCoordinates pointCoordinates,
        out Vector2 direction)
    {
        direction = Vector2.UnitX;

        if (TryResolveLaneTraversalForwardCoordinates(uid, attacker, pointIndex, origin, out var forwardCoordinates) &&
            TryGetNormalizedMapDirection(pointCoordinates, forwardCoordinates, out direction))
        {
            return true;
        }

        if (TryResolveLaneTraversalPreviousCoordinates(attacker, pointIndex, out var previousCoordinates) &&
            TryGetNormalizedMapDirection(previousCoordinates, pointCoordinates, out direction))
        {
            return true;
        }

        return false;
    }

    private bool TryResolveLaneTraversalForwardCoordinates(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        int pointIndex,
        EntityCoordinates origin,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        for (var i = pointIndex + 1; i < attacker.LanePoints.Count; i++)
        {
            if (TryGetLanePointCoordinates(attacker, i, out coordinates))
                return true;
        }

        if (attacker.Objective is not { } objective || Deleted(objective))
            return false;

        if (_objectiveNavigation.TryResolveObjectiveAssaultTarget(uid, origin, objective, out var assaultTarget, out _))
        {
            coordinates = assaultTarget;
            return true;
        }

        coordinates = Transform(objective).Coordinates;
        return coordinates.IsValid(EntityManager);
    }

    private bool TryResolveLaneTraversalPreviousCoordinates(
        WH40KWaveDefenceAttackerComponent attacker,
        int pointIndex,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        for (var i = pointIndex - 1; i >= 0; i--)
        {
            if (TryGetLanePointCoordinates(attacker, i, out coordinates))
                return true;
        }

        coordinates = attacker.RouteStartCoordinates;
        return coordinates.IsValid(EntityManager);
    }

    private bool TryGetNormalizedMapDirection(EntityCoordinates from, EntityCoordinates to, out Vector2 direction)
    {
        direction = Vector2.Zero;

        var fromMap = _transform.ToMapCoordinates(from);
        var toMap = _transform.ToMapCoordinates(to);
        if (fromMap.MapId == MapId.Nullspace || fromMap.MapId != toMap.MapId)
            return false;

        var vector = toMap.Position - fromMap.Position;
        if (vector.LengthSquared() <= 0.001f)
            return false;

        direction = Vector2.Normalize(vector);
        return true;
    }

    private bool HasCrossedLaneGate(
        EntityCoordinates origin,
        EntityCoordinates gateCoordinates,
        Vector2 exitDirection,
        float gateWidth,
        float passThreshold)
    {
        var originMap = _transform.ToMapCoordinates(origin);
        var gateMap = _transform.ToMapCoordinates(gateCoordinates);
        if (originMap.MapId == MapId.Nullspace || originMap.MapId != gateMap.MapId)
            return false;

        if (exitDirection.LengthSquared() <= 0.001f)
            return false;

        var delta = originMap.Position - gateMap.Position;
        var forward = Vector2.Dot(delta, exitDirection);
        var lateral = MathF.Abs(Vector2.Dot(delta, new Vector2(-exitDirection.Y, exitDirection.X)));
        return forward >= passThreshold && lateral <= gateWidth * GateTraversalLateralScale;
    }

    private bool IsWithinExpandedLaneGateCorridor(
        EntityCoordinates origin,
        EntityCoordinates gateCoordinates,
        Vector2 exitDirection,
        float gateWidth,
        float anchorDepth)
    {
        var originMap = _transform.ToMapCoordinates(origin);
        var gateMap = _transform.ToMapCoordinates(gateCoordinates);
        if (originMap.MapId == MapId.Nullspace || originMap.MapId != gateMap.MapId)
            return false;

        if (exitDirection.LengthSquared() <= 0.001f)
            return false;

        var delta = originMap.Position - gateMap.Position;
        var forward = Vector2.Dot(delta, exitDirection);
        var lateral = MathF.Abs(Vector2.Dot(delta, new Vector2(-exitDirection.Y, exitDirection.X)));
        return forward >= -Math.Max(0.4f, anchorDepth * 0.35f) &&
               lateral <= gateWidth * GateTraversalExpandedLateralScale;
    }

    private bool ShouldForceAdvanceStalledLanePoint(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        float currentProgress,
        float pointProgress,
        EntityUid pointUid,
        float epsilon)
    {
        if (attacker.LastProgressAt == TimeSpan.Zero ||
            _timing.CurTime - attacker.LastProgressAt < TimeSpan.FromSeconds(LaneTraversalStallAdvanceSeconds))
        {
            return false;
        }

        if (!TryComp(pointUid, out TransformComponent? pointXform) ||
            !origin.TryDistance(EntityManager, pointXform.Coordinates, out var pointDistance))
        {
            return false;
        }

        var arrivalRange = ResolvePointArrivalRange(attacker, pointUid);
        if (pointDistance <= arrivalRange + LaneTraversalStallDistanceSlack &&
            currentProgress + epsilon >= pointProgress - LaneTraversalStallProgressSlack)
        {
            return true;
        }

        if (attacker.NoPathCount >= 3 &&
            pointDistance <= arrivalRange + LaneTraversalStallDistanceSlack + 0.6f &&
            currentProgress + Math.Max(epsilon, 0.08f) >= pointProgress - 0.1f)
        {
            return true;
        }

        if (!TryResolveLaneTraversalGeometry(uid, attacker, attacker.LanePointIndex, origin, 0.2f, out _, out var pointCoordinates, out var exitDirection, out var gateWidth, out var anchorDepth))
            return false;

        return IsWithinExpandedLaneGateCorridor(origin, pointCoordinates, exitDirection, gateWidth, anchorDepth) &&
               currentProgress + Math.Max(epsilon, SwarmFrontAssistSlack) >= pointProgress - (LaneTraversalStallProgressSlack * 1.5f);
    }

    private bool TryResolveAdaptiveLaneTraversalTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityUid referenceUid,
        MapCoordinates pointMap,
        Vector2 exitDirection,
        float gateWidth,
        float anchorDepth,
        out EntityCoordinates target)
    {
        target = EntityCoordinates.Invalid;

        var perpendicular = new Vector2(-exitDirection.Y, exitDirection.X);
        var searchScale = ResolveLaneSearchScale(attacker);
        var bandScale = Math.Clamp(gateWidth * 0.45f * searchScale, 0.25f, 1.8f);
        var preferredOffset = SwarmBandOffsets[Math.Clamp(attacker.SwarmBandIndex, 0, SwarmBandOffsets.Length - 1)] * bandScale;
        var candidateOffsets = BuildSwarmBandCandidateOffsets(preferredOffset, bandScale, gateWidth);
        var depthScales = searchScale > 1.2f
            ? new[] { 1.2f, 1.0f, 0.75f, 0.5f, 0.2f, -0.15f, -0.35f }
            : new[] { 1.0f, 0.75f, 0.5f, 0.2f, -0.15f };

        var bestScore = float.MinValue;
        foreach (var depthScale in depthScales)
        {
            var depth = Math.Max(0.25f, anchorDepth * MathF.Abs(depthScale));
            var forwardVector = exitDirection * depth * depthScale;
            foreach (var lateral in candidateOffsets)
            {
                var candidatePosition = pointMap.Position + forwardVector + perpendicular * lateral;
                var candidate = _transform.ToCoordinates(referenceUid, new MapCoordinates(candidatePosition, pointMap.MapId));
                if (!candidate.IsValid(EntityManager) || _pathfinding.GetPoly(candidate) == null)
                    continue;

                var score = ScoreLaneTraversalCandidate(uid, attacker, origin, pointMap, candidate, exitDirection, gateWidth, lateral, depthScale);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                target = candidate;
            }
        }

        return target.IsValid(EntityManager);
    }

    private float ScoreLaneTraversalCandidate(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        MapCoordinates pointMap,
        EntityCoordinates candidate,
        Vector2 exitDirection,
        float gateWidth,
        float lateralOffset,
        float depthScale)
    {
        var candidateMap = _transform.ToMapCoordinates(candidate);
        if (candidateMap.MapId == MapId.Nullspace || candidateMap.MapId != pointMap.MapId)
            return float.MinValue;

        var delta = candidateMap.Position - pointMap.Position;
        var forward = Vector2.Dot(delta, exitDirection);
        var lateral = MathF.Abs(lateralOffset);
        var score = forward * 6f - lateral * 1.35f;

        if (origin.TryDistance(EntityManager, candidate, out var travelDistance))
            score -= travelDistance * 0.25f;

        if (depthScale < 0f)
            score -= 1.8f;

        score += EvaluateLaneCandidateClearance(uid, attacker, origin, candidate, gateWidth, lateralOffset);
        if (lateral > gateWidth * 1.35f)
            score -= 3.5f;

        return score;
    }

    private float EvaluateLaneCandidateClearance(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates candidate,
        float corridorHalfWidth = float.NaN,
        float lateralOffset = 0f)
    {
        if (!candidate.IsValid(EntityManager))
            return float.MinValue;

        var candidateMap = _transform.ToMapCoordinates(candidate);
        if (candidateMap.MapId == MapId.Nullspace)
            return float.MinValue;

        var score = 0f;
        if (!float.IsNaN(corridorHalfWidth))
        {
            var clearanceScore = EvaluateBodyClearanceScore(uid, attacker, corridorHalfWidth, lateralOffset);
            if (clearanceScore == float.MinValue)
                return float.MinValue;

            score += clearanceScore;
        }

        if (!HasSufficientPhysicalClearanceAlongPath(uid, attacker, origin, candidate))
            return float.MinValue;

        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, TransformComponent>();
        while (query.MoveNext(out var otherUid, out var other, out var xform))
        {
            if (otherUid == uid ||
                !other.RuntimeInitialized ||
                xform.MapID != candidateMap.MapId ||
                !string.Equals(other.LaneId, attacker.LaneId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!xform.Coordinates.TryDistance(EntityManager, candidate, out var distance) ||
                distance > LaneTraversalCrowdAvoidanceRadius)
            {
                continue;
            }

            if (distance < LaneTraversalCrowdHardBlockRadius)
                return float.MinValue;

            score -= (LaneTraversalCrowdAvoidanceRadius - distance) * 4f;
        }

        return score;
    }

    private bool HasSufficientPhysicalClearanceAlongPath(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates candidate)
    {
        return !TryGetPhysicalClearanceFailureAlongPath(
            uid,
            attacker,
            origin,
            candidate,
            out _,
            out _,
            out _);
    }

    private bool TryGetPhysicalClearanceFailureAlongPath(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates candidate,
        out string reason,
        out EntityUid blocker,
        out EntityCoordinates sample)
    {
        blocker = EntityUid.Invalid;
        sample = EntityCoordinates.Invalid;

        if (!origin.IsValid(EntityManager) || !candidate.IsValid(EntityManager))
        {
            reason = "invalid-coordinates";
            return true;
        }

        var originMap = _transform.ToMapCoordinates(origin);
        var candidateMap = _transform.ToMapCoordinates(candidate);
        if (originMap.MapId == MapId.Nullspace || originMap.MapId != candidateMap.MapId)
        {
            reason = "map-mismatch";
            return true;
        }

        var delta = candidateMap.Position - originMap.Position;
        var distance = delta.Length();
        var steps = Math.Max(1, (int) MathF.Ceiling(distance / PhysicalClearanceSampleStep));

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float) steps;
            var samplePosition = originMap.Position + delta * t;
            if (!TryGetPhysicalClearanceFailureAtPoint(uid, attacker, candidateMap.MapId, samplePosition, out blocker))
                continue;

            var referenceUid = _transform.GetGrid(candidate) ?? _transform.GetMap(candidate);
            sample = referenceUid != null
                ? _transform.ToCoordinates(referenceUid.Value, new MapCoordinates(samplePosition, candidateMap.MapId))
                : EntityCoordinates.Invalid;
            reason = blocker.IsValid()
                ? $"blocked:{i}/{steps}"
                : $"blocked-sample:{i}/{steps}";
            return true;
        }

        reason = "clear";
        return false;
    }

    private bool HasSufficientPhysicalClearanceAtPoint(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        MapId mapId,
        Vector2 position)
    {
        return !TryGetPhysicalClearanceFailureAtPoint(uid, attacker, mapId, position, out _);
    }

    private bool TryGetPhysicalClearanceFailureAtPoint(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        MapId mapId,
        Vector2 position,
        out EntityUid blocker)
    {
        blocker = EntityUid.Invalid;
        var radius = ResolveBodyClearanceRadius(uid, attacker) + (MinimumBodyClearanceMargin * PhysicalClearanceMarginScale);
        var bounds = new Box2(position - new Vector2(radius, radius), position + new Vector2(radius, radius));

        _clearanceIntersecting.Clear();
        _lookup.GetEntitiesIntersecting(mapId, bounds, _clearanceIntersecting, LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Approximate);
        foreach (var otherUid in _clearanceIntersecting)
        {
            if (otherUid == uid ||
                Deleted(otherUid) ||
                HasComp<WH40KWaveDefenceAttackerComponent>(otherUid))
            {
                continue;
            }

            if (!TryComp<PhysicsComponent>(otherUid, out var body) ||
                !body.CanCollide ||
                !body.Hard ||
                (body.CollisionLayer & PhysicalClearanceCollisionMask) == 0)
            {
                continue;
            }

            var xform = Transform(otherUid);
            var aabb = _lookup.GetAABBNoContainer(otherUid, xform.Coordinates.Position, xform.LocalRotation);
            if (aabb.Intersects(bounds))
            {
                blocker = otherUid;
                return true;
            }
        }

        return false;
    }

    private float EvaluateBodyClearanceScore(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        float corridorHalfWidth,
        float lateralOffset)
    {
        if (corridorHalfWidth <= 0.05f)
            return float.MinValue;

        var radius = ResolveBodyClearanceRadius(uid, attacker);
        var availableMargin = corridorHalfWidth - MathF.Abs(lateralOffset) - radius;
        if (availableMargin < MinimumBodyClearanceMargin)
            return float.MinValue;

        var score = MathF.Min(2.4f, availableMargin * BodyClearanceBonusScale);
        if (availableMargin < PreferredBodyClearanceMargin)
            score -= (PreferredBodyClearanceMargin - availableMargin) * BodyClearancePenaltyScale;

        return score;
    }

    private float ResolveBodyClearanceRadius(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker)
    {
        if (attacker.BodyClearanceCachedAt != TimeSpan.Zero &&
            _timing.CurTime - attacker.BodyClearanceCachedAt <= TimeSpan.FromSeconds(BodyClearanceCacheSeconds) &&
            attacker.BodyClearanceRadius > 0.05f)
        {
            return attacker.BodyClearanceRadius;
        }

        var radius = DefaultBodyClearanceRadius;
        if (TryComp(uid, out TransformComponent? xform))
        {
            var bounds = _lookup.GetAABBNoContainer(uid, xform.Coordinates.Position, xform.LocalRotation);
            var halfWidth = MathF.Abs(bounds.Right - bounds.Left) * 0.5f;
            var halfHeight = MathF.Abs(bounds.Top - bounds.Bottom) * 0.5f;
            radius = MathF.Max(radius, MathF.Max(halfWidth, halfHeight));
        }

        attacker.BodyClearanceRadius = radius;
        attacker.BodyClearanceDiameter = radius * 2f;
        attacker.BodyClearanceCachedAt = _timing.CurTime;
        return radius;
    }

    private float ResolveLaneSearchScale(WH40KWaveDefenceAttackerComponent attacker)
    {
        var bonus = attacker.RecoveryLevel * LaneRecoverySearchScaleStep;
        bonus += MathF.Min(0.55f, attacker.NoPathCount * 0.08f);
        bonus += MathF.Min(0.35f, attacker.RecoveryAttempts * 0.02f);
        return 1f + MathF.Min(LaneRecoverySearchScaleMaxBonus, bonus);
    }

    private bool TryResolveLaneBlockerTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates routeTarget,
        Vector2 preferredDirection,
        out EntityCoordinates target,
        out string label)
    {
        target = EntityCoordinates.Invalid;
        label = string.Empty;

        var originMap = _transform.ToMapCoordinates(origin);
        var targetMap = _transform.ToMapCoordinates(routeTarget);
        if (originMap.MapId == MapId.Nullspace || originMap.MapId != targetMap.MapId)
            return false;

        var direction = targetMap.Position - originMap.Position;
        var length = direction.Length();
        if (length <= 0.6f)
            return false;

        var normalized = direction.LengthSquared() > 0.001f
            ? Vector2.Normalize(direction)
            : preferredDirection.LengthSquared() > 0.001f
                ? Vector2.Normalize(preferredDirection)
                : Vector2.UnitX;

        var ray = new CollisionRay(originMap.Position, normalized, (int) LaneBlockerRayMask);
        foreach (var hit in _physics.IntersectRayWithPredicate(
                     originMap.MapId,
                     ray,
                     length,
                     entity => entity == uid || Deleted(entity) || HasComp<WH40KWaveDefenceAttackerComponent>(entity),
                     false))
        {
            if (!IsLaneActionableBlockerCandidate(hit.HitEntity, attacker))
                continue;

            if (!TryResolvePointBeyondLaneBlocker(routeTarget, targetMap.MapId, targetMap.Position, hit.HitPos, normalized, out target))
                continue;

            label = $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}:breach";
            return true;
        }

        return false;
    }

    private bool IsLaneActionableBlockerCandidate(EntityUid entity, WH40KWaveDefenceAttackerComponent attacker)
    {
        if (entity == EntityUid.Invalid)
            return false;

        if (TryComp<DoorComponent>(entity, out _))
            return attacker.CanInteract || attacker.CanPry || attacker.CanSmash;

        if (TryComp<ClimbableComponent>(entity, out _))
            return attacker.CanClimb;

        if (TryComp<DestructibleComponent>(entity, out _) ||
            TryComp<DamageableComponent>(entity, out _))
        {
            return attacker.CanSmash;
        }

        return false;
    }

    private bool TryResolvePointBeyondLaneBlocker(
        EntityCoordinates referenceCoordinates,
        MapId mapId,
        Vector2 objectiveWorldPosition,
        Vector2 hitPosition,
        Vector2 direction,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var bestScore = float.MaxValue;
        var found = false;

        for (var forwardStep = 1; forwardStep <= 3; forwardStep++)
        {
            var forward = LaneBlockerForwardOffset * forwardStep;
            foreach (var lateralSign in new[] { 0f, 1f, -1f, 1.8f, -1.8f })
            {
                var lateral = LaneBlockerLateralOffset * lateralSign;
                var candidatePosition = hitPosition + direction * forward + perpendicular * lateral;
                var candidate = _transform.ToCoordinates(
                    referenceCoordinates.EntityId,
                    new MapCoordinates(candidatePosition, mapId));

                if (_pathfinding.GetPoly(candidate) == null)
                    continue;

                var candidateMap = _transform.ToMapCoordinates(candidate);
                if (candidateMap.MapId == MapId.Nullspace)
                    continue;

                var score = Vector2.Distance(candidateMap.Position, objectiveWorldPosition) + MathF.Abs(lateral) * 0.65f;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                coordinates = candidate;
                found = true;
            }
        }

        return found;
    }

    private bool HasHardLaneCrowding(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates candidate)
    {
        return EvaluateLaneCandidateClearance(uid, attacker, origin, candidate) == float.MinValue;
    }

    private float ResolveTraversalGateWidth(WH40KWaveDefenceAttackerComponent attacker, int pointIndex, EntityUid pointUid)
    {
        if (TryComp<WH40KWaveLanePointComponent>(pointUid, out var point))
        {
            if (point.ProgressGateWidth > 0.05f)
                return Math.Max(0.8f, point.ProgressGateWidth);

            if (point.SegmentWidth > 0.05f)
                return Math.Max(0.8f, point.SegmentWidth * 0.5f);

            if (point.ArrivalRange > 0.05f)
                return Math.Max(0.8f, point.ArrivalRange);
        }

        return Math.Max(1.4f, ResolveSegmentWidth(attacker, pointIndex));
    }

    private float ResolveTraversalAnchorDepth(WH40KWaveDefenceAttackerComponent attacker, int pointIndex, EntityUid pointUid, float movementRange)
    {
        var arrivalRange = ResolvePointArrivalRange(attacker, pointUid);
        var gateWidth = ResolveTraversalGateWidth(attacker, pointIndex, pointUid);
        return Math.Max(
            GateTraversalMinDepth,
            Math.Max(gateWidth * 0.7f, arrivalRange + movementRange + GateTraversalAnchorSlack));
    }

    private bool IsFallbackAnchor(WH40KWaveDefenceAttackerComponent attacker, int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= attacker.LanePoints.Count)
            return false;

        var pointUid = attacker.LanePoints[pointIndex];
        if (!TryComp<WH40KWaveLanePointComponent>(pointUid, out var point))
            return true;

        return point.FallbackAnchor ||
               point.PointType is WH40KWaveLanePointType.Waypoint or
                   WH40KWaveLanePointType.Rally or
                   WH40KWaveLanePointType.Fallback or
                   WH40KWaveLanePointType.Breach or
                   WH40KWaveLanePointType.Siege;
    }

    private bool TryGetLanePointCoordinates(
        WH40KWaveDefenceAttackerComponent attacker,
        int pointIndex,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        if (pointIndex < 0 || pointIndex >= attacker.LanePoints.Count)
            return false;

        var pointUid = attacker.LanePoints[pointIndex];
        if (Deleted(pointUid) || !TryComp(pointUid, out TransformComponent? xform))
            return false;

        coordinates = xform.Coordinates;
        return true;
    }

    private bool TryGetStrategicLanePointCoordinates(
        WH40KWaveDefenceAttackerComponent attacker,
        out int pointIndex,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        pointIndex = -1;

        if (attacker.LanePointIndex < 0 || attacker.LanePointIndex >= attacker.LanePoints.Count)
            return false;

        for (var i = attacker.LanePointIndex; i < attacker.LanePoints.Count; i++)
        {
            if (!TryGetLanePointCoordinates(attacker, i, out coordinates))
                continue;

            pointIndex = i;
            return true;
        }

        return TryGetLanePointCoordinates(attacker, attacker.LanePointIndex, out coordinates) &&
               (pointIndex = attacker.LanePointIndex) >= 0;
    }

    private float ComputeRouteProgressRatio(WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates origin)
    {
        if (attacker.TotalLanePointCount <= 0)
            return 1f;

        if (attacker.RouteCompleted)
            return 1f;

        var completedPoints = Math.Max(0, attacker.LastReachedLanePointIndex + 1);
        var baseRatio = completedPoints / (float) attacker.TotalLanePointCount;

        if (attacker.LanePointIndex < 0 ||
            attacker.LanePointIndex >= attacker.LanePoints.Count ||
            !TryGetLanePointCoordinates(attacker, attacker.LanePointIndex, out var currentCoordinates) ||
            completedPoints <= 0 ||
            !TryGetLanePointCoordinates(attacker, completedPoints - 1, out var previousCoordinates))
        {
            return Math.Clamp(baseRatio, 0f, 0.999f);
        }

        if (!previousCoordinates.TryDistance(EntityManager, currentCoordinates, out var segmentLength) ||
            segmentLength <= 0.05f ||
            !origin.TryDistance(EntityManager, currentCoordinates, out var currentDistance))
        {
            return Math.Clamp(baseRatio, 0f, 0.999f);
        }

        var localProgress = Math.Clamp(1f - currentDistance / segmentLength, 0f, 0.999f);
        return Math.Clamp((completedPoints + localProgress) / attacker.TotalLanePointCount, 0f, 0.999f);
    }

    private string DescribeLanePoint(WH40KWaveDefenceAttackerComponent attacker, int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= attacker.LanePoints.Count)
            return "-";

        var pointUid = attacker.LanePoints[pointIndex];
        if (Deleted(pointUid))
            return $"idx-{pointIndex}[deleted]";

        if (!TryComp(pointUid, out WH40KWaveLanePointComponent? point))
            return $"idx-{pointIndex}";

        var pointId = string.IsNullOrWhiteSpace(point.PointId)
            ? $"ord-{point.Order}"
            : point.PointId;

        return $"{pointId}[{point.PointType}]";
    }

    private static TimeSpan GetLocomotionThinkDelay(EntityUid uid)
    {
        var stagger = 0.004f * (Math.Abs(uid.Id.GetHashCode()) % 5);
        return TimeSpan.FromSeconds(LocomotionThinkIntervalSeconds + stagger);
    }

    private void ClearLocomotionTarget(WH40KWaveDefenceAttackerComponent attacker, bool clearStickyObjective)
    {
        attacker.LocomotionMode = WH40KWaveDefenceLocomotionMode.None;
        attacker.LocomotionTarget = EntityCoordinates.Invalid;
        attacker.LocomotionTargetLabel = string.Empty;
        attacker.MovementTargetDirective = EntityCoordinates.Invalid;
        attacker.MovementTargetDirectiveLabel = string.Empty;
        MarkNavigationState(attacker, "locomotion-cleared");
        ClearLocalLaneCorridor(attacker);
        ClearPreparedLanePlan(attacker);

        if (clearStickyObjective)
        {
            attacker.StickyObjectiveTarget = EntityCoordinates.Invalid;
            attacker.StickyObjectiveTargetUntil = TimeSpan.Zero;
        }
    }

    private bool HasLargeCoordinateChange(EntityCoordinates a, EntityCoordinates b)
    {
        if (!a.IsValid(EntityManager) || !b.IsValid(EntityManager))
            return true;

        if (a.EntityId != b.EntityId)
            return true;

        return (a.Position - b.Position).LengthSquared() >= HardRetargetDistance * HardRetargetDistance;
    }

    private static void MarkNavigationState(WH40KWaveDefenceAttackerComponent attacker, string label)
    {
        if (string.Equals(attacker.NavigationStateLabel, label, StringComparison.Ordinal))
            return;

        attacker.NavigationEpoch++;
        attacker.LastAcceptedNavigationEpoch = attacker.NavigationEpoch;
        attacker.NavigationStateLabel = label;
    }

    private bool SameCoordinates(EntityCoordinates a, EntityCoordinates b)
    {
        if (!a.IsValid(EntityManager) || !b.IsValid(EntityManager))
            return !a.IsValid(EntityManager) && !b.IsValid(EntityManager);

        if (a.EntityId != b.EntityId)
            return false;

        return (a.Position - b.Position).LengthSquared() <= 0.01f;
    }
}
