using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Server._WH40K.WaveDefence.HTN;
using Content.Server.GameTicking;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared._WH40K.WaveDefence;
using Content.Shared.Climbing.Components;
using Content.Shared.CombatMode;
using Content.Shared.Interaction.Components;
using Content.Shared.Prying.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.WaveDefence;

public sealed class WH40KWaveDefenceAISystem : EntitySystem
{
    private const string VisionRadiusKey = "VisionRadius";
    private const string AggroVisionRadiusKey = "AggroVisionRadius";
    private const string TargetKey = "Target";
    private const string TargetCoordinatesKey = "TargetCoordinates";
    private const string AttackTargetCoordinatesKey = "AttackTargetCoordinates";
    private const float SimpleSwarmThinkIntervalSeconds = 0.12f;
    private const float AdvancedThinkIntervalSeconds = 0.12f;
    private const float SimpleSwarmDeliberationIntervalSeconds = 0.28f;
    private const float AdvancedDeliberationIntervalSeconds = 0.40f;
    private const float EngagedDeliberationIntervalSeconds = 0.18f;
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
    private const float SyncInRangeAdvanceDelaySeconds = 0.08f;
    private const float SyncNoSteeringAdvanceDelaySeconds = 0.10f;
    private const float SyncNoSteeringDelaySeconds = 0.35f;
    private const float SyncNoPlanDelaySeconds = 0.5f;
    private const float SyncDriftDelaySeconds = 0.6f;
    private const float SyncProgressiveAdvanceDelaySeconds = 0.22f;
    private const float SyncProgressGraceSeconds = 0.35f;
    private const float SyncSignificantDriftDistance = 0.85f;
    private const float SyncEquivalentLaneRetargetDelaySeconds = 0.55f;
    private const float SyncEquivalentLaneHardRetargetDistance = 1.35f;
    private const float SlowTargetReactionDelaySeconds = 0.75f;
    private const float SlowTargetReactionCooldownSeconds = 1.5f;
    private const float ActionableObstacleEncounterGraceSeconds = 1.5f;
    private const float ActionableObstacleRecoveryGraceSeconds = 4.5f;
    private const float ForcedTargetProgressEpsilon = 0.20f;
    private const float CombatProgressDistanceEpsilon = 0.25f;
    private const float CombatProgressCloseRange = 1.85f;
    private const float PostTacticalContactRecoveryGraceSeconds = 1.25f;
    private const float VisibleContactGraceSeconds = 0.45f;
    private const float RelayContactFreshnessSeconds = 0.75f;
    private const float RelayCoordinateUpdateDistance = 0.9f;
    private const float LaneTraversalStallAdvanceSeconds = 1.35f;
    private const float LaneTraversalStallProgressSlack = 0.035f;
    private const float LaneTraversalStallDistanceSlack = 0.9f;

    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly PathfindingSystem _pathfinding = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly WH40KWaveDefenceMapRegistrySystem _registry = default!;
    [Dependency] private readonly WH40KWaveDefenceObjectiveNavigationSystem _objectiveNavigation = default!;
    [Dependency] private readonly WH40KWaveDefencePerceptionSchedulerSystem _perceptionScheduler = default!;
    [Dependency] private readonly WH40KWaveDefenceLocomotionSystem _locomotion = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.wave.ai");
        SubscribeLocalEvent<DamageDealtEvent>(OnDamageDealt);
        SubscribeLocalEvent<WH40KWaveDefenceAttackerComponent, MobStateChangedEvent>(OnAttackerMobStateChanged);
        SubscribeLocalEvent<WH40KWaveDefenceAttackerComponent, ComponentShutdown>(OnAttackerShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnAttackerMobStateChanged(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        TryComp(uid, out HTNComponent? htn);
        DeactivateAttackerRuntime(uid, attacker, htn, $"mob-state:{args.NewMobState.ToString().ToLowerInvariant()}", sleepNpc: true);
    }

    private void OnAttackerShutdown(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, ComponentShutdown args)
    {
        TryComp(uid, out HTNComponent? htn);
        DeactivateAttackerRuntime(uid, attacker, htn, "component-shutdown", sleepNpc: false);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        CleanupAllAttackers("round-restart");
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        if (args.New == GameRunLevel.InRound)
            return;

        CleanupAllAttackers($"runlevel:{args.New}");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var attacker, out var htn, out var xform))
        {
            if (!IsAttackerOperational(uid, htn, xform))
            {
                if (HasRuntimeActivity(uid, attacker))
                    DeactivateAttackerRuntime(uid, attacker, htn, xform.MapID == MapId.Nullspace ? "nullspace" : "inactive-runtime", sleepNpc: false);

                continue;
            }

            EnsureInitialized(uid, attacker, htn, xform);
            UpdateRouteProgress(attacker, xform.Coordinates);

            if (_timing.CurTime < attacker.NextTacticalThinkAt)
                continue;

            attacker.NextTacticalThinkAt = _timing.CurTime + GetTacticalThinkDelay(uid, attacker);
            ConsumePerceptionResult(uid, attacker, htn, xform);
            UpdateRememberedPlayer(uid, attacker, htn);
            var previousContactMode = attacker.PlayerContactMode;
            RefreshPlayerContactPolicy(attacker, xform.Coordinates);
            HandlePlayerContactTransition(uid, attacker, htn, xform.Coordinates, previousContactMode);
            QueuePerceptionEvaluation(uid, attacker, htn, xform);
            UpdateForcedTarget(uid, attacker, htn, xform);
            UpdateGeometryRecoveryTarget(attacker, xform.Coordinates);
            RefreshProgress(uid, attacker, xform);
            var engaged = IsEngaged(attacker, htn);
            var visibleCombatContact = HasVisibleCombatContact(attacker);
            var investigatingPlayer = HasInvestigationContact(attacker);
            var tacticalPlayerContact = visibleCombatContact || investigatingPlayer;
            RefreshDesiredTargetProposal(uid, attacker, htn, xform, engaged);
            RefreshTargetRoles(attacker, xform.Coordinates);
            EnsureDesiredTargetSynced(uid, attacker, htn, xform, engaged);
            UpdateDebugState(uid, attacker, htn, xform);

            var steering = CompOrNull<NPCSteeringComponent>(uid);
            var noPath = steering?.Status == SteeringStatus.NoPath;
            var planless = IsPlanless(uid, attacker, htn, steering, engaged, tacticalPlayerContact);
            var disengage = ShouldDisengageFromCombat(attacker, xform);
            var stalled = ShouldRecover(uid, attacker, xform.Coordinates, steering, tacticalPlayerContact);
            TraceDiagnostics(uid, attacker, htn, xform, steering, engaged, noPath, planless, stalled, disengage);

            if ((!noPath && !stalled && !planless && !disengage) || _timing.CurTime < attacker.NextRecoveryAttemptAt)
                continue;

            AttemptRecovery(uid, attacker, htn, xform, steering, engaged, visibleCombatContact, investigatingPlayer, noPath, planless, stalled, disengage);
            UpdateDebugState(uid, attacker, htn, xform);
        }
    }

    private void EnsureInitialized(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        if (attacker.RuntimeInitialized)
            return;

        attacker.RuntimeInitialized = true;
        attacker.CandidateLaneIds = attacker.CandidateLaneIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (attacker.CandidateLaneIds.Count == 0)
            attacker.CandidateLaneIds = _registry.GetLaneIds(xform.MapID).OrderBy(id => id).ToList();

        attacker.BaseNavInteract = htn.Blackboard.GetValueOrDefault<bool>(NPCBlackboard.NavInteract, EntityManager);
        attacker.BaseNavPry = htn.Blackboard.GetValueOrDefault<bool>(NPCBlackboard.NavPry, EntityManager);
        attacker.BaseNavSmash = htn.Blackboard.GetValueOrDefault<bool>(NPCBlackboard.NavSmash, EntityManager);
        attacker.BaseNavClimb = htn.Blackboard.GetValueOrDefault<bool>(NPCBlackboard.NavClimb, EntityManager);

        attacker.CanInteract = attacker.BaseNavInteract || HasComp<ComplexInteractionComponent>(uid);
        attacker.CanPry = attacker.BaseNavPry || HasComp<PryingComponent>(uid);
        attacker.CanSmash = attacker.BaseNavSmash || (HasComp<MeleeWeaponComponent>(uid) && HasComp<CombatModeComponent>(uid));
        attacker.CanClimb = attacker.BaseNavClimb || HasComp<ClimbingComponent>(uid);

        if (!string.IsNullOrWhiteSpace(attacker.LaneId) && attacker.LanePoints.Count == 0)
            attacker.LanePoints = _registry.GetLaneRoute(xform.MapID, attacker.LaneId, attacker.Role);

        attacker.LastLaneChangeAt = _timing.CurTime;
        attacker.LaneCommitUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(4f, attacker.LaneCommitSeconds));
        attacker.RouteStartCoordinates = xform.Coordinates;
        attacker.SwarmBandIndex = Math.Abs(uid.Id.GetHashCode()) % SwarmBandOffsets.Length;
        InitializeRouteProgress(attacker, xform.Coordinates, rebaseFromClosest: false);
        _npc.SetBlackboard(uid, VisionRadiusKey, Math.Max(6f, attacker.VisionRadius), htn);
        _npc.SetBlackboard(uid, AggroVisionRadiusKey, Math.Max(attacker.VisionRadius, attacker.AggroVisionRadius), htn);
        ApplyNavigationPolicy(uid, attacker, htn);
        ResetProgress(attacker, xform.Coordinates);
        SetIntent(attacker, attacker.Intent, "brain", "initialize");
        MarkPerceptionState(attacker, "none");
        MarkNavigationState(attacker, "initialize");
        RefreshDesiredTargetProposal(uid, attacker, htn, xform, engaged: false, force: true);
        RefreshTargetRoles(attacker, xform.Coordinates);

        if (TryGetMovementTargetDirective(attacker, out var initialTarget, out _))
        {
            PushTarget(uid, attacker, htn, initialTarget, "initial-assault-seed");
            attacker.DebugState = "Seeded initial assault target.";
        }

        QueuePerceptionEvaluation(uid, attacker, htn, xform);
    }

    private void ConsumePerceptionResult(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        if (!_perceptionScheduler.TryConsumeResult(uid, out var result))
            return;

        if (result.RequestEpoch != attacker.PendingPerceptionRequestEpoch)
            return;

        attacker.LastAppliedPerceptionRequestEpoch = result.RequestEpoch;
        attacker.PendingPerceptionRequestEpoch = 0;

        if (!result.HasDirectContact ||
            Deleted(result.Target) ||
            !_mobState.IsAlive(result.Target) ||
            !TryComp(result.Target, out TransformComponent? targetXform) ||
            targetXform.MapID != xform.MapID)
        {
            if (!attacker.VisiblePlayer.IsValid())
            {
                var label = attacker.RememberedPlayer.IsValid() ? "memory-only" : "no-contact";
                MarkPerceptionState(attacker, label);
            }

            return;
        }

        var changedTarget = attacker.VisiblePlayer != result.Target ||
                            attacker.RememberedPlayer != result.Target;

        RememberDirectContact(attacker, result.Target, targetXform.Coordinates, result.Label);
        RelayDirectContact(uid, attacker, xform.MapID, xform.Coordinates, result.Target, targetXform.Coordinates);

        if (changedTarget)
            _htn.Replan(htn);
    }

    private void QueuePerceptionEvaluation(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        var aggroFocus = ShouldUseAggroPerception(attacker, htn);
        var visionRadius = htn.Blackboard.GetValueOrDefault<float>(VisionRadiusKey, EntityManager);
        var aggroVisionRadius = htn.Blackboard.GetValueOrDefault<float>(AggroVisionRadiusKey, EntityManager);
        var radius = Math.Max(0f, aggroFocus ? aggroVisionRadius : visionRadius);
        if (xform.MapID == MapId.Nullspace || radius <= 0f)
            return;

        var objectivePressure = IsObjectivePressureState(attacker);
        var objectiveCoordinates = EntityCoordinates.Invalid;
        if (objectivePressure &&
            attacker.Objective is { } objective &&
            !Deleted(objective))
        {
            objectiveCoordinates = Transform(objective).Coordinates;
        }

        var preferredTarget = attacker.CombatFocusTarget.IsValid() ? attacker.CombatFocusTarget : EntityUid.Invalid;
        var rememberedTarget = attacker.RememberedPlayer.IsValid() ? attacker.RememberedPlayer : EntityUid.Invalid;

        if (!_perceptionScheduler.ShouldRequestEvaluation(
                uid,
                xform.Coordinates,
                radius,
                aggroFocus,
                objectivePressure,
                preferredTarget,
                rememberedTarget))
        {
            return;
        }

        var requestEpoch = ++attacker.PerceptionRequestEpoch;
        attacker.PendingPerceptionRequestEpoch = requestEpoch;
        _perceptionScheduler.RequestEvaluation(
            uid,
            requestEpoch,
            xform,
            visionRadius,
            aggroVisionRadius,
            aggroFocus,
            objectivePressure,
            objectiveCoordinates,
            preferredTarget,
            rememberedTarget);
    }

    private bool ShouldUseAggroPerception(WH40KWaveDefenceAttackerComponent attacker, HTNComponent htn)
    {
        if (attacker.RememberedPlayer.IsValid() &&
            attacker.RememberedPlayerUntil != TimeSpan.Zero)
        {
            return true;
        }

        return TryGetBlackboardPlayerCombatTarget(htn, out var currentTarget, out _) &&
               currentTarget.IsValid() &&
               attacker.Objective != currentTarget;
    }

    private void UpdateRememberedPlayer(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn)
    {
        UpdateVisiblePlayerContact(attacker);

        if (!attacker.RememberedPlayer.IsValid())
            return;

        var deleted = Deleted(attacker.RememberedPlayer);
        var targetXform = deleted ? null : Transform(attacker.RememberedPlayer);
        var expired = attacker.RememberedPlayerUntil != TimeSpan.Zero && _timing.CurTime >= attacker.RememberedPlayerUntil;
        var invalid =
            deleted ||
            targetXform?.MapID == MapId.Nullspace ||
            !IsAttackablePlayerTarget(attacker.RememberedPlayer);

        if (!expired && !invalid)
        {
            var policy = EvaluatePlayerContactPolicy(attacker, Transform(uid).Coordinates);
            if (policy.Mode == WH40KWaveDefencePlayerContactMode.PassiveMemory &&
                TryGetBlackboardPlayerCombatTarget(htn, out var focusedMemoryTarget, out _) &&
                focusedMemoryTarget == attacker.RememberedPlayer)
            {
                MarkPerceptionState(attacker, "memory-passive");
                attacker.NextDeliberationAt = TimeSpan.Zero;
                attacker.NextTacticalThinkAt = TimeSpan.Zero;
                _htn.Replan(htn);
            }

            return;
        }

        var focusedTarget = TryGetBlackboardPlayerCombatTarget(htn, out var currentTarget, out _) &&
                            currentTarget == attacker.RememberedPlayer;
        var rememberedTarget = attacker.RememberedPlayer;
        var clearReason = expired
            ? "expired"
            : invalid
                ? "invalid"
                : "unknown";

        ClearRememberedPlayer(attacker);
        if (attacker.VisiblePlayer == rememberedTarget)
            ClearVisiblePlayer(attacker);
        ClearCombatPursuitState(attacker);
        MarkPerceptionState(attacker, "memory-cleared");
        _sawmill.Debug(
            $"WaveDefence memory cleared for {ToPrettyString(uid)}: target={ToPrettyString(rememberedTarget)}, reason={clearReason}, focusedTarget={focusedTarget}, willReplan={focusedTarget}.");

        if (focusedTarget)
            _htn.Replan(htn);
    }

    private void RememberDirectContact(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityUid target,
        EntityCoordinates coordinates,
        string perceptionLabel)
    {
        attacker.VisiblePlayer = target;
        attacker.VisiblePlayerCoordinates = coordinates;
        attacker.VisiblePlayerUntil = _timing.CurTime + TimeSpan.FromSeconds(VisibleContactGraceSeconds);
        attacker.RememberedPlayer = target;
        attacker.RememberedPlayerCoordinates = coordinates;
        attacker.RememberedPlayerUntil = attacker.PlayerMemorySeconds > 0f
            ? _timing.CurTime + TimeSpan.FromSeconds(attacker.PlayerMemorySeconds)
            : TimeSpan.Zero;
        attacker.RememberedPlayerSource = WH40KWaveDefencePlayerContactSource.DirectSight;
        attacker.RememberedPlayerReceivedAt = _timing.CurTime;
        MarkPerceptionState(attacker, perceptionLabel);
        attacker.NextTacticalThinkAt = TimeSpan.Zero;
        attacker.NextDeliberationAt = TimeSpan.Zero;
        attacker.NextLocomotionThinkAt = TimeSpan.Zero;
    }

    private void RelayDirectContact(
        EntityUid owner,
        WH40KWaveDefenceAttackerComponent attacker,
        MapId ownerMapId,
        EntityCoordinates ownerCoordinates,
        EntityUid target,
        EntityCoordinates coordinates)
    {
        var relayRadius = MathF.Max(0f, attacker.PlayerRelayRadius);
        if (relayRadius <= 0f)
            return;

        var relayCooldown = MathF.Max(0f, attacker.PlayerRelayCooldownSeconds);
        if (attacker.LastPlayerRelayAt != TimeSpan.Zero &&
            _timing.CurTime - attacker.LastPlayerRelayAt < TimeSpan.FromSeconds(relayCooldown))
        {
            return;
        }

        attacker.LastPlayerRelayAt = _timing.CurTime;

        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, HTNComponent, ActiveNPCComponent, TransformComponent>();
        while (query.MoveNext(out var allyUid, out var ally, out var allyHtn, out _, out var allyXform))
        {
            if (allyUid == owner ||
                !ally.RuntimeInitialized ||
                allyXform.MapID != ownerMapId ||
                ally.Objective != attacker.Objective)
            {
                continue;
            }

            if (!ownerCoordinates.TryDistance(EntityManager, allyXform.Coordinates, out var allyDistance) ||
                allyDistance > relayRadius)
            {
                continue;
            }

            if (!ShouldAcceptRelayedContact(ally, target, coordinates))
                continue;

            if (!RememberRelayedContact(ally, target, coordinates))
                continue;

            ally.NextTacticalThinkAt = TimeSpan.Zero;
            ally.NextDeliberationAt = TimeSpan.Zero;
            ally.NextLocomotionThinkAt = TimeSpan.Zero;
            _htn.Replan(allyHtn);
        }
    }

    private bool ShouldAcceptRelayedContact(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityUid target,
        EntityCoordinates coordinates)
    {
        if (!IsAttackablePlayerTarget(target) || !coordinates.IsValid(EntityManager))
            return false;

        if (attacker.VisiblePlayer == target &&
            attacker.VisiblePlayerUntil != TimeSpan.Zero &&
            _timing.CurTime < attacker.VisiblePlayerUntil)
        {
            return false;
        }

        if (attacker.RememberedPlayer != target ||
            attacker.RememberedPlayerUntil == TimeSpan.Zero ||
            _timing.CurTime >= attacker.RememberedPlayerUntil)
        {
            return true;
        }

        if (attacker.RememberedPlayerSource == WH40KWaveDefencePlayerContactSource.DirectSight &&
            attacker.RememberedPlayerReceivedAt != TimeSpan.Zero &&
            _timing.CurTime - attacker.RememberedPlayerReceivedAt < TimeSpan.FromSeconds(RelayContactFreshnessSeconds))
        {
            return false;
        }

        if (attacker.RememberedPlayerCoordinates.IsValid(EntityManager) &&
            attacker.RememberedPlayerCoordinates.TryDistance(EntityManager, coordinates, out var relayDrift) &&
            relayDrift < RelayCoordinateUpdateDistance &&
            attacker.RememberedPlayerReceivedAt != TimeSpan.Zero &&
            _timing.CurTime - attacker.RememberedPlayerReceivedAt < TimeSpan.FromSeconds(RelayContactFreshnessSeconds))
        {
            return false;
        }

        return true;
    }

    private bool RememberRelayedContact(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityUid target,
        EntityCoordinates coordinates)
    {
        if (!IsAttackablePlayerTarget(target) || !coordinates.IsValid(EntityManager))
            return false;

        var relayMemorySeconds = attacker.PlayerRelayMemorySeconds > 0f
            ? MathF.Min(attacker.PlayerMemorySeconds, attacker.PlayerRelayMemorySeconds)
            : attacker.PlayerMemorySeconds;

        if (relayMemorySeconds <= 0f)
            return false;

        ClearVisiblePlayer(attacker);
        attacker.RememberedPlayer = target;
        attacker.RememberedPlayerCoordinates = coordinates;
        attacker.RememberedPlayerUntil = _timing.CurTime + TimeSpan.FromSeconds(relayMemorySeconds);
        attacker.RememberedPlayerSource = WH40KWaveDefencePlayerContactSource.AllyRelay;
        attacker.RememberedPlayerReceivedAt = _timing.CurTime;
        MarkPerceptionState(attacker, "relay-contact");
        return true;
    }

    private void UpdateForcedTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        if (!attacker.ForcedTarget.IsValid(EntityManager))
            return;

        TrackForcedTargetProgress(attacker, xform.Coordinates);
        var arrived = xform.Coordinates.TryDistance(EntityManager, attacker.ForcedTarget, out var distance) &&
                      distance <= attacker.PointArrivalRange;

        if (arrived)
        {
            ReleaseForcedTarget(attacker, htn, xform.Coordinates);
            return;
        }

        if (IsStabilizingForcedTarget(attacker))
        {
            var fallbackStallSeconds = Math.Max(attacker.StallSeconds, attacker.FallbackStallSeconds);
            if (fallbackStallSeconds <= 0f ||
                attacker.LastForcedTargetProgressAt == TimeSpan.Zero ||
                _timing.CurTime - attacker.LastForcedTargetProgressAt < TimeSpan.FromSeconds(fallbackStallSeconds))
            {
                return;
            }

            ReleaseForcedTarget(attacker, htn, xform.Coordinates);
            return;
        }

        var expired = attacker.ForcedTargetUntil != TimeSpan.Zero && _timing.CurTime >= attacker.ForcedTargetUntil;
        if (!expired)
            return;

        ReleaseForcedTarget(attacker, htn, xform.Coordinates);
    }

    private void RefreshProgress(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        TransformComponent xform)
    {
        RefreshCombatProgress(attacker, xform.Coordinates);
        var score = GetProgressScore(uid, attacker, xform.Coordinates);
        if (score <= attacker.BestProgressScore + 0.35f)
        {
            if (TryComp(uid, out NPCSteeringComponent? steering) &&
                steering.ActionableObstacle &&
                steering.LastObstacleProgressAt > attacker.LastProgressAt)
            {
                attacker.LastProgressAt = steering.LastObstacleProgressAt;
            }

            return;
        }

        attacker.BestProgressScore = score;
        attacker.LastProgressAt = _timing.CurTime;
    }

    private bool ShouldRecover(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        NPCSteeringComponent? steering,
        bool tacticalPlayerContact)
    {
        if (tacticalPlayerContact || IsStabilizingForcedTarget(attacker))
            return false;

        if (steering == null)
            return false;

        if (IsInRangeTraversalDeadlock(uid, attacker, origin, steering))
            return true;

        if (steering.Status != SteeringStatus.Moving)
            return false;

        if (steering.ActionableObstacle &&
            (steering.DoAfterId != null ||
             steering.LastObstacleSeenAt != TimeSpan.Zero &&
             _timing.CurTime - steering.LastObstacleSeenAt < TimeSpan.FromSeconds(ActionableObstacleEncounterGraceSeconds) ||
             steering.LastObstacleProgressAt != TimeSpan.Zero &&
             _timing.CurTime - steering.LastObstacleProgressAt < TimeSpan.FromSeconds(ActionableObstacleRecoveryGraceSeconds)))
        {
            return false;
        }

        var stallSeconds = attacker.StallSeconds;
        if (stallSeconds <= 0f)
            return false;

        return _timing.CurTime - attacker.LastProgressAt >= TimeSpan.FromSeconds(stallSeconds);
    }

    private bool IsInRangeTraversalDeadlock(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        NPCSteeringComponent steering)
    {
        if (steering.Status != SteeringStatus.InRange ||
            attacker.AiProfile != WH40KWaveAiProfile.SimpleSwarm ||
            attacker.RouteCompleted ||
            attacker.LanePointIndex < 0 ||
            attacker.LanePointIndex >= attacker.LanePoints.Count)
        {
            return false;
        }

        var graceSeconds = MathF.Max(1.25f, MathF.Min(attacker.StallSeconds, 2.5f));
        if (attacker.LastProgressAt == TimeSpan.Zero ||
            _timing.CurTime - attacker.LastProgressAt < TimeSpan.FromSeconds(graceSeconds))
        {
            return false;
        }

        if (!TryBuildRouteGeometry(attacker, out var vertices, out var pointVertices, out var totalLength) ||
            !TryGetPointProgressRatio(attacker.LanePointIndex, pointVertices, vertices, totalLength, out var pointProgress))
        {
            return true;
        }

        var currentProgress = ComputeSimpleSwarmProgress(attacker, vertices, totalLength, origin);
        var epsilon = ResolvePointProgressEpsilon(attacker, attacker.LanePointIndex, totalLength);
        var pointUid = attacker.LanePoints[attacker.LanePointIndex];

        if (ShouldForceAdvanceStalledLanePoint(attacker, origin, currentProgress, pointProgress, pointUid, epsilon))
            return true;

        if (origin.TryDistance(EntityManager, steering.Coordinates, out var targetDistance) &&
            targetDistance <= Math.Max(attacker.PointArrivalRange + 0.6f, 1f))
        {
            return true;
        }

        if (TryComp(pointUid, out TransformComponent? pointXform) &&
            origin.TryDistance(EntityManager, pointXform.Coordinates, out var pointDistance) &&
            pointDistance <= ResolvePointArrivalRange(attacker, pointUid) + LaneTraversalStallDistanceSlack)
        {
            return true;
        }

        return currentProgress + Math.Max(epsilon, 0.05f) >= pointProgress - 0.075f ||
               attacker.SharedLaneFrontProgress >= pointProgress + SwarmFrontAssistLead;
    }

    private bool IsPlanless(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        NPCSteeringComponent? steering,
        bool engaged,
        bool tacticalPlayerContact)
    {
        if (engaged || tacticalPlayerContact || steering != null || htn.Planning || htn.Plan != null || IsStabilizingForcedTarget(attacker))
            return false;

        if (!TryResolveCurrentTarget(uid, attacker, out _))
            return false;

        var graceSeconds = MathF.Max(1.25f, MathF.Min(attacker.StallSeconds, 2.5f));
        return _timing.CurTime - attacker.LastProgressAt >= TimeSpan.FromSeconds(graceSeconds);
    }

    private void AttemptRecovery(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        bool engaged,
        bool visibleCombatContact,
        bool investigatingPlayer,
        bool noPath,
        bool planless,
        bool stalled,
        bool disengage)
    {
        if (disengage && TryDisengageToLane(uid, attacker, htn, xform))
        {
            attacker.DebugState = $"Combat disengage engaged on lane '{attacker.LaneId}'.";
            _sawmill.Debug(
                $"WaveDefence recovery step succeeded for {ToPrettyString(uid)}: step=disengage, lane={attacker.LaneId}, reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, CompOrNull<NPCSteeringComponent>(uid), engaged)}");
            return;
        }

        if (engaged || visibleCombatContact || investigatingPlayer)
        {
            attacker.NextRecoveryAttemptAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, attacker.RecoveryCooldownSeconds));
            attacker.DebugState = visibleCombatContact
                ? "Pursuing defender; delaying route recovery escalation."
                : investigatingPlayer
                    ? "Investigating defender memory; delaying route recovery escalation."
                    : "Combat branch active; delaying route recovery escalation.";
            _sawmill.Debug(
                $"WaveDefence recovery delayed for {ToPrettyString(uid)}: reason={DescribeRecoveryDelayReason(engaged, visibleCombatContact, investigatingPlayer)}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
            return;
        }

        attacker.RecoveryAttempts++;
        if (noPath)
            attacker.NoPathCount++;

        attacker.NextRecoveryAttemptAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1f, attacker.RecoveryCooldownSeconds));
        _sawmill.Debug(
            $"WaveDefence recovery attempt for {ToPrettyString(uid)}: reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, attempt={attacker.RecoveryAttempts}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");

        if (planless && TryKickMovement(uid, attacker, htn))
        {
            attacker.DebugState = "Recovered from failed planning by re-pushing the current target.";
            ResetProgress(attacker, xform.Coordinates);
            _sawmill.Debug(
                $"WaveDefence recovery step succeeded for {ToPrettyString(uid)}: step=kick-movement, reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, CompOrNull<NPCSteeringComponent>(uid), engaged)}");
            return;
        }

        if (TryEscalateObstaclePolicy(uid, attacker, htn, xform))
        {
            attacker.DebugState = $"Escalated obstacle policy to level {attacker.RecoveryLevel}.";
            _sawmill.Debug(
                $"WaveDefence recovery step succeeded for {ToPrettyString(uid)}: step=escalate-policy, level={attacker.RecoveryLevel}, reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, CompOrNull<NPCSteeringComponent>(uid), engaged)}");
            return;
        }

        if (TryForceBreachTarget(uid, attacker, htn, xform))
        {
            attacker.DebugState = $"Forced breach target on lane '{attacker.LaneId}'.";
            _sawmill.Debug(
                $"WaveDefence recovery step succeeded for {ToPrettyString(uid)}: step=force-breach, lane={attacker.LaneId}, reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, CompOrNull<NPCSteeringComponent>(uid), engaged)}");
            return;
        }

        if (TryRecoverComplexGeometryInLane(uid, attacker, htn, xform))
        {
            attacker.DebugState = $"Recovered lane traversal on lane '{attacker.LaneId}'.";
            _sawmill.Debug(
                $"WaveDefence recovery step succeeded for {ToPrettyString(uid)}: step=lane-geometry-recover, lane={attacker.LaneId}, reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, CompOrNull<NPCSteeringComponent>(uid), engaged)}");
            return;
        }

        if (TryFallback(uid, attacker, htn, xform))
        {
            attacker.DebugState = $"Fallback target engaged on lane '{attacker.LaneId}'.";
            _sawmill.Debug(
                $"WaveDefence recovery step succeeded for {ToPrettyString(uid)}: step=fallback, lane={attacker.LaneId}, reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, CompOrNull<NPCSteeringComponent>(uid), engaged)}");
            return;
        }

        if (TryReroute(uid, attacker, htn, xform))
        {
            attacker.DebugState = $"Rerouted into lane '{attacker.LaneId}'.";
            _sawmill.Debug(
                $"WaveDefence recovery step succeeded for {ToPrettyString(uid)}: step=reroute, lane={attacker.LaneId}, reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, CompOrNull<NPCSteeringComponent>(uid), engaged)}");
            return;
        }

        if (TryDirectObjective(uid, attacker, htn))
        {
            attacker.DebugState = "Direct objective push engaged after repeated stall.";
            _sawmill.Debug(
                $"WaveDefence recovery step succeeded for {ToPrettyString(uid)}: step=direct-objective, reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, CompOrNull<NPCSteeringComponent>(uid), engaged)}");
            return;
        }

        if (noPath && steering != null)
            steering.Status = SteeringStatus.Moving;

        _htn.Replan(htn);
        _sawmill.Debug(
            $"WaveDefence recovery exhausted for {ToPrettyString(uid)} on lane '{attacker.LaneId}': reason={BuildRecoveryReason(noPath, planless, stalled, disengage)}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
    }

    private bool TryEscalateObstaclePolicy(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        var current = BuildNavigationPolicy(attacker);
        for (var level = attacker.RecoveryLevel + 1; level <= 3; level++)
        {
            var next = BuildNavigationPolicy(attacker, level);
            if (next == current)
                continue;

            attacker.RecoveryLevel = level;
            ApplyNavigationPolicy(uid, attacker, htn);

            if (TryResolveCurrentTarget(uid, attacker, out var target))
                PushTarget(uid, attacker, htn, target, $"recovery-escalate-l{attacker.RecoveryLevel}");

            ResetProgress(attacker, xform.Coordinates);
            return true;
        }

        return false;
    }

    private bool TryForceBreachTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm)
            return false;

        if (attacker.Role != WH40KWaveSquadRole.Breacher ||
            string.IsNullOrWhiteSpace(attacker.LaneId) ||
            !TryGetUpcomingBreachPoint(xform.MapID, attacker, out var breachPoint))
        {
            return false;
        }

        SetForcedTarget(
            uid,
            attacker,
            htn,
            breachPoint.Xform.Coordinates,
            $"breach:{attacker.LaneId}",
            TimeSpan.FromSeconds(8),
            WH40KWaveDefenceAttackerIntent.Advance,
            WH40KWaveDefenceForcedTargetKind.Breach);

        ResetProgress(attacker, xform.Coordinates);
        return true;
    }

    private bool TryFallback(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        if (attacker.Intent == WH40KWaveDefenceAttackerIntent.Fallback ||
            string.IsNullOrWhiteSpace(attacker.LaneId))
        {
            return false;
        }

        if (!TryGetFallbackAnchorTarget(attacker, out var fallbackCoordinates, out var fallbackLabel))
            return false;

        SetForcedTarget(
            uid,
            attacker,
            htn,
            fallbackCoordinates,
            fallbackLabel,
            TimeSpan.Zero,
            WH40KWaveDefenceAttackerIntent.Fallback,
            WH40KWaveDefenceForcedTargetKind.Fallback,
            TimeSpan.FromSeconds(Math.Max(0.5f, attacker.FallbackCommitSeconds)));

        attacker.FallbackCount++;
        ResetProgress(attacker, xform.Coordinates);
        return true;
    }

    private bool TryRecoverComplexGeometryInLane(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm)
            return false;

        if (!_locomotion.TryRecoverComplexGeometry(
                uid,
                attacker,
                xform.Coordinates,
                out var target,
                out _,
                out var advancedLanePoint))
        {
            return false;
        }

        if (target.IsValid(EntityManager))
        {
            var label = advancedLanePoint
                ? $"recover:lane-advance:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}"
                : $"recover:lane-detour:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}";
            SetGeometryRecoveryTarget(attacker, target, label);
            SetDesiredTargetProposal(attacker, target, label);
            SetMovementTargetDirective(attacker, target, label);
            PushTarget(
                uid,
                attacker,
                htn,
                target,
                advancedLanePoint ? "recovery-lane-advance" : "recovery-lane-detour",
                requestReplan: false);
        }
        else if (advancedLanePoint)
        {
            ClearGeometryRecoveryTarget(attacker, clearDirectiveState: true);
            _htn.Replan(htn);
        }

        ResetProgress(attacker, xform.Coordinates);
        return true;
    }

    private bool TryDisengageToLane(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        if (!TryGetLastReachedPointCoordinates(attacker, out var disengageCoordinates) &&
            !TryGetFallbackAnchorTarget(attacker, out disengageCoordinates, out _))
        {
            return false;
        }

        var disengageLabel = $"disengage:{attacker.LaneId}";
        SetForcedTarget(
            uid,
            attacker,
            htn,
            disengageCoordinates,
            disengageLabel,
            TimeSpan.Zero,
            WH40KWaveDefenceAttackerIntent.Disengage,
            WH40KWaveDefenceForcedTargetKind.DisengageToLane,
            TimeSpan.FromSeconds(Math.Max(0.5f, attacker.CombatDisengageCommitSeconds)));

        ClearCombatPursuitState(attacker);
        ResetProgress(attacker, xform.Coordinates);
        return true;
    }

    private bool TryReroute(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        if (_timing.CurTime < attacker.LaneCommitUntil && attacker.NoPathCount < 2)
            return false;

        var candidateLanes = attacker.CandidateLaneIds.Count > 0
            ? attacker.CandidateLaneIds
            : _registry.GetLaneIds(xform.MapID).OrderBy(id => id).ToList();

        if (candidateLanes.Count == 0)
            return false;

        var allowCrossLaneReroute =
            attacker.NoPathCount >= 3 ||
            attacker.RecoveryAttempts >= 2 ||
            string.IsNullOrWhiteSpace(attacker.HomeLaneId);

        var currentScore = EvaluateLaneScore(xform.MapID, attacker.LaneId, attacker.Role, attacker.AiProfile, xform.Coordinates);

        var catastrophicLaneFailure =
            attacker.NoPathCount >= 4 ||
            attacker.RecoveryAttempts >= 3 ||
            currentScore == float.MinValue;

        var bestLane = attacker.LaneId;
        var bestScore = currentScore;

        foreach (var laneId in candidateLanes)
        {
            if (string.IsNullOrWhiteSpace(laneId))
                continue;

            var isCrossLane = !string.Equals(laneId, attacker.LaneId, StringComparison.OrdinalIgnoreCase);
            if (isCrossLane && !allowCrossLaneReroute)
                continue;

            if (isCrossLane &&
                !catastrophicLaneFailure &&
                !string.IsNullOrWhiteSpace(attacker.HomeLaneId) &&
                !string.Equals(laneId, attacker.HomeLaneId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = EvaluateLaneScore(xform.MapID, laneId, attacker.Role, attacker.AiProfile, xform.Coordinates);
            if (isCrossLane)
            {
                if (!string.IsNullOrWhiteSpace(attacker.HomeLaneId) &&
                    string.Equals(laneId, attacker.HomeLaneId, StringComparison.OrdinalIgnoreCase))
                {
                    score += catastrophicLaneFailure ? 12f : 18f;
                }
                else
                {
                    score -= catastrophicLaneFailure ? 10f : 25f;
                }
            }

            if (score <= bestScore + 3f)
                continue;

            bestLane = laneId;
            bestScore = score;
        }

        if (string.IsNullOrWhiteSpace(bestLane))
            return false;

        if (string.Equals(bestLane, attacker.LaneId, StringComparison.OrdinalIgnoreCase))
            return false;

        var carriedProgress = CaptureRouteProgress(attacker);
        if (!AssignLane(attacker, xform.MapID, bestLane, xform.Coordinates, carriedProgress))
            return false;

        SetIntent(attacker, WH40KWaveDefenceAttackerIntent.Reroute, "recovery", $"reroute:{attacker.LaneId}");
        attacker.LaneRerouteCount++;
        ClearForcedTarget(attacker);
        ResetProgress(attacker, xform.Coordinates);

        if (TryResolveCurrentTarget(uid, attacker, out var target))
            PushTarget(uid, attacker, htn, target, $"reroute:{attacker.LaneId}");

        return true;
    }

    private bool TryDirectObjective(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, HTNComponent htn)
    {
        var origin = Transform(uid).Coordinates;
        var handoffReady = IsSimpleSwarmFinalObjectiveHandoffReady(
            attacker,
            origin,
            attacker.CurrentRouteProgressRatio,
            attacker.SharedLaneFrontProgress);

        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm &&
            !attacker.RouteCompleted &&
            !handoffReady &&
            attacker.NoPathCount < 2 &&
            attacker.RecoveryAttempts < 3)
        {
            return false;
        }

        if (attacker.Objective is not { } objective || Deleted(objective))
        {
            return false;
        }

        if (!_objectiveNavigation.TryResolveObjectiveAssaultTarget(uid, origin, objective, out var objectiveTarget, out var blocker))
            return false;

        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm &&
            _objectiveNavigation.TryResolveSwarmSlotTarget(uid, origin, objectiveTarget, out var slottedTarget))
        {
            objectiveTarget = slottedTarget;
        }

        SetForcedTarget(
            uid,
            attacker,
            htn,
            objectiveTarget,
            blocker.IsValid() ? $"objective-blocker:{ToPrettyString(blocker)}" : "objective-direct",
            TimeSpan.FromSeconds(attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm ? 8 : 12),
            attacker.RouteCompleted
                ? WH40KWaveDefenceAttackerIntent.SiegeObjective
                : WH40KWaveDefenceAttackerIntent.DirectObjective,
            WH40KWaveDefenceForcedTargetKind.DirectObjective,
            null,
            blocker);
        return true;
    }

    private void SetForcedTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        EntityCoordinates coordinates,
        string label,
        TimeSpan duration,
        WH40KWaveDefenceAttackerIntent intent,
        WH40KWaveDefenceForcedTargetKind kind = WH40KWaveDefenceForcedTargetKind.None,
        TimeSpan? commitDuration = null,
        EntityUid blocker = default)
    {
        ClearGeometryRecoveryTarget(attacker, clearDirectiveState: true);
        attacker.ForcedTarget = coordinates;
        attacker.ForcedTargetLabel = label;
        attacker.ForcedTargetKind = kind;
        attacker.ForcedTargetUntil = duration > TimeSpan.Zero
            ? _timing.CurTime + duration
            : TimeSpan.Zero;
        attacker.ForcedTargetCommitUntil = commitDuration.HasValue && commitDuration.Value > TimeSpan.Zero
            ? _timing.CurTime + commitDuration.Value
            : TimeSpan.Zero;
        attacker.LastForcedTargetDistance = float.MaxValue;
        attacker.LastForcedTargetProgressAt = _timing.CurTime;
        SetIntent(attacker, intent, ResolveDecisionPriority(intent, kind, label), ResolveDecisionReason(intent, kind, label));
        attacker.ActiveSiegeBlocker = blocker;
        attacker.ActiveSiegeBlockerLabel = blocker.IsValid() ? label : string.Empty;
        SetDesiredTargetProposal(attacker, coordinates, $"forced:{label}");
        attacker.LastDeliberationAt = _timing.CurTime;
        attacker.NextDeliberationAt = _timing.CurTime + GetDeliberationDelay(uid, attacker, engaged: false);
        PushTarget(uid, attacker, htn, coordinates, $"forced:{label}");
    }

    private static void ClearForcedTarget(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.ForcedTarget = EntityCoordinates.Invalid;
        attacker.ForcedTargetUntil = TimeSpan.Zero;
        attacker.ForcedTargetCommitUntil = TimeSpan.Zero;
        attacker.ForcedTargetLabel = string.Empty;
        attacker.ForcedTargetKind = WH40KWaveDefenceForcedTargetKind.None;
        attacker.LastForcedTargetDistance = float.MaxValue;
        attacker.LastForcedTargetProgressAt = TimeSpan.Zero;
        attacker.ActiveSiegeBlocker = EntityUid.Invalid;
        attacker.ActiveSiegeBlockerLabel = string.Empty;
    }

    private void ReleaseForcedTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        EntityCoordinates origin)
    {
        ClearForcedTarget(attacker);
        ClearDesiredTargetProposal(attacker);
        SetIntent(
            attacker,
            attacker.RouteCompleted
                ? WH40KWaveDefenceAttackerIntent.SiegeObjective
                : WH40KWaveDefenceAttackerIntent.Advance,
            "brain",
            attacker.RouteCompleted ? "resume-siege-after-forced-target" : "resume-advance-after-forced-target");
        ResetProgress(attacker, origin);
        _htn.Replan(htn);
    }

    private void TrackForcedTargetProgress(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        if (!attacker.ForcedTarget.IsValid(EntityManager) ||
            !origin.TryDistance(EntityManager, attacker.ForcedTarget, out var distance))
        {
            return;
        }

        if (attacker.LastForcedTargetDistance == float.MaxValue ||
            distance + ForcedTargetProgressEpsilon < attacker.LastForcedTargetDistance)
        {
            attacker.LastForcedTargetDistance = distance;
            attacker.LastForcedTargetProgressAt = _timing.CurTime;
            return;
        }

        if (distance < attacker.LastForcedTargetDistance)
            attacker.LastForcedTargetDistance = distance;
    }

    private static bool IsStabilizingForcedTarget(WH40KWaveDefenceAttackerComponent attacker)
    {
        return attacker.ForcedTargetKind is WH40KWaveDefenceForcedTargetKind.Fallback or
            WH40KWaveDefenceForcedTargetKind.DisengageToLane;
    }

    private void UpdateGeometryRecoveryTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        if (!attacker.GeometryRecoveryTarget.IsValid(EntityManager))
            return;

        var clearRecovery = false;
        if (attacker.GeometryRecoveryUntil != TimeSpan.Zero &&
            _timing.CurTime >= attacker.GeometryRecoveryUntil)
        {
            clearRecovery = true;
        }
        else if (attacker.GeometryRecoveryLanePointIndex >= 0 &&
                 attacker.LanePointIndex != attacker.GeometryRecoveryLanePointIndex)
        {
            clearRecovery = true;
        }
        else if (attacker.CurrentRouteProgressRatio >= attacker.GeometryRecoveryStartProgress + attacker.GeometryRecoveryProgressDelta)
        {
            clearRecovery = true;
        }
        else if (origin.TryDistance(EntityManager, attacker.GeometryRecoveryTarget, out var distance))
        {
            if (attacker.GeometryRecoveryBestDistance == float.MaxValue ||
                distance + ForcedTargetProgressEpsilon < attacker.GeometryRecoveryBestDistance)
            {
                attacker.GeometryRecoveryBestDistance = distance;
                attacker.GeometryRecoveryLastProgressAt = _timing.CurTime;
            }
            else if (distance < attacker.GeometryRecoveryBestDistance)
            {
                attacker.GeometryRecoveryBestDistance = distance;
            }

            if (distance <= Math.Max(attacker.PointArrivalRange + 0.6f, 1f))
            {
                clearRecovery = true;
            }
            else if (attacker.GeometryRecoveryLastProgressAt != TimeSpan.Zero &&
                     _timing.CurTime - attacker.GeometryRecoveryLastProgressAt >= TimeSpan.FromSeconds(Math.Max(0.45f, attacker.GeometryRecoveryStallSeconds)))
            {
                clearRecovery = true;
            }
        }

        if (!clearRecovery)
            return;

        ClearGeometryRecoveryTarget(attacker, clearDirectiveState: true);
    }

    private bool TryResolveGeometryRecoveryTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityCoordinates target,
        out string label)
    {
        if (attacker.GeometryRecoveryTarget.IsValid(EntityManager))
        {
            target = attacker.GeometryRecoveryTarget;
            label = attacker.GeometryRecoveryLabel;
            return true;
        }

        target = EntityCoordinates.Invalid;
        label = string.Empty;
        return false;
    }

    private void SetGeometryRecoveryTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        string label)
    {
        attacker.GeometryRecoveryTarget = target;
        attacker.GeometryRecoveryLabel = label;
        attacker.GeometryRecoveryUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0.75f, attacker.GeometryRecoveryCommitSeconds));
        attacker.GeometryRecoveryStartedAt = _timing.CurTime;
        attacker.GeometryRecoveryLastProgressAt = _timing.CurTime;
        attacker.GeometryRecoveryStartProgress = attacker.CurrentRouteProgressRatio;
        attacker.GeometryRecoveryBestDistance = float.MaxValue;
        attacker.GeometryRecoveryLanePointIndex = attacker.LanePointIndex;
    }

    private void ClearGeometryRecoveryTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        bool clearDirectiveState)
    {
        var previousTarget = attacker.GeometryRecoveryTarget;
        var previousLabel = attacker.GeometryRecoveryLabel;

        attacker.GeometryRecoveryTarget = EntityCoordinates.Invalid;
        attacker.GeometryRecoveryLabel = string.Empty;
        attacker.GeometryRecoveryUntil = TimeSpan.Zero;
        attacker.GeometryRecoveryStartedAt = TimeSpan.Zero;
        attacker.GeometryRecoveryLastProgressAt = TimeSpan.Zero;
        attacker.GeometryRecoveryStartProgress = 0f;
        attacker.GeometryRecoveryBestDistance = float.MaxValue;
        attacker.GeometryRecoveryLanePointIndex = -1;

        if (!clearDirectiveState)
            return;

        if (SameCoordinates(attacker.DesiredTargetProposal, previousTarget) &&
            string.Equals(attacker.DesiredTargetProposalLabel, previousLabel, StringComparison.Ordinal))
        {
            ClearDesiredTargetProposal(attacker);
            return;
        }

        if (SameCoordinates(attacker.MovementTargetDirective, previousTarget) &&
            string.Equals(attacker.MovementTargetDirectiveLabel, previousLabel, StringComparison.Ordinal))
        {
            ClearMovementTargetDirective(attacker);
        }
    }

    private void RefreshTargetRoles(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        RefreshPlayerContactPolicy(attacker, origin);

        if (attacker.DesiredTargetProposal.IsValid(EntityManager))
        {
            if (IsNavigationMovementLabel(attacker.DesiredTargetProposalLabel) &&
                attacker.LocomotionTarget.IsValid(EntityManager) &&
                IsNavigationMovementLabel(attacker.LocomotionTargetLabel))
            {
                SetMovementTargetDirective(attacker, attacker.LocomotionTarget, attacker.LocomotionTargetLabel);
                return;
            }

            SetMovementTargetDirective(attacker, attacker.DesiredTargetProposal, attacker.DesiredTargetProposalLabel);
            return;
        }

        if (attacker.LocomotionTarget.IsValid(EntityManager) &&
            IsNavigationMovementLabel(attacker.LocomotionTargetLabel))
        {
            SetMovementTargetDirective(attacker, attacker.LocomotionTarget, attacker.LocomotionTargetLabel);
            return;
        }

        ClearMovementTargetDirective(attacker);
    }

    private void RefreshPlayerContactPolicy(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        ApplyPlayerContactPolicy(attacker, origin, EvaluatePlayerContactPolicy(attacker, origin));
    }

    private void HandlePlayerContactTransition(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        EntityCoordinates origin,
        WH40KWaveDefencePlayerContactMode previousMode)
    {
        if (previousMode == attacker.PlayerContactMode)
            return;

        if (!IsTacticalContactMode(previousMode) ||
            IsTacticalContactMode(attacker.PlayerContactMode))
        {
            return;
        }

        ResetProgress(attacker, origin);
        var recoveryGrace = TimeSpan.FromSeconds(Math.Max(0.5f, PostTacticalContactRecoveryGraceSeconds));
        var graceUntil = _timing.CurTime + recoveryGrace;
        if (attacker.NextRecoveryAttemptAt < graceUntil)
            attacker.NextRecoveryAttemptAt = graceUntil;

        if (IsStabilizingForcedTarget(attacker) &&
            HasImmediateObjectiveOpportunity(uid, attacker, origin))
        {
            ReleaseForcedTarget(attacker, htn, origin);
            return;
        }

        _htn.Replan(htn);
    }

    private PlayerContactPolicyResult EvaluatePlayerContactPolicy(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        if (TryGetVisiblePlayerContact(attacker, out var combatTarget, out var combatCoordinates))
        {
            return new PlayerContactPolicyResult(
                WH40KWaveDefencePlayerContactMode.VisibleCombat,
                $"player:{ToPrettyString(combatTarget)}",
                ShouldOverrideObjective: true,
                combatTarget,
                combatCoordinates,
                EntityUid.Invalid,
                EntityCoordinates.Invalid);
        }

        if (TryGetRememberedPlayerContact(attacker, out var rememberedTarget, out var rememberedCoordinates))
        {
            var investigateLabel =
                $"investigate:{ToPrettyString(rememberedTarget)}:{FormatContactSource(attacker.RememberedPlayerSource)}";

            if (CanInvestigateRememberedContact(attacker, origin, rememberedCoordinates))
            {
                return new PlayerContactPolicyResult(
                    WH40KWaveDefencePlayerContactMode.InvestigateMemory,
                    investigateLabel,
                    ShouldOverrideObjective: false,
                    EntityUid.Invalid,
                    EntityCoordinates.Invalid,
                    rememberedTarget,
                    rememberedCoordinates);
            }

            return new PlayerContactPolicyResult(
                WH40KWaveDefencePlayerContactMode.PassiveMemory,
                $"memory-passive:{FormatContactSource(attacker.RememberedPlayerSource)}",
                ShouldOverrideObjective: false,
                EntityUid.Invalid,
                EntityCoordinates.Invalid,
                EntityUid.Invalid,
                EntityCoordinates.Invalid);
        }

        return new PlayerContactPolicyResult(
            WH40KWaveDefencePlayerContactMode.None,
            "no-contact",
            ShouldOverrideObjective: false,
            EntityUid.Invalid,
            EntityCoordinates.Invalid,
            EntityUid.Invalid,
            EntityCoordinates.Invalid);
    }

    private void ApplyPlayerContactPolicy(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        PlayerContactPolicyResult policy)
    {
        attacker.PlayerContactMode = policy.Mode;
        attacker.PlayerContactPolicyLabel = policy.Label;
        attacker.PlayerContactShouldOverrideObjective = policy.ShouldOverrideObjective;

        switch (policy.Mode)
        {
            case WH40KWaveDefencePlayerContactMode.VisibleCombat:
                SetCombatFocus(attacker, policy.CombatTarget, policy.CombatCoordinates, policy.Label);
                ClearInvestigationTarget(attacker);
                break;

            case WH40KWaveDefencePlayerContactMode.InvestigateMemory:
                ClearCombatFocus(attacker);
                SetInvestigationTarget(attacker, policy.InvestigationTarget, policy.InvestigationCoordinates, policy.Label, origin);
                break;

            default:
                ClearCombatFocus(attacker);
                ClearInvestigationTarget(attacker);
                break;
        }
    }

    private static bool HasVisibleCombatContact(
        WH40KWaveDefenceAttackerComponent attacker)
    {
        return attacker.PlayerContactMode == WH40KWaveDefencePlayerContactMode.VisibleCombat;
    }

    private static bool HasInvestigationContact(
        WH40KWaveDefenceAttackerComponent attacker)
    {
        return attacker.PlayerContactMode == WH40KWaveDefencePlayerContactMode.InvestigateMemory;
    }

    private static bool IsTacticalContactMode(WH40KWaveDefencePlayerContactMode mode)
    {
        return mode is WH40KWaveDefencePlayerContactMode.VisibleCombat or
            WH40KWaveDefencePlayerContactMode.InvestigateMemory;
    }

    private bool TryGetVisiblePlayerContact(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityUid target,
        out EntityCoordinates coordinates)
    {
        if (attacker.VisiblePlayer.IsValid() &&
            attacker.VisiblePlayerUntil != TimeSpan.Zero &&
            _timing.CurTime < attacker.VisiblePlayerUntil &&
            IsAttackablePlayerTarget(attacker.VisiblePlayer) &&
            attacker.VisiblePlayerCoordinates.IsValid(EntityManager))
        {
            target = attacker.VisiblePlayer;
            coordinates = attacker.VisiblePlayerCoordinates;
            return true;
        }

        target = EntityUid.Invalid;
        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private bool TryGetRememberedPlayerContact(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityUid target,
        out EntityCoordinates coordinates)
    {
        if (attacker.RememberedPlayer.IsValid() &&
            attacker.RememberedPlayerUntil != TimeSpan.Zero &&
            _timing.CurTime < attacker.RememberedPlayerUntil &&
            IsAttackablePlayerTarget(attacker.RememberedPlayer) &&
            attacker.RememberedPlayerCoordinates.IsValid(EntityManager))
        {
            target = attacker.RememberedPlayer;
            coordinates = attacker.RememberedPlayerCoordinates;
            return true;
        }

        target = EntityUid.Invalid;
        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private static bool IsObjectivePressureState(WH40KWaveDefenceAttackerComponent attacker)
    {
        return attacker.RouteCompleted ||
               attacker.Intent is WH40KWaveDefenceAttackerIntent.SiegeObjective or WH40KWaveDefenceAttackerIntent.DirectObjective ||
               attacker.LocomotionMode == WH40KWaveDefenceLocomotionMode.Objective;
    }

    private bool CanInvestigateRememberedContact(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates rememberedCoordinates)
    {
        if (!rememberedCoordinates.IsValid(EntityManager))
            return false;

        if (attacker.RememberedPlayerReceivedAt == TimeSpan.Zero)
            return false;

        float searchSeconds;
        float searchDistance;
        var objectivePressure = IsObjectivePressureState(attacker);

        if (!objectivePressure)
        {
            searchSeconds = MathF.Min(attacker.PlayerMemorySeconds, attacker.InvestigationSearchSeconds);
            searchDistance = attacker.InvestigationLeashDistance;
        }
        else
        {
            switch (attacker.RememberedPlayerSource)
            {
                case WH40KWaveDefencePlayerContactSource.DirectSight:
                    searchSeconds = attacker.ObjectiveMemorySearchSeconds;
                    searchDistance = attacker.ObjectiveMemorySearchDistance;
                    break;
                case WH40KWaveDefencePlayerContactSource.AllyRelay:
                    searchSeconds = attacker.ObjectiveRelaySearchSeconds;
                    searchDistance = attacker.ObjectiveRelaySearchDistance;
                    break;
                default:
                    return false;
            }
        }

        var maxAge = TimeSpan.FromSeconds(Math.Max(0.1f, searchSeconds));
        if (_timing.CurTime - attacker.RememberedPlayerReceivedAt > maxAge)
            return false;

        if (!origin.TryDistance(EntityManager, rememberedCoordinates, out var memoryDistance))
            return false;

        var distanceLimit = Math.Max(attacker.PointArrivalRange + 0.35f, searchDistance);
        if (memoryDistance > distanceLimit)
            return false;

        if (attacker.PlayerContactMode == WH40KWaveDefencePlayerContactMode.InvestigateMemory &&
            attacker.InvestigationTarget == attacker.RememberedPlayer)
        {
            TrackInvestigationProgress(attacker, origin, rememberedCoordinates);

            if (attacker.InvestigationAnchorCoordinates.IsValid(EntityManager) &&
                origin.TryDistance(EntityManager, attacker.InvestigationAnchorCoordinates, out var leashDistance))
            {
                var leashLimit = objectivePressure
                    ? Math.Max(distanceLimit, attacker.PointArrivalRange + 1.25f)
                    : Math.Max(distanceLimit, attacker.InvestigationLeashDistance);
                if (leashDistance > leashLimit)
                    return false;
            }

            var stallSeconds = Math.Max(0.35f, attacker.InvestigationStallSeconds);
            if (attacker.LastInvestigationProgressAt != TimeSpan.Zero &&
                _timing.CurTime - attacker.LastInvestigationProgressAt > TimeSpan.FromSeconds(stallSeconds))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetCombatFocusTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityUid target,
        out EntityCoordinates coordinates)
    {
        if (attacker.CombatFocusTarget.IsValid() &&
            attacker.CombatFocusCoordinates.IsValid(EntityManager))
        {
            target = attacker.CombatFocusTarget;
            coordinates = attacker.CombatFocusCoordinates;
            return true;
        }

        target = EntityUid.Invalid;
        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private bool TryGetInvestigationTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityUid target,
        out EntityCoordinates coordinates)
    {
        if (attacker.InvestigationTarget.IsValid() &&
            attacker.InvestigationCoordinates.IsValid(EntityManager))
        {
            target = attacker.InvestigationTarget;
            coordinates = attacker.InvestigationCoordinates;
            return true;
        }

        target = EntityUid.Invalid;
        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private void TrackInvestigationProgress(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityCoordinates rememberedCoordinates)
    {
        if (!origin.TryDistance(EntityManager, rememberedCoordinates, out var distance))
            return;

        if (attacker.LastInvestigationDistance == float.MaxValue ||
            distance + ForcedTargetProgressEpsilon < attacker.LastInvestigationDistance)
        {
            attacker.LastInvestigationDistance = distance;
            attacker.LastInvestigationProgressAt = _timing.CurTime;
            return;
        }

        if (distance < attacker.LastInvestigationDistance)
            attacker.LastInvestigationDistance = distance;
    }

    private static bool IsNavigationMovementLabel(string label)
    {
        return label.StartsWith("lane:", StringComparison.Ordinal) ||
               label.StartsWith("objective:", StringComparison.Ordinal);
    }

    private void RefreshCombatProgress(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        if (!TryGetCombatFocusTarget(attacker, out var target, out var targetCoordinates))
        {
            ClearCombatPursuitState(attacker);
            return;
        }

        if (attacker.CombatAnchorTarget != target ||
            !attacker.CombatAnchorCoordinates.IsValid(EntityManager))
        {
            attacker.CombatAnchorTarget = target;
            attacker.CombatAnchorCoordinates = origin;
            attacker.CombatAnchorSetAt = _timing.CurTime;
            attacker.CombatDisengageCommitUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0.5f, attacker.CombatDisengageCommitSeconds));
            attacker.BestAttackRangeDistance = float.MaxValue;
            attacker.LastAttackRangeImprovementAt = _timing.CurTime;
        }

        if (!origin.TryDistance(EntityManager, targetCoordinates, out var targetDistance))
            return;

        var closeRange = Math.Max(CombatProgressCloseRange, attacker.PointArrivalRange + 0.55f);
        if (targetDistance <= closeRange)
        {
            attacker.BestAttackRangeDistance = Math.Min(attacker.BestAttackRangeDistance, targetDistance);
            attacker.LastAttackRangeImprovementAt = _timing.CurTime;
            return;
        }

        if (attacker.BestAttackRangeDistance == float.MaxValue ||
            targetDistance + CombatProgressDistanceEpsilon < attacker.BestAttackRangeDistance)
        {
            attacker.BestAttackRangeDistance = targetDistance;
            attacker.LastAttackRangeImprovementAt = _timing.CurTime;
        }
    }

    private bool ShouldDisengageFromCombat(
        WH40KWaveDefenceAttackerComponent attacker,
        TransformComponent xform)
    {
        if (!HasVisibleCombatContact(attacker) ||
            !TryGetCombatFocusTarget(attacker, out _, out var targetCoordinates) ||
            !attacker.CombatAnchorCoordinates.IsValid(EntityManager))
        {
            return false;
        }

        if (attacker.CombatDisengageCommitUntil != TimeSpan.Zero &&
            _timing.CurTime < attacker.CombatDisengageCommitUntil)
        {
            return false;
        }

        if (!xform.Coordinates.TryDistance(EntityManager, targetCoordinates, out var targetDistance))
            return false;

        var closeRange = Math.Max(CombatProgressCloseRange, attacker.PointArrivalRange + 0.55f);
        if (targetDistance <= closeRange)
            return false;

        if (attacker.LastAttackRangeImprovementAt != TimeSpan.Zero &&
            _timing.CurTime - attacker.LastAttackRangeImprovementAt < TimeSpan.FromSeconds(Math.Max(1f, attacker.CombatStallSeconds)))
        {
            return false;
        }

        if (attacker.LastSuccessfulDamageDealtAt != TimeSpan.Zero &&
            _timing.CurTime - attacker.LastSuccessfulDamageDealtAt < TimeSpan.FromSeconds(Math.Max(1f, attacker.CombatStallSeconds)))
        {
            return false;
        }

        if (!xform.Coordinates.TryDistance(EntityManager, attacker.CombatAnchorCoordinates, out var leashDistance))
            return false;

        return leashDistance >= Math.Max(1.5f, attacker.PursuitLeashDistance);
    }

    private static void ClearCombatPursuitState(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.CombatAnchorTarget = EntityUid.Invalid;
        attacker.CombatAnchorCoordinates = EntityCoordinates.Invalid;
        attacker.CombatAnchorSetAt = TimeSpan.Zero;
        attacker.CombatDisengageCommitUntil = TimeSpan.Zero;
        attacker.BestAttackRangeDistance = float.MaxValue;
        attacker.LastAttackRangeImprovementAt = TimeSpan.Zero;
        attacker.LastSuccessfulDamageDealtAt = TimeSpan.Zero;
    }

    private void OnDamageDealt(ref DamageDealtEvent args)
    {
        if (args.Origin is not { } origin ||
            !TryResolveDamageOriginAttacker(origin, out var attacker))
        {
            return;
        }

        attacker.LastSuccessfulDamageDealtAt = _timing.CurTime;
        attacker.LastAttackRangeImprovementAt = _timing.CurTime;

        if (attacker.CombatDisengageCommitUntil < _timing.CurTime + TimeSpan.FromSeconds(0.5f))
            attacker.CombatDisengageCommitUntil = _timing.CurTime + TimeSpan.FromSeconds(0.5f);
    }

    private bool TryResolveDamageOriginAttacker(
        EntityUid origin,
        out WH40KWaveDefenceAttackerComponent attacker)
    {
        if (TryComp<WH40KWaveDefenceAttackerComponent>(origin, out var directAttacker))
        {
            attacker = directAttacker;
            return true;
        }

        if (TryComp(origin, out TransformComponent? originXform) &&
            originXform.ParentUid.IsValid() &&
            TryComp<WH40KWaveDefenceAttackerComponent>(originXform.ParentUid, out var parentAttacker))
        {
            attacker = parentAttacker;
            return true;
        }

        attacker = default!;
        return false;
    }

    private bool TryKickMovement(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, HTNComponent htn)
    {
        RefreshDesiredTargetProposal(uid, attacker, htn, Transform(uid), engaged: false, force: true);
        RefreshTargetRoles(attacker, Transform(uid).Coordinates);

        if (!TryGetMovementTargetDirective(attacker, out var target, out _))
            return false;

        PushTarget(uid, attacker, htn, target, "recovery-planless-kick", requestReplan: false);
        return true;
    }

    private void PushTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        EntityCoordinates target,
        string reason,
        bool refreshSteering = true,
        bool requestReplan = true)
    {
        var label = DescribeTargetLabel(attacker, htn, target);
        ApplyAuthoritativeBlackboardTargetState(uid, attacker, htn, target, label);
        var pathFlags = _pathfinding.GetFlags(htn.Blackboard);
        NPCSteeringComponent? steering = null;

        if (refreshSteering)
        {
            steering = _steering.Register(uid, target);
            steering.Flags = pathFlags;
        }
        else if (TryComp(uid, out steering))
        {
            steering.Flags = pathFlags;
        }

        if (requestReplan)
            _htn.Replan(htn);

        var pushChanged = !SameCoordinates(attacker.LastTargetPushCoordinates, target) ||
                          !string.Equals(attacker.LastTargetPushReason, reason, StringComparison.Ordinal) ||
                          !string.Equals(attacker.LastTargetPushLabel, label, StringComparison.Ordinal);
        if (pushChanged && ShouldLogTargetPushReason(reason))
        {
            _sawmill.Debug(
                $"WaveDefence target push for {ToPrettyString(uid)}: reason={reason}, target={label}, coords={FormatCoordinates(target)}, flags={pathFlags}, refreshSteering={refreshSteering}, replan={requestReplan}, intent={attacker.Intent}, progress={(attacker.CurrentRouteProgressRatio * 100f):0}%/{(attacker.SharedLaneFrontProgress * 100f):0}%.");
        }

        attacker.LastTargetPushAt = _timing.CurTime;
        attacker.LastTargetPushReason = reason;
        attacker.LastTargetPushLabel = label;
        attacker.LastTargetPushCoordinates = target;
        attacker.LastLoggedReactionDelayAt = TimeSpan.Zero;
    }

    private void EnsureDesiredTargetSynced(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        bool engaged)
    {
        if (engaged &&
            TryGetBlackboardPlayerCombatTarget(htn, out var combatTarget, out _) &&
            combatTarget.IsValid() &&
            (!attacker.Objective.HasValue || combatTarget != attacker.Objective.Value))
        {
            return;
        }

        RefreshDesiredTargetProposal(uid, attacker, htn, xform, engaged);
        RefreshTargetRoles(attacker, xform.Coordinates);

        if (!TryGetMovementTargetDirective(attacker, out var desiredTarget, out var desiredLabel))
            return;

        if (ShouldDeferPursuitSyncToObjectivePlan(attacker, htn, desiredLabel))
        {
            if (TryHandOffObjectivePlanToPlayer(uid, attacker, htn))
                return;

            if (!htn.Planning)
                _htn.Replan(htn);
            return;
        }

        var steering = CompOrNull<NPCSteeringComponent>(uid);
        var noSteering = steering == null;
        var noPlan = !htn.Planning && htn.Plan == null;
        var moving = steering?.Status == SteeringStatus.Moving;
        var inRange = steering?.Status == SteeringStatus.InRange;
        var labelChanged = !string.Equals(desiredLabel, attacker.LastTargetPushLabel, StringComparison.Ordinal);
        var coordinatesChanged = HasSignificantCoordinateChange(desiredTarget, attacker.LastTargetPushCoordinates);
        var pushAge = attacker.LastTargetPushAt == TimeSpan.Zero
            ? TimeSpan.MaxValue
            : _timing.CurTime - attacker.LastTargetPushAt;
        var progressAge = attacker.LastProgressAt == TimeSpan.Zero
            ? TimeSpan.MaxValue
            : _timing.CurTime - attacker.LastProgressAt;
        var hasRecentProgress = progressAge < TimeSpan.FromSeconds(SyncProgressGraceSeconds);
        var equivalentLaneRetarget = labelChanged &&
                                     AreEquivalentLaneSubTargets(desiredLabel, attacker.LastTargetPushLabel);
        var equivalentLaneRetargetHeld = equivalentLaneRetarget &&
                                         hasRecentProgress &&
                                         pushAge < TimeSpan.FromSeconds(SyncEquivalentLaneRetargetDelaySeconds) &&
                                         !HasHardRetargetCoordinateChange(desiredTarget, attacker.LastTargetPushCoordinates);
        var effectiveLabelChanged = labelChanged && !equivalentLaneRetargetHeld;
        var effectiveCoordinatesChanged = coordinatesChanged && !equivalentLaneRetargetHeld;

        string? reason = null;
        var refreshSteering = true;
        var requestReplan = true;
        if (attacker.LastTargetPushAt == TimeSpan.Zero)
        {
            reason = "sync-missing-push";
        }
        else if (effectiveLabelChanged)
        {
            reason = $"sync-target-change:{desiredLabel}";
        }
        else if (inRange &&
                 effectiveCoordinatesChanged &&
                 pushAge >= TimeSpan.FromSeconds(SyncInRangeAdvanceDelaySeconds))
        {
            reason = "sync-in-range-bridge";
            requestReplan = false;
        }
        else if (moving &&
                 effectiveCoordinatesChanged &&
                 pushAge >= TimeSpan.FromSeconds(SyncProgressiveAdvanceDelaySeconds))
        {
            reason = "sync-progressive-advance";
            requestReplan = false;
        }
        else if (noSteering &&
                 effectiveCoordinatesChanged &&
                 pushAge >= TimeSpan.FromSeconds(SyncNoSteeringAdvanceDelaySeconds))
        {
            reason = "sync-no-steering-advance";
            requestReplan = false;
        }
        else if (noSteering &&
                 !hasRecentProgress &&
                 pushAge >= TimeSpan.FromSeconds(SyncNoSteeringDelaySeconds))
        {
            reason = "sync-no-steering";
            requestReplan = false;
        }
        else if (noPlan &&
                 !moving &&
                 !hasRecentProgress &&
                 pushAge >= TimeSpan.FromSeconds(SyncNoPlanDelaySeconds))
        {
            reason = "sync-no-plan";
            requestReplan = false;
        }
        else if (effectiveCoordinatesChanged &&
                 !moving &&
                 !hasRecentProgress &&
                 pushAge >= TimeSpan.FromSeconds(SyncDriftDelaySeconds))
        {
            reason = "sync-drift";
            requestReplan = false;
        }

        if (reason == null)
            return;

        PushTarget(uid, attacker, htn, desiredTarget, reason, refreshSteering, requestReplan);
    }

    private bool TryGetDesiredTargetProposal(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        bool engaged,
        out EntityCoordinates target,
        bool forceRefresh = false)
    {
        RefreshDesiredTargetProposal(uid, attacker, htn, xform, engaged, forceRefresh);
        target = attacker.DesiredTargetProposal;
        return target.IsValid(EntityManager);
    }

    private void RefreshDesiredTargetProposal(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        bool engaged,
        bool force = false)
    {
        if (!force &&
            attacker.DesiredTargetProposal.IsValid(EntityManager) &&
            attacker.NextDeliberationAt != TimeSpan.Zero &&
            _timing.CurTime < attacker.NextDeliberationAt)
        {
            return;
        }

        var previousTarget = attacker.DesiredTargetProposal;
        var previousLabel = attacker.DesiredTargetProposalLabel;
        var previousValid = previousTarget.IsValid(EntityManager);

        if (TryResolveBrainTarget(uid, attacker, out var target, out var label))
        {
            SetDesiredTargetProposal(attacker, target, label);
            NoteDecisionFromProposal(attacker, label);
            attacker.LastDeliberationAt = _timing.CurTime;
            attacker.NextDeliberationAt = _timing.CurTime + GetDeliberationDelay(uid, attacker, engaged);

            if (force ||
                !previousValid ||
                !string.Equals(previousLabel, label, StringComparison.Ordinal))
            {
                _sawmill.Debug(
                    $"WaveDefence deliberation refresh for {ToPrettyString(uid)}: target={label}, coords={FormatCoordinates(target)}, force={force}, engaged={engaged}, nextIn={FormatDuration(attacker.NextDeliberationAt - _timing.CurTime)}.");
            }

            return;
        }

        ClearDesiredTargetProposal(attacker);
        NoteDecisionFromProposal(attacker, "<none>");
        attacker.LastDeliberationAt = _timing.CurTime;
        attacker.NextDeliberationAt = _timing.CurTime + GetDeliberationDelay(uid, attacker, engaged);

        if (force || previousValid)
        {
            _sawmill.Debug(
                $"WaveDefence deliberation refresh for {ToPrettyString(uid)}: target=<none>, force={force}, engaged={engaged}, nextIn={FormatDuration(attacker.NextDeliberationAt - _timing.CurTime)}.");
        }
    }

    private void ApplyNavigationPolicy(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, HTNComponent htn)
    {
        var policy = BuildNavigationPolicy(attacker);
        _npc.SetBlackboard(uid, NPCBlackboard.NavInteract, policy.Interact, htn);
        _npc.SetBlackboard(uid, NPCBlackboard.NavPry, policy.Pry, htn);
        _npc.SetBlackboard(uid, NPCBlackboard.NavSmash, policy.Smash, htn);
        _npc.SetBlackboard(uid, NPCBlackboard.NavClimb, policy.Climb, htn);

        if (TryComp<NPCSteeringComponent>(uid, out var steering))
            steering.Flags = _pathfinding.GetFlags(htn.Blackboard);
    }

    private NavigationPolicy BuildNavigationPolicy(WH40KWaveDefenceAttackerComponent attacker, int? recoveryLevel = null)
    {
        var level = recoveryLevel ?? attacker.RecoveryLevel;
        var interact = attacker.CanInteract && (attacker.BaseNavInteract || level >= 1);
        var pry = attacker.CanPry && (attacker.BaseNavPry || attacker.Role == WH40KWaveSquadRole.Breacher || level >= 1);
        var climb = attacker.CanClimb && (attacker.BaseNavClimb || level >= 1);
        var smash = attacker.CanSmash &&
                    (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm
                        ? attacker.BaseNavSmash || level >= 1
                        : attacker.Role == WH40KWaveSquadRole.Breacher || level >= 2);

        return new NavigationPolicy(interact, pry, smash, climb);
    }

    private bool AssignLane(
        WH40KWaveDefenceAttackerComponent attacker,
        MapId mapId,
        string laneId,
        EntityCoordinates origin,
        RouteProgressSnapshot? carriedProgress = null)
    {
        var route = _registry.GetLaneRoute(mapId, laneId, attacker.Role);
        if (route.Count == 0 && attacker.Objective == null)
            return false;

        if (string.IsNullOrWhiteSpace(attacker.HomeLaneId))
            attacker.HomeLaneId = laneId;

        attacker.LaneId = laneId;
        attacker.LanePoints = route;
        attacker.LastLaneChangeAt = _timing.CurTime;
        attacker.LaneCommitUntil = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(4f, attacker.LaneCommitSeconds));
        ClearDesiredTargetProposal(attacker);
        InitializeRouteProgress(attacker, origin, rebaseFromClosest: carriedProgress == null, carriedProgress);
        return true;
    }

    private float EvaluateLaneScore(
        MapId mapId,
        string laneId,
        WH40KWaveSquadRole role,
        WH40KWaveAiProfile aiProfile,
        EntityCoordinates origin)
    {
        if (string.IsNullOrWhiteSpace(laneId))
            return float.MinValue;

        var route = _registry.GetLanePoints(mapId, laneId, role);
        if (route.Count == 0)
            return float.MinValue;

        var activeAttackers = 0;
        var recoveringAttackers = 0;
        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, TransformComponent>();
        while (query.MoveNext(out var otherUid, out var other, out var otherXform))
        {
            if (!CountsAsOperationalLaneMember(otherUid, other) ||
                otherXform.MapID != mapId ||
                !string.Equals(other.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            activeAttackers++;
            if (other.RecoveryAttempts > 0 &&
                _timing.CurTime - other.LastProgressAt > TimeSpan.FromSeconds(2))
            {
                recoveringAttackers++;
            }
        }

        var startIndex = FindClosestRouteIndex(origin, route.Select(point => point.Uid).ToList());
        var startCoords = route[Math.Clamp(startIndex, 0, route.Count - 1)].Xform.Coordinates;
        var distancePenalty = origin.TryDistance(EntityManager, startCoords, out var distance)
            ? distance * 0.4f
            : 0f;

        var score = 100f - activeAttackers * 12f - recoveringAttackers * 18f - distancePenalty;
        if (aiProfile != WH40KWaveAiProfile.SimpleSwarm &&
            role == WH40KWaveSquadRole.Breacher &&
            _registry.LaneHasPointType(mapId, laneId, WH40KWaveLanePointType.Breach, role))
        {
            score += 8f;
        }
        else if (aiProfile != WH40KWaveAiProfile.SimpleSwarm &&
                 role != WH40KWaveSquadRole.Breacher &&
                 _registry.LaneHasPointType(mapId, laneId, WH40KWaveLanePointType.Breach))
        {
            score -= 15f;
        }

        return score;
    }

    private float GetProgressScore(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates origin)
    {
        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm)
            return attacker.CurrentRouteProgressRatio * 10000f;

        if (attacker.ForcedTarget.IsValid(EntityManager) &&
            origin.TryDistance(EntityManager, attacker.ForcedTarget, out var forcedDistance))
        {
            return -forcedDistance;
        }

        var completedPoints = Math.Max(0, attacker.FurthestReachedLanePointIndex + 1);
        if (attacker.LanePointIndex < attacker.LanePoints.Count &&
            !Deleted(attacker.LanePoints[attacker.LanePointIndex]))
        {
            var pointXform = Transform(attacker.LanePoints[attacker.LanePointIndex]);
            if (!origin.TryDistance(EntityManager, pointXform.Coordinates, out var pointDistance))
                return 0f;

            return completedPoints * 1000f - pointDistance;
        }

        if (attacker.Objective is { } objective && !Deleted(objective))
        {
            var objectiveXform = Transform(objective);
            if (!origin.TryDistance(EntityManager, objectiveXform.Coordinates, out var objectiveDistance))
                return 0f;

            return attacker.TotalLanePointCount * 1000f + 500f - objectiveDistance;
        }

        return 0f;
    }

    private static RouteProgressSnapshot CaptureRouteProgress(WH40KWaveDefenceAttackerComponent attacker)
    {
        return new RouteProgressSnapshot(
            Math.Max(0, attacker.TotalLanePointCount),
            attacker.LanePointIndex,
            attacker.LastReachedLanePointIndex,
            attacker.FurthestReachedLanePointIndex,
            Math.Clamp(attacker.RouteProgressRatio, 0f, 1f),
            attacker.RouteCompleted);
    }

    private bool IsEngaged(WH40KWaveDefenceAttackerComponent attacker, HTNComponent htn)
    {
        var owner = htn.Blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var hasCombatComponent = HasComp<NPCMeleeCombatComponent>(owner) ||
                                 HasComp<NPCRangedCombatComponent>(owner);
        if (!hasCombatComponent)
            return false;

        var playerCombatRole = htn.Blackboard.TryGetValue<bool>(WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole, out var playerRole, EntityManager) &&
                               playerRole;

        if (playerCombatRole &&
            TryGetBlackboardPlayerCombatTarget(htn, out var target, out _) &&
            (!attacker.Objective.HasValue || target != attacker.Objective.Value))
        {
            if (!IsAttackablePlayerTarget(target))
                return false;

            var losRange = Math.Max(attacker.VisionRadius, attacker.AggroVisionRadius) + 0.5f;

            if (_examine.InRangeUnOccluded(owner, target, losRange, null))
                return true;

            if (TryGetCombatFocusTarget(attacker, out var combatFocus, out _) &&
                target == combatFocus)
            {
                return true;
            }

            return false;
        }

        var objectiveCombatRole = htn.Blackboard.TryGetValue<bool>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole, out var objectiveRole, EntityManager) &&
                                  objectiveRole;
        if (objectiveCombatRole &&
            TryGetBlackboardObjectiveCombatTarget(htn, out var objectiveTarget, out _))
        {
            return attacker.Objective.HasValue && objectiveTarget == attacker.Objective.Value;
        }

        return false;
    }

    private bool TryResolveCurrentTarget(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, out EntityCoordinates target)
    {
        return TryResolveBrainTarget(uid, attacker, out target, out _);
    }

    private bool TryResolveBrainTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityCoordinates target,
        out string label)
    {
        var origin = Transform(uid).Coordinates;

        if (ShouldPlayerOverrideForcedTarget(attacker, origin) &&
            TryResolvePlayerPursuitTarget(attacker, origin, out target, out label))
        {
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            ClearActiveRouteTarget(attacker);
            return true;
        }

        if (attacker.ForcedTarget.IsValid(EntityManager))
        {
            if (!attacker.ActiveSiegeBlocker.IsValid())
                attacker.ActiveSiegeBlockerLabel = string.Empty;

            ClearActiveRouteTarget(attacker);
            target = attacker.ForcedTarget;
            label = $"forced:{attacker.ForcedTargetLabel}";
            return true;
        }

        if (TryResolvePlayerPursuitTarget(attacker, origin, out target, out label))
        {
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            ClearActiveRouteTarget(attacker);
            return true;
        }

        if (TryResolveInvestigationTarget(attacker, out target, out label))
        {
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            ClearActiveRouteTarget(attacker);
            return true;
        }

        if (TryResolveGeometryRecoveryTarget(attacker, out target, out label))
        {
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            return true;
        }

        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm &&
            attacker.LocomotionTarget.IsValid(EntityManager))
        {
            if (attacker.LocomotionMode != WH40KWaveDefenceLocomotionMode.Route)
                ClearActiveRouteTarget(attacker);

            target = attacker.LocomotionTarget;
            label = ResolveLocomotionBrainLabel(attacker);
            return true;
        }

        if (TryResolveRouteTarget(uid, attacker, origin, out target))
        {
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            label = ResolveRouteBrainLabel(attacker);
            return true;
        }

        if (attacker.Objective is { } objective && !Deleted(objective))
        {
            if (_objectiveNavigation.TryResolveObjectiveAssaultTarget(uid, origin, objective, out target, out var blocker))
            {
                if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm &&
                    _objectiveNavigation.TryResolveSwarmSlotTarget(uid, origin, target, out var slottedTarget))
                {
                    target = slottedTarget;
                }

                attacker.ActiveSiegeBlocker = blocker;
                attacker.ActiveSiegeBlockerLabel = blocker.IsValid() ? $"objective-blocker:{ToPrettyString(blocker)}" : string.Empty;
                ClearActiveRouteTarget(attacker);
                label = $"objective:{ToPrettyString(objective)}";
                return true;
            }
        }

        if (TryGetLastReachedPointCoordinates(attacker, out target))
        {
            attacker.ActiveSiegeBlocker = EntityUid.Invalid;
            attacker.ActiveSiegeBlockerLabel = string.Empty;
            ClearActiveRouteTarget(attacker);
            label = ResolveFallbackBrainLabel(attacker);
            return true;
        }

        attacker.ActiveSiegeBlocker = EntityUid.Invalid;
        attacker.ActiveSiegeBlockerLabel = string.Empty;
        ClearActiveRouteTarget(attacker);
        target = EntityCoordinates.Invalid;
        label = "<none>";
        var hasObjective = attacker.Objective is { } objectiveUid && !Deleted(objectiveUid);
        _sawmill.Debug(
            $"WaveDefence AI could not resolve target for {ToPrettyString(uid)}: lane={attacker.LaneId}, idx={attacker.LanePointIndex}/{attacker.TotalLanePointCount}, routeCompleted={attacker.RouteCompleted}, forced={attacker.ForcedTarget.IsValid(EntityManager)}, objective={hasObjective}.");
        return false;
    }

    private bool TryResolvePlayerPursuitTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target)
    {
        return TryResolvePlayerPursuitTarget(attacker, origin, out target, out _);
    }

    private bool TryResolvePlayerPursuitTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target,
        out string label)
    {
        if (attacker.PlayerContactMode == WH40KWaveDefencePlayerContactMode.VisibleCombat &&
            TryGetCombatFocusTarget(attacker, out var player, out var coordinates))
        {
            target = coordinates;
            label = attacker.CombatFocusLabel;
            return true;
        }

        target = EntityCoordinates.Invalid;
        label = string.Empty;
        return false;
    }

    private bool HasImmediateObjectiveOpportunity(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        if (attacker.Objective is not { } objective || Deleted(objective))
            return false;

        if (!_objectiveNavigation.TryResolveObjectiveAssaultTarget(uid, origin, objective, out var objectiveTarget, out var blocker) ||
            blocker.IsValid())
        {
            return false;
        }

        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm &&
            _objectiveNavigation.TryResolveSwarmSlotTarget(uid, origin, objectiveTarget, out var slottedTarget))
        {
            objectiveTarget = slottedTarget;
        }

        if (origin.TryDistance(EntityManager, objectiveTarget, out var targetDistance) &&
            targetDistance <= Math.Max(attacker.PointArrivalRange + 0.85f, 2.35f))
        {
            return true;
        }

        var objectiveCoordinates = Transform(objective).Coordinates;
        return origin.TryDistance(EntityManager, objectiveCoordinates, out var objectiveDistance) &&
               objectiveDistance <= Math.Max(attacker.PointArrivalRange + 0.85f, 2.35f);
    }

    private bool TryResolveInvestigationTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityCoordinates target,
        out string label)
    {
        if (attacker.PlayerContactMode == WH40KWaveDefencePlayerContactMode.InvestigateMemory &&
            TryGetInvestigationTarget(attacker, out _, out var coordinates))
        {
            target = coordinates;
            label = attacker.InvestigationLabel;
            return true;
        }

        target = EntityCoordinates.Invalid;
        label = string.Empty;
        return false;
    }

    private bool ShouldDeferPursuitSyncToObjectivePlan(
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        string desiredLabel)
    {
        if (!attacker.PlayerContactShouldOverrideObjective ||
            attacker.PlayerContactMode != WH40KWaveDefencePlayerContactMode.VisibleCombat ||
            !desiredLabel.StartsWith("player:", StringComparison.Ordinal) ||
            attacker.Objective is not { } objective ||
            !TryGetBlackboardObjectiveCombatTarget(htn, out var currentTarget, out _))
        {
            return false;
        }

        return currentTarget == objective;
    }

    private bool TryHandOffObjectivePlanToPlayer(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn)
    {
        if (!TryGetCombatFocusTarget(attacker, out var target, out var coordinates))
            return false;

        PushTarget(uid, attacker, htn, coordinates, "sync-objective-player-handoff");
        return true;
    }

    private string ResolveLocomotionBrainLabel(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (!string.IsNullOrWhiteSpace(attacker.LocomotionTargetLabel))
            return attacker.LocomotionTargetLabel;

        return attacker.LocomotionMode == WH40KWaveDefenceLocomotionMode.Objective
            ? attacker.Objective is { } objective && !Deleted(objective)
                ? $"objective:{ToPrettyString(objective)}"
                : "objective"
            : ResolveRouteBrainLabel(attacker);
    }

    private string ResolveRouteBrainLabel(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (!string.IsNullOrWhiteSpace(attacker.ActiveRouteTargetLabel))
            return attacker.ActiveRouteTargetLabel;

        if (attacker.LanePointIndex < attacker.LanePoints.Count)
            return $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}";

        return $"lane:{attacker.LaneId}:complete";
    }

    private string ResolveFallbackBrainLabel(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (attacker.LastReachedLanePointIndex >= 0)
            return $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LastReachedLanePointIndex)}:fallback";

        return $"lane:{attacker.LaneId}:fallback";
    }

    private bool ShouldPlayerOverrideForcedTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        if (!attacker.ForcedTarget.IsValid(EntityManager))
            return false;

        return attacker.ForcedTargetKind switch
        {
            WH40KWaveDefenceForcedTargetKind.Fallback or
                WH40KWaveDefenceForcedTargetKind.DisengageToLane => true,
            WH40KWaveDefenceForcedTargetKind.Breach or
                WH40KWaveDefenceForcedTargetKind.DirectObjective => ShouldVisibleCombatOverrideStrategicForcedTarget(attacker, origin),
            _ => false,
        };
    }

    private bool ShouldVisibleCombatOverrideStrategicForcedTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin)
    {
        if (attacker.PlayerContactMode != WH40KWaveDefencePlayerContactMode.VisibleCombat ||
            !TryGetCombatFocusTarget(attacker, out _, out var targetCoordinates) ||
            !origin.TryDistance(EntityManager, targetCoordinates, out var targetDistance))
        {
            return false;
        }

        var closeOverrideDistance = Math.Max(
            attacker.ForcedStrategicPlayerOverrideDistance,
            Math.Max(CombatProgressCloseRange + 0.35f, attacker.PointArrivalRange + 0.8f));
        if (targetDistance <= closeOverrideDistance)
            return true;

        if (attacker.Objective is not { } objective ||
            Deleted(objective))
        {
            return false;
        }

        var objectiveCoordinates = Transform(objective).Coordinates;
        return targetCoordinates.TryDistance(EntityManager, objectiveCoordinates, out var objectiveDistance) &&
               objectiveDistance <= Math.Max(closeOverrideDistance, attacker.ForcedStrategicObjectiveGuardDistance);
    }

    private bool TryResolveRouteTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target)
    {
        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm &&
            TryResolveSimpleSwarmRouteTarget(uid, attacker, origin, out target))
        {
            return true;
        }

        while (attacker.LanePointIndex < attacker.LanePoints.Count)
        {
            var nextPoint = attacker.LanePoints[attacker.LanePointIndex];
            if (Deleted(nextPoint))
            {
                MarkLanePointReached(attacker, attacker.LanePointIndex);
                continue;
            }

            if (TryComp<WH40KWaveLanePointComponent>(nextPoint, out var lanePoint) &&
                lanePoint.PointType == WH40KWaveLanePointType.Breach &&
                ShouldHoldBeforeBreach(uid, attacker, origin, nextPoint) &&
                TryGetHoldPointBeforeBreach(attacker, out var holdTarget))
            {
                target = holdTarget;
                SetActiveRouteTarget(attacker, target);
                return true;
            }

            target = Transform(nextPoint).Coordinates;
            SetActiveRouteTarget(attacker, target);

            return true;
        }

        ClearActiveRouteTarget(attacker);
        target = EntityCoordinates.Invalid;
        return false;
    }

    private void InitializeRouteProgress(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        bool rebaseFromClosest,
        RouteProgressSnapshot? carriedProgress = null)
    {
        attacker.TotalLanePointCount = attacker.LanePoints.Count;

        if (attacker.TotalLanePointCount == 0)
        {
            attacker.LanePointIndex = 0;
            attacker.LastReachedLanePointIndex = -1;
            attacker.FurthestReachedLanePointIndex = -1;
            attacker.LastFallbackAnchorIndex = -1;
            attacker.RouteCompleted = true;
            attacker.CurrentRouteProgressRatio = 1f;
            attacker.SharedLaneFrontProgress = 1f;
            attacker.RouteProgressRatio = 1f;
            return;
        }

        if (carriedProgress is { } snapshot)
        {
            ApplyTransferredRouteProgress(attacker, snapshot);
        }
        else if (rebaseFromClosest)
        {
            attacker.LanePointIndex = Math.Clamp(FindClosestRouteIndex(origin, attacker.LanePoints), 0, attacker.TotalLanePointCount - 1);
            attacker.LastReachedLanePointIndex = Math.Max(-1, attacker.LanePointIndex - 1);
            attacker.FurthestReachedLanePointIndex = attacker.LastReachedLanePointIndex;
            attacker.LastFallbackAnchorIndex = FindPreviousFallbackAnchorIndex(attacker, attacker.LastReachedLanePointIndex);
        }
        else
        {
            attacker.LanePointIndex = 0;
            attacker.LastReachedLanePointIndex = -1;
            attacker.FurthestReachedLanePointIndex = -1;
            attacker.LastFallbackAnchorIndex = -1;
        }

        attacker.RouteCompleted = false;
        attacker.CurrentRouteProgressRatio = 0f;
        attacker.SharedLaneFrontProgress = 0f;
        attacker.RouteProgressRatio = 0f;
        UpdateRouteProgress(attacker, origin);
    }

    private void ApplyTransferredRouteProgress(
        WH40KWaveDefenceAttackerComponent attacker,
        RouteProgressSnapshot snapshot)
    {
        var routeCount = attacker.TotalLanePointCount;
        if (routeCount <= 0)
        {
            attacker.LanePointIndex = 0;
            attacker.LastReachedLanePointIndex = -1;
            attacker.FurthestReachedLanePointIndex = -1;
            attacker.LastFallbackAnchorIndex = -1;
            attacker.RouteCompleted = true;
            attacker.CurrentRouteProgressRatio = 1f;
            attacker.SharedLaneFrontProgress = 1f;
            attacker.RouteProgressRatio = 1f;
            return;
        }

        var transferredRatio = Math.Clamp(snapshot.RouteCompleted ? 1f : snapshot.ProgressRatio, 0f, 1f);
        if (transferredRatio >= 0.999f)
        {
            attacker.LanePointIndex = routeCount;
            attacker.LastReachedLanePointIndex = routeCount - 1;
            attacker.FurthestReachedLanePointIndex = routeCount - 1;
            attacker.LastFallbackAnchorIndex = FindPreviousFallbackAnchorIndex(attacker, routeCount - 1);
            attacker.RouteCompleted = true;
            attacker.CurrentRouteProgressRatio = 1f;
            attacker.SharedLaneFrontProgress = 1f;
            attacker.RouteProgressRatio = 1f;
            return;
        }

        var carriedPointProgress = transferredRatio * routeCount;
        attacker.LanePointIndex = Math.Clamp((int) MathF.Floor(carriedPointProgress + 0.0001f), 0, routeCount - 1);
        attacker.LastReachedLanePointIndex = Math.Max(-1, attacker.LanePointIndex - 1);
        attacker.FurthestReachedLanePointIndex = attacker.LastReachedLanePointIndex;
        attacker.LastFallbackAnchorIndex = FindPreviousFallbackAnchorIndex(attacker, attacker.LastReachedLanePointIndex);
        attacker.RouteCompleted = false;
        attacker.CurrentRouteProgressRatio = Math.Clamp(transferredRatio, 0f, 0.999f);
        attacker.SharedLaneFrontProgress = attacker.CurrentRouteProgressRatio;
        attacker.RouteProgressRatio = Math.Clamp(transferredRatio, 0f, 0.999f);
    }

    private void UpdateRouteProgress(WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates origin)
    {
        attacker.TotalLanePointCount = attacker.LanePoints.Count;

        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm)
        {
            UpdateSimpleSwarmRouteProgress(attacker, origin);
            return;
        }

        while (attacker.LanePointIndex < attacker.LanePoints.Count)
        {
            var pointUid = attacker.LanePoints[attacker.LanePointIndex];
            if (Deleted(pointUid) || !TryComp(pointUid, out TransformComponent? pointXform))
            {
                MarkLanePointReached(attacker, attacker.LanePointIndex);
                continue;
            }

            var arrivalRange = ResolvePointArrivalRange(attacker, pointUid);
            if (!origin.TryDistance(EntityManager, pointXform.Coordinates, out var distance) || distance > arrivalRange)
                break;

            MarkLanePointReached(attacker, attacker.LanePointIndex);
        }

        attacker.RouteCompleted = attacker.TotalLanePointCount == 0 || attacker.LanePointIndex >= attacker.TotalLanePointCount;
        attacker.RouteProgressRatio = ComputeRouteProgressRatio(attacker, origin);

        if (!attacker.ForcedTarget.IsValid(EntityManager) &&
            attacker.Intent is WH40KWaveDefenceAttackerIntent.Advance or WH40KWaveDefenceAttackerIntent.SiegeObjective)
        {
            SetIntent(
                attacker,
                attacker.RouteCompleted
                    ? WH40KWaveDefenceAttackerIntent.SiegeObjective
                    : WH40KWaveDefenceAttackerIntent.Advance,
                "brain",
                attacker.RouteCompleted ? "route-complete" : "route-progress");
        }
    }

    private void UpdateSimpleSwarmRouteProgress(WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates origin)
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
        var frontProgress = Math.Max(attacker.SharedLaneFrontProgress, currentProgress);
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

            var epsilon = ResolvePointProgressEpsilon(attacker, attacker.LanePointIndex, totalLength);
            if (currentProgress + epsilon < pointProgress)
            {
                var frontPassedGate = frontProgress >= pointProgress + SwarmFrontAssistLead || frontProgress >= 0.999f;
                var almostAtGate = currentProgress + Math.Max(epsilon, SwarmFrontAssistSlack) >= pointProgress;
                var closeEnoughToGate =
                    TryComp(pointUid, out TransformComponent? pointXform) &&
                    origin.TryDistance(EntityManager, pointXform.Coordinates, out var gateDistance) &&
                    gateDistance <= ResolvePointArrivalRange(attacker, pointUid) + 0.45f;

                if ((!frontPassedGate || (!almostAtGate && !closeEnoughToGate)) &&
                    !ShouldForceAdvanceStalledLanePoint(attacker, origin, currentProgress, pointProgress, pointUid, epsilon))
                {
                    break;
                }
            }

            MarkLanePointReached(attacker, attacker.LanePointIndex);
        }

        TryPromoteFinalLanePointToObjective(attacker, origin, currentProgress, frontProgress);

        attacker.RouteCompleted = attacker.TotalLanePointCount == 0 || attacker.LanePointIndex >= attacker.TotalLanePointCount;
        if (attacker.RouteCompleted)
        {
            attacker.CurrentRouteProgressRatio = 1f;
            attacker.RouteProgressRatio = 1f;
        }

        if (!attacker.ForcedTarget.IsValid(EntityManager) &&
            attacker.Intent is WH40KWaveDefenceAttackerIntent.Advance or WH40KWaveDefenceAttackerIntent.SiegeObjective)
        {
            SetIntent(
                attacker,
                attacker.RouteCompleted
                    ? WH40KWaveDefenceAttackerIntent.SiegeObjective
                    : WH40KWaveDefenceAttackerIntent.Advance,
                "brain",
                attacker.RouteCompleted ? "simple-swarm-route-complete" : "simple-swarm-route-progress");
        }
    }

    private bool TryResolveSimpleSwarmRouteTarget(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        out EntityCoordinates target)
    {
        target = EntityCoordinates.Invalid;

        if (attacker.LanePointIndex >= attacker.LanePoints.Count)
        {
            ClearActiveRouteTarget(attacker);
            return false;
        }

        if (!TryBuildRouteGeometry(attacker, out var vertices, out var pointVertices, out var totalLength) || totalLength <= 0.05f)
        {
            ClearActiveRouteTarget(attacker);
            return false;
        }

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
            ClearActiveRouteTarget(attacker);
            return false;
        }

        if (TryResolveSwarmBandTarget(attacker, baseTarget, segmentDirection, segmentWidth, out target))
        {
            SetActiveRouteTarget(attacker, target);
            return true;
        }

        target = baseTarget;
        SetActiveRouteTarget(attacker, target);
        return true;
    }

    private void SetActiveRouteTarget(WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates target)
    {
        attacker.ActiveRouteTarget = target;
        attacker.ActiveRouteTargetLabel = attacker.LanePointIndex < attacker.LanePoints.Count
            ? $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}"
            : $"lane:{attacker.LaneId}:complete";
    }

    private void SetIntent(
        WH40KWaveDefenceAttackerComponent attacker,
        WH40KWaveDefenceAttackerIntent intent,
        string priority,
        string reason)
    {
        if (attacker.Intent == intent &&
            string.Equals(attacker.DecisionPriority, priority, StringComparison.Ordinal) &&
            string.Equals(attacker.DecisionReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        attacker.Intent = intent;
        attacker.DecisionEpoch++;
        attacker.DecisionPriority = priority;
        attacker.DecisionReason = reason;
    }

    private static string ResolveDecisionPriority(
        WH40KWaveDefenceAttackerIntent intent,
        WH40KWaveDefenceForcedTargetKind kind,
        string label)
    {
        return kind switch
        {
            WH40KWaveDefenceForcedTargetKind.Fallback => "fallback",
            WH40KWaveDefenceForcedTargetKind.DisengageToLane => "combat-disengage",
            WH40KWaveDefenceForcedTargetKind.Breach => "breach",
            WH40KWaveDefenceForcedTargetKind.DirectObjective => "objective",
            _ => intent switch
            {
                WH40KWaveDefenceAttackerIntent.SiegeObjective => "objective",
                WH40KWaveDefenceAttackerIntent.DirectObjective => "objective",
                WH40KWaveDefenceAttackerIntent.Reroute => "recovery",
                WH40KWaveDefenceAttackerIntent.Disengage => "combat-disengage",
                WH40KWaveDefenceAttackerIntent.Fallback => "fallback",
                _ => string.IsNullOrWhiteSpace(label) ? "brain" : "forced",
            }
        };
    }

    private static string ResolveDecisionReason(
        WH40KWaveDefenceAttackerIntent intent,
        WH40KWaveDefenceForcedTargetKind kind,
        string label)
    {
        return kind switch
        {
            WH40KWaveDefenceForcedTargetKind.Fallback => $"forced-fallback:{label}",
            WH40KWaveDefenceForcedTargetKind.DisengageToLane => $"forced-disengage:{label}",
            WH40KWaveDefenceForcedTargetKind.Breach => $"forced-breach:{label}",
            WH40KWaveDefenceForcedTargetKind.DirectObjective => $"forced-objective:{label}",
            _ => $"intent:{intent}:{label}",
        };
    }

    private void NoteDecisionFromProposal(WH40KWaveDefenceAttackerComponent attacker, string label)
    {
        var priority = label.StartsWith("player:", StringComparison.Ordinal)
            ? "player-direct"
            : label.StartsWith("investigate:", StringComparison.Ordinal)
                ? "player-investigation"
                : label.StartsWith("forced:", StringComparison.Ordinal)
                    ? "forced"
                    : label.StartsWith("objective:", StringComparison.Ordinal)
                        ? "objective"
                        : label.StartsWith("lane:", StringComparison.Ordinal)
                            ? "lane"
                            : label.StartsWith("breach:", StringComparison.Ordinal)
                                ? "breach"
                                : label == "<none>"
                                    ? "idle"
                                    : "navigation";

        var reason = $"proposal:{label}";
        if (string.Equals(attacker.DecisionPriority, priority, StringComparison.Ordinal) &&
            string.Equals(attacker.DecisionReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        attacker.DecisionEpoch++;
        attacker.DecisionPriority = priority;
        attacker.DecisionReason = reason;
    }

    private void MarkPerceptionState(WH40KWaveDefenceAttackerComponent attacker, string label)
    {
        if (string.Equals(attacker.PerceptionStateLabel, label, StringComparison.Ordinal))
            return;

        attacker.PerceptionEpoch++;
        attacker.LastAcceptedPerceptionEpoch = attacker.PerceptionEpoch;
        attacker.PerceptionStateLabel = label;
    }

    private void MarkNavigationState(WH40KWaveDefenceAttackerComponent attacker, string label)
    {
        if (string.Equals(attacker.NavigationStateLabel, label, StringComparison.Ordinal))
            return;

        attacker.NavigationEpoch++;
        attacker.LastAcceptedNavigationEpoch = attacker.NavigationEpoch;
        attacker.NavigationStateLabel = label;
    }

    private void SetDesiredTargetProposal(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        string label)
    {
        if (SameCoordinates(attacker.DesiredTargetProposal, target) &&
            string.Equals(attacker.DesiredTargetProposalLabel, label, StringComparison.Ordinal))
        {
            return;
        }

        attacker.DesiredTargetProposal = target;
        attacker.DesiredTargetProposalLabel = label;
        MarkNavigationState(attacker, label);
    }

    private static void SetCombatFocus(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityUid target,
        EntityCoordinates coordinates,
        string label)
    {
        attacker.CombatFocusTarget = target;
        attacker.CombatFocusCoordinates = coordinates;
        attacker.CombatFocusLabel = label;
    }

    private static void ClearCombatFocus(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.CombatFocusTarget = EntityUid.Invalid;
        attacker.CombatFocusCoordinates = EntityCoordinates.Invalid;
        attacker.CombatFocusLabel = string.Empty;
    }

    private void SetInvestigationTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityUid target,
        EntityCoordinates coordinates,
        string label,
        EntityCoordinates origin)
    {
        var changed = attacker.InvestigationTarget != target ||
                      !SameCoordinates(attacker.InvestigationCoordinates, coordinates);
        attacker.InvestigationTarget = target;
        attacker.InvestigationCoordinates = coordinates;
        attacker.InvestigationLabel = label;

        if (!changed &&
            attacker.InvestigationAnchorCoordinates.IsValid(EntityManager))
        {
            return;
        }

        attacker.InvestigationAnchorCoordinates = origin;
        attacker.InvestigationAnchorSetAt = _timing.CurTime;
        attacker.LastInvestigationDistance = float.MaxValue;
        attacker.LastInvestigationProgressAt = _timing.CurTime;
    }

    private static void ClearInvestigationTarget(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.InvestigationTarget = EntityUid.Invalid;
        attacker.InvestigationCoordinates = EntityCoordinates.Invalid;
        attacker.InvestigationLabel = string.Empty;
        attacker.InvestigationAnchorCoordinates = EntityCoordinates.Invalid;
        attacker.InvestigationAnchorSetAt = TimeSpan.Zero;
        attacker.LastInvestigationDistance = float.MaxValue;
        attacker.LastInvestigationProgressAt = TimeSpan.Zero;
    }

    private void SetMovementTargetDirective(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        string label)
    {
        if (SameCoordinates(attacker.MovementTargetDirective, target) &&
            string.Equals(attacker.MovementTargetDirectiveLabel, label, StringComparison.Ordinal))
        {
            return;
        }

        attacker.MovementTargetDirective = target;
        attacker.MovementTargetDirectiveLabel = label;
    }

    private bool TryGetMovementTargetDirective(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityCoordinates target,
        out string label)
    {
        if (attacker.MovementTargetDirective.IsValid(EntityManager))
        {
            target = attacker.MovementTargetDirective;
            label = attacker.MovementTargetDirectiveLabel;
            return true;
        }

        if (attacker.LocomotionTarget.IsValid(EntityManager) &&
            IsNavigationMovementLabel(attacker.LocomotionTargetLabel))
        {
            target = attacker.LocomotionTarget;
            label = attacker.LocomotionTargetLabel;
            return true;
        }

        if (attacker.DesiredTargetProposal.IsValid(EntityManager))
        {
            target = attacker.DesiredTargetProposal;
            label = attacker.DesiredTargetProposalLabel;
            return true;
        }

        target = EntityCoordinates.Invalid;
        label = string.Empty;
        return false;
    }

    private static void ClearMovementTargetDirective(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.MovementTargetDirective = EntityCoordinates.Invalid;
        attacker.MovementTargetDirectiveLabel = string.Empty;
    }

    private static void ClearActiveRouteTarget(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.ActiveRouteTarget = EntityCoordinates.Invalid;
        attacker.ActiveRouteTargetLabel = string.Empty;
    }

    private void ClearDesiredTargetProposal(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (!attacker.DesiredTargetProposal.IsValid(EntityManager) &&
            string.IsNullOrWhiteSpace(attacker.DesiredTargetProposalLabel))
        {
            return;
        }

        attacker.DesiredTargetProposal = EntityCoordinates.Invalid;
        attacker.DesiredTargetProposalLabel = string.Empty;
        attacker.NextDeliberationAt = TimeSpan.Zero;
        ClearMovementTargetDirective(attacker);
        MarkNavigationState(attacker, "proposal-cleared");
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
                !CountsAsOperationalLaneMember(otherUid, other) ||
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
            if (startMap.MapId == MapId.Nullspace || startMap.MapId != endMap.MapId || startMap.MapId != originMap.MapId)
            {
                cumulative += Vector2.Distance(startMap.Position, endMap.Position);
                continue;
            }

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
        {
            return Math.Clamp(ComputeRouteProgressRatio(attacker, origin), 0f, 0.999f);
        }

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

    private bool ShouldForceAdvanceStalledLanePoint(
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

        return pointDistance <= ResolvePointArrivalRange(attacker, pointUid) + LaneTraversalStallDistanceSlack &&
               currentProgress + epsilon >= pointProgress - LaneTraversalStallProgressSlack;
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
        WH40KWaveDefenceAttackerComponent attacker,
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
        var candidateOffsets = new[]
        {
            preferredOffset,
            preferredOffset * 0.5f,
            0f,
            preferredOffset * -0.5f,
        };
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

                target = candidate;
                return true;
            }
        }

        return _pathfinding.GetPoly(baseTarget) != null;
    }

    private void MarkLanePointReached(WH40KWaveDefenceAttackerComponent attacker, int pointIndex)
    {
        attacker.LastReachedLanePointIndex = Math.Max(attacker.LastReachedLanePointIndex, pointIndex);
        attacker.FurthestReachedLanePointIndex = Math.Max(attacker.FurthestReachedLanePointIndex, pointIndex);

        if (IsFallbackAnchor(attacker, pointIndex))
            attacker.LastFallbackAnchorIndex = pointIndex;

        attacker.LanePointIndex = pointIndex + 1;
        ClearDesiredTargetProposal(attacker);
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
        if (attacker.AiProfile != WH40KWaveAiProfile.SimpleSwarm ||
            attacker.RouteCompleted ||
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

    private int FindPreviousFallbackAnchorIndex(WH40KWaveDefenceAttackerComponent attacker, int fromIndex)
    {
        for (var i = Math.Min(fromIndex, attacker.LanePoints.Count - 1); i >= 0; i--)
        {
            if (IsFallbackAnchor(attacker, i))
                return i;
        }

        return -1;
    }

    private bool TryGetFallbackAnchorTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        out EntityCoordinates coordinates,
        out string label)
    {
        foreach (var index in GetFallbackCandidateIndices(attacker))
        {
            if (!TryGetLanePointCoordinates(attacker, index, out coordinates))
                continue;

            label = $"fallback:{attacker.LaneId}#{index}";
            return true;
        }

        coordinates = EntityCoordinates.Invalid;
        label = string.Empty;
        return false;
    }

    private IEnumerable<int> GetFallbackCandidateIndices(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (attacker.LastReachedLanePointIndex >= 0)
            yield return attacker.LastReachedLanePointIndex;

        if (attacker.LastFallbackAnchorIndex >= 0 &&
            attacker.LastFallbackAnchorIndex != attacker.LastReachedLanePointIndex)
        {
            yield return attacker.LastFallbackAnchorIndex;
        }
    }

    private bool TryGetLastReachedPointCoordinates(WH40KWaveDefenceAttackerComponent attacker, out EntityCoordinates coordinates)
    {
        if (attacker.LastReachedLanePointIndex >= 0 &&
            TryGetLanePointCoordinates(attacker, attacker.LastReachedLanePointIndex, out coordinates))
        {
            return true;
        }

        coordinates = EntityCoordinates.Invalid;
        return false;
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

    private bool TryGetUpcomingBreachPoint(
        MapId mapId,
        WH40KWaveDefenceAttackerComponent attacker,
        out (EntityUid Uid, WH40KWaveLanePointComponent Point, TransformComponent Xform) breachPoint)
    {
        breachPoint = default;

        if (string.IsNullOrWhiteSpace(attacker.LaneId))
            return false;

        var breachPoints = _registry.GetLanePoints(
            mapId,
            attacker.LaneId,
            attacker.Role,
            WH40KWaveLanePointType.Breach);

        if (breachPoints.Count == 0)
            return false;

        foreach (var point in breachPoints)
        {
            var routeIndex = attacker.LanePoints.IndexOf(point.Uid);
            if (routeIndex == -1 || routeIndex < attacker.LanePointIndex)
                continue;

            breachPoint = point;
            return true;
        }

        return false;
    }

    private bool ShouldHoldBeforeBreach(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityUid breachPoint)
    {
        if (attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm ||
            attacker.Role == WH40KWaveSquadRole.Breacher)
        {
            return false;
        }

        if (attacker.RecoveryLevel >= 2 || attacker.NoPathCount >= 2)
            return false;

        if (!TryComp<WH40KWaveLanePointComponent>(breachPoint, out var point) ||
            point.PointType != WH40KWaveLanePointType.Breach)
        {
            return false;
        }

        if (!TryGetHoldPointBeforeBreach(attacker, out var holdTarget))
            return false;

        return !IsBreachAdvanceOpen(uid, attacker, origin, breachPoint, holdTarget);
    }

    private bool IsBreachAdvanceOpen(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates origin,
        EntityUid breachPoint,
        EntityCoordinates holdTarget)
    {
        if (Deleted(breachPoint) || !TryComp(breachPoint, out TransformComponent? breachXform))
            return false;

        var arrivalRange = ResolvePointArrivalRange(attacker, breachPoint);
        if (origin.TryDistance(EntityManager, breachXform.Coordinates, out var distance) &&
            distance <= arrivalRange + 0.15f)
        {
            return true;
        }

        if (HasUnobstructedRouteSegment(uid, origin, breachXform.Coordinates, arrivalRange))
            return true;

        if (holdTarget.IsValid(EntityManager) &&
            HasUnobstructedRouteSegment(uid, holdTarget, breachXform.Coordinates, arrivalRange))
        {
            return true;
        }

        return TryGetLanePointCoordinates(attacker, attacker.LastReachedLanePointIndex, out var lastReached) &&
               HasUnobstructedRouteSegment(uid, lastReached, breachXform.Coordinates, arrivalRange);
    }

    private bool HasUnobstructedRouteSegment(
        EntityUid uid,
        EntityCoordinates start,
        EntityCoordinates end,
        float arrivalRange)
    {
        var startMap = _transform.ToMapCoordinates(start);
        var endMap = _transform.ToMapCoordinates(end);
        if (startMap.MapId == MapId.Nullspace || startMap.MapId != endMap.MapId)
            return false;

        var range = Vector2.Distance(startMap.Position, endMap.Position) + Math.Max(0.5f, arrivalRange);
        return _examine.InRangeUnOccluded(
            startMap,
            endMap,
            range,
            entity => entity == uid,
            true,
            EntityManager);
    }

    private bool TryGetHoldPointBeforeBreach(WH40KWaveDefenceAttackerComponent attacker, out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        var fallbackPoint = EntityCoordinates.Invalid;
        for (var i = 0; i < attacker.LanePointIndex; i++)
        {
            var pointUid = attacker.LanePoints[i];
            if (Deleted(pointUid) || !TryComp<WH40KWaveLanePointComponent>(pointUid, out var lanePoint))
                continue;

            if (lanePoint.PointType == WH40KWaveLanePointType.Breach)
                break;

            if (lanePoint.PointType is WH40KWaveLanePointType.Rally or WH40KWaveLanePointType.Waypoint)
            {
                fallbackPoint = Transform(pointUid).Coordinates;
            }
        }

        if (!fallbackPoint.IsValid(EntityManager))
            return false;

        coordinates = fallbackPoint;
        return true;
    }

    public string BuildAiStatusText(int maxEntries = 18)
    {
        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent, TransformComponent>();
        var entries = new List<(EntityUid Uid, WH40KWaveDefenceAttackerComponent Attacker, TransformComponent Xform)>();

        while (query.MoveNext(out var uid, out var attacker, out var xform))
        {
            if (xform.MapID == MapId.Nullspace ||
                !CountsAsOperationalLaneMember(uid, attacker))
            {
                continue;
            }

            entries.Add((uid, attacker, xform));
        }

        var lines = new List<string>
        {
            $"WaveDefence AI attackers: {entries.Count}"
        };

        foreach (var laneGroup in entries
                     .GroupBy(entry => string.IsNullOrWhiteSpace(entry.Attacker.LaneId) ? "<none>" : entry.Attacker.LaneId)
                     .OrderBy(group => group.Key))
        {
            lines.Add($"  lane {laneGroup.Key}: {laneGroup.Count()}");
        }

        foreach (var entry in entries
                     .OrderBy(entry => entry.Attacker.LaneId)
                     .ThenBy(entry => entry.Attacker.Role)
                     .ThenBy(entry => entry.Uid.Id)
                     .Take(Math.Max(1, maxEntries)))
        {
            var currentPoint = DescribeLanePoint(entry.Attacker, entry.Attacker.LanePointIndex);
            var reachedPoint = DescribeLanePoint(entry.Attacker, entry.Attacker.LastReachedLanePointIndex);
            var blocker = entry.Attacker.ActiveSiegeBlocker.IsValid()
                ? entry.Attacker.ActiveSiegeBlockerLabel
                : "-";
            lines.Add(
                $"{ToPrettyString(entry.Uid)} role={entry.Attacker.Role} lane={entry.Attacker.LaneId} idx={entry.Attacker.LanePointIndex}/{entry.Attacker.TotalLanePointCount} current={currentPoint} reached={reachedPoint} furthest={entry.Attacker.FurthestReachedLanePointIndex} prog={(entry.Attacker.RouteProgressRatio * 100f):0}% cur={(entry.Attacker.CurrentRouteProgressRatio * 100f):0}% front={(entry.Attacker.SharedLaneFrontProgress * 100f):0}% intent={entry.Attacker.Intent} blocker={blocker} rec={entry.Attacker.RecoveryLevel} np={entry.Attacker.NoPathCount} rr={entry.Attacker.LaneRerouteCount} fb={entry.Attacker.FallbackCount} state={entry.Attacker.DebugState}");
        }

        if (entries.Count > maxEntries)
            lines.Add($"... {entries.Count - maxEntries} more attacker(s)");

        return string.Join('\n', lines);
    }

    private void CleanupAllAttackers(string reason)
    {
        var query = EntityQueryEnumerator<WH40KWaveDefenceAttackerComponent>();
        while (query.MoveNext(out var uid, out var attacker))
        {
            TryComp(uid, out HTNComponent? htn);
            DeactivateAttackerRuntime(uid, attacker, htn, reason, sleepNpc: htn != null);
        }
    }

    private bool IsAttackerOperational(EntityUid uid, HTNComponent htn, TransformComponent xform)
    {
        if (xform.MapID == MapId.Nullspace)
            return false;

        if (!_mobState.IsAlive(uid))
            return false;

        return _npc.IsAwake(uid, htn);
    }

    private bool CountsAsOperationalLaneMember(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker)
    {
        return attacker.RuntimeInitialized &&
               HasComp<ActiveNPCComponent>(uid) &&
               _mobState.IsAlive(uid);
    }

    private bool HasRuntimeActivity(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker)
    {
        return attacker.RuntimeInitialized ||
               attacker.VisiblePlayer.IsValid() ||
               attacker.RememberedPlayer.IsValid() ||
               attacker.PlayerContactMode != WH40KWaveDefencePlayerContactMode.None ||
               attacker.CombatFocusTarget.IsValid() ||
               attacker.InvestigationTarget.IsValid() ||
               attacker.GeometryRecoveryTarget.IsValid(EntityManager) ||
               attacker.ForcedTarget.IsValid(EntityManager) ||
               attacker.DesiredTargetProposal.IsValid(EntityManager) ||
               attacker.MovementTargetDirective.IsValid(EntityManager) ||
               attacker.LocomotionTarget.IsValid(EntityManager) ||
               attacker.StrategicRouteTarget.IsValid(EntityManager) ||
               attacker.PendingPerceptionRequestEpoch != 0 ||
               attacker.PendingNavigationRequestEpoch != 0 ||
               attacker.HasCommittedRoute ||
               attacker.HasShadowRoute ||
               HasComp<NPCSteeringComponent>(uid) ||
               HasComp<NPCMeleeCombatComponent>(uid) ||
               HasComp<NPCRangedCombatComponent>(uid);
    }

    private void DeactivateAttackerRuntime(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent? htn,
        string reason,
        bool sleepNpc)
    {
        if (sleepNpc && htn != null)
            _npc.SleepNPC(uid, htn);

        _perceptionScheduler.CancelEvaluation(uid);
        _locomotion.DeactivateAttackerRuntime(uid, attacker);
        RemCompDeferred<NPCMeleeCombatComponent>(uid);
        RemCompDeferred<NPCRangedCombatComponent>(uid);

        if (htn != null)
            ClearWaveBlackboardState(htn);

        ClearVisiblePlayer(attacker);
        ClearRememberedPlayer(attacker);
        ClearForcedTarget(attacker);
        ClearCombatPursuitState(attacker);

        attacker.RuntimeInitialized = false;
        attacker.LanePoints.Clear();
        attacker.LanePointIndex = 0;
        attacker.LastReachedLanePointIndex = -1;
        attacker.FurthestReachedLanePointIndex = -1;
        attacker.LastFallbackAnchorIndex = -1;
        attacker.TotalLanePointCount = 0;
        attacker.RouteProgressRatio = 0f;
        attacker.RouteCompleted = false;
        attacker.RouteStartCoordinates = EntityCoordinates.Invalid;
        attacker.CurrentRouteProgressRatio = 0f;
        attacker.SharedLaneFrontProgress = 0f;
        attacker.NextRecoveryAttemptAt = TimeSpan.Zero;
        attacker.NextTacticalThinkAt = TimeSpan.Zero;
        attacker.NextDeliberationAt = TimeSpan.Zero;
        attacker.LastDeliberationAt = TimeSpan.Zero;
        attacker.NextLocomotionThinkAt = TimeSpan.Zero;
        attacker.DesiredTargetProposal = EntityCoordinates.Invalid;
        attacker.DesiredTargetProposalLabel = string.Empty;
        attacker.CombatFocusTarget = EntityUid.Invalid;
        attacker.CombatFocusCoordinates = EntityCoordinates.Invalid;
        attacker.CombatFocusLabel = string.Empty;
        attacker.InvestigationTarget = EntityUid.Invalid;
        attacker.InvestigationCoordinates = EntityCoordinates.Invalid;
        attacker.InvestigationLabel = string.Empty;
        attacker.InvestigationAnchorCoordinates = EntityCoordinates.Invalid;
        attacker.InvestigationAnchorSetAt = TimeSpan.Zero;
        attacker.LastInvestigationDistance = float.MaxValue;
        attacker.LastInvestigationProgressAt = TimeSpan.Zero;
        attacker.MovementTargetDirective = EntityCoordinates.Invalid;
        attacker.MovementTargetDirectiveLabel = string.Empty;
        attacker.GeometryRecoveryTarget = EntityCoordinates.Invalid;
        attacker.GeometryRecoveryLabel = string.Empty;
        attacker.GeometryRecoveryUntil = TimeSpan.Zero;
        attacker.GeometryRecoveryStartedAt = TimeSpan.Zero;
        attacker.GeometryRecoveryLastProgressAt = TimeSpan.Zero;
        attacker.GeometryRecoveryStartProgress = 0f;
        attacker.GeometryRecoveryBestDistance = float.MaxValue;
        attacker.GeometryRecoveryLanePointIndex = -1;
        attacker.BestProgressScore = float.MinValue;
        attacker.RecoveryLevel = 0;
        attacker.RecoveryAttempts = 0;
        attacker.NoPathCount = 0;
        attacker.LaneRerouteCount = 0;
        attacker.FallbackCount = 0;
        attacker.Intent = WH40KWaveDefenceAttackerIntent.Advance;
        attacker.DecisionEpoch = 0;
        attacker.DecisionReason = reason;
        attacker.DecisionPriority = "inactive";
        attacker.PerceptionEpoch = 0;
        attacker.LastAcceptedPerceptionEpoch = 0;
        attacker.PerceptionRequestEpoch = 0;
        attacker.PendingPerceptionRequestEpoch = 0;
        attacker.LastAppliedPerceptionRequestEpoch = 0;
        attacker.PerceptionStateLabel = "inactive";
        attacker.PlayerContactMode = WH40KWaveDefencePlayerContactMode.None;
        attacker.PlayerContactPolicyLabel = "inactive";
        attacker.PlayerContactShouldOverrideObjective = false;
        attacker.NavigationEpoch = 0;
        attacker.LastAcceptedNavigationEpoch = 0;
        attacker.NavigationRequestEpoch = 0;
        attacker.PendingNavigationRequestEpoch = 0;
        attacker.LastAppliedNavigationRequestEpoch = 0;
        attacker.NavigationStateLabel = "inactive";
        attacker.LastProgressAt = TimeSpan.Zero;
        attacker.LastPlayerRelayAt = TimeSpan.Zero;
        attacker.LastTargetPushAt = TimeSpan.Zero;
        attacker.LastTargetPushReason = string.Empty;
        attacker.LastTargetPushLabel = string.Empty;
        attacker.LastTargetPushCoordinates = EntityCoordinates.Invalid;
        attacker.LastLoggedTargetLabel = string.Empty;
        attacker.LastLoggedTargetEntity = EntityUid.Invalid;
        attacker.LastTargetChangeAt = TimeSpan.Zero;
        attacker.LastLoggedSteeringStatus = string.Empty;
        attacker.LastSteeringChangeAt = TimeSpan.Zero;
        attacker.LastLoggedPlanning = false;
        attacker.LastLoggedHasPlan = false;
        attacker.LastPlanningStateChangeAt = TimeSpan.Zero;
        attacker.LastLoggedPlanlessAt = TimeSpan.Zero;
        attacker.LastLoggedNoPathAt = TimeSpan.Zero;
        attacker.LastLoggedStallAt = TimeSpan.Zero;
        attacker.LastLoggedPlanningDelayAt = TimeSpan.Zero;
        attacker.LastLoggedReactionDelayAt = TimeSpan.Zero;
        attacker.LastLoggedRememberedPlayer = EntityUid.Invalid;
        attacker.LastLoggedHadMemory = false;
        attacker.LastLoggedRememberedPlayerSource = WH40KWaveDefencePlayerContactSource.None;
        attacker.LastMemoryChangeAt = TimeSpan.Zero;
        attacker.LastLoggedIntent = WH40KWaveDefenceAttackerIntent.Advance;
        attacker.LastIntentChangeAt = TimeSpan.Zero;
        attacker.DebugState = $"Inactive ({reason})";
    }

    private static void ClearWaveBlackboardState(HTNComponent htn)
    {
        htn.Blackboard.Remove<EntityUid>(WH40KWaveDefenceHtnBlackboardKeys.PlayerTarget);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates);
        htn.Blackboard.Remove<EntityUid>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTarget);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates);
        htn.Blackboard.Remove<EntityUid>(TargetKey);
        htn.Blackboard.Remove<EntityCoordinates>(TargetCoordinatesKey);
        htn.Blackboard.Remove<EntityCoordinates>(AttackTargetCoordinatesKey);
        htn.Blackboard.Remove<bool>(WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole);
        htn.Blackboard.Remove<bool>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole);
        htn.Blackboard.Remove<bool>(WH40KWaveDefenceHtnBlackboardKeys.MovementRole);
    }

    private int FindClosestRouteIndex(EntityCoordinates origin, List<EntityUid> route)
    {
        if (route.Count == 0)
            return 0;

        var bestIndex = 0;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < route.Count; i++)
        {
            var point = route[i];
            if (Deleted(point))
                continue;

            var xform = Transform(point);

            if (!origin.TryDistance(EntityManager, xform.Coordinates, out var distance) || distance >= bestDistance)
                continue;

            bestIndex = i;
            bestDistance = distance;
        }

        return bestIndex;
    }

    private (EntityUid Uid, WH40KWaveLanePointComponent Point, TransformComponent Xform)? PickNearestPoint(
        EntityCoordinates origin,
        List<(EntityUid Uid, WH40KWaveLanePointComponent Point, TransformComponent Xform)> points)
    {
        (EntityUid Uid, WH40KWaveLanePointComponent Point, TransformComponent Xform)? best = null;
        var bestDistance = float.MaxValue;

        foreach (var point in points)
        {
            if (!origin.TryDistance(EntityManager, point.Xform.Coordinates, out var distance) || distance >= bestDistance)
                continue;

            best = point;
            bestDistance = distance;
        }

        return best;
    }

    private void ResetProgress(WH40KWaveDefenceAttackerComponent attacker, EntityCoordinates origin)
    {
        attacker.BestProgressScore = float.MinValue;
        attacker.LastProgressAt = _timing.CurTime;
    }

    private void TraceDiagnostics(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        bool engaged,
        bool noPath,
        bool planless,
        bool stalled,
        bool disengage)
    {
        TraceIntentTransition(uid, attacker, htn, xform, steering, engaged);
        TraceTargetTransition(uid, attacker, htn, xform, steering, engaged);
        TraceMemoryTransition(uid, attacker);
        TracePlanningTransition(uid, attacker, htn, xform, steering, engaged);
        TraceSteeringTransition(uid, attacker, htn, xform, steering, engaged);
        TracePersistentSignals(uid, attacker, htn, xform, steering, engaged, noPath, planless, stalled, disengage);
    }

    private void TraceIntentTransition(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        bool engaged)
    {
        if (attacker.Intent == attacker.LastLoggedIntent)
            return;

        var previous = attacker.LastLoggedIntent;
        attacker.LastLoggedIntent = attacker.Intent;
        attacker.LastIntentChangeAt = _timing.CurTime;
        _sawmill.Debug(
            $"WaveDefence intent transition for {ToPrettyString(uid)}: {previous} -> {attacker.Intent}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
    }

    private void TraceTargetTransition(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        bool engaged)
    {
        ResolveObservedTarget(attacker, htn, out var targetEntity, out var targetLabel, out var targetCoordinates);
        if (targetEntity == attacker.LastLoggedTargetEntity &&
            string.Equals(targetLabel, attacker.LastLoggedTargetLabel, StringComparison.Ordinal))
        {
            return;
        }

        var previous = string.IsNullOrWhiteSpace(attacker.LastLoggedTargetLabel)
            ? "<none>"
            : attacker.LastLoggedTargetLabel;
        var switchAge = attacker.LastTargetChangeAt == TimeSpan.Zero
            ? "first"
            : FormatDuration(_timing.CurTime - attacker.LastTargetChangeAt);

        attacker.LastLoggedTargetEntity = targetEntity;
        attacker.LastLoggedTargetLabel = targetLabel;
        attacker.LastTargetChangeAt = _timing.CurTime;
        _sawmill.Debug(
            $"WaveDefence target transition for {ToPrettyString(uid)}: {previous} -> {targetLabel}, targetPos={FormatCoordinates(targetCoordinates)}, sincePrevious={switchAge}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
    }

    private void TraceMemoryTransition(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker)
    {
        var hasMemory = attacker.RememberedPlayer.IsValid() &&
                        attacker.RememberedPlayerUntil != TimeSpan.Zero &&
                        _timing.CurTime < attacker.RememberedPlayerUntil;

        if (hasMemory == attacker.LastLoggedHadMemory &&
            (!hasMemory ||
             attacker.RememberedPlayer == attacker.LastLoggedRememberedPlayer &&
             attacker.RememberedPlayerSource == attacker.LastLoggedRememberedPlayerSource))
        {
            return;
        }

        attacker.LastLoggedHadMemory = hasMemory;
        attacker.LastLoggedRememberedPlayer = hasMemory ? attacker.RememberedPlayer : EntityUid.Invalid;
        attacker.LastLoggedRememberedPlayerSource = hasMemory
            ? attacker.RememberedPlayerSource
            : WH40KWaveDefencePlayerContactSource.None;
        attacker.LastMemoryChangeAt = _timing.CurTime;
        var ttl = hasMemory
            ? FormatDuration(attacker.RememberedPlayerUntil - _timing.CurTime)
            : "0s";
        var target = hasMemory
            ? ToPrettyString(attacker.RememberedPlayer)
            : "<none>";
        _sawmill.Debug(
            $"WaveDefence memory transition for {ToPrettyString(uid)}: target={target}, active={hasMemory}, ttl={ttl}, source={FormatContactSource(attacker.RememberedPlayerSource)}, rememberedPos={FormatCoordinates(attacker.RememberedPlayerCoordinates)}.");
    }

    private void TracePlanningTransition(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        bool engaged)
    {
        var planning = htn.Planning;
        var hasPlan = htn.Plan != null;
        var planningChanged = planning != attacker.LastLoggedPlanning;
        var hasPlanChanged = hasPlan != attacker.LastLoggedHasPlan;
        if (planningChanged || hasPlanChanged)
        {
            var previous = $"planning={attacker.LastLoggedPlanning}, hasPlan={attacker.LastLoggedHasPlan}";
            attacker.LastLoggedPlanning = planning;
            attacker.LastLoggedHasPlan = hasPlan;
            attacker.LastPlanningStateChangeAt = _timing.CurTime;
            attacker.LastLoggedPlanningDelayAt = TimeSpan.Zero;

            if (hasPlanChanged || (!hasPlan && planningChanged))
            {
                _sawmill.Debug(
                    $"WaveDefence planning transition for {ToPrettyString(uid)}: {previous} -> planning={planning}, hasPlan={hasPlan}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
            }
        }

        if (!planning ||
            hasPlan ||
            attacker.LastPlanningStateChangeAt == TimeSpan.Zero ||
            _timing.CurTime - attacker.LastPlanningStateChangeAt < TimeSpan.FromSeconds(0.8) ||
            (attacker.LastLoggedPlanningDelayAt != TimeSpan.Zero &&
             _timing.CurTime - attacker.LastLoggedPlanningDelayAt < TimeSpan.FromSeconds(0.8)))
        {
            return;
        }

        attacker.LastLoggedPlanningDelayAt = _timing.CurTime;
        _sawmill.Debug(
            $"WaveDefence slow planning for {ToPrettyString(uid)}: planningAge={FormatDuration(_timing.CurTime - attacker.LastPlanningStateChangeAt)}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
    }

    private void TraceSteeringTransition(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        bool engaged)
    {
        var status = steering?.Status.ToString() ?? "NoSteering";
        if (string.Equals(status, attacker.LastLoggedSteeringStatus, StringComparison.Ordinal))
            return;

        var previousStatus = attacker.LastLoggedSteeringStatus;
        var previous = string.IsNullOrWhiteSpace(previousStatus)
            ? "<unset>"
            : previousStatus;
        attacker.LastLoggedSteeringStatus = status;
        attacker.LastSteeringChangeAt = _timing.CurTime;

        if (!ShouldLogSteeringTransition(previousStatus, status))
            return;

        var reaction = attacker.LastTargetPushAt == TimeSpan.Zero
            ? "n/a"
            : $"{FormatDuration(_timing.CurTime - attacker.LastTargetPushAt)} after push:{attacker.LastTargetPushReason}";
        _sawmill.Debug(
            $"WaveDefence steering transition for {ToPrettyString(uid)}: {previous} -> {status}, reaction={reaction}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
    }

    private void TracePersistentSignals(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        bool engaged,
        bool noPath,
        bool planless,
        bool stalled,
        bool disengage)
    {
        if (noPath && ShouldEmitSignal(ref attacker.LastLoggedNoPathAt, 1.0))
        {
            _sawmill.Debug(
                $"WaveDefence signal NoPath for {ToPrettyString(uid)}: {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
        }

        if (planless && ShouldEmitSignal(ref attacker.LastLoggedPlanlessAt, 1.0))
        {
            _sawmill.Debug(
                $"WaveDefence signal NoPlan for {ToPrettyString(uid)}: {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
        }

        if (stalled && ShouldEmitSignal(ref attacker.LastLoggedStallAt, 1.0))
        {
            _sawmill.Debug(
                $"WaveDefence signal Stall for {ToPrettyString(uid)}: {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
        }

        if (disengage && ShouldEmitSignal(ref attacker.LastLoggedStallAt, 1.0))
        {
            _sawmill.Debug(
                $"WaveDefence signal Disengage for {ToPrettyString(uid)}: {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
        }

        var waitingOnReaction = !engaged &&
                                !IsLowValueSyncReason(attacker.LastTargetPushReason) &&
                                attacker.LastTargetPushAt != TimeSpan.Zero &&
                                _timing.CurTime - attacker.LastTargetPushAt >= TimeSpan.FromSeconds(SlowTargetReactionDelaySeconds) &&
                                (steering == null ||
                                 steering.Status is not (SteeringStatus.Moving or SteeringStatus.InRange));
        if (waitingOnReaction &&
            ShouldEmitSignal(ref attacker.LastLoggedReactionDelayAt, SlowTargetReactionCooldownSeconds))
        {
            _sawmill.Debug(
                $"WaveDefence slow target reaction for {ToPrettyString(uid)}: pushAge={FormatDuration(_timing.CurTime - attacker.LastTargetPushAt)}, lastPush={attacker.LastTargetPushReason}, {BuildTraceContext(uid, attacker, htn, xform, steering, engaged)}");
        }
    }

    private bool ShouldLogTargetPushReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return false;

        if (reason.StartsWith("initial-", StringComparison.Ordinal) ||
            reason.StartsWith("sync-target-change:", StringComparison.Ordinal) ||
            reason.StartsWith("forced:", StringComparison.Ordinal) ||
            reason.StartsWith("reroute:", StringComparison.Ordinal) ||
            reason.StartsWith("recovery-", StringComparison.Ordinal))
        {
            return true;
        }

        return reason is "sync-missing-push" or "sync-drift";
    }

    private bool ShouldLogSteeringTransition(string previousStatus, string currentStatus)
    {
        return string.Equals(previousStatus, nameof(SteeringStatus.NoPath), StringComparison.Ordinal) ||
               string.Equals(currentStatus, nameof(SteeringStatus.NoPath), StringComparison.Ordinal);
    }

    private bool IsLowValueSyncReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return false;

        if (!reason.StartsWith("sync-", StringComparison.Ordinal))
            return false;

        return !reason.StartsWith("sync-target-change:", StringComparison.Ordinal) &&
               reason != "sync-missing-push" &&
               reason != "sync-drift";
    }

    private bool ShouldEmitSignal(ref TimeSpan lastAt, double cooldownSeconds)
    {
        if (lastAt != TimeSpan.Zero &&
            _timing.CurTime - lastAt < TimeSpan.FromSeconds(cooldownSeconds))
        {
            return false;
        }

        lastAt = _timing.CurTime;
        return true;
    }

    private string BuildRecoveryReason(bool noPath, bool planless, bool stalled, bool disengage)
    {
        if (disengage && noPath && planless)
            return "NoPath+NoPlan+Disengage";
        if (disengage && noPath)
            return "NoPath+Disengage";
        if (disengage && planless)
            return "NoPlan+Disengage";
        if (disengage)
            return "Disengage";
        if (noPath && planless && stalled)
            return "NoPath+NoPlan+Stall";
        if (noPath && planless)
            return "NoPath+NoPlan";
        if (noPath && stalled)
            return "NoPath+Stall";
        if (planless && stalled)
            return "NoPlan+Stall";
        if (noPath)
            return "NoPath";
        if (planless)
            return "NoPlan";
        if (stalled)
            return "Stall";
        return "Unknown";
    }

    private static string DescribeRecoveryDelayReason(
        bool engaged,
        bool visibleCombatContact,
        bool investigatingPlayer)
    {
        if (visibleCombatContact)
            return "visible-combat-contact";

        if (investigatingPlayer)
            return "investigating-memory";

        if (engaged)
            return "combat-role-active";

        return "none";
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

        if (attacker.GeometryRecoveryTarget.IsValid(EntityManager))
            return $"geometry-recovery:{attacker.GeometryRecoveryLabel}";

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
            return $"{attacker.PlayerContactMode}:{attacker.PlayerContactPolicyLabel}:{FormatContactSource(attacker.RememberedPlayerSource)}";
        }

        return $"{attacker.PlayerContactMode}:{attacker.PlayerContactPolicyLabel}";
    }

    private string DescribeRecoveryOwner(
        WH40KWaveDefenceAttackerComponent attacker,
        NPCSteeringComponent? steering)
    {
        if (attacker.ForcedTarget.IsValid(EntityManager))
            return $"forced:{attacker.ForcedTargetKind}:{attacker.ForcedTargetLabel}";

        if (attacker.GeometryRecoveryTarget.IsValid(EntityManager))
            return $"geometry-recovery:{attacker.GeometryRecoveryLabel}";

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

    private string BuildTraceContext(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform,
        NPCSteeringComponent? steering,
        bool engaged)
    {
        ResolveObservedTarget(attacker, htn, out _, out var targetLabel, out var targetCoordinates);
        var currentLabel = DescribeLanePoint(attacker, attacker.LanePointIndex);
        var reachedLabel = DescribeLanePoint(attacker, attacker.LastReachedLanePointIndex);
        var steeringStatus = steering?.Status.ToString() ?? "NoSteering";
        var lastProgress = FormatDuration(_timing.CurTime - attacker.LastProgressAt);
        var lastPush = attacker.LastTargetPushAt == TimeSpan.Zero
            ? "n/a"
            : $"{attacker.LastTargetPushReason}/{FormatDuration(_timing.CurTime - attacker.LastTargetPushAt)}";
        var memory = attacker.RememberedPlayer.IsValid() && attacker.RememberedPlayerUntil > _timing.CurTime
            ? $"{ToPrettyString(attacker.RememberedPlayer)}:{FormatDuration(attacker.RememberedPlayerUntil - _timing.CurTime)}:{FormatContactSource(attacker.RememberedPlayerSource)}"
            : "-";
        var contactPolicy = $"{attacker.PlayerContactMode}:{attacker.PlayerContactPolicyLabel}";
        var brainOwner = DescribeBrainOwner(attacker);
        var combatOwner = DescribeCombatOwner(uid, attacker, htn);
        var movementOwner = DescribeMovementOwner(attacker, htn);
        var memoryOwner = DescribeMemoryOwner(attacker);
        var recoveryOwner = DescribeRecoveryOwner(attacker, steering);
        var epochSummary = BuildEpochSummary(attacker);
        var combatFocus = attacker.CombatFocusTarget.IsValid()
            ? $"{ToPrettyString(attacker.CombatFocusTarget)}@{FormatCoordinates(attacker.CombatFocusCoordinates)}"
            : "-";
        var investigate = attacker.InvestigationTarget.IsValid()
            ? $"{ToPrettyString(attacker.InvestigationTarget)}@{FormatCoordinates(attacker.InvestigationCoordinates)}"
            : "-";
        var movement = attacker.MovementTargetDirective.IsValid(EntityManager)
            ? $"{attacker.MovementTargetDirectiveLabel}@{FormatCoordinates(attacker.MovementTargetDirective)}"
            : "-";
        var forced = attacker.ForcedTarget.IsValid(EntityManager)
            ? $"{attacker.ForcedTargetKind}:{attacker.ForcedTargetLabel}"
            : "-";
        var forcedProgress = attacker.LastForcedTargetProgressAt == TimeSpan.Zero
            ? "-"
            : FormatDuration(_timing.CurTime - attacker.LastForcedTargetProgressAt);
        var combatProgress = attacker.LastAttackRangeImprovementAt == TimeSpan.Zero
            ? "-"
            : FormatDuration(_timing.CurTime - attacker.LastAttackRangeImprovementAt);
        var lastDamage = attacker.LastSuccessfulDamageDealtAt == TimeSpan.Zero
            ? "-"
            : FormatDuration(_timing.CurTime - attacker.LastSuccessfulDamageDealtAt);
        var perceptionRequests = $"{attacker.PendingPerceptionRequestEpoch}/{attacker.LastAppliedPerceptionRequestEpoch}";
        var navigationRequests = $"{attacker.PendingNavigationRequestEpoch}/{attacker.LastAppliedNavigationRequestEpoch}";
        var navObstacle = steering != null && steering.ActionableObstacle
            ? $"{steering.ActiveObstacleMode}:{ToPrettyString(steering.ActiveObstacle)}"
            : "-";
        var staticClearance = $"{attacker.ClearanceDebugLabel}:{attacker.ClearanceDebugReason}";
        if (!string.IsNullOrWhiteSpace(attacker.ClearanceDebugBlockerLabel))
            staticClearance += $":{attacker.ClearanceDebugBlockerLabel}";

        var dynamicClearance = $"{attacker.DynamicClearanceDebugLabel}:{attacker.DynamicClearanceDebugReason}";
        if (!string.IsNullOrWhiteSpace(attacker.DynamicClearanceDebugBlockerLabel))
            dynamicClearance += $":{attacker.DynamicClearanceDebugBlockerLabel}";

        return
            $"intent={attacker.Intent}, decision={attacker.DecisionEpoch}:{attacker.DecisionPriority}:{attacker.DecisionReason}, owners=brain:{brainOwner}|combat:{combatOwner}|move:{movementOwner}|memory:{memoryOwner}|recovery:{recoveryOwner}, epochs={epochSummary}, perception={attacker.PerceptionEpoch}/{attacker.LastAcceptedPerceptionEpoch}:{attacker.PerceptionStateLabel}, perceptionReq={perceptionRequests}, navigation={attacker.NavigationEpoch}/{attacker.LastAcceptedNavigationEpoch}:{attacker.NavigationStateLabel}, navigationReq={navigationRequests}, steering={steeringStatus}, planning={htn.Planning}, hasPlan={htn.Plan != null}, engaged={engaged}, target={targetLabel}, targetPos={FormatCoordinates(targetCoordinates)}, pos={FormatCoordinates(xform.Coordinates)}, current={currentLabel}, reached={reachedLabel}, furthest={attacker.FurthestReachedLanePointIndex}, prog={(attacker.RouteProgressRatio * 100f):0}%, cur={(attacker.CurrentRouteProgressRatio * 100f):0}%, front={(attacker.SharedLaneFrontProgress * 100f):0}%, memory={memory}, contactPolicy={contactPolicy}, overrideObjective={attacker.PlayerContactShouldOverrideObjective}, combat={combatFocus}, investigate={investigate}, move={movement}, forced={forced}, lastProgress={lastProgress}, forcedProgress={forcedProgress}, combatProgress={combatProgress}, lastDamage={lastDamage}, lastPush={lastPush}, blocker={attacker.ActiveSiegeBlockerLabel}, navObstacle={navObstacle}, body=r{attacker.BodyClearanceRadius:0.00}/d{attacker.BodyClearanceDiameter:0.00}, staticClr={staticClearance}, dynamicClr={dynamicClearance}";
    }

    private void ResolveObservedTarget(
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        out EntityUid targetEntity,
        out string targetLabel,
        out EntityCoordinates targetCoordinates)
    {
        targetEntity = EntityUid.Invalid;
        targetCoordinates = EntityCoordinates.Invalid;

        if (TryGetBlackboardPlayerCombatTarget(htn, out var playerTarget, out var playerCoordinates))
        {
            targetEntity = playerTarget;
            targetCoordinates = playerCoordinates.IsValid(EntityManager) || Deleted(playerTarget)
                ? playerCoordinates
                : Transform(playerTarget).Coordinates;
            targetLabel = $"player:{ToPrettyString(playerTarget)}";
            return;
        }

        if (TryGetBlackboardObjectiveCombatTarget(htn, out var objectiveTarget, out var objectiveCoordinates))
        {
            targetEntity = objectiveTarget;
            targetCoordinates = objectiveCoordinates.IsValid(EntityManager) || Deleted(objectiveTarget)
                ? objectiveCoordinates
                : Transform(objectiveTarget).Coordinates;
            targetLabel = attacker.ForcedTargetKind == WH40KWaveDefenceForcedTargetKind.DirectObjective &&
                          SameCoordinates(attacker.ForcedTarget, targetCoordinates)
                ? $"forced:{attacker.ForcedTargetLabel}"
                : $"objective:{ToPrettyString(objectiveTarget)}";
            return;
        }

        if (attacker.ForcedTarget.IsValid(EntityManager))
        {
            targetLabel = $"forced:{attacker.ForcedTargetLabel}";
            if (!TryGetBlackboardMovementTarget(htn, out targetCoordinates))
                targetCoordinates = attacker.ForcedTarget;
            return;
        }

        if (TryGetBlackboardMovementTarget(htn, out var movementCoordinates))
            targetCoordinates = movementCoordinates;

        if (attacker.MovementTargetDirective.IsValid(EntityManager))
        {
            targetLabel = string.IsNullOrWhiteSpace(attacker.MovementTargetDirectiveLabel)
                ? "movement"
                : attacker.MovementTargetDirectiveLabel;

            if (!targetCoordinates.IsValid(EntityManager))
                targetCoordinates = attacker.MovementTargetDirective;

            if (attacker.CombatFocusTarget.IsValid() &&
                attacker.CombatFocusCoordinates.IsValid(EntityManager) &&
                SameCoordinates(attacker.CombatFocusCoordinates, targetCoordinates))
            {
                targetEntity = attacker.CombatFocusTarget;
            }
            else if (attacker.InvestigationTarget.IsValid() &&
                     attacker.InvestigationCoordinates.IsValid(EntityManager) &&
                     SameCoordinates(attacker.InvestigationCoordinates, targetCoordinates))
            {
                targetEntity = attacker.InvestigationTarget;
            }

            return;
        }

        if (attacker.LocomotionTarget.IsValid(EntityManager))
        {
            targetLabel = string.IsNullOrWhiteSpace(attacker.LocomotionTargetLabel)
                ? attacker.LocomotionMode == WH40KWaveDefenceLocomotionMode.Objective
                    ? "objective"
                    : $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}"
                : attacker.LocomotionTargetLabel;

            if (!targetCoordinates.IsValid(EntityManager))
                targetCoordinates = attacker.LocomotionTarget;

            if (attacker.LocomotionMode == WH40KWaveDefenceLocomotionMode.Objective &&
                attacker.Objective is { } objectiveUid &&
                !Deleted(objectiveUid))
            {
                targetEntity = objectiveUid;
            }

            return;
        }

        if (attacker.LanePointIndex < attacker.LanePoints.Count)
        {
            targetLabel = $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}";
            if (!targetCoordinates.IsValid(EntityManager) &&
                attacker.ActiveRouteTarget.IsValid(EntityManager))
            {
                targetCoordinates = attacker.ActiveRouteTarget;
            }
            return;
        }

        if (attacker.Objective is { } objective && !Deleted(objective))
        {
            targetEntity = objective;
            targetLabel = $"objective:{ToPrettyString(objective)}";
            if (!targetCoordinates.IsValid(EntityManager))
                targetCoordinates = Transform(objective).Coordinates;
            return;
        }

        targetLabel = "<none>";
    }

    private void ApplyAuthoritativeBlackboardTargetState(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        EntityCoordinates target,
        string label)
    {
        if (TryGetCombatFocusTarget(attacker, out var combatTarget, out var combatCoordinates) &&
            SameCoordinates(combatCoordinates, target))
        {
            ApplyPlayerCombatBlackboardState(uid, htn, combatTarget, combatCoordinates);
            return;
        }

        if (TryResolveObjectiveBlackboardState(attacker, target, label, out var objective))
        {
            ApplyObjectiveCombatBlackboardState(uid, htn, objective, target);
            return;
        }

        ApplyMovementBlackboardState(uid, htn, target);
    }

    private void ApplyPlayerCombatBlackboardState(
        EntityUid uid,
        HTNComponent htn,
        EntityUid target,
        EntityCoordinates coordinates)
    {
        _npc.SetBlackboard(uid, WH40KWaveDefenceHtnBlackboardKeys.PlayerTarget, target, htn);
        _npc.SetBlackboard(uid, WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates, coordinates, htn);
        htn.Blackboard.Remove<EntityUid>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTarget);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates);
        SetBlackboardRoles(uid, htn, playerCombat: true, objectiveCombat: false, movement: false);
        ClearLegacySharedTargetState(htn);
    }

    private void ApplyObjectiveCombatBlackboardState(
        EntityUid uid,
        HTNComponent htn,
        EntityUid objective,
        EntityCoordinates coordinates)
    {
        _npc.SetBlackboard(uid, WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTarget, objective, htn);
        _npc.SetBlackboard(uid, WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates, coordinates, htn);
        htn.Blackboard.Remove<EntityUid>(WH40KWaveDefenceHtnBlackboardKeys.PlayerTarget);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates);
        SetBlackboardRoles(uid, htn, playerCombat: false, objectiveCombat: true, movement: false);
        ClearLegacySharedTargetState(htn);
    }

    private void ApplyMovementBlackboardState(
        EntityUid uid,
        HTNComponent htn,
        EntityCoordinates coordinates)
    {
        _npc.SetBlackboard(uid, WH40KWaveDefenceHtnBlackboardKeys.MovementTargetCoordinates, coordinates, htn);
        htn.Blackboard.Remove<EntityUid>(WH40KWaveDefenceHtnBlackboardKeys.PlayerTarget);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.PlayerTargetCoordinates);
        htn.Blackboard.Remove<EntityUid>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTarget);
        htn.Blackboard.Remove<EntityCoordinates>(WH40KWaveDefenceHtnBlackboardKeys.ObjectiveTargetCoordinates);
        SetBlackboardRoles(uid, htn, playerCombat: false, objectiveCombat: false, movement: true);
        ClearLegacySharedTargetState(htn);
    }

    private void SetBlackboardRoles(
        EntityUid uid,
        HTNComponent htn,
        bool playerCombat,
        bool objectiveCombat,
        bool movement)
    {
        _npc.SetBlackboard(uid, WH40KWaveDefenceHtnBlackboardKeys.PlayerCombatRole, playerCombat, htn);
        _npc.SetBlackboard(uid, WH40KWaveDefenceHtnBlackboardKeys.ObjectiveCombatRole, objectiveCombat, htn);
        _npc.SetBlackboard(uid, WH40KWaveDefenceHtnBlackboardKeys.MovementRole, movement, htn);
    }

    private static void ClearLegacySharedTargetState(HTNComponent htn)
    {
        htn.Blackboard.Remove<EntityUid>(TargetKey);
        htn.Blackboard.Remove<EntityCoordinates>(TargetCoordinatesKey);
        htn.Blackboard.Remove<EntityCoordinates>(AttackTargetCoordinatesKey);
    }

    private bool TryResolveObjectiveBlackboardState(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        string label,
        out EntityUid objective)
    {
        objective = EntityUid.Invalid;
        if (attacker.Objective is not { } objectiveUid || Deleted(objectiveUid))
            return false;

        if (attacker.ForcedTargetKind == WH40KWaveDefenceForcedTargetKind.DirectObjective &&
            SameCoordinates(attacker.ForcedTarget, target))
        {
            objective = objectiveUid;
            return true;
        }

        if (label.StartsWith("objective:", StringComparison.Ordinal) ||
            string.Equals(label, "objective", StringComparison.Ordinal))
        {
            objective = objectiveUid;
            return true;
        }

        if (attacker.LocomotionMode == WH40KWaveDefenceLocomotionMode.Objective &&
            attacker.LocomotionTarget.IsValid(EntityManager) &&
            SameCoordinates(attacker.LocomotionTarget, target))
        {
            objective = objectiveUid;
            return true;
        }

        return false;
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

    private bool TryGetBlackboardMovementTarget(
        HTNComponent htn,
        out EntityCoordinates coordinates)
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

    private string DescribeTargetLabel(
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        EntityCoordinates target)
    {
        ResolveObservedTarget(attacker, htn, out _, out var label, out var observedCoordinates);
        if (label != "<none>" &&
            SameCoordinates(observedCoordinates, target))
            return label;

        if (attacker.ForcedTarget.IsValid(EntityManager))
            return $"forced:{attacker.ForcedTargetLabel}";

        if (TryDescribePlayerContactLabel(attacker, target, out var playerLabel))
            return playerLabel;

        if (attacker.LocomotionTarget.IsValid(EntityManager) &&
            !string.IsNullOrWhiteSpace(attacker.LocomotionTargetLabel))
        {
            return attacker.LocomotionTargetLabel;
        }

        if (attacker.LanePointIndex < attacker.LanePoints.Count)
            return $"lane:{attacker.LaneId}:{DescribeLanePoint(attacker, attacker.LanePointIndex)}";

        if (attacker.Objective is { } objective && !Deleted(objective))
            return $"objective:{ToPrettyString(objective)}";

        return SameCoordinates(observedCoordinates, target)
            ? label
            : $"coords:{FormatCoordinates(target)}";
    }

    private bool TryDescribePlayerContactLabel(
        WH40KWaveDefenceAttackerComponent attacker,
        EntityCoordinates target,
        out string label)
    {
        if (attacker.VisiblePlayer.IsValid() &&
            attacker.VisiblePlayerUntil != TimeSpan.Zero &&
            _timing.CurTime < attacker.VisiblePlayerUntil &&
            attacker.VisiblePlayerCoordinates.IsValid(EntityManager) &&
            SameCoordinates(attacker.VisiblePlayerCoordinates, target))
        {
            label = $"player:{ToPrettyString(attacker.VisiblePlayer)}";
            return true;
        }

        if (attacker.InvestigationTarget.IsValid() &&
            attacker.InvestigationCoordinates.IsValid(EntityManager) &&
            SameCoordinates(attacker.InvestigationCoordinates, target))
        {
            label = string.IsNullOrWhiteSpace(attacker.InvestigationLabel)
                ? $"investigate:{ToPrettyString(attacker.InvestigationTarget)}"
                : attacker.InvestigationLabel;
            return true;
        }

        label = string.Empty;
        return false;
    }

    private bool SameCoordinates(EntityCoordinates a, EntityCoordinates b)
    {
        if (!a.IsValid(EntityManager) || !b.IsValid(EntityManager))
            return !a.IsValid(EntityManager) && !b.IsValid(EntityManager);

        if (a.EntityId != b.EntityId)
            return false;

        return (a.Position - b.Position).LengthSquared() <= 0.01f;
    }

    private bool HasSignificantCoordinateChange(EntityCoordinates a, EntityCoordinates b)
    {
        if (!a.IsValid(EntityManager) || !b.IsValid(EntityManager))
            return !SameCoordinates(a, b);

        if (a.EntityId != b.EntityId)
            return true;

        return (a.Position - b.Position).LengthSquared() >= SyncSignificantDriftDistance * SyncSignificantDriftDistance;
    }

    private bool HasHardRetargetCoordinateChange(EntityCoordinates a, EntityCoordinates b)
    {
        if (!a.IsValid(EntityManager) || !b.IsValid(EntityManager))
            return !SameCoordinates(a, b);

        if (a.EntityId != b.EntityId)
            return true;

        return (a.Position - b.Position).LengthSquared() >= SyncEquivalentLaneHardRetargetDistance * SyncEquivalentLaneHardRetargetDistance;
    }

    private static bool AreEquivalentLaneSubTargets(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        var leftFamily = GetLaneSubTargetFamily(left);
        var rightFamily = GetLaneSubTargetFamily(right);
        return !string.IsNullOrWhiteSpace(leftFamily) &&
               string.Equals(leftFamily, rightFamily, StringComparison.Ordinal);
    }

    private static string? GetLaneSubTargetFamily(string label)
    {
        if (!label.StartsWith("lane:", StringComparison.Ordinal))
            return null;

        var bracketIndex = label.IndexOf(']');
        if (bracketIndex <= 0)
            return label;

        return label[..(bracketIndex + 1)];
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
            return "0ms";

        if (span.TotalSeconds >= 1d)
            return $"{span.TotalSeconds:0.00}s";

        return $"{span.TotalMilliseconds:0}ms";
    }

    private string FormatCoordinates(EntityCoordinates coordinates)
    {
        if (!coordinates.IsValid(EntityManager))
            return "<invalid>";

        return $"({coordinates.Position.X:0.00},{coordinates.Position.Y:0.00})";
    }

    private TimeSpan GetTacticalThinkDelay(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker)
    {
        var baseInterval = attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm
            ? SimpleSwarmThinkIntervalSeconds
            : AdvancedThinkIntervalSeconds;
        var stagger = 0.015f * (Math.Abs(uid.Id.GetHashCode()) % 5);
        return TimeSpan.FromSeconds(baseInterval + stagger);
    }

    private TimeSpan GetDeliberationDelay(EntityUid uid, WH40KWaveDefenceAttackerComponent attacker, bool engaged)
    {
        var baseInterval = engaged
            ? EngagedDeliberationIntervalSeconds
            : attacker.AiProfile == WH40KWaveAiProfile.SimpleSwarm
                ? SimpleSwarmDeliberationIntervalSeconds
                : AdvancedDeliberationIntervalSeconds;
        var stagger = 0.02f * (Math.Abs(uid.Id.GetHashCode()) % 5);
        return TimeSpan.FromSeconds(baseInterval + stagger);
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

    private void UpdateDebugState(
        EntityUid uid,
        WH40KWaveDefenceAttackerComponent attacker,
        HTNComponent htn,
        TransformComponent xform)
    {
        var engaged = IsEngaged(attacker, htn);
        var steering = CompOrNull<NPCSteeringComponent>(uid);
        var steeringStatus = steering?.Status.ToString() ?? "NoSteering";
        ResolveObservedTarget(attacker, htn, out _, out var targetLabel, out _);
        var brainOwner = DescribeBrainOwner(attacker);
        var combatOwner = DescribeCombatOwner(uid, attacker, htn);
        var movementOwner = DescribeMovementOwner(attacker, htn);
        var memoryOwner = DescribeMemoryOwner(attacker);
        var recoveryOwner = DescribeRecoveryOwner(attacker, steering);
        var memoryLabel = attacker.RememberedPlayer.IsValid()
            ? $"{ToPrettyString(attacker.RememberedPlayer)}:{Math.Max(0f, (float) (attacker.RememberedPlayerUntil - _timing.CurTime).TotalSeconds):0.0}s:{FormatContactSource(attacker.RememberedPlayerSource)}"
            : "-";
        var forcedLabel = attacker.ForcedTarget.IsValid(EntityManager)
            ? $"{attacker.ForcedTargetKind}:{attacker.ForcedTargetLabel}"
            : "-";
        var epochLabel = BuildEpochSummary(attacker);
        var reachedLabel = DescribeLanePoint(attacker, attacker.LastReachedLanePointIndex);
        var currentLabel = DescribeLanePoint(attacker, attacker.LanePointIndex);
        var blockerLabel = attacker.ActiveSiegeBlocker.IsValid()
            ? $"{ToPrettyString(attacker.ActiveSiegeBlocker)}:{attacker.ActiveSiegeBlockerLabel}"
            : "-";
        var navObstacleLabel = steering is { ActionableObstacle: true }
            ? $"{steering.ActiveObstacleMode}:{ToPrettyString(steering.ActiveObstacle)}"
            : "-";
        var staticClearanceLabel = $"{attacker.ClearanceDebugLabel}:{attacker.ClearanceDebugReason}";
        if (!string.IsNullOrWhiteSpace(attacker.ClearanceDebugBlockerLabel))
            staticClearanceLabel += $":{attacker.ClearanceDebugBlockerLabel}";

        var dynamicClearanceLabel = $"{attacker.DynamicClearanceDebugLabel}:{attacker.DynamicClearanceDebugReason}";
        if (!string.IsNullOrWhiteSpace(attacker.DynamicClearanceDebugBlockerLabel))
            dynamicClearanceLabel += $":{attacker.DynamicClearanceDebugBlockerLabel}";

        attacker.DebugState =
            $"profile={attacker.AiProfile}, intent={attacker.Intent}, decision={brainOwner}, owners=brain:{brainOwner}|combat:{combatOwner}|move:{movementOwner}|memory:{memoryOwner}|recovery:{recoveryOwner}, epochs={epochLabel}, lane={attacker.LaneId}, target={targetLabel}, forced={forcedLabel}, current={currentLabel}, reached={reachedLabel}, furthest={attacker.FurthestReachedLanePointIndex}, progress={(attacker.RouteProgressRatio * 100f):0}%, currentProgress={(attacker.CurrentRouteProgressRatio * 100f):0}%, front={(attacker.SharedLaneFrontProgress * 100f):0}%, memory={memoryLabel}, blocker={blockerLabel}, navObstacle={navObstacleLabel}, body=r{attacker.BodyClearanceRadius:0.00}/d{attacker.BodyClearanceDiameter:0.00}, staticClr={staticClearanceLabel}, dynamicClr={dynamicClearanceLabel}, engaged={engaged}, steering={steeringStatus}, recoveryLevel={attacker.RecoveryLevel}, attempts={attacker.RecoveryAttempts}, noPath={attacker.NoPathCount}, reroutes={attacker.LaneRerouteCount}, fallbacks={attacker.FallbackCount}";
    }

    private static void ClearRememberedPlayer(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.RememberedPlayer = EntityUid.Invalid;
        attacker.RememberedPlayerCoordinates = EntityCoordinates.Invalid;
        attacker.RememberedPlayerUntil = TimeSpan.Zero;
        attacker.RememberedPlayerSource = WH40KWaveDefencePlayerContactSource.None;
        attacker.RememberedPlayerReceivedAt = TimeSpan.Zero;
    }

    private static void ClearVisiblePlayer(WH40KWaveDefenceAttackerComponent attacker)
    {
        attacker.VisiblePlayer = EntityUid.Invalid;
        attacker.VisiblePlayerCoordinates = EntityCoordinates.Invalid;
        attacker.VisiblePlayerUntil = TimeSpan.Zero;
    }

    private void UpdateVisiblePlayerContact(WH40KWaveDefenceAttackerComponent attacker)
    {
        if (!attacker.VisiblePlayer.IsValid())
            return;

        var deleted = Deleted(attacker.VisiblePlayer);
        var targetXform = deleted ? null : Transform(attacker.VisiblePlayer);
        var expired = attacker.VisiblePlayerUntil != TimeSpan.Zero && _timing.CurTime >= attacker.VisiblePlayerUntil;
        var invalid =
            deleted ||
            targetXform?.MapID == MapId.Nullspace ||
            !IsAttackablePlayerTarget(attacker.VisiblePlayer);

        if (!expired && !invalid)
            return;

        ClearVisiblePlayer(attacker);
    }

    private bool IsAttackablePlayerTarget(EntityUid target)
    {
        return target.IsValid() &&
               Exists(target) &&
               _mobState.IsAlive(target);
    }

    private static string FormatContactSource(WH40KWaveDefencePlayerContactSource source)
    {
        return source switch
        {
            WH40KWaveDefencePlayerContactSource.DirectSight => "direct",
            WH40KWaveDefencePlayerContactSource.AllyRelay => "relay",
            _ => "none",
        };
    }

    private readonly record struct PlayerContactPolicyResult(
        WH40KWaveDefencePlayerContactMode Mode,
        string Label,
        bool ShouldOverrideObjective,
        EntityUid CombatTarget,
        EntityCoordinates CombatCoordinates,
        EntityUid InvestigationTarget,
        EntityCoordinates InvestigationCoordinates);

    private readonly record struct RouteProgressSnapshot(
        int TotalPointCount,
        int CurrentIndex,
        int LastReachedIndex,
        int FurthestReachedIndex,
        float ProgressRatio,
        bool RouteCompleted);

    private readonly record struct NavigationPolicy(bool Interact, bool Pry, bool Smash, bool Climb);
}
