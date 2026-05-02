using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WH40K.Cinematic;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Cinematic;

public sealed partial class WH40KCinematicSystem
{
    private const string MainSceneContextId = "main";

    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    private void InitializeSceneControlFeatures()
    {
    }

    public IReadOnlyList<string> GetActiveRunDebugLines()
    {
        var runs = EnumerateActiveRuns()
            .OrderBy(run => run.IsScoped)
            .ThenBy(run => run.RunSerial)
            .ToArray();

        if (runs.Length == 0)
            return Array.Empty<string>();

        return runs.Select(run =>
        {
            var stepId = string.IsNullOrWhiteSpace(run.CurrentStep.Id) ? "<not-started>" : run.CurrentStep.Id;
            var contextId = string.IsNullOrWhiteSpace(run.CurrentContextId) ? MainSceneContextId : run.CurrentContextId;
            return
                $"run={run.RunSerial} cinematic='{run.Prototype.ID}' scoped={run.IsScoped} paused={run.ManuallyPaused} restore={run.RestorePhaseActive} step='{stepId}' wait={run.WaitMode} audience={run.AudienceUserIds.Count} context='{contextId}' scenes={run.SceneContexts.Count}.";
        }).ToArray();
    }

    public IReadOnlyList<string> GetActiveRunIds()
    {
        return EnumerateActiveRuns()
            .OrderBy(run => run.IsScoped)
            .ThenBy(run => run.RunSerial)
            .Select(run => run.RunSerial.ToString())
            .ToArray();
    }

    public IReadOnlyList<string> GetActiveRunStepIds(int runSerial)
    {
        if (!TryFindRun(runSerial, out var run))
            return Array.Empty<string>();

        return run.Prototype.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.Id))
            .Select(step => step.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(stepId => stepId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryStopRun(int runSerial, string reason, bool markCompleted, out string message)
    {
        message = string.Empty;
        if (!TryFindRun(runSerial, out var run))
        {
            message = $"No cinematic run with id '{runSerial}' is active.";
            return false;
        }

        AbortRun(run, reason, markCompleted);
        message = $"Stopped cinematic run {run.RunSerial} ('{run.Prototype.ID}').";
        return true;
    }

    public bool TryPauseRun(int runSerial, out string message)
    {
        message = string.Empty;
        if (!TryFindRun(runSerial, out var run))
        {
            message = $"No cinematic run with id '{runSerial}' is active.";
            return false;
        }

        run.ManuallyPaused = true;
        BroadcastActiveState(run);
        message = $"Paused cinematic run {run.RunSerial} ('{run.Prototype.ID}').";
        return true;
    }

    public bool TryResumeRun(int runSerial, out string message)
    {
        message = string.Empty;
        if (!TryFindRun(runSerial, out var run))
        {
            message = $"No cinematic run with id '{runSerial}' is active.";
            return false;
        }

        run.ManuallyPaused = false;
        BroadcastActiveState(run);
        message = $"Resumed cinematic run {run.RunSerial} ('{run.Prototype.ID}').";
        return true;
    }

    public bool TryAdvanceRun(int runSerial, out string message)
    {
        message = string.Empty;
        if (!TryFindRun(runSerial, out var run))
        {
            message = $"No cinematic run with id '{runSerial}' is active.";
            return false;
        }

        run.ManuallyPaused = false;
        AdvanceToNextStep(run, "Advanced by admin/runtime control.");
        message = $"Advanced cinematic run {run.RunSerial} ('{run.Prototype.ID}') to the next step.";
        return true;
    }

    public bool TryJumpRun(int runSerial, string stepId, out string message)
    {
        message = string.Empty;
        if (!TryFindRun(runSerial, out var run))
        {
            message = $"No cinematic run with id '{runSerial}' is active.";
            return false;
        }

        var targetIndex = run.Prototype.Steps.FindIndex(step =>
            string.Equals(step.Id, stepId, StringComparison.OrdinalIgnoreCase));

        if (targetIndex < 0)
        {
            message = $"Run {run.RunSerial} ('{run.Prototype.ID}') does not contain step '{stepId}'.";
            return false;
        }

        run.ManuallyPaused = false;
        run.CurrentStepIndex = targetIndex - 1;
        AdvanceToNextStep(run, $"Jumped to step '{stepId}' by admin/runtime control.");
        message = $"Jumped cinematic run {run.RunSerial} ('{run.Prototype.ID}') to step '{stepId}'.";
        return true;
    }

    public bool TryEmitSignal(int runSerial, string signal, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(signal))
        {
            message = "Signal id must not be empty.";
            return false;
        }

        if (!TryFindRun(runSerial, out var run))
        {
            message = $"No cinematic run with id '{runSerial}' is active.";
            return false;
        }

        EmitSignal(run, signal.Trim());
        message = $"Emitted signal '{signal}' to cinematic run {run.RunSerial} ('{run.Prototype.ID}').";
        return true;
    }

    public int EmitSignalToMatchingRuns(string signal, string? cinematicId = null, MapId? mapId = null)
    {
        if (string.IsNullOrWhiteSpace(signal))
            return 0;

        var emitted = 0;
        foreach (var run in EnumerateActiveRuns())
        {
            if (!string.IsNullOrWhiteSpace(cinematicId) &&
                !string.Equals(run.Prototype.ID, cinematicId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (mapId != null && !RunTouchesMap(run, mapId.Value))
                continue;

            EmitSignal(run, signal.Trim());
            emitted++;
        }

        return emitted;
    }

    private IEnumerable<ActiveCinematicRun> EnumerateActiveRuns()
    {
        if (_active != null)
            yield return _active;

        foreach (var run in _scopedRuns)
        {
            yield return run;
        }
    }

    private bool TryFindRun(int runSerial, out ActiveCinematicRun run)
    {
        run = default!;
        if (_active != null && _active.RunSerial == runSerial)
        {
            run = _active;
            return true;
        }

        foreach (var scoped in _scopedRuns)
        {
            if (scoped.RunSerial != runSerial)
                continue;

            run = scoped;
            return true;
        }

        return false;
    }

    private void EnsureRunMainContext(ActiveCinematicRun run)
    {
        if (!run.SceneContexts.TryGetValue(MainSceneContextId, out var context))
        {
            run.SceneContexts[MainSceneContextId] = new SceneContextRuntime(
                MainSceneContextId,
                DetermineRunRootMapId(run) ?? MapId.Nullspace,
                null,
                false,
                WH40KCinematicSceneCleanupPolicy.KeepAlive,
                WH40KCinematicSceneTransferMode.CameraOnly,
                WH40KCinematicSceneReturnPolicy.OriginalPosition,
                null,
                null,
                false);
            return;
        }

        if (context.MapId != MapId.Nullspace)
            return;

        var rootMapId = DetermineRunRootMapId(run);
        if (rootMapId == null)
            return;

        context.MapId = rootMapId.Value;
    }

    private MapId? DetermineRunRootMapId(ActiveCinematicRun run)
    {
        if (run.TriggerUserId != null &&
            _players.TryGetSessionById(run.TriggerUserId.Value, out var triggerSession) &&
            triggerSession.AttachedEntity is { Valid: true } triggerEntity &&
            !Deleted(triggerEntity))
        {
            return Transform(triggerEntity).MapID;
        }

        foreach (var userId in run.RequestedAudienceUserIds)
        {
            if (!_players.TryGetSessionById(userId, out var session) ||
                session.AttachedEntity is not { Valid: true } entity ||
                Deleted(entity))
            {
                continue;
            }

            return Transform(entity).MapID;
        }

        foreach (var userId in run.AudienceUserIds)
        {
            if (!_players.TryGetSessionById(userId, out var session) ||
                session.AttachedEntity is not { Valid: true } entity ||
                Deleted(entity))
            {
                continue;
            }

            return Transform(entity).MapID;
        }

        return null;
    }

    private string ResolveEffectiveContextId(ActiveCinematicRun run, string? explicitContextId)
    {
        if (!string.IsNullOrWhiteSpace(explicitContextId))
            return explicitContextId.Trim();

        return string.IsNullOrWhiteSpace(run.CurrentContextId)
            ? MainSceneContextId
            : run.CurrentContextId;
    }

    private bool TryGetContext(ActiveCinematicRun run, string? explicitContextId, out SceneContextRuntime context)
    {
        EnsureRunMainContext(run);
        var contextId = ResolveEffectiveContextId(run, explicitContextId);
        if (run.SceneContexts.TryGetValue(contextId, out var resolved))
        {
            context = resolved;
            return true;
        }

        context = default!;
        return false;
    }

    private bool TryGetContextMapId(ActiveCinematicRun run, string? explicitContextId, out MapId mapId)
    {
        mapId = MapId.Nullspace;
        if (!TryGetContext(run, explicitContextId, out var context))
            return false;

        if (context.MapId == MapId.Nullspace)
            return false;

        mapId = context.MapId;
        return true;
    }

    private bool RunTouchesMap(ActiveCinematicRun run, MapId mapId)
    {
        EnsureRunMainContext(run);
        return run.SceneContexts.Values.Any(context => context.MapId == mapId);
    }

    private bool DoesEntityMatchContext(EntityUid uid, ActiveCinematicRun run, string? explicitContextId)
    {
        if (!TryGetContextMapId(run, explicitContextId, out var mapId))
            return true;

        return Transform(uid).MapID == mapId;
    }

    private bool ShouldFallbackToAnyContext(ActiveCinematicRun? run, string? explicitContextId)
    {
        return run != null && string.IsNullOrWhiteSpace(explicitContextId);
    }

    private void TrackEntitiesInSet(ActiveCinematicRun run, string? entitySetId, IEnumerable<EntityUid> entities)
    {
        if (string.IsNullOrWhiteSpace(entitySetId))
            return;

        var key = entitySetId.Trim();
        if (!run.EntitySets.TryGetValue(key, out var set))
        {
            set = new HashSet<EntityUid>();
            run.EntitySets[key] = set;
        }

        foreach (var entity in entities)
        {
            if (entity.Valid && !Deleted(entity))
                set.Add(entity);
        }
    }

    private void ClearEntitySet(ActiveCinematicRun run, string? entitySetId)
    {
        if (string.IsNullOrWhiteSpace(entitySetId))
            return;

        run.EntitySets.Remove(entitySetId.Trim());
    }

    private void PruneEntitySets(ActiveCinematicRun run)
    {
        foreach (var set in run.EntitySets.Values)
        {
            set.RemoveWhere(entity => !entity.Valid || Deleted(entity));
        }
    }

    private bool AreEntitySetConditionsSatisfied(ActiveCinematicRun run, WH40KCinematicStepDefinition step)
    {
        if (step.WaitEntitySets.Count == 0)
            return false;

        PruneEntitySets(run);
        var checks = new List<bool>();
        foreach (var entitySetId in step.WaitEntitySets)
        {
            if (string.IsNullOrWhiteSpace(entitySetId))
                continue;

            var key = entitySetId.Trim();
            var empty = !run.EntitySets.TryGetValue(key, out var set) || set.Count == 0;
            checks.Add(empty);
        }

        if (checks.Count == 0)
            return false;

        return step.WaitConditionMode == WH40KCinematicWaitConditionAggregationMode.Any
            ? checks.Any(static result => result)
            : checks.All(static result => result);
    }

    private bool AreSignalConditionsSatisfied(ActiveCinematicRun run, WH40KCinematicStepDefinition step, bool consumeSignals)
    {
        if (step.WaitSignals.Count == 0)
            return false;

        var checks = new List<bool>();
        foreach (var signal in step.WaitSignals)
        {
            if (string.IsNullOrWhiteSpace(signal))
                continue;

            checks.Add(run.PendingSignals.ContainsKey(signal.Trim()));
        }

        if (checks.Count == 0)
            return false;

        var satisfied = step.WaitConditionMode == WH40KCinematicWaitConditionAggregationMode.Any
            ? checks.Any(static result => result)
            : checks.All(static result => result);

        if (!satisfied || !consumeSignals)
            return satisfied;

        if (step.WaitConditionMode == WH40KCinematicWaitConditionAggregationMode.Any)
        {
            foreach (var signal in step.WaitSignals)
            {
                if (string.IsNullOrWhiteSpace(signal))
                    continue;

                var key = signal.Trim();
                if (!run.PendingSignals.ContainsKey(key))
                    continue;

                ConsumePendingSignal(run, key);
                break;
            }

            return true;
        }

        foreach (var signal in step.WaitSignals)
        {
            if (!string.IsNullOrWhiteSpace(signal))
                ConsumePendingSignal(run, signal.Trim());
        }

        return true;
    }

    private void ConsumePendingSignal(ActiveCinematicRun run, string signal)
    {
        if (!run.PendingSignals.TryGetValue(signal, out var count))
            return;

        count--;
        if (count <= 0)
            run.PendingSignals.Remove(signal);
        else
            run.PendingSignals[signal] = count;
    }

    private void EmitSignal(ActiveCinematicRun run, string signal)
    {
        if (string.IsNullOrWhiteSpace(signal))
            return;

        var key = signal.Trim();
        run.PendingSignals[key] = run.PendingSignals.GetValueOrDefault(key) + 1;

        if (run.RestorePhaseActive)
            return;

        switch (run.WaitMode)
        {
            case WH40KCinematicWaitMode.AwaitSignal:
                if (AreSignalConditionsSatisfied(run, run.CurrentStep, consumeSignals: true))
                    AdvanceToNextStep(run, $"Signal '{key}' received.");
                else
                    BroadcastActiveState(run);
                break;

            case WH40KCinematicWaitMode.AwaitSignalOrTimeout:
                if (AreSignalConditionsSatisfied(run, run.CurrentStep, consumeSignals: true))
                    AdvanceToNextStep(run, $"Signal '{key}' received before timeout.");
                else
                    BroadcastActiveState(run);
                break;

            default:
                BroadcastActiveState(run);
                break;
        }
    }

    private bool TryLoadSceneMapAction(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        out string failureReason)
    {
        failureReason = string.Empty;
        var contextId = ResolveEffectiveContextId(run, action.ContextId);
        if (string.IsNullOrWhiteSpace(contextId))
        {
            failureReason = "LoadSceneMap requires a non-empty contextId.";
            return false;
        }

        if (action.SceneMapPath == null == (action.SceneGridPath == null))
        {
            failureReason = $"LoadSceneMap '{contextId}' requires exactly one of sceneMapPath or sceneGridPath.";
            return false;
        }

        if (run.SceneContexts.TryGetValue(contextId, out var existing) && existing.IsRuntimeScene)
        {
            if (action.SwitchToContext)
                run.CurrentContextId = contextId;

            return true;
        }

        EntityUid? mapUid = null;
        MapId mapId;
        var options = DeserializationOptions.Default with { InitializeMaps = true };
        if (action.SceneMapPath != null)
        {
            if (!_mapLoader.TryLoadMap(action.SceneMapPath.Value, out var loadedMap, out _, options))
            {
                failureReason = $"Failed to load scene map '{action.SceneMapPath.Value}' for context '{contextId}'.";
                return false;
            }

            mapUid = loadedMap!.Value.Owner;
            mapId = loadedMap.Value.Comp.MapId;
        }
        else
        {
            mapUid = _map.CreateMap(out mapId);
            if (!_mapLoader.TryLoadGrid(mapId, action.SceneGridPath!.Value, out _, options))
            {
                QueueDel(mapUid.Value);
                failureReason = $"Failed to load scene grid '{action.SceneGridPath.Value}' for context '{contextId}'.";
                return false;
            }
        }

        _metaData.SetEntityName(mapUid.Value, $"WH40K-Cinematic-{run.RunSerial}-{contextId}");

        var context = new SceneContextRuntime(
            contextId,
            mapId,
            mapUid,
            true,
            action.SceneCleanupPolicy,
            action.SceneTransferMode,
            action.SceneReturnPolicy,
            action.EntryAnchorId,
            action.ReturnAnchorId,
            action.PauseSourceMap);
        run.SceneContexts[contextId] = context;

        if (context.TransferMode == WH40KCinematicSceneTransferMode.TeleportParticipants)
        {
            if (!TryTeleportAudienceToSceneContext(run, context, out failureReason))
            {
                DestroySceneContextMap(context);
                run.SceneContexts.Remove(contextId);
                return false;
            }
        }

        if (context.PauseSourceMap)
            PauseSourceMapsForContext(run);

        if (action.SwitchToContext)
            run.CurrentContextId = contextId;

        return true;
    }

    private bool TryUnloadSceneMapAction(
        ActiveCinematicRun run,
        WH40KCinematicActionDefinition action,
        out string failureReason)
    {
        failureReason = string.Empty;
        var contextId = ResolveEffectiveContextId(run, action.ContextId);
        if (!run.SceneContexts.TryGetValue(contextId, out var context) || !context.IsRuntimeScene)
        {
            failureReason = $"No runtime scene context '{contextId}' is currently loaded for run {run.RunSerial}.";
            return false;
        }

        ReturnAudienceFromSceneContext(run, context);
        CleanupSceneContextRuntime(run, context);
        run.SceneContexts.Remove(contextId);
        if (string.Equals(run.CurrentContextId, contextId, StringComparison.OrdinalIgnoreCase))
            run.CurrentContextId = MainSceneContextId;

        return true;
    }

    private bool TrySwitchContextAction(ActiveCinematicRun run, string? contextId, out string failureReason)
    {
        failureReason = string.Empty;
        var resolved = ResolveEffectiveContextId(run, contextId);
        EnsureRunMainContext(run);
        if (!run.SceneContexts.ContainsKey(resolved))
        {
            failureReason = $"Scene context '{resolved}' is not loaded for run {run.RunSerial}.";
            return false;
        }

        run.CurrentContextId = resolved;
        return true;
    }

    private bool TryTeleportAudienceToSceneContext(
        ActiveCinematicRun run,
        SceneContextRuntime context,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(context.EntryAnchorId))
        {
            failureReason = $"Scene context '{context.ContextId}' uses TeleportParticipants but has no entryAnchorId.";
            return false;
        }

        if (!TryResolveAnchorPose(run, context.EntryAnchorId, context.ContextId, out var entryCoordinates, out var entryRotation))
        {
            failureReason = $"Scene context '{context.ContextId}' could not resolve entry anchor '{context.EntryAnchorId}'.";
            return false;
        }

        foreach (var session in _players.Sessions)
        {
            if (!run.AudienceUserIds.Contains(session.UserId) ||
                session.AttachedEntity is not { Valid: true } entity ||
                Deleted(entity))
            {
                continue;
            }

            if (!run.ParticipantReturns.ContainsKey(session.UserId))
            {
                run.ParticipantReturns[session.UserId] = new ParticipantReturnState(
                    Transform(entity).Coordinates,
                    Transform(entity).LocalRotation);
            }

            _xform.SetCoordinates(entity, entryCoordinates);
            _xform.SetLocalRotation(entity, entryRotation);
            _xform.AttachToGridOrMap(entity);
            context.TeleportedAudienceUserIds.Add(session.UserId);
        }

        context.ParticipantsTeleported = true;
        RefreshScenePauseState(run);
        return true;
    }

    private void ReturnAudienceFromSceneContext(ActiveCinematicRun run, SceneContextRuntime context)
    {
        if (!context.ParticipantsTeleported)
            return;

        foreach (var userId in context.TeleportedAudienceUserIds.ToArray())
        {
            if (!_players.TryGetSessionById(userId, out var session) ||
                session.AttachedEntity is not { Valid: true } entity ||
                Deleted(entity))
            {
                continue;
            }

            switch (context.ReturnPolicy)
            {
                case WH40KCinematicSceneReturnPolicy.OriginalPosition:
                    if (run.ParticipantReturns.TryGetValue(userId, out var original))
                    {
                        _xform.SetCoordinates(entity, original.Coordinates);
                        _xform.SetLocalRotation(entity, original.Rotation);
                        _xform.AttachToGridOrMap(entity);
                    }
                    else
                    {
                        Log.Warning($"WH40K cinematic run {run.RunSerial} could not restore original position for user '{userId}' because no return state was recorded.");
                    }
                    break;

                case WH40KCinematicSceneReturnPolicy.ReturnAnchor:
                    if (!string.IsNullOrWhiteSpace(context.ReturnAnchorId) &&
                        TryResolveAnchorPose(run, context.ReturnAnchorId, MainSceneContextId, out var returnCoordinates, out var returnRotation))
                    {
                        _xform.SetCoordinates(entity, returnCoordinates);
                        _xform.SetLocalRotation(entity, returnRotation);
                        _xform.AttachToGridOrMap(entity);
                    }
                    else if (run.ParticipantReturns.TryGetValue(userId, out var fallbackOriginal))
                    {
                        Log.Warning($"WH40K cinematic run {run.RunSerial} could not resolve return anchor '{context.ReturnAnchorId}' for context '{context.ContextId}'. Falling back to the participant's original position.");
                        _xform.SetCoordinates(entity, fallbackOriginal.Coordinates);
                        _xform.SetLocalRotation(entity, fallbackOriginal.Rotation);
                        _xform.AttachToGridOrMap(entity);
                    }
                    else
                    {
                        Log.Warning($"WH40K cinematic run {run.RunSerial} could not resolve return anchor '{context.ReturnAnchorId}' for context '{context.ContextId}', and no original return state was recorded for user '{userId}'.");
                    }
                    break;
            }
        }

        context.TeleportedAudienceUserIds.Clear();
        context.ParticipantsTeleported = false;
        RefreshScenePauseState(run);
    }

    private bool TryResolveAnchorPose(
        ActiveCinematicRun run,
        string anchorId,
        string? contextId,
        out EntityCoordinates coordinates,
        out Angle rotation)
    {
        coordinates = default;
        rotation = Angle.Zero;

        var anchors = ResolveSpawnAnchors(run, anchorId, contextId);
        if (anchors.Count == 0)
            anchors = ResolveSoundAnchors(run, anchorId, contextId);
        if (anchors.Count == 0)
            anchors = ResolveNpcAnchors(run, anchorId, contextId);

        if (anchors.Count == 0)
            return false;

        var anchorUid = anchors[0];
        var xform = Transform(anchorUid);
        coordinates = xform.Coordinates;
        rotation = TryComp<WH40KCinematicNpcAnchorComponent>(anchorUid, out var npcAnchor)
            ? Angle.FromDegrees(npcAnchor.RotationDegrees)
            : xform.LocalRotation;
        return true;
    }

    private void RefreshScenePauseState(ActiveCinematicRun run)
    {
        var hasTeleportedAudience = run.SceneContexts.Values.Any(context => context.ParticipantsTeleported);
        var keepsSourceMapsPaused = run.SceneContexts.Values.Any(context =>
            context.IsRuntimeScene &&
            context.ParticipantsTeleported &&
            context.PauseSourceMap);

        run.SuppressAudienceMapPause = hasTeleportedAudience;

        if (!keepsSourceMapsPaused)
            ReleasePausedMaps(run);

        if (!run.AudienceLocked || run.SuppressAudienceMapPause)
            return;

        foreach (var session in _players.Sessions)
        {
            if (!run.AudienceUserIds.Contains(session.UserId) ||
                session.AttachedEntity is not { Valid: true } entity ||
                Deleted(entity))
            {
                continue;
            }

            EnsurePausedMapForEntity(run, entity);
        }
    }

    private void PauseSourceMapsForContext(ActiveCinematicRun run)
    {
        foreach (var original in run.ParticipantReturns.Values)
        {
            var mapId = _xform.GetMapId(original.Coordinates);
            PauseRunMap(run, mapId);
        }
    }

    private void PauseRunMap(ActiveCinematicRun run, MapId mapId)
    {
        if (mapId == MapId.Nullspace || run.PausedMaps.ContainsKey(mapId))
            return;

        var wasPaused = _map.IsPaused(mapId);
        run.PausedMaps[mapId] = wasPaused;
        if (!wasPaused)
            _map.SetPaused(mapId, true);
    }

    private void CleanupSceneContextRuntime(ActiveCinematicRun run, SceneContextRuntime context)
    {
        if (!context.IsRuntimeScene || context.CleanupPolicy != WH40KCinematicSceneCleanupPolicy.DestroyOnFinish)
            return;

        DestroySceneContextMap(context);
    }

    private void DestroySceneContextMap(SceneContextRuntime context)
    {
        if (context.MapId != MapId.Nullspace && _map.MapExists(context.MapId))
            _map.QueueDeleteMap(context.MapId);
    }

    private void CleanupSceneContexts(ActiveCinematicRun run)
    {
        foreach (var context in run.SceneContexts.Values.ToArray())
        {
            ReturnAudienceFromSceneContext(run, context);
            CleanupSceneContextRuntime(run, context);
        }

        run.SceneContexts.Clear();
        run.ParticipantReturns.Clear();
        run.PendingSignals.Clear();
        run.EntitySets.Clear();
        run.CurrentContextId = MainSceneContextId;
        run.SuppressAudienceMapPause = false;
    }

    private sealed class SceneContextRuntime
    {
        public string ContextId { get; }
        public MapId MapId;
        public EntityUid? MapUid;
        public bool IsRuntimeScene;
        public WH40KCinematicSceneCleanupPolicy CleanupPolicy;
        public WH40KCinematicSceneTransferMode TransferMode;
        public WH40KCinematicSceneReturnPolicy ReturnPolicy;
        public string? EntryAnchorId;
        public string? ReturnAnchorId;
        public bool PauseSourceMap;
        public bool ParticipantsTeleported;
        public HashSet<NetUserId> TeleportedAudienceUserIds { get; } = new();

        public SceneContextRuntime(
            string contextId,
            MapId mapId,
            EntityUid? mapUid,
            bool isRuntimeScene,
            WH40KCinematicSceneCleanupPolicy cleanupPolicy,
            WH40KCinematicSceneTransferMode transferMode,
            WH40KCinematicSceneReturnPolicy returnPolicy,
            string? entryAnchorId,
            string? returnAnchorId,
            bool pauseSourceMap)
        {
            ContextId = contextId;
            MapId = mapId;
            MapUid = mapUid;
            IsRuntimeScene = isRuntimeScene;
            CleanupPolicy = cleanupPolicy;
            TransferMode = transferMode;
            ReturnPolicy = returnPolicy;
            EntryAnchorId = entryAnchorId;
            ReturnAnchorId = returnAnchorId;
            PauseSourceMap = pauseSourceMap;
        }
    }

    private readonly record struct ParticipantReturnState(EntityCoordinates Coordinates, Angle Rotation);
}
