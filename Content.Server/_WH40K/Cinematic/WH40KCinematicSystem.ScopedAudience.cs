using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Damage.Systems;
using Content.Server.Mind;
using Content.Server.Roles.Jobs;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Ghost;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.Players;
using Content.Shared.Roles.Jobs;
using Content.Shared.Trigger;
using Content.Shared._WH40K.Cinematic;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Cinematic;

public sealed partial class WH40KCinematicSystem
{
    [Dependency] private  GodmodeSystem _godmode = default!;
    [Dependency] private  DamageableSystem _damageable = default!;
    [Dependency] private  MobStateSystem _mobState = default!;
    [Dependency] private  MindSystem _mind = default!;
    [Dependency] private  JobSystem _jobs = default!;
    [Dependency] private  NpcFactionSystem _npcFaction = default!;
    [Dependency] private  SharedTransformSystem _xform = default!;

    private readonly List<ActiveCinematicRun> _scopedRuns = new();
    private readonly Queue<QueuedScopedCinematicRequest> _scopedQueue = new();

    private void InitializeScopedAudienceFeatures()
    {
        SubscribeLocalEvent<WH40KCinematicTriggerComponent, TriggerEvent>(OnCinematicTrigger);
    }

    public bool TryQueueForUsers(
        ProtoId<WH40KCinematicPrototype> cinematicId,
        IEnumerable<NetUserId> audienceUserIds,
        out string message,
        NetUserId? triggerUserId = null,
        WH40KCinematicGhostAudiencePolicy? ghostAudiencePolicy = null,
        int? priority = null)
    {
        message = string.Empty;

        if (!_prototypes.TryIndex(cinematicId, out var prototype))
        {
            message = $"Unknown cinematic prototype '{cinematicId}'.";
            return false;
        }

        return TryQueueForUsers(prototype, audienceUserIds, out message, triggerUserId, ghostAudiencePolicy, priority);
    }

    public bool TryQueueForUsers(
        WH40KCinematicPrototype prototype,
        IEnumerable<NetUserId> audienceUserIds,
        out string message,
        NetUserId? triggerUserId = null,
        WH40KCinematicGhostAudiencePolicy? ghostAudiencePolicy = null,
        int? priority = null)
    {
        message = string.Empty;
        var requestedAudience = new HashSet<NetUserId>(audienceUserIds);
        if (requestedAudience.Count == 0)
        {
            message = $"Scoped cinematic '{prototype.ID}' requires at least one target user.";
            return false;
        }

        var errors = ValidatePrototype(prototype);
        if (errors.Count > 0)
        {
            message = $"Cinematic '{prototype.ID}' is invalid: {string.Join("; ", errors)}";
            return false;
        }

        if (prototype.WorldFreezeMode == WH40KCinematicWorldFreezeMode.PauseMap)
        {
            message = $"Scoped cinematic '{prototype.ID}' cannot use PauseMap when queued for specific users.";
            return false;
        }

        if (!prototype.AllowRepeat && HasActiveOrQueuedPrototype(prototype.ID))
        {
            message = $"Cinematic '{prototype.ID}' is already active or queued and cannot repeat.";
            return false;
        }

        var request = new QueuedScopedCinematicRequest(
            prototype,
            _timing.CurTime,
            requestedAudience,
            triggerUserId,
            priority ?? prototype.Priority,
            ghostAudiencePolicy ?? prototype.GhostAudiencePolicy);

        return TryQueueScoped(request, out message);
    }

    private bool TryQueueScoped(QueuedScopedCinematicRequest request, out string message)
    {
        message = string.Empty;

        if (_active != null)
        {
            if (request.Prototype.QueueMode == WH40KCinematicQueueMode.IgnoreIfBusy)
            {
                message = $"Scoped cinematic '{request.Prototype.ID}' ignored because a global cinematic is active.";
                return false;
            }

            _scopedQueue.Enqueue(request);
            message = $"Queued scoped cinematic '{request.Prototype.ID}' while a global cinematic is active.";
            return true;
        }

        var overlapping = _scopedRuns
            .Where(run => run.RequestedAudienceUserIds.Overlaps(request.AudienceUserIds))
            .ToArray();

        if (overlapping.Length == 0)
        {
            StartScopedRun(request);
            message = $"Started scoped cinematic '{request.Prototype.ID}' for {request.AudienceUserIds.Count} user(s).";
            return true;
        }

        var highestExistingPriority = overlapping.Max(run => run.Priority);
        if (request.Priority > highestExistingPriority)
        {
            foreach (var run in overlapping)
            {
                AbortRun(run, $"Interrupted by higher-priority scoped cinematic '{request.Prototype.ID}'.", markCompleted: false);
            }

            StartScopedRun(request);
            message = $"Started scoped cinematic '{request.Prototype.ID}' by interrupting lower-priority scoped run(s).";
            return true;
        }

        if (request.Prototype.QueueMode == WH40KCinematicQueueMode.IgnoreIfBusy)
        {
            message = $"Scoped cinematic '{request.Prototype.ID}' ignored because overlapping audience already has an equal or higher priority cinematic.";
            return false;
        }

        _scopedQueue.Enqueue(request);
        message = $"Queued scoped cinematic '{request.Prototype.ID}' due to overlapping audience conflict.";
        return true;
    }

    private void StartScopedRun(QueuedScopedCinematicRequest request)
    {
        var run = new ActiveCinematicRun(++_runSerial, request.Prototype, request.QueuedAt, _timing.CurTime)
        {
            IsScoped = true,
            Priority = request.Priority,
            TriggerUserId = request.TriggerUserId,
            GhostAudiencePolicy = request.GhostAudiencePolicy,
            AudienceLocked = request.Prototype.LockAudienceOnStart,
        };

        foreach (var userId in request.AudienceUserIds)
        {
            run.RequestedAudienceUserIds.Add(userId);
        }

        _scopedRuns.Add(run);
        EnrollCurrentAudience(run);
        EnsureRunMainContext(run);
        TraceInfo($"Started scoped WH40K cinematic '{request.Prototype.ID}' with {run.RequestedAudienceUserIds.Count} requested user(s).");
        AdvanceToNextStep(run, "Scoped start");
    }

    private void UpdateScopedRuns()
    {
        if (_scopedRuns.Count == 0)
            return;

        var activeRuns = _scopedRuns.ToArray();
        foreach (var run in activeRuns)
        {
            if (_scopedRuns.Contains(run))
                UpdateRun(run);
        }
    }

    private void TryStartNextQueuedScoped()
    {
        if (_scopedQueue.Count == 0 || _active != null)
            return;

        var remaining = _scopedQueue.Count;
        while (remaining-- > 0 && _scopedQueue.TryDequeue(out var request))
        {
            if (!request.Prototype.AllowRepeat &&
                (_completedNonRepeatable.Contains(request.Prototype.ID) || HasActiveOrQueuedPrototype(request.Prototype.ID)))
            {
                continue;
            }

            if (TryQueueScoped(request, out _))
                continue;

            _scopedQueue.Enqueue(request);
        }
    }

    private void UpdateRun(ActiveCinematicRun active)
    {
        if (active.RestorePhaseActive)
        {
            if (active.UnlockAt != null && _timing.CurTime >= active.UnlockAt.Value)
                CompleteRestorePhase(active, "Restore delay elapsed.", markCompleted: true);

            return;
        }

        RefreshActionRuntimes(active);
        if (active.RestorePhaseActive)
            return;

        PruneEntitySets(active);

        if (active.ManuallyPaused)
            return;

        if (!string.IsNullOrWhiteSpace(active.CurrentStep.Id) &&
            _timing.CurTime >= active.NextStateBroadcastAt)
        {
            BroadcastActiveState(active);
        }

        switch (active.WaitMode)
        {
            case WH40KCinematicWaitMode.Duration:
                if (active.StepEndsAt != null && _timing.CurTime >= active.StepEndsAt.Value)
                    AdvanceToNextStep(active, "Duration elapsed.");
                break;

            case WH40KCinematicWaitMode.AwaitCompletion:
                if (AreCurrentStepBlockingActionsComplete(active))
                    AdvanceToNextStep(active, "Blocking actions completed.");
                break;

            case WH40KCinematicWaitMode.AwaitCompletionOrTimeout:
                if (AreCurrentStepBlockingActionsComplete(active))
                {
                    AdvanceToNextStep(active, "Blocking actions completed before timeout.");
                    break;
                }

                if (active.StepEndsAt != null && _timing.CurTime >= active.StepEndsAt.Value)
                    AdvanceToNextStep(active, "AwaitCompletionOrTimeout reached timeout.");
                break;

            case WH40KCinematicWaitMode.AwaitSignal:
                if (AreSignalConditionsSatisfied(active, active.CurrentStep, consumeSignals: true))
                    AdvanceToNextStep(active, "Required signal received.");
                break;

            case WH40KCinematicWaitMode.AwaitSignalOrTimeout:
                if (AreSignalConditionsSatisfied(active, active.CurrentStep, consumeSignals: true))
                {
                    AdvanceToNextStep(active, "Required signal received before timeout.");
                    break;
                }

                if (active.StepEndsAt != null && _timing.CurTime >= active.StepEndsAt.Value)
                    AdvanceToNextStep(active, "AwaitSignalOrTimeout reached timeout.");
                break;

            case WH40KCinematicWaitMode.AwaitEntitySetEmpty:
                if (AreEntitySetConditionsSatisfied(active, active.CurrentStep))
                    AdvanceToNextStep(active, "Tracked entity set became empty.");
                break;
        }
    }

    private void AdvanceToNextStep(ActiveCinematicRun active, string reason)
    {
        if (active.RestorePhaseActive)
            return;

        while (true)
        {
            active.CurrentStepIndex++;

            if (active.CurrentStepIndex >= active.Prototype.Steps.Count)
            {
                BeginRestorePhase(active, $"Reached end of timeline ({reason})", markCompleted: true);
                return;
            }

            var step = active.Prototype.Steps[active.CurrentStepIndex];
            var previousShot = active.CurrentShot;
            active.CurrentStep = step;
            active.StepStartedAt = _timing.CurTime;
            active.StepEndsAt = null;
            active.WaitMode = step.WaitMode;
            active.CurrentShot = step.Type == WH40KCinematicStepType.Shot
                ? null
                : RetainCurrentShotIfValid(previousShot);
            EnsureRunMainContext(active);

            if (!string.IsNullOrWhiteSpace(step.ContextId))
            {
                if (!TrySwitchContextAction(active, step.ContextId, out var contextFailure))
                {
                    AbortRun(active, contextFailure, markCompleted: false);
                    return;
                }
            }

            ClearAudienceViewSubscriptions(active);
            ApplyStepAudienceLockDirective(active, step);

            var stepSummary =
                $"Entering WH40K cinematic '{active.Prototype.ID}' step '{step.Id}' " +
                $"(index {active.CurrentStepIndex + 1}/{active.Prototype.Steps.Count}, type={step.Type}, wait={step.WaitMode}";
            if (step.WaitMode == WH40KCinematicWaitMode.Duration)
                stepSummary += $", duration={step.DurationSeconds:0.##}s";
            else if (step.WaitMode == WH40KCinematicWaitMode.AwaitCompletionOrTimeout)
                stepSummary += $", timeout={step.TimeoutSeconds:0.##}s";

            if (step.Type == WH40KCinematicStepType.Shot &&
                step.CameraSource == WH40KCinematicCameraSource.FixedPoint &&
                !string.IsNullOrWhiteSpace(step.CameraPointId))
            {
                stepSummary += $", cameraPoint={step.CameraPointId}";
            }

            stepSummary += $", actions={step.Actions.Count}).";
            TraceInfo(stepSummary);

            if (step.Type == WH40KCinematicStepType.Shot &&
                step.CameraSource == WH40KCinematicCameraSource.FixedPoint)
            {
                if (!TryResolveShot(active.Prototype, active, step, out var shot))
                {
                    if (step.OptionalCameraPoint)
                    {
                        Log.Warning($"Skipping shot step '{step.Id}' in cinematic '{active.Prototype.ID}' because camera point '{step.CameraPointId}' was not found.");
                        continue;
                    }

                    AbortRun(active, $"Missing required camera point '{step.CameraPointId}' in step '{step.Id}'.", markCompleted: false);
                    return;
                }

                active.CurrentShot = shot;
                SyncAudienceViewSubscriptions(active);
            }
            else if (active.CurrentShot != null)
            {
                SyncAudienceViewSubscriptions(active);
            }

            if (!TryExecuteStepActions(active, step, out var actionFailure))
            {
                AbortRun(active, actionFailure, markCompleted: false);
                return;
            }

            active.CurrentShot = RetainCurrentShotIfValid(active.CurrentShot);

            if (step.Type == WH40KCinematicStepType.EndCinematic ||
                step.WaitMode == WH40KCinematicWaitMode.Terminal)
            {
                BeginRestorePhase(active, $"Terminal step '{step.Id}' reached.", markCompleted: true);
                return;
            }

            switch (step.WaitMode)
            {
                case WH40KCinematicWaitMode.Instant:
                    BroadcastActiveState(active);
                    continue;

                case WH40KCinematicWaitMode.Duration:
                    active.StepEndsAt = _timing.CurTime + TimeSpan.FromSeconds(step.DurationSeconds);
                    BroadcastActiveState(active);
                    return;

                case WH40KCinematicWaitMode.AwaitCompletion:
                    BroadcastActiveState(active);
                    if (AreCurrentStepBlockingActionsComplete(active))
                        continue;

                    return;

                case WH40KCinematicWaitMode.AwaitCompletionOrTimeout:
                    active.StepEndsAt = _timing.CurTime + TimeSpan.FromSeconds(step.TimeoutSeconds);
                    BroadcastActiveState(active);
                    if (AreCurrentStepBlockingActionsComplete(active))
                        continue;

                    return;

                case WH40KCinematicWaitMode.AwaitSignal:
                    BroadcastActiveState(active);
                    if (AreSignalConditionsSatisfied(active, step, consumeSignals: true))
                        continue;

                    return;

                case WH40KCinematicWaitMode.AwaitSignalOrTimeout:
                {
                    var timeoutSeconds = step.TimeoutSeconds > 0f
                        ? step.TimeoutSeconds
                        : active.Prototype.DefaultWaitTimeoutSeconds ?? 0f;
                    active.StepEndsAt = timeoutSeconds > 0f
                        ? _timing.CurTime + TimeSpan.FromSeconds(timeoutSeconds)
                        : null;
                    BroadcastActiveState(active);
                    if (AreSignalConditionsSatisfied(active, step, consumeSignals: true))
                        continue;

                    return;
                }

                case WH40KCinematicWaitMode.AwaitEntitySetEmpty:
                    BroadcastActiveState(active);
                    if (AreEntitySetConditionsSatisfied(active, step))
                        continue;

                    return;

                default:
                    AbortRun(active, $"Unsupported waitMode '{step.WaitMode}' encountered at runtime.", markCompleted: false);
                    return;
            }
        }
    }

    private WH40KCinematicShotRuntimeState? RetainCurrentShotIfValid(WH40KCinematicShotRuntimeState? shot)
    {
        if (shot?.CameraPointEntity is not { Valid: true } cameraPoint || Deleted(cameraPoint))
            return null;

        return shot;
    }

    private void BeginRestorePhase(ActiveCinematicRun active, string reason, bool markCompleted)
    {
        if (active.RestorePhaseActive)
            return;

        active.RestorePhaseActive = true;
        ClearAudienceViewSubscriptions(active);
        var unlockDelay = active.AudienceLocked
            ? Math.Max(0f, active.Prototype.RestoreInputDelaySeconds)
            : 0f;
        active.UnlockAt = _timing.CurTime + TimeSpan.FromSeconds(unlockDelay);

        BroadcastStoppedEvent(active, markCompleted, reason, unlockDelay);

        if (unlockDelay <= 0f)
            CompleteRestorePhase(active, reason, markCompleted);
    }

    private void CompleteRestorePhase(ActiveCinematicRun active, string reason, bool markCompleted)
    {
        var wasGlobal = ReferenceEquals(_active, active);
        if (wasGlobal)
            _active = null;
        else
            _scopedRuns.Remove(active);

        CleanupRun(active);

        if (markCompleted && !active.Prototype.AllowRepeat)
            _completedNonRepeatable.Add(active.Prototype.ID);

        TraceInfo($"Stopped WH40K cinematic '{active.Prototype.ID}'. Completed={markCompleted}. Reason={reason}");

        if (wasGlobal && _queue.Count > 0)
            TryStartNextQueued();
    }

    private void AbortRun(ActiveCinematicRun active, string reason, bool markCompleted)
    {
        BroadcastStoppedEvent(active, markCompleted, reason, 0f);
        CompleteRestorePhase(active, reason, markCompleted);
    }

    private void BroadcastActiveState(ActiveCinematicRun active)
    {
        if (active.RestorePhaseActive || string.IsNullOrWhiteSpace(active.CurrentStep.Id))
            return;

        SyncAudienceMembership(active);
        SyncAudienceViewSubscriptions(active);
        active.NextStateBroadcastAt = _timing.CurTime + ActiveStateResyncInterval;
        var state = BuildActiveNetState(active);
        foreach (var session in _players.Sessions)
        {
            if (!active.AudienceUserIds.Contains(session.UserId))
                continue;

            RaiseNetworkEvent(new WH40KCinematicStateEvent(state), session);
        }
    }

    private void SyncAudienceMembership(ActiveCinematicRun active)
    {
        foreach (var session in _players.Sessions)
        {
            var entity = session.AttachedEntity;
            var qualifies = entity is { Valid: true } valid &&
                            !Deleted(valid) &&
                            ShouldAffectEntity(active, session, valid);

            if (qualifies)
            {
                TryEnrollSession(active, session, entity);
                continue;
            }

            if (!active.AudienceUserIds.Remove(session.UserId))
                continue;

            RemoveAudienceViewSubscription(active, session);

            if (entity is not { Valid: true } attached || Deleted(attached))
                continue;

            ReleaseLock(attached, active.RunSerial);
            ReleaseProtection(attached, active.RunSerial);
        }
    }

    private bool ShouldAffectSession(ActiveCinematicRun active, ICommonSession session, EntityUid entity)
    {
        if (active.ExcludedAudienceUserIds.Contains(session.UserId))
            return false;

        var isGhost = HasComp<GhostComponent>(entity);
        var explicitlyRequested = !active.IsScoped || active.RequestedAudienceUserIds.Contains(session.UserId);
        var includeAllGhosts = active.GhostAudiencePolicy == WH40KCinematicGhostAudiencePolicy.IncludeAllGhosts && isGhost;

        if (!explicitlyRequested && !includeAllGhosts)
            return false;

        return active.GhostAudiencePolicy switch
        {
            WH40KCinematicGhostAudiencePolicy.Never => explicitlyRequested && !isGhost,
            WH40KCinematicGhostAudiencePolicy.MirrorAudience => active.IsScoped ? explicitlyRequested : !isGhost,
            WH40KCinematicGhostAudiencePolicy.OnlyGhosts => (explicitlyRequested || includeAllGhosts) && isGhost,
            WH40KCinematicGhostAudiencePolicy.IncludeAllGhosts => explicitlyRequested || isGhost,
            _ => explicitlyRequested && !isGhost
        };
    }

    private void ApplyGlobalRunConflictPolicy(ActiveCinematicRun run)
    {
        if (run.Prototype.GlobalLocalConflictPolicy == WH40KCinematicGlobalLocalConflictPolicy.InterruptLocals)
        {
            foreach (var scoped in _scopedRuns.ToArray())
            {
                AbortRun(scoped, $"Interrupted by global cinematic '{run.Prototype.ID}'.", markCompleted: false);
            }

            return;
        }

        foreach (var scoped in _scopedRuns)
        {
            foreach (var userId in scoped.RequestedAudienceUserIds)
            {
                run.ExcludedAudienceUserIds.Add(userId);
            }
        }
    }

    private void ApplyProtection(EntityUid entity, ActiveCinematicRun active)
    {
        var protectedComp = EnsureComp<WH40KCinematicProtectedComponent>(entity);
        if (protectedComp.RunSerial != 0 && protectedComp.RunSerial != active.RunSerial)
            return;

        protectedComp.RunSerial = active.RunSerial;

        if (active.Prototype.GodmodeWhileAudienceLocked && !HasComp<GodmodeComponent>(entity))
        {
            _godmode.EnableGodmode(entity);
            protectedComp.GrantedGodmode = true;
        }

        Dirty(entity, protectedComp);
    }

    private void ReleaseProtection(EntityUid entity, int runSerial)
    {
        if (!TryComp<WH40KCinematicProtectedComponent>(entity, out var protectedComp) ||
            protectedComp.RunSerial != runSerial)
        {
            return;
        }

        if (protectedComp.GrantedGodmode && HasComp<GodmodeComponent>(entity))
            _godmode.DisableGodmode(entity);

        RemComp<WH40KCinematicProtectedComponent>(entity);
    }

    private int GetRunQueueLength(ActiveCinematicRun active)
    {
        return active.IsScoped ? _scopedQueue.Count : _queue.Count;
    }

    private bool HasActiveOrQueuedPrototype(string prototypeId)
    {
        if (string.Equals(_active?.Prototype.ID, prototypeId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (_queue.Any(entry => string.Equals(entry.Prototype.ID, prototypeId, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (_scopedRuns.Any(run => string.Equals(run.Prototype.ID, prototypeId, StringComparison.OrdinalIgnoreCase)))
            return true;

        return _scopedQueue.Any(entry => string.Equals(entry.Prototype.ID, prototypeId, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearScopedQueue()
    {
        _scopedQueue.Clear();
    }

    private void HandleDetachedEntityForRun(ActiveCinematicRun? run, PlayerDetachedEvent ev)
    {
        if (run == null)
            return;

        if (!TryComp<WH40KCinematicLockedComponent>(ev.Entity, out var locked) ||
            locked.RunSerial != run.RunSerial)
        {
            return;
        }

        RemoveAudienceViewSubscription(run, ev.Player);
        RemCompDeferred<WH40KCinematicLockedComponent>(ev.Entity);
        ReleaseProtection(ev.Entity, run.RunSerial);
    }

    private void ResetScopedAudienceRuntimeState()
    {
        foreach (var run in _scopedRuns.ToArray())
        {
            CleanupRun(run);
        }

        _scopedRuns.Clear();
        _scopedQueue.Clear();

        var triggerQuery = EntityQueryEnumerator<WH40KCinematicTriggerComponent>();
        while (triggerQuery.MoveNext(out _, out var trigger))
        {
            trigger.TriggeredThisRound = false;
            trigger.TriggeredUsers.Clear();
        }
    }

    private void OnCinematicTrigger(Entity<WH40KCinematicTriggerComponent> ent, ref TriggerEvent args)
    {
        if (args.User is not { Valid: true } user ||
            !_players.TryGetSessionByEntity(user, out var triggeringSession))
        {
            return;
        }

        if (ent.Comp.OncePerRound && ent.Comp.TriggeredThisRound)
            return;

        if (ent.Comp.OncePerUser && ent.Comp.TriggeredUsers.Contains(triggeringSession.UserId))
            return;

        if (!string.IsNullOrWhiteSpace(ent.Comp.Signal))
        {
            var targetCinematicId = ent.Comp.SignalTargetCinematicId?.Id ?? ent.Comp.CinematicId?.Id;
            var mapId = ent.Comp.SignalScopeCurrentMapOnly
                ? Transform(ent).MapID
                : (MapId?) null;
            var emitted = EmitSignalToMatchingRuns(ent.Comp.Signal, targetCinematicId, mapId);
            if (emitted <= 0)
                return;

            ent.Comp.TriggeredThisRound = true;
            if (ent.Comp.OncePerUser)
                ent.Comp.TriggeredUsers.Add(triggeringSession.UserId);

            args.Handled = true;
            return;
        }

        if (ent.Comp.CinematicId == null)
            return;

        var audience = ResolveTriggerAudience(ent, triggeringSession);
        if (audience.Count == 0)
            return;

        if (!TryQueueForUsers(
                ent.Comp.CinematicId.Value,
                audience,
                out var message,
                triggeringSession.UserId,
                ent.Comp.GhostAudiencePolicy,
                ent.Comp.Priority))
        {
            Log.Warning($"WH40K cinematic trigger on {ToPrettyString(ent.Owner)} failed: {message}");
            return;
        }

        ent.Comp.TriggeredThisRound = true;
        if (ent.Comp.OncePerUser)
            ent.Comp.TriggeredUsers.Add(triggeringSession.UserId);

        args.Handled = true;
    }

    private HashSet<NetUserId> ResolveTriggerAudience(Entity<WH40KCinematicTriggerComponent> ent, ICommonSession triggeringSession)
    {
        var result = new HashSet<NetUserId>();
        var origin = _xform.ToMapCoordinates(Transform(ent).Coordinates);
        var radiusSquared = ent.Comp.Radius * ent.Comp.Radius;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } attached || Deleted(attached))
                continue;

            if (ent.Comp.AudienceMode == WH40KCinematicTriggerAudienceMode.TriggerUser &&
                session.UserId != triggeringSession.UserId)
            {
                continue;
            }

            if (ent.Comp.AudienceMode == WH40KCinematicTriggerAudienceMode.AllPlayersOnMap &&
                Transform(attached).MapID != origin.MapId)
            {
                continue;
            }

            if (ent.Comp.AudienceMode == WH40KCinematicTriggerAudienceMode.Radius)
            {
                var attachedCoords = _xform.ToMapCoordinates(Transform(attached).Coordinates);
                if (attachedCoords.MapId != origin.MapId ||
                    (attachedCoords.Position - origin.Position).LengthSquared() > radiusSquared)
                {
                    continue;
                }
            }

            if (!PassesTriggerAudienceFilters(ent.Comp, attached))
                continue;

            result.Add(session.UserId);
        }

        return result;
    }

    private bool PassesTriggerAudienceFilters(WH40KCinematicTriggerComponent trigger, EntityUid entity)
    {
        if (trigger.NonGhostOnly && HasComp<GhostComponent>(entity))
            return false;

        if (trigger.AliveOnly)
        {
            if (!TryComp<MobStateComponent>(entity, out var mobState) ||
                (!_mobState.IsAlive(entity, mobState) && !_mobState.IsCritical(entity, mobState)))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(trigger.TeamId) &&
            (!TryComp<WH40KTeamMemberComponent>(entity, out var teamMember) ||
             !string.Equals(teamMember.TeamId, trigger.TeamId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (trigger.NpcFactionId is { } factionId &&
            (!TryComp<NpcFactionMemberComponent>(entity, out var factionMember) ||
             !_npcFaction.IsMember((entity, factionMember), factionId)))
        {
            return false;
        }

        if (trigger.JobId is { } jobId)
        {
            if (!_mind.TryGetMind(entity, out var mindId, out _) ||
                !_jobs.MindHasJobWithId(mindId, jobId))
            {
                return false;
            }
        }

        return true;
    }

    private void ExecuteLocalAudienceDamageAction(ActiveCinematicRun active, WH40KCinematicActionDefinition action)
    {
        if (action.Damage == null || action.Damage.Empty)
            return;

        foreach (var entity in GetAudienceEntities(active, action.TeamId))
        {
            _damageable.TryChangeDamage(entity, action.Damage, origin: entity);
        }
    }

    private List<EntityUid> GetAudienceEntities(ActiveCinematicRun active, string? teamIdOverride)
    {
        var result = new List<EntityUid>();

        foreach (var session in _players.Sessions)
        {
            if (!active.AudienceUserIds.Contains(session.UserId))
                continue;

            if (session.AttachedEntity is not { Valid: true } entity || Deleted(entity))
                continue;

            if (!string.IsNullOrWhiteSpace(teamIdOverride) &&
                (!TryComp<WH40KTeamMemberComponent>(entity, out var member) ||
                 !string.Equals(member.TeamId, teamIdOverride, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(entity);
        }

        return result;
    }

    private Filter BuildSoundFilter(
        ActiveCinematicRun active,
        WH40KCinematicActionDefinition action,
        EntityUid? sourceEntity = null,
        MapCoordinates? sourceCoordinates = null)
    {
        switch (action.DeliveryScope)
        {
            case WH40KCinematicSoundDeliveryScope.Audience:
                return BuildAudienceFilter(active, action.TeamId);

            case WH40KCinematicSoundDeliveryScope.Broadcast:
                return Filter.Broadcast();

            case WH40KCinematicSoundDeliveryScope.Pvs:
                if (sourceEntity is { Valid: true } entity && !Deleted(entity))
                    return Filter.Pvs(entity, entityManager: EntityManager);

                if (sourceCoordinates != null)
                {
                    return Filter.Empty().AddPlayersByPvs(
                        sourceCoordinates.Value,
                        entManager: EntityManager,
                        playerMan: _players,
                        cfgMan: _config);
                }

                break;

            case WH40KCinematicSoundDeliveryScope.Radius:
                if (sourceCoordinates != null && action.Radius is > 0f)
                    return Filter.Empty().AddInRange(sourceCoordinates.Value, action.Radius.Value);
                break;

            case WH40KCinematicSoundDeliveryScope.Map:
                if (sourceCoordinates != null)
                    return BuildMapFilter(sourceCoordinates.Value.MapId);

                if (sourceEntity is { Valid: true } mapEntity && !Deleted(mapEntity))
                    return BuildMapFilter(Transform(mapEntity).MapID);
                break;
        }

        return Filter.Empty();
    }

    private Filter BuildMapFilter(MapId mapId)
    {
        var filter = Filter.Empty();
        if (mapId == MapId.Nullspace)
            return filter;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } entity || Deleted(entity))
                continue;

            if (Transform(entity).MapID == mapId)
                filter.AddPlayer(session);
        }

        return filter;
    }

    private readonly record struct QueuedScopedCinematicRequest(
        WH40KCinematicPrototype Prototype,
        TimeSpan QueuedAt,
        HashSet<NetUserId> AudienceUserIds,
        NetUserId? TriggerUserId,
        int Priority,
        WH40KCinematicGhostAudiencePolicy GhostAudiencePolicy);
}
