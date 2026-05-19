using System.Linq;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Server._WH40K.WaveDefence.HTN;
using Content.Shared.Examine;
using Content.Shared.NPC;
using Content.Shared._WH40K.WaveDefence;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WaveDefence;

public sealed class WH40KWaveDefenceAiDebugOverlaySystem : SharedWH40KWaveDefenceAiDebugOverlaySystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<ICommonSession> _observers = [];
    private TimeSpan? _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;

        foreach (var observer in new List<ICommonSession>(_observers))
        {
            RemoveObserver(observer);
        }
    }

    public bool ToggleObserver(ICommonSession observer)
    {
        if (HasObserver(observer))
        {
            RemoveObserver(observer);
            return false;
        }

        AddObserver(observer);
        return true;
    }

    public bool HasObserver(ICommonSession observer)
    {
        return _observers.Contains(observer);
    }

    public void AddObserver(ICommonSession observer)
    {
        if (_observers.Add(observer))
            _nextTick = _timing.CurTime;
    }

    public void RemoveObserver(ICommonSession observer)
    {
        if (!_observers.Remove(observer))
            return;

        RaiseNetworkEvent(new WH40KWaveDefenceAiDebugOverlayDisableMessage(), observer.Channel);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame)
            RemoveObserver(args.Session);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_observers.Count == 0)
            return;

        if (_nextTick is { } nextTick && _timing.CurTime < nextTick)
            return;

        foreach (var observer in _observers)
        {
            if (observer.AttachedEntity is not { Valid: true } viewer ||
                Deleted(viewer) ||
                !TryComp(viewer, out TransformComponent? viewerXform) ||
                viewerXform.MapID == MapId.Nullspace)
            {
                continue;
            }

            var viewerMap = _transform.GetMapCoordinates(viewer, xform: viewerXform);
            var worldBounds = Box2.CenteredAround(viewerMap.Position, new Vector2(LocalViewRange, LocalViewRange));
            var entries = BuildEntries(worldBounds, viewerXform.MapID);
            RaiseNetworkEvent(new WH40KWaveDefenceAiDebugOverlayMessage(entries), observer.Channel);
        }

        _nextTick = _timing.CurTime + Cooldown;
    }

    private WH40KWaveDefenceAiDebugEntry[] BuildEntries(Box2 worldBounds, MapId mapId)
    {
        var entries = new List<WH40KWaveDefenceAiDebugEntry>();
        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, HTNComponent, ActiveNPCComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var attacker, out var htn, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var npcPosition = _transform.GetMapCoordinates(uid, xform: xform);
            if (!worldBounds.Contains(npcPosition.Position))
                continue;

            entries.Add(BuildEntry(uid, attacker, htn, xform, npcPosition));
        }

        return entries.ToArray();
    }

    private WH40KWaveDefenceAiDebugEntry BuildEntry(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        MapCoordinates npcPosition)
    {
        var objectivePosition = MapCoordinates.Nullspace;
        var hasObjectivePosition = TryGetObjectivePosition(attacker, out objectivePosition);

        var currentTargetPosition = MapCoordinates.Nullspace;
        var hasCurrentTargetPosition = TryResolveDisplayTarget(uid, attacker, htn, out currentTargetPosition);

        var rememberedTargetPosition = MapCoordinates.Nullspace;
        var hasRememberedTargetPosition = attacker.RememberedPlayerCoordinates.IsValid(EntityManager);
        if (hasRememberedTargetPosition)
            rememberedTargetPosition = _transform.ToMapCoordinates(attacker.RememberedPlayerCoordinates);

        var memoryRemainingSeconds = 0f;
        if (attacker.RememberedPlayer.IsValid() &&
            attacker.RememberedPlayerUntil != TimeSpan.Zero &&
            _timing.CurTime < attacker.RememberedPlayerUntil)
        {
            memoryRemainingSeconds = (float) (attacker.RememberedPlayerUntil - _timing.CurTime).TotalSeconds;
        }

        var targetKind = ResolveTargetKind(uid, attacker, htn);
        var hasLineOfSightToPlayer = HasLineOfSightToTrackedPlayer(uid, attacker, htn);
        var steering = CompOrNull<NPCSteeringComponent>(uid);
        var steeringStatus = steering?.Status.ToString() ?? "NoSteering";
        var noPath = steering?.Status == SteeringStatus.NoPath;
        var brainOwner = DescribeBrainOwner(attacker);
        var combatOwner = DescribeCombatOwner(uid, attacker, htn);
        var movementOwner = DescribeMovementOwner(attacker, htn);
        var memoryOwner = DescribeMemoryOwner(attacker);
        var recoveryOwner = DescribeRecoveryOwner(attacker, steering);
        var epochSummary = BuildEpochSummary(attacker);
        var engaged = !string.Equals(combatOwner, "none", StringComparison.Ordinal);

        var currentTask = "NoPlan";
        if (htn.Planning)
            currentTask = "Planning";
        else if (htn.Plan is { } plan && plan.Tasks.Count > 0)
            currentTask = FormatOperatorName(plan.CurrentOperator.GetType().Name);

        var hasCurrentLanePoint = TryGetLanePointDebugInfo(attacker, attacker.LanePointIndex, out var currentLanePointId, out var currentLanePointType);
        var hasLastReachedLanePoint = TryGetLanePointDebugInfo(attacker, attacker.LastReachedLanePointIndex, out var lastReachedLanePointId, out var lastReachedLanePointType);
        var hasSiegeBlocker = attacker.ActiveSiegeBlocker.IsValid();
        var siegeBlockerLabel = hasSiegeBlocker
            ? attacker.ActiveSiegeBlockerLabel
            : string.Empty;

        return new WH40KWaveDefenceAiDebugEntry(
            Label: $"{MetaData(uid).EntityName}#{uid.Id}",
            NpcPosition: npcPosition,
            VisionRadius: Math.Max(0f, attacker.VisionRadius),
            AggroVisionRadius: Math.Max(attacker.VisionRadius, attacker.AggroVisionRadius),
            ObjectivePosition: objectivePosition,
            HasObjectivePosition: hasObjectivePosition,
            CurrentTargetPosition: currentTargetPosition,
            HasCurrentTargetPosition: hasCurrentTargetPosition,
            RememberedTargetPosition: rememberedTargetPosition,
            HasRememberedTargetPosition: hasRememberedTargetPosition,
            MemoryRemainingSeconds: MathF.Max(0f, memoryRemainingSeconds),
            TargetKind: targetKind,
            HasLineOfSightToPlayer: hasLineOfSightToPlayer,
            NoPath: noPath,
            Engaged: engaged,
            RecoveryLevel: attacker.RecoveryLevel,
            LaneId: string.IsNullOrWhiteSpace(attacker.LaneId) ? "<none>" : attacker.LaneId,
            Intent: attacker.Intent.ToString(),
            CurrentLanePointIndex: attacker.LanePointIndex,
            LastReachedLanePointIndex: attacker.LastReachedLanePointIndex,
            FurthestReachedLanePointIndex: attacker.FurthestReachedLanePointIndex,
            TotalLanePointCount: attacker.TotalLanePointCount,
            RouteProgressRatio: Math.Clamp(attacker.RouteProgressRatio, 0f, 1f),
            RouteCompleted: attacker.RouteCompleted,
            SiegeBlockerLabel: siegeBlockerLabel,
            HasSiegeBlocker: hasSiegeBlocker,
            CurrentLanePointId: currentLanePointId,
            CurrentLanePointType: currentLanePointType,
            HasCurrentLanePoint: hasCurrentLanePoint,
            LastReachedLanePointId: lastReachedLanePointId,
            LastReachedLanePointType: lastReachedLanePointType,
            HasLastReachedLanePoint: hasLastReachedLanePoint,
            RootTask: htn.RootTask.Task,
            CurrentTask: currentTask,
            SteeringStatus: steeringStatus,
            BrainOwner: brainOwner,
            CombatOwner: combatOwner,
            MovementOwner: movementOwner,
            MemoryOwner: memoryOwner,
            RecoveryOwner: recoveryOwner,
            EpochSummary: epochSummary,
            DebugState: attacker.DebugState,
            BodyClearanceRadius: attacker.BodyClearanceRadius,
            BodyClearanceDiameter: attacker.BodyClearanceDiameter,
            ClearanceDebugLabel: attacker.ClearanceDebugLabel,
            ClearanceDebugReason: attacker.ClearanceDebugReason,
            ClearanceDebugBlockerLabel: attacker.ClearanceDebugBlockerLabel,
            ClearanceDebugSamplePosition: attacker.ClearanceDebugSample.IsValid(EntityManager)
                ? _transform.ToMapCoordinates(attacker.ClearanceDebugSample)
                : MapCoordinates.Nullspace,
            HasClearanceDebugSamplePosition: attacker.ClearanceDebugSample.IsValid(EntityManager),
            DynamicClearanceDebugLabel: attacker.DynamicClearanceDebugLabel,
            DynamicClearanceDebugReason: attacker.DynamicClearanceDebugReason,
            DynamicClearanceDebugBlockerLabel: attacker.DynamicClearanceDebugBlockerLabel,
            DynamicClearanceDebugSamplePosition: attacker.DynamicClearanceDebugSample.IsValid(EntityManager)
                ? _transform.ToMapCoordinates(attacker.DynamicClearanceDebugSample)
                : MapCoordinates.Nullspace,
            HasDynamicClearanceDebugSamplePosition: attacker.DynamicClearanceDebugSample.IsValid(EntityManager),
            HasCommittedRoute: attacker.HasCommittedRoute,
            CommittedRouteCost: attacker.CommittedRouteCost,
            CommittedRouteRemainingCost: attacker.CommittedRouteRemainingCost,
            CommittedRouteTopologyVersion: attacker.CommittedRouteTopologyVersion,
            CommittedRoutePoints: attacker.CommittedRoutePoints.ToArray(),
            CommittedRouteCumulativeCosts: attacker.CommittedRouteCumulativeCosts.ToArray(),
            HasShadowRoute: attacker.HasShadowRoute,
            ShadowRouteCost: attacker.ShadowRouteCost,
            ShadowRouteTopologyVersion: attacker.ShadowRouteTopologyVersion,
            ShadowRoutePoints: attacker.ShadowRoutePoints.ToArray(),
            ShadowRouteCumulativeCosts: attacker.ShadowRouteCumulativeCosts.ToArray(),
            RouteMindDecision: attacker.RouteMindDecision);
    }

    private static string DescribeBrainOwner(WH40KWaveDefenceAttackerComponent attacker)
    {
        return $"{attacker.DecisionPriority}:{attacker.DecisionReason}";
    }

    private string DescribeCombatOwner(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn)
    {
        if (TryGetBlackboardPlayerCombatTarget(htn, out var playerTarget, out _))
            return $"player-role:{ToPrettyString(playerTarget)}";

        if (TryGetBlackboardObjectiveCombatTarget(htn, out var objectiveTarget, out _))
            return $"objective-role:{ToPrettyString(objectiveTarget)}";

        if (attacker.CombatFocusTarget.IsValid())
            return $"focus:{attacker.CombatFocusLabel}";

        if (HasComp<NPCMeleeCombatComponent>(uid) || HasComp<NPCRangedCombatComponent>(uid))
            return "combat-comp-only";

        return "none";
    }

    private string DescribeMovementOwner(
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn)
    {
        if (attacker.ForcedTarget.IsValid(EntityManager))
            return $"forced:{attacker.ForcedTargetKind}:{attacker.ForcedTargetLabel}";

        if (attacker.InvestigationTarget.IsValid() &&
            attacker.InvestigationCoordinates.IsValid(EntityManager))
        {
            return $"investigate:{attacker.InvestigationLabel}";
        }

        if (TryGetBlackboardMovementTarget(htn, out _))
        {
            return attacker.MovementTargetDirective.IsValid(EntityManager)
                ? $"movement-role:{attacker.MovementTargetDirectiveLabel}"
                : "movement-role";
        }

        if (attacker.LocomotionTarget.IsValid(EntityManager))
        {
            return string.IsNullOrWhiteSpace(attacker.LocomotionTargetLabel)
                ? $"locomotion:{attacker.LocomotionMode}"
                : $"locomotion:{attacker.LocomotionTargetLabel}";
        }

        if (attacker.MovementTargetDirective.IsValid(EntityManager))
            return $"directive:{attacker.MovementTargetDirectiveLabel}";

        return "none";
    }

    private string DescribeMemoryOwner(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (attacker.RememberedPlayer.IsValid() &&
            attacker.RememberedPlayerUntil != TimeSpan.Zero &&
            _timing.CurTime < attacker.RememberedPlayerUntil)
        {
            return $"{attacker.PlayerContactMode}:{attacker.PlayerContactPolicyLabel}:{attacker.RememberedPlayerSource}";
        }

        return $"{attacker.PlayerContactMode}:{attacker.PlayerContactPolicyLabel}";
    }

    private string DescribeRecoveryOwner(
        WH40KWaveDefenceAttackerComponent attacker,
        NPCSteeringComponent? steering)
    {
        if (attacker.ForcedTarget.IsValid(EntityManager))
            return $"forced:{attacker.ForcedTargetKind}:{attacker.ForcedTargetLabel}";

        if (steering?.Status == SteeringStatus.NoPath)
            return "signal:nopath";

        if (attacker.RecoveryLevel > 0 || attacker.RecoveryAttempts > 0)
            return $"route-recovery:l{attacker.RecoveryLevel}/a{attacker.RecoveryAttempts}";

        return "idle";
    }

    private static string BuildEpochSummary(WH40KWaveDefenceAttackerComponent attacker)
    {
        return $"d{attacker.DecisionEpoch}|p{attacker.PerceptionEpoch}/{attacker.LastAcceptedPerceptionEpoch}({attacker.PendingPerceptionRequestEpoch}/{attacker.LastAppliedPerceptionRequestEpoch})|n{attacker.NavigationEpoch}/{attacker.LastAcceptedNavigationEpoch}({attacker.PendingNavigationRequestEpoch}/{attacker.LastAppliedNavigationRequestEpoch})";
    }

    private bool TryResolveDisplayTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        out MapCoordinates coordinates)
    {
        coordinates = MapCoordinates.Nullspace;

        var targetKind = ResolveTargetKind(uid, attacker, htn);
        if (targetKind == WH40KWaveDefenceAiDebugTargetKind.VisiblePlayer &&
            TryGetBlackboardPlayerCombatTarget(htn, out var targetEntity, out var playerCoordinates) &&
            Exists(targetEntity))
        {
            coordinates = playerCoordinates.IsValid(EntityManager)
                ? _transform.ToMapCoordinates(playerCoordinates)
                : _transform.GetMapCoordinates(targetEntity);
            return true;
        }

        if (targetKind == WH40KWaveDefenceAiDebugTargetKind.RememberedPlayer &&
            attacker.RememberedPlayerCoordinates.IsValid(EntityManager))
        {
            coordinates = _transform.ToMapCoordinates(attacker.RememberedPlayerCoordinates);
            return true;
        }

        if (targetKind == WH40KWaveDefenceAiDebugTargetKind.Objective &&
            (TryGetBlackboardObjectiveCombatTarget(htn, out _, out var objectiveCoordinates) ||
             TryGetObjectivePosition(attacker, out _)))
        {
            coordinates = objectiveCoordinates.IsValid(EntityManager)
                ? _transform.ToMapCoordinates(objectiveCoordinates)
                : _transform.GetMapCoordinates(attacker.Objective!.Value);
            return true;
        }

        if (targetKind == WH40KWaveDefenceAiDebugTargetKind.ForcedPoint &&
            attacker.ForcedTarget.IsValid(EntityManager))
        {
            coordinates = _transform.ToMapCoordinates(attacker.ForcedTarget);
            return true;
        }

        if (TryGetBlackboardMovementTarget(htn, out var targetCoordinates) &&
            targetCoordinates.IsValid(EntityManager))
        {
            coordinates = _transform.ToMapCoordinates(targetCoordinates);
            return true;
        }

        if (attacker.LanePointIndex < attacker.LanePoints.Count &&
            attacker.LanePoints[attacker.LanePointIndex].IsValid() &&
            !Deleted(attacker.LanePoints[attacker.LanePointIndex]))
        {
            coordinates = _transform.GetMapCoordinates(attacker.LanePoints[attacker.LanePointIndex]);
            return true;
        }

        if (TryGetObjectivePosition(attacker, out var objectivePosition))
        {
            coordinates = objectivePosition;
            return true;
        }

        return false;
    }

    private bool TryGetLanePointDebugInfo(
        WH40KWaveDefenceAttackerComponent attacker,
        int pointIndex,
        out string pointId,
        out WH40KWaveLanePointType pointType)
    {
        pointId = string.Empty;
        pointType = WH40KWaveLanePointType.Waypoint;

        if (pointIndex < 0 || pointIndex >= attacker.LanePoints.Count)
            return false;

        var pointUid = attacker.LanePoints[pointIndex];
        if (Deleted(pointUid))
            return false;

        if (!TryComp(pointUid, out WH40KWaveLanePointComponent? point))
        {
            pointId = $"idx-{pointIndex}";
            return true;
        }

        pointType = point.PointType;
        pointId = string.IsNullOrWhiteSpace(point.PointId)
            ? $"ord-{point.Order}"
            : point.PointId;
        return true;
    }

    private bool TryGetObjectivePosition(WH40KWaveDefenceAttackerComponent attacker, out MapCoordinates position)
    {
        position = MapCoordinates.Nullspace;

        if (attacker.Objective is not { } objective || Deleted(objective))
            return false;

        position = _transform.GetMapCoordinates(objective);
        return position.MapId != MapId.Nullspace;
    }

    private WH40KWaveDefenceAiDebugTargetKind ResolveTargetKind(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn)
    {
        if (TryGetBlackboardPlayerCombatTarget(htn, out var targetEntity, out _))
        {
            return HasLineOfSightToEntity(uid, attacker, targetEntity)
                ? WH40KWaveDefenceAiDebugTargetKind.VisiblePlayer
                : WH40KWaveDefenceAiDebugTargetKind.RememberedPlayer;
        }

        if (TryGetBlackboardObjectiveCombatTarget(htn, out _, out _))
            return WH40KWaveDefenceAiDebugTargetKind.Objective;

        if (attacker.ForcedTarget.IsValid(EntityManager))
            return WH40KWaveDefenceAiDebugTargetKind.ForcedPoint;

        if (attacker.InvestigationTarget.IsValid() &&
            attacker.InvestigationCoordinates.IsValid(EntityManager))
        {
            return WH40KWaveDefenceAiDebugTargetKind.RememberedPlayer;
        }

        if (!attacker.RouteCompleted && attacker.LanePointIndex < attacker.LanePoints.Count)
            return WH40KWaveDefenceAiDebugTargetKind.LanePoint;

        if (attacker.Objective != null)
            return WH40KWaveDefenceAiDebugTargetKind.Objective;

        return WH40KWaveDefenceAiDebugTargetKind.None;
    }

    private bool HasLineOfSightToTrackedPlayer(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn)
    {
        if (TryGetBlackboardPlayerCombatTarget(htn, out var targetEntity, out _) &&
            targetEntity.IsValid() &&
            attacker.RememberedPlayer.IsValid() &&
            targetEntity == attacker.RememberedPlayer)
        {
            return HasLineOfSightToEntity(uid, attacker, targetEntity);
        }

        return false;
    }

    private bool HasLineOfSightToEntity(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, EntityUid target)
    {
        if (Deleted(target))
            return false;

        var losRange = Math.Max(attacker.VisionRadius, attacker.AggroVisionRadius) + 0.5f;
        return _examine.InRangeUnOccluded(uid, target, losRange, null);
    }

    private bool TryGetBlackboardPlayerCombatTarget(
        HTNComponent htn,
        out EntityUid target,
        out EntityCoordinates coordinates)
    {
        target = EntityUid.Invalid;
        coordinates = EntityCoordinates.Invalid;

        if (!htn.Blackboard.TryGetValue<bool>(WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole, out var isPlayerRole, EntityManager) ||
            !isPlayerRole ||
            !htn.Blackboard.TryGetValue<EntityUid>(WH40KWaveDefenceHtnBlackboardKeys.PlayerTarget, out target, EntityManager) ||
            !target.IsValid())
        {
            target = EntityUid.Invalid;
            return false;
        }

        coordinates = htn.Blackboard.GetValueOrDefault<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates, EntityManager);
        return true;
    }

    private bool TryGetBlackboardObjectiveCombatTarget(
        HTNComponent htn,
        out EntityUid target,
        out EntityCoordinates coordinates)
    {
        target = EntityUid.Invalid;
        coordinates = EntityCoordinates.Invalid;

        if (!htn.Blackboard.TryGetValue<bool>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole, out var isObjectiveRole, EntityManager) ||
            !isObjectiveRole ||
            !htn.Blackboard.TryGetValue<EntityUid>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTarget, out target, EntityManager) ||
            !target.IsValid())
        {
            target = EntityUid.Invalid;
            return false;
        }

        coordinates = htn.Blackboard.GetValueOrDefault<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates, EntityManager);
        return true;
    }

    private bool TryGetBlackboardMovementTarget(HTNComponent htn, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        if (!htn.Blackboard.TryGetValue<bool>(WH40KWaveDefenceHtnBlackboardKeys.MovementRole, out var isMovementRole, EntityManager) ||
            !isMovementRole)
        {
            return false;
        }

        coordinates = htn.Blackboard.GetValueOrDefault<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates, EntityManager);
        return coordinates.IsValid(EntityManager);
    }

    private static string FormatOperatorName(string name)
    {
        return name.EndsWith("Operator", StringComparison.Ordinal)
            ? name[..^"Operator".Length]
            : name;
    }
}
