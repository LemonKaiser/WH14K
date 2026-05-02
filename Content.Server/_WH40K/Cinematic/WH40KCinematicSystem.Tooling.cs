using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WH40K.Cinematic;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Server._WH40K.Cinematic;

public sealed partial class WH40KCinematicSystem
{
    private const string ShotPreviewMarkerPrototype = "WH40KCinematicShotPreviewMarker";
    private const string AnchorPreviewMarkerPrototype = "WH40KCinematicAnchorPreviewMarker";

    public IReadOnlyList<string> GetKnownShotStepIds(string cinematicId)
    {
        if (!_prototypes.TryIndex<WH40KCinematicPrototype>(cinematicId, out var prototype))
            return Array.Empty<string>();

        return prototype.Steps
            .Where(step => step.Type == WH40KCinematicStepType.Shot && !string.IsNullOrWhiteSpace(step.Id))
            .Select(step => step.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(stepId => stepId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetKnownCameraPointIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var query = EntityQueryEnumerator<WH40KCinematicCameraPointComponent>();
        while (query.MoveNext(out _, out var point))
        {
            if (!string.IsNullOrWhiteSpace(point.PointId))
                ids.Add(point.PointId);
        }

        return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> GetKnownAnchorIds(WH40KCinematicPreviewAnchorMode mode = WH40KCinematicPreviewAnchorMode.Any)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in ResolvePreviewAnchors(null, mode))
        {
            if (!string.IsNullOrWhiteSpace(anchor.AnchorId))
                ids.Add(anchor.AnchorId);
        }

        return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool TryDescribePrototype(string cinematicId, out string message)
    {
        message = string.Empty;
        if (!_prototypes.TryIndex<WH40KCinematicPrototype>(cinematicId, out var prototype))
        {
            message = $"Unknown cinematic prototype '{cinematicId}'.";
            return false;
        }

        var debug = BuildPrototypeDebugInfo(prototype);
        message =
            $"Cinematic '{prototype.ID}': steps={debug.StepCount}, shots={debug.ShotCount}, actions={debug.ActionCount}, freezeMode={prototype.WorldFreezeMode}, restoreDelay={debug.RestoreDelaySeconds:0.##}." +
            $" CameraPoints=[{JoinIds(debug.CameraPointIds)}], SoundAnchors=[{JoinIds(debug.SoundAnchorIds)}], SpawnAnchors=[{JoinIds(debug.SpawnAnchorIds)}], ActionAnchors=[{JoinIds(debug.ActionAnchorIds)}], Flows=[{JoinIds(debug.FlowIds)}].";
        return true;
    }

    public bool TryValidateLoadedPrototype(string cinematicId, out string message)
    {
        message = string.Empty;
        if (!_prototypes.TryIndex<WH40KCinematicPrototype>(cinematicId, out var prototype))
        {
            message = $"Unknown cinematic prototype '{cinematicId}'.";
            return false;
        }

        var syntaxErrors = ValidatePrototype(prototype);
        if (syntaxErrors.Count > 0)
        {
            message = $"Cinematic '{prototype.ID}' is invalid before loaded validation: {string.Join("; ", syntaxErrors)}";
            return false;
        }

        var loaded = ValidateLoadedPrototypeInternal(prototype);
        if (loaded.Errors.Count > 0)
        {
            message = $"Loaded validation for cinematic '{prototype.ID}' failed: {string.Join("; ", loaded.Errors)}";
            if (loaded.Warnings.Count > 0)
                message += $" Warnings: {string.Join("; ", loaded.Warnings)}";

            return false;
        }

        message = $"Loaded validation for cinematic '{prototype.ID}' passed.";
        if (loaded.Warnings.Count > 0)
            message += $" Warnings: {string.Join("; ", loaded.Warnings)}";

        return true;
    }

    public bool TryPreviewShot(string cinematicId, string stepId, out string message, float lifetimeSeconds = 8f)
    {
        message = string.Empty;
        if (!_prototypes.TryIndex<WH40KCinematicPrototype>(cinematicId, out var prototype))
        {
            message = $"Unknown cinematic prototype '{cinematicId}'.";
            return false;
        }

        var step = prototype.Steps.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, stepId, StringComparison.OrdinalIgnoreCase));

        if (step == null)
        {
            message = $"Cinematic '{prototype.ID}' does not contain step '{stepId}'.";
            return false;
        }

        if (step.Type != WH40KCinematicStepType.Shot)
        {
            message = $"Step '{step.Id}' in cinematic '{prototype.ID}' is not a shot step.";
            return false;
        }

        if (!TryResolveShot(prototype, step, out var shot))
        {
            message = step.OptionalCameraPoint
                ? $"Shot step '{step.Id}' is optional and its camera point '{step.CameraPointId}' is not currently loaded."
                : $"Missing required camera point '{step.CameraPointId}' for shot step '{step.Id}'.";
            return false;
        }

        SpawnPreviewMarker(ShotPreviewMarkerPrototype, shot.Coordinates, lifetimeSeconds);
        message =
            $"Previewed shot '{step.Id}' from cinematic '{prototype.ID}' at cameraPoint '{shot.CameraPointId}' for {Math.Max(0.1f, lifetimeSeconds):0.##} second(s)." +
            $" zoom={shot.Zoom:0.##}, rotation={shot.RotationDegrees:0.##}, transition={shot.TransitionMode}, shake={shot.ShakeIntensity:0.##}.";
        return true;
    }

    public bool TryPreviewAnchor(
        string anchorId,
        WH40KCinematicPreviewAnchorMode mode,
        out string message,
        float lifetimeSeconds = 8f)
    {
        message = string.Empty;
        var anchors = ResolvePreviewAnchors(anchorId, mode);
        if (anchors.Count == 0)
        {
            message = $"No cinematic anchors matched id '{anchorId}' for preview mode '{mode}'.";
            return false;
        }

        foreach (var anchor in anchors)
        {
            SpawnPreviewMarker(AnchorPreviewMarkerPrototype, anchor.Coordinates, lifetimeSeconds);
        }

        var groupedKinds = anchors.Select(anchor => anchor.SourceKind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase);

        message =
            $"Previewed anchor '{anchorId}' in mode '{mode}' with {anchors.Count} match(es) for {Math.Max(0.1f, lifetimeSeconds):0.##} second(s)." +
            $" kinds=[{string.Join(", ", groupedKinds)}].";
        return true;
    }

    public bool TryPreviewCinematic(string cinematicId, out string message, float lifetimeSeconds = 8f)
    {
        message = string.Empty;
        if (!_prototypes.TryIndex<WH40KCinematicPrototype>(cinematicId, out var prototype))
        {
            message = $"Unknown cinematic prototype '{cinematicId}'.";
            return false;
        }

        var syntaxErrors = ValidatePrototype(prototype);
        if (syntaxErrors.Count > 0)
        {
            message = $"Cinematic '{prototype.ID}' is invalid before preview: {string.Join("; ", syntaxErrors)}";
            return false;
        }

        var loaded = ValidateLoadedPrototypeInternal(prototype);
        if (loaded.Errors.Count > 0)
        {
            message = $"Cannot preview cinematic '{prototype.ID}' because loaded validation failed: {string.Join("; ", loaded.Errors)}";
            return false;
        }

        var shotCount = 0;
        var anchorCount = 0;
        var previewedFlowIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in prototype.Steps)
        {
            if (step.Type == WH40KCinematicStepType.Shot && TryResolveShot(prototype, step, out var shot))
            {
                SpawnPreviewMarker(ShotPreviewMarkerPrototype, shot.Coordinates, lifetimeSeconds);
                shotCount++;
            }

            foreach (var action in step.Actions)
            {
                anchorCount += PreviewActionReferences(action, lifetimeSeconds, previewedFlowIds);
            }
        }

        var debug = BuildPrototypeDebugInfo(prototype);
        message =
            $"Previewed cinematic '{prototype.ID}' for {Math.Max(0.1f, lifetimeSeconds):0.##} second(s)." +
            $" shots={shotCount}, anchorRefs={anchorCount}, flows={previewedFlowIds.Count}, steps={debug.StepCount}, actions={debug.ActionCount}.";

        if (loaded.Warnings.Count > 0)
            message += $" Warnings: {string.Join("; ", loaded.Warnings)}";

        return true;
    }

    private int PreviewResolvedAnchors(string? anchorId, WH40KCinematicPreviewAnchorMode mode, float lifetimeSeconds)
    {
        var previewed = 0;
        foreach (var anchor in ResolvePreviewAnchors(anchorId, mode))
        {
            SpawnPreviewMarker(AnchorPreviewMarkerPrototype, anchor.Coordinates, lifetimeSeconds);
            previewed++;
        }

        return previewed;
    }

    private int PreviewActionReferences(
        WH40KCinematicActionDefinition action,
        float lifetimeSeconds,
        HashSet<string> previewedFlowIds)
    {
        var anchorCount = 0;

        switch (action.Type)
        {
            case WH40KCinematicActionType.PlayAnchorSound:
                anchorCount += PreviewResolvedAnchors(action.AnchorId, WH40KCinematicPreviewAnchorMode.Sound, lifetimeSeconds);
                break;

            case WH40KCinematicActionType.SpawnAtAnchor:
                anchorCount += PreviewResolvedAnchors(action.AnchorId, WH40KCinematicPreviewAnchorMode.Spawn, lifetimeSeconds);
                break;

            case WH40KCinematicActionType.SpawnNpc:
            case WH40KCinematicActionType.NpcMoveToAnchor:
            case WH40KCinematicActionType.NpcPathToAnchor:
            case WH40KCinematicActionType.BindExistingEntityAsNpc:
                anchorCount += PreviewResolvedAnchors(action.AnchorId, WH40KCinematicPreviewAnchorMode.Npc, lifetimeSeconds);
                break;

            case WH40KCinematicActionType.NpcPathThroughAnchors:
                foreach (var anchorId in action.AnchorIds)
                {
                    anchorCount += PreviewResolvedAnchors(anchorId, WH40KCinematicPreviewAnchorMode.Npc, lifetimeSeconds);
                }
                break;

            case WH40KCinematicActionType.NpcUseEntity:
            case WH40KCinematicActionType.NpcFaceDirection:
            case WH40KCinematicActionType.NpcAttackDirection:
                if (!string.IsNullOrWhiteSpace(action.AnchorId))
                    anchorCount += PreviewResolvedAnchors(action.AnchorId, WH40KCinematicPreviewAnchorMode.Any, lifetimeSeconds);
                break;

            case WH40KCinematicActionType.RunLavaFlow:
                if (!string.IsNullOrWhiteSpace(action.FlowId) && previewedFlowIds.Add(action.FlowId))
                    TryPreviewLavaFlow(action.FlowId, out _, lifetimeSeconds);
                break;

            case WH40KCinematicActionType.PlayActorTrack:
                if (action.TrackId != null &&
                    _prototypes.TryIndex<WH40KCinematicActorTrackPrototype>(action.TrackId.Value, out var track) &&
                    TryResolveActorTrackSegments(track, action.TrackSegmentId, out var segments))
                {
                    foreach (var segment in segments)
                    {
                        foreach (var entry in segment.Entries)
                        {
                            anchorCount += PreviewActionReferences(entry.Action, lifetimeSeconds, previewedFlowIds);
                        }
                    }
                }
                break;
        }

        return anchorCount;
    }

    private LoadedValidationResult ValidateLoadedPrototypeInternal(WH40KCinematicPrototype prototype)
    {
        var result = new LoadedValidationResult();

        for (var i = 0; i < prototype.Steps.Count; i++)
        {
            var step = prototype.Steps[i];
            var stepLabel = $"step[{i}] '{step.Id}'";

            if (step.Type == WH40KCinematicStepType.Shot)
            {
                var matches = ResolveCameraPoints(step.CameraPointId);
                if (matches.Count == 0)
                {
                    if (IsDeferredContextReference(step.ContextId))
                    {
                        result.Warnings.Add(
                            $"{stepLabel}: camera point '{step.CameraPointId}' for deferred scene context '{step.ContextId}' is not currently loaded and will resolve only after that context is loaded.");
                    }
                    else if (step.OptionalCameraPoint)
                    {
                        result.Warnings.Add($"{stepLabel}: optional camera point '{step.CameraPointId}' is not currently loaded.");
                    }
                    else
                    {
                        result.Errors.Add($"{stepLabel}: required camera point '{step.CameraPointId}' is not currently loaded.");
                    }
                }
                else if (matches.Count > 1)
                {
                    result.Warnings.Add(
                        $"{stepLabel}: camera point id '{step.CameraPointId}' has {matches.Count} loaded matches and runtime will use the first one found.");
                }
            }

            for (var actionIndex = 0; actionIndex < step.Actions.Count; actionIndex++)
            {
                var action = step.Actions[actionIndex];
                var actionLabel = $"{stepLabel}.action[{actionIndex}] ({action.Type})";
                ValidateLoadedActionReferences(actionLabel, action, result, inheritedContextId: null);
            }
        }

        return result;
    }

    private void ValidateLoadedActionReferences(
        string actionLabel,
        WH40KCinematicActionDefinition action,
        LoadedValidationResult result,
        string? inheritedContextId)
    {
        var effectiveContextId = string.IsNullOrWhiteSpace(action.ContextId) ? inheritedContextId : action.ContextId;

        switch (action.Type)
        {
            case WH40KCinematicActionType.PlayAnchorSound:
                ValidateAnchorReference(actionLabel, action.AnchorId, action.OptionalAnchor, WH40KCinematicPreviewAnchorMode.Sound, result, effectiveContextId);
                break;

            case WH40KCinematicActionType.SpawnAtAnchor:
                ValidateAnchorReference(actionLabel, action.AnchorId, action.OptionalAnchor, WH40KCinematicPreviewAnchorMode.Spawn, result, effectiveContextId);
                break;

            case WH40KCinematicActionType.SpawnNpc:
            case WH40KCinematicActionType.NpcMoveToAnchor:
            case WH40KCinematicActionType.NpcPathToAnchor:
            case WH40KCinematicActionType.BindExistingEntityAsNpc:
                ValidateAnchorReference(actionLabel, action.AnchorId, action.OptionalAnchor, WH40KCinematicPreviewAnchorMode.Npc, result, effectiveContextId);
                break;

            case WH40KCinematicActionType.NpcPathThroughAnchors:
                foreach (var anchorId in action.AnchorIds)
                {
                    ValidateAnchorReference(actionLabel, anchorId, action.OptionalAnchor, WH40KCinematicPreviewAnchorMode.Npc, result, effectiveContextId);
                }
                break;

            case WH40KCinematicActionType.NpcUseEntity:
            case WH40KCinematicActionType.NpcFaceDirection:
            case WH40KCinematicActionType.NpcAttackDirection:
                if (!string.IsNullOrWhiteSpace(action.AnchorId))
                    ValidateAnchorReference(actionLabel, action.AnchorId, action.OptionalAnchor, WH40KCinematicPreviewAnchorMode.Any, result, effectiveContextId);
                break;

            case WH40KCinematicActionType.RunLavaFlow:
            {
                var flowErrors = ValidateLavaFlow(action.FlowId ?? string.Empty);
                if (flowErrors.Count == 0)
                    break;

                if (IsDeferredContextReference(effectiveContextId))
                {
                    result.Warnings.Add(
                        $"{actionLabel}: lava flow '{action.FlowId}' for deferred scene context '{effectiveContextId}' is not currently loaded and will resolve only after that context is loaded.");
                }
                else if (action.OptionalAnchor)
                {
                    result.Warnings.Add($"{actionLabel}: optional lava flow '{action.FlowId}' is not fully valid on the loaded map: {string.Join("; ", flowErrors)}");
                }
                else
                {
                    result.Errors.Add($"{actionLabel}: lava flow '{action.FlowId}' is not valid on the loaded map: {string.Join("; ", flowErrors)}");
                }

                break;
            }

            case WH40KCinematicActionType.PlayActorTrack:
                if (action.TrackId == null ||
                    !_prototypes.TryIndex<WH40KCinematicActorTrackPrototype>(action.TrackId.Value, out var track))
                {
                    break;
                }

                if (!TryResolveActorTrackSegments(track, action.TrackSegmentId, out var segments))
                {
                    result.Errors.Add($"{actionLabel}: actor track '{track.ID}' does not contain segment '{action.TrackSegmentId}'.");
                    break;
                }

                foreach (var segment in segments)
                {
                    for (var entryIndex = 0; entryIndex < segment.Entries.Count; entryIndex++)
                    {
                        var entry = segment.Entries[entryIndex];
                        ValidateLoadedActionReferences(
                            $"{actionLabel}.track '{track.ID}'.segment '{segment.Id}'.entry[{entryIndex}] ({entry.Action.Type})",
                            entry.Action,
                            result,
                            effectiveContextId);
                    }
                }

                break;
        }
    }

    private static bool TryResolveActorTrackSegments(
        WH40KCinematicActorTrackPrototype track,
        string? segmentId,
        out IReadOnlyList<WH40KCinematicActorTrackSegmentDefinition> segments)
    {
        if (string.IsNullOrWhiteSpace(segmentId))
        {
            segments = track.Segments;
            return track.Segments.Count > 0;
        }

        var segment = track.Segments.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, segmentId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (segment == null)
        {
            segments = Array.Empty<WH40KCinematicActorTrackSegmentDefinition>();
            return false;
        }

        segments = new[] { segment };
        return true;
    }

    private void ValidateAnchorReference(
        string actionLabel,
        string? anchorId,
        bool optional,
        WH40KCinematicPreviewAnchorMode mode,
        LoadedValidationResult result,
        string? contextId)
    {
        var matches = ResolvePreviewAnchors(anchorId, mode);
        if (matches.Count > 0)
            return;

        if (IsDeferredContextReference(contextId))
            result.Warnings.Add($"{actionLabel}: anchor '{anchorId}' for deferred scene context '{contextId}' is not currently loaded and will resolve only after that context is loaded.");
        else if (optional)
            result.Warnings.Add($"{actionLabel}: optional anchor '{anchorId}' has no loaded matches for mode '{mode}'.");
        else
            result.Errors.Add($"{actionLabel}: required anchor '{anchorId}' has no loaded matches for mode '{mode}'.");
    }

    private static bool IsDeferredContextReference(string? contextId)
    {
        return !string.IsNullOrWhiteSpace(contextId) &&
               !string.Equals(contextId.Trim(), MainSceneContextId, StringComparison.OrdinalIgnoreCase);
    }

    private PrototypeDebugInfo BuildPrototypeDebugInfo(WH40KCinematicPrototype prototype)
    {
        var cameraPointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var soundAnchorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spawnAnchorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actionAnchorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var flowIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actionCount = 0;
        var shotCount = 0;

        foreach (var step in prototype.Steps)
        {
            if (step.Type == WH40KCinematicStepType.Shot)
            {
                shotCount++;
                if (!string.IsNullOrWhiteSpace(step.CameraPointId))
                    cameraPointIds.Add(step.CameraPointId);
            }

            foreach (var action in step.Actions)
            {
                actionCount++;
                switch (action.Type)
                {
                    case WH40KCinematicActionType.PlayAnchorSound:
                        if (!string.IsNullOrWhiteSpace(action.AnchorId))
                            soundAnchorIds.Add(action.AnchorId);
                        break;

                    case WH40KCinematicActionType.SpawnAtAnchor:
                        if (!string.IsNullOrWhiteSpace(action.AnchorId))
                            spawnAnchorIds.Add(action.AnchorId);
                        break;

                    case WH40KCinematicActionType.RunLavaFlow:
                        if (!string.IsNullOrWhiteSpace(action.FlowId))
                            flowIds.Add(action.FlowId);
                        break;
                }

                if (action.Type is not WH40KCinematicActionType.PlayAnchorSound and not WH40KCinematicActionType.SpawnAtAnchor &&
                    !string.IsNullOrWhiteSpace(action.AnchorId))
                {
                    actionAnchorIds.Add(action.AnchorId);
                }
            }
        }

        return new PrototypeDebugInfo(
            prototype.Steps.Count,
            shotCount,
            actionCount,
            prototype.RestoreInputDelaySeconds,
            cameraPointIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            soundAnchorIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            spawnAnchorIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            actionAnchorIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            flowIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private List<CameraPointPreviewTarget> ResolveCameraPoints(string? pointId)
    {
        var result = new List<CameraPointPreviewTarget>();
        if (string.IsNullOrWhiteSpace(pointId))
            return result;

        var query = EntityQueryEnumerator<WH40KCinematicCameraPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (!string.Equals(point.PointId, pointId, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new CameraPointPreviewTarget(uid, point.PointId, xform.Coordinates, point.Zoom, point.RotationDegrees));
        }

        return result;
    }

    private List<AnchorPreviewTarget> ResolvePreviewAnchors(string? anchorId, WH40KCinematicPreviewAnchorMode mode)
    {
        var result = new List<AnchorPreviewTarget>();
        var seen = new HashSet<EntityUid>();

        if (anchorId != null && string.IsNullOrWhiteSpace(anchorId))
            return result;

        bool Matches(string candidate)
        {
            return anchorId == null || string.Equals(candidate, anchorId, StringComparison.OrdinalIgnoreCase);
        }

        if (mode is WH40KCinematicPreviewAnchorMode.Any or WH40KCinematicPreviewAnchorMode.Action)
        {
            var actionQuery = EntityQueryEnumerator<WH40KCinematicActionAnchorComponent, TransformComponent>();
            while (actionQuery.MoveNext(out var uid, out var anchor, out var xform))
            {
                if (!Matches(anchor.AnchorId) || !seen.Add(uid))
                    continue;

                result.Add(new AnchorPreviewTarget(uid, anchor.AnchorId, "action", xform.Coordinates));
            }
        }

        if (mode is WH40KCinematicPreviewAnchorMode.Any or WH40KCinematicPreviewAnchorMode.Spawn)
        {
            var spawnQuery = EntityQueryEnumerator<WH40KCinematicSpawnAnchorComponent, TransformComponent>();
            while (spawnQuery.MoveNext(out var uid, out var anchor, out var xform))
            {
                if (!Matches(anchor.AnchorId) || !seen.Add(uid))
                    continue;

                result.Add(new AnchorPreviewTarget(uid, anchor.AnchorId, "spawn", xform.Coordinates));
            }

            if (mode == WH40KCinematicPreviewAnchorMode.Spawn)
            {
                var actionFallbackQuery = EntityQueryEnumerator<WH40KCinematicActionAnchorComponent, TransformComponent>();
                while (actionFallbackQuery.MoveNext(out var uid, out var anchor, out var xform))
                {
                    if (!Matches(anchor.AnchorId) || !seen.Add(uid))
                        continue;

                    result.Add(new AnchorPreviewTarget(uid, anchor.AnchorId, "action-fallback", xform.Coordinates));
                }
            }
        }

        if (mode is WH40KCinematicPreviewAnchorMode.Any or WH40KCinematicPreviewAnchorMode.Sound)
        {
            var soundQuery = EntityQueryEnumerator<WH40KCinematicSoundAnchorComponent, TransformComponent>();
            while (soundQuery.MoveNext(out var uid, out var anchor, out var xform))
            {
                if (!Matches(anchor.AnchorId) || !seen.Add(uid))
                    continue;

                result.Add(new AnchorPreviewTarget(uid, anchor.AnchorId, "sound", xform.Coordinates));
            }

            if (mode == WH40KCinematicPreviewAnchorMode.Sound)
            {
                var actionFallbackQuery = EntityQueryEnumerator<WH40KCinematicActionAnchorComponent, TransformComponent>();
                while (actionFallbackQuery.MoveNext(out var uid, out var anchor, out var xform))
                {
                    if (!Matches(anchor.AnchorId) || !seen.Add(uid))
                        continue;

                    result.Add(new AnchorPreviewTarget(uid, anchor.AnchorId, "action-fallback", xform.Coordinates));
                }
            }
        }

        if (mode is WH40KCinematicPreviewAnchorMode.Any or WH40KCinematicPreviewAnchorMode.Npc)
        {
            var npcQuery = EntityQueryEnumerator<WH40KCinematicNpcAnchorComponent, TransformComponent>();
            while (npcQuery.MoveNext(out var uid, out var anchor, out var xform))
            {
                if (!Matches(anchor.AnchorId) || !seen.Add(uid))
                    continue;

                result.Add(new AnchorPreviewTarget(uid, anchor.AnchorId, "npc", xform.Coordinates));
            }
        }

        return result;
    }

    private EntityUid SpawnPreviewMarker(string prototypeId, EntityCoordinates coordinates, float lifetimeSeconds)
    {
        var marker = Spawn(prototypeId, coordinates);
        var despawn = EnsureComp<TimedDespawnComponent>(marker);
        despawn.Lifetime = Math.Max(0.1f, lifetimeSeconds);
        return marker;
    }

    private EntityUid SpawnPreviewMarker(string prototypeId, NetCoordinates coordinates, float lifetimeSeconds)
    {
        return SpawnPreviewMarker(prototypeId, GetCoordinates(coordinates), lifetimeSeconds);
    }

    private static string JoinIds(IReadOnlyList<string> ids)
    {
        return ids.Count == 0 ? "-" : string.Join(", ", ids);
    }

    private sealed class LoadedValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    private readonly record struct PrototypeDebugInfo(
        int StepCount,
        int ShotCount,
        int ActionCount,
        float RestoreDelaySeconds,
        string[] CameraPointIds,
        string[] SoundAnchorIds,
        string[] SpawnAnchorIds,
        string[] ActionAnchorIds,
        string[] FlowIds);

    private readonly record struct CameraPointPreviewTarget(
        EntityUid Uid,
        string PointId,
        EntityCoordinates Coordinates,
        float Zoom,
        float RotationDegrees);

    private readonly record struct AnchorPreviewTarget(
        EntityUid Uid,
        string AnchorId,
        string SourceKind,
        EntityCoordinates Coordinates);
}

public enum WH40KCinematicPreviewAnchorMode : byte
{
    Any,
    Action,
    Spawn,
    Sound,
    Npc
}
