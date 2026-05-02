using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Content.Shared._WH40K.Cinematic;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Cinematic;

public sealed partial class WH40KCinematicSystem
{
    [Dependency] private readonly TileSystem _tiles = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private const string LavaPreviewMarkerPrototype = "WH40KCinematicLavaPreviewMarker";

    public IReadOnlyList<string> GetKnownLavaFlowIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var query = EntityQueryEnumerator<WH40KCinematicLavaMarkerComponent>();
        while (query.MoveNext(out _, out var marker))
        {
            if (!string.IsNullOrWhiteSpace(marker.FlowId))
                ids.Add(marker.FlowId.Trim());
        }

        return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> ValidateLavaFlow(string flowId)
    {
        return BuildLavaFlow(run: null, flowId, explicitContextId: null, action: null).Errors;
    }

    public bool TryGetLavaFlowDebugInfo(string flowId, out WH40KCinematicLavaFlowDebugInfo info, out string message)
    {
        info = default;
        var build = BuildLavaFlow(run: null, flowId, explicitContextId: null, action: null);
        if (!build.Success || build.Plan == null)
        {
            message = build.Errors.Count == 0
                ? $"Lava flow '{flowId}' is invalid."
                : $"Lava flow '{flowId}' is invalid: {string.Join("; ", build.Errors)}";
            return false;
        }

        var plan = build.Plan;
        info = new WH40KCinematicLavaFlowDebugInfo(
            plan.FlowId,
            plan.NodeCount,
            plan.CenterTiles.Count,
            plan.Tiles.Count,
            plan.Settings.Width,
            plan.Settings.WidthShape,
            plan.Settings.ObstacleMode,
            plan.Truncated,
            plan.TruncationReason);

        message =
            $"Lava flow '{plan.FlowId}' valid: nodes={plan.NodeCount}, centerTiles={plan.CenterTiles.Count}, tiles={plan.Tiles.Count}, width={plan.Settings.Width}, shape={plan.Settings.WidthShape}, obstacleMode={plan.Settings.ObstacleMode}.";

        if (build.Warnings.Count > 0)
            message += $" Warnings: {string.Join("; ", build.Warnings)}";

        return true;
    }

    public bool TryPreviewLavaFlow(string flowId, out string message, float previewLifetimeSeconds = 8f)
    {
        if (!TryGetLavaFlowDebugInfo(flowId, out var info, out message))
            return false;

        var build = BuildLavaFlow(run: null, flowId, explicitContextId: null, action: null);
        if (!build.Success || build.Plan == null)
            return false;

        var lifetime = Math.Max(0.1f, previewLifetimeSeconds);
        foreach (var tile in build.Plan.Tiles)
        {
            var preview = Spawn(LavaPreviewMarkerPrototype, _map.ToCenterCoordinates(build.Plan.GridUid, tile, build.Plan.Grid));
            var despawn = EnsureComp<TimedDespawnComponent>(preview);
            despawn.Lifetime = lifetime;
        }

        message =
            $"Previewed lava flow '{info.FlowId}' with {info.TileCount} tile(s) for {lifetime:0.##} second(s)." +
            (info.Truncated && !string.IsNullOrWhiteSpace(info.TruncationReason)
                ? $" Route truncated: {info.TruncationReason}"
                : string.Empty);
        return true;
    }

    private bool TryExecuteLavaFlowAction(
        ActiveCinematicRun active,
        WH40KCinematicActionDefinition action,
        string actionLabel,
        out string failureReason)
    {
        failureReason = string.Empty;

        if (active.Prototype.WorldFreezeMode == WH40KCinematicWorldFreezeMode.PauseMap)
        {
            failureReason =
                $"runLavaFlow is not compatible with PauseMap in cinematic '{active.Prototype.ID}'. Use LockPlayersOnly or None.";
            return false;
        }

        var build = BuildLavaFlow(active, action.FlowId, action.ContextId, action);
        if (!build.Success || build.Plan == null)
        {
            if (action.OptionalAnchor)
            {
                Log.Warning(
                    $"Skipping optional cinematic lava flow '{action.FlowId}' for {actionLabel}: {string.Join("; ", build.Errors)}");
                return true;
            }

            failureReason =
                $"Invalid lava flow '{action.FlowId}' for {actionLabel}: {string.Join("; ", build.Errors)}";
            return false;
        }

        foreach (var warning in build.Warnings)
        {
            Log.Warning($"WH40K cinematic lava flow '{build.Plan.FlowId}' warning for {actionLabel}: {warning}");
        }

        var runtime = new LavaFlowActionRuntime(
            action.Id,
            active.CurrentStepIndex,
            active.CurrentStep.Id,
            actionLabel,
            action.Blocking,
            action.PersistAfterCinematic,
            build.Plan);

        runtime.Tick(this);
        if (!runtime.IsComplete(this))
            active.ActiveActions.Add(runtime);

        return true;
    }

    private LavaFlowBuildResult BuildLavaFlow(
        ActiveCinematicRun? run,
        string? flowId,
        string? explicitContextId,
        WH40KCinematicActionDefinition? action)
    {
        var result = new LavaFlowBuildResult();
        var normalizedFlowId = flowId?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedFlowId))
        {
            result.Errors.Add("flowId must not be empty.");
            return result;
        }

        var markers = ResolveLavaMarkers(run, normalizedFlowId, explicitContextId, result.Errors);
        if (result.Errors.Count > 0)
            return result;

        if (markers.Count == 0)
        {
            result.Errors.Add($"No WH40K lava markers found for flow '{normalizedFlowId}'.");
            return result;
        }

        var startMarkers = markers.Where(marker => marker.Role == WH40KCinematicLavaMarkerRole.Start).ToArray();
        if (startMarkers.Length != 1)
        {
            result.Errors.Add(
                $"Flow '{normalizedFlowId}' requires exactly one lava start marker but found {startMarkers.Length}.");
        }

        var endMarkers = markers.Where(marker => marker.Role == WH40KCinematicLavaMarkerRole.End).ToArray();
        if (endMarkers.Length != 1)
        {
            result.Errors.Add(
                $"Flow '{normalizedFlowId}' requires exactly one lava end marker but found {endMarkers.Length}.");
        }

        var guides = markers.Where(marker => marker.Role == WH40KCinematicLavaMarkerRole.Guide).ToArray();
        var duplicateGuideOrder = guides
            .GroupBy(marker => marker.NodeIndex)
            .FirstOrDefault(group => group.Key <= 0 || group.Count() > 1);

        if (duplicateGuideOrder != null)
        {
            if (duplicateGuideOrder.Key <= 0)
                result.Errors.Add($"Flow '{normalizedFlowId}' has a guide marker with nodeIndex <= 0.");
            else
                result.Errors.Add(
                    $"Flow '{normalizedFlowId}' has duplicate guide nodeIndex '{duplicateGuideOrder.Key}'.");
        }

        if (result.Errors.Count > 0)
            return result;

        var start = startMarkers[0];
        var end = endMarkers[0];
        var orderedGuides = guides.OrderBy(marker => marker.NodeIndex).ToArray();

        var settings = ResolveLavaSettings(start, action, result.Errors);
        if (settings == null || result.Errors.Count > 0)
            return result;

        var nodes = new List<Vector2i>(orderedGuides.Length + 2)
        {
            start.Tile
        };

        foreach (var guide in orderedGuides)
        {
            nodes.Add(guide.Tile);
        }

        nodes.Add(end.Tile);

        var centerTiles = BuildLavaCenterline(start.GridUid, start.Grid, normalizedFlowId, nodes, settings, result);
        if (result.Errors.Count > 0)
            return result;

        if (result.Truncated && !string.IsNullOrWhiteSpace(result.TruncationReason))
            result.Warnings.Add(result.TruncationReason);

        var advanceBatches = BuildLavaAdvanceBatches(start.GridUid, start.Grid, centerTiles, settings);
        var tiles = FlattenLavaAdvanceBatches(advanceBatches);
        if (tiles.Count == 0)
        {
            result.Errors.Add($"Flow '{normalizedFlowId}' produced zero valid lava tiles after width expansion.");
            return result;
        }

        result.Plan = new LavaFlowPlan(
            normalizedFlowId,
            start.GridUid,
            start.Grid,
            settings,
            nodes.Count,
            start.Tile,
            centerTiles,
            advanceBatches,
            tiles,
            result.Truncated,
            result.TruncationReason);
        return result;
    }

    private List<LavaMarkerRuntime> ResolveLavaMarkers(
        ActiveCinematicRun? run,
        string flowId,
        string? explicitContextId,
        List<string> errors)
    {
        var markers = ResolveLavaMarkersInternal(run, flowId, explicitContextId, errors, respectContext: true);
        if (markers.Count > 0 || !ShouldFallbackToAnyContext(run, explicitContextId))
            return markers;

        return ResolveLavaMarkersInternal(run, flowId, explicitContextId, errors, respectContext: false);
    }

    private List<LavaMarkerRuntime> ResolveLavaMarkersInternal(
        ActiveCinematicRun? run,
        string flowId,
        string? explicitContextId,
        List<string> errors,
        bool respectContext)
    {
        var markers = new List<LavaMarkerRuntime>();
        EntityUid? gridUid = null;

        var query = AllEntityQuery<WH40KCinematicLavaMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            if (!string.Equals(marker.FlowId, flowId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (respectContext && run != null && !DoesEntityMatchContext(uid, run, explicitContextId))
                continue;

            if (xform.GridUid is not { } markerGridUid || !TryComp<MapGridComponent>(markerGridUid, out var markerGrid))
            {
                errors.Add(
                    $"Flow '{flowId}' marker '{uid}' is not attached to a grid and cannot be used for lava routing.");
                continue;
            }

            if (gridUid == null)
            {
                gridUid = markerGridUid;
            }
            else if (gridUid != markerGridUid)
            {
                errors.Add($"Flow '{flowId}' has markers on multiple grids, which is not supported in v1.");
                continue;
            }

            markers.Add(new LavaMarkerRuntime(
                uid,
                markerGridUid,
                markerGrid,
                _map.TileIndicesFor(markerGridUid, markerGrid, xform.Coordinates),
                marker.Role,
                marker.NodeIndex,
                marker));
        }

        return markers;
    }

    private LavaFlowSettings? ResolveLavaSettings(
        LavaMarkerRuntime startMarker,
        WH40KCinematicActionDefinition? action,
        List<string> errors)
    {
        var width = NormalizeLavaWidth(action?.Width ?? startMarker.Marker.Width);
        var widthShape = action?.WidthShape ?? startMarker.Marker.WidthShape;
        var obstacleMode = action?.ObstacleMode ?? startMarker.Marker.ObstacleMode;
        var preserveExistingFloor = action?.PreserveExistingFloor ?? startMarker.Marker.PreserveExistingFloor;
        var advanceInterval = Math.Max(0f, action?.AdvanceIntervalSeconds ?? startMarker.Marker.AdvanceIntervalSeconds);
        var tilesPerAdvance = Math.Max(1, action?.TilesPerAdvance ?? startMarker.Marker.TilesPerAdvance);
        var startClearRadius = Math.Max(0, startMarker.Marker.StartClearRadius);
        var startFillRadius = Math.Max(0, startMarker.Marker.StartFillRadius);
        var startFillShape = startMarker.Marker.StartFillShape;
        var floorTileId = action?.FloorTile ?? startMarker.Marker.FloorTile;
        var lavaPrototypeId = action?.LavaPrototype ?? startMarker.Marker.LavaPrototype;
        var startClearPrototypeIds = new HashSet<string>(StringComparer.Ordinal);

        if (!_prototypes.TryIndex(floorTileId, out ContentTileDefinition? floorTile))
            errors.Add($"Unknown lava floor tile prototype '{floorTileId}'.");

        if (!_prototypes.HasIndex<EntityPrototype>(lavaPrototypeId))
            errors.Add($"Unknown lava overlay prototype '{lavaPrototypeId}'.");

        foreach (var prototypeId in startMarker.Marker.StartClearPrototypes)
        {
            if (!_prototypes.HasIndex<EntityPrototype>(prototypeId))
            {
                errors.Add($"Unknown startClearPrototypes entity prototype '{prototypeId}'.");
                continue;
            }

            startClearPrototypeIds.Add(prototypeId);
        }

        if (errors.Count > 0 || floorTile == null)
            return null;

        return new LavaFlowSettings(
            width,
            widthShape,
            obstacleMode,
            preserveExistingFloor,
            floorTileId,
            floorTile,
            lavaPrototypeId,
            advanceInterval,
            tilesPerAdvance,
            startClearRadius,
            startClearPrototypeIds,
            startFillRadius,
            startFillShape);
    }

    private List<Vector2i> BuildLavaCenterline(
        EntityUid gridUid,
        MapGridComponent grid,
        string flowId,
        List<Vector2i> nodes,
        LavaFlowSettings settings,
        LavaFlowBuildResult result)
    {
        var centerTiles = new List<Vector2i>();
        var seen = new HashSet<Vector2i>();

        for (var segmentIndex = 0; segmentIndex < nodes.Count - 1; segmentIndex++)
        {
            var segment = new GridLineEnumerator(nodes[segmentIndex], nodes[segmentIndex + 1]);
            var skipFirst = segmentIndex > 0;

            while (segment.MoveNext())
            {
                var tile = segment.Current;
                if (skipFirst)
                {
                    skipFirst = false;
                    continue;
                }

                if (!TryAcceptLavaCenterTile(gridUid, grid, tile, settings, out var reason))
                {
                    if (settings.ObstacleMode == WH40KCinematicLavaObstacleMode.StopOnWallOrEmpty)
                    {
                        result.Truncated = true;
                        result.TruncationReason =
                            $"Stopped near tile {tile} because {reason?.ToLowerInvariant() ?? "of an obstacle"}.";

                        if (centerTiles.Count == 0)
                            result.Errors.Add($"Flow '{flowId}' cannot start because {reason?.ToLowerInvariant() ?? "the first tile is invalid"}.");

                        return centerTiles;
                    }

                    continue;
                }

                if (seen.Add(tile))
                    centerTiles.Add(tile);
            }
        }

        if (centerTiles.Count == 0)
            result.Errors.Add($"Flow '{flowId}' produced zero valid centerline tiles.");

        return centerTiles;
    }

    private List<List<Vector2i>> BuildLavaAdvanceBatches(
        EntityUid gridUid,
        MapGridComponent grid,
        List<Vector2i> centerTiles,
        LavaFlowSettings settings)
    {
        var batches = new List<List<Vector2i>>();
        var seen = new HashSet<Vector2i>();

        foreach (var center in centerTiles)
        {
            var batch = new List<Vector2i>();
            foreach (var offset in EnumerateLavaWidthOffsets(settings.Width, settings.WidthShape))
            {
                var tile = center + offset;
                if (!CanPlaceLavaAt(gridUid, grid, tile, settings.ObstacleMode))
                    continue;

                if (seen.Add(tile))
                    batch.Add(tile);
            }

            if (batch.Count > 0)
                batches.Add(batch);
        }

        return batches;
    }

    private static List<Vector2i> FlattenLavaAdvanceBatches(List<List<Vector2i>> advanceBatches)
    {
        var tiles = new List<Vector2i>();
        foreach (var batch in advanceBatches)
        {
            tiles.AddRange(batch);
        }

        return tiles;
    }

    private bool TryAcceptLavaCenterTile(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        LavaFlowSettings settings,
        out string? reason)
    {
        reason = null;

        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
        {
            reason = "encountered an empty or missing tile";
            return false;
        }

        if (settings.ObstacleMode == WH40KCinematicLavaObstacleMode.StopOnWallOrEmpty &&
            HasWallAt(gridUid, grid, tile))
        {
            reason = "encountered a wall-tagged anchored obstacle";
            return false;
        }

        return true;
    }

    private bool CanPlaceLavaAt(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        WH40KCinematicLavaObstacleMode obstacleMode)
    {
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        if (obstacleMode == WH40KCinematicLavaObstacleMode.StopOnWallOrEmpty &&
            HasWallAt(gridUid, grid, tile))
        {
            return false;
        }

        return true;
    }

    private bool HasWallAt(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var entity))
        {
            if (_tags.HasTag(entity.Value, WallTag))
                return true;
        }

        return false;
    }

    private static int NormalizeLavaWidth(int width)
    {
        if (width < 1)
            width = 1;

        if (width % 2 == 0)
            width++;

        return width;
    }

    private static IEnumerable<Vector2i> EnumerateLavaWidthOffsets(int width, WH40KCinematicLavaWidthShape shape)
    {
        var radius = (NormalizeLavaWidth(width) - 1) / 2;

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (shape == WH40KCinematicLavaWidthShape.Diamond && Math.Abs(x) + Math.Abs(y) > radius)
                    continue;

                yield return new Vector2i(x, y);
            }
        }
    }

    private void ApplyLavaTile(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        LavaFlowSettings settings,
        string flowId)
    {
        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef))
            return;

        if (tileRef.Tile.IsEmpty)
        {
            var variantSeed = StableLavaSeed(flowId, tile);
            var variant = _tiles.PickVariant(settings.FloorTile, variantSeed);
            _tiles.ReplaceTile(tileRef, settings.FloorTile, gridUid, grid, variant);
        }
        else if (!settings.PreserveExistingFloor && tileRef.Tile.TypeId != settings.FloorTile.TileId)
        {
            var variantSeed = StableLavaSeed(flowId, tile);
            var variant = _tiles.PickVariant(settings.FloorTile, variantSeed);
            _tiles.ReplaceTile(tileRef, settings.FloorTile, gridUid, grid, variant);
        }

        if (HasLavaOverlay(gridUid, grid, tile, settings.LavaPrototype))
            return;

        Spawn(settings.LavaPrototype, _map.ToCenterCoordinates(gridUid, tile, grid));
    }

    private void ClearLavaStartObstacles(EntityUid gridUid, MapGridComponent grid, LavaFlowPlan plan)
    {
        var radius = plan.Settings.StartClearRadius;
        if (radius <= 0 || plan.Settings.StartClearPrototypeIds.Count == 0)
            return;

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                var tile = plan.StartTile + new Vector2i(x, y);
                var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
                while (anchored.MoveNext(out var entity))
                {
                    if (Deleted(entity.Value))
                        continue;

                    var prototypeId = MetaData(entity.Value).EntityPrototype?.ID;
                    if (string.IsNullOrWhiteSpace(prototypeId) ||
                        !plan.Settings.StartClearPrototypeIds.Contains(prototypeId))
                    {
                        continue;
                    }

                    QueueDel(entity.Value);
                }
            }
        }
    }

    private void SeedLavaAtFlowStart(EntityUid gridUid, MapGridComponent grid, LavaFlowPlan plan)
    {
        var radius = plan.Settings.StartFillRadius;
        if (radius <= 0)
            return;

        foreach (var offset in EnumerateLavaWidthOffsets((radius * 2) + 1, plan.Settings.StartFillShape))
        {
            ApplyLavaTile(gridUid, grid, plan.StartTile + offset, plan.Settings, plan.FlowId);
        }
    }

    private bool HasLavaOverlay(EntityUid gridUid, MapGridComponent grid, Vector2i tile, EntProtoId lavaPrototype)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var entity))
        {
            if (string.Equals(MetaData(entity.Value).EntityPrototype?.ID, lavaPrototype, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int StableLavaSeed(string flowId, Vector2i tile)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in flowId)
            {
                hash = (hash * 31) + char.ToUpperInvariant(c);
            }

            hash = (hash * 31) + tile.X;
            hash = (hash * 31) + tile.Y;
            return hash;
        }
    }

    private sealed class LavaFlowActionRuntime : ActiveActionRuntime
    {
        private readonly LavaFlowPlan _plan;
        private readonly bool _persistAfterCinematic;
        private readonly TimeSpan _advanceInterval;
        private int _nextBatchIndex;
        private TimeSpan _nextAdvanceAt;
        private bool _primed;
        private bool _startAreaCleared;
        private bool _startAreaSeeded;

        public LavaFlowActionRuntime(
            string? runtimeId,
            int stepIndex,
            string stepId,
            string actionLabel,
            bool blocking,
            bool persistAfterCinematic,
            LavaFlowPlan plan)
            : base(runtimeId, stepIndex, stepId, actionLabel, blocking)
        {
            _plan = plan;
            _persistAfterCinematic = persistAfterCinematic;
            _advanceInterval = TimeSpan.FromSeconds(plan.Settings.AdvanceIntervalSeconds);
            _nextAdvanceAt = TimeSpan.Zero;
        }

        public override void Tick(WH40KCinematicSystem system)
        {
            if (!system.TryComp<MapGridComponent>(_plan.GridUid, out var grid))
            {
                _nextBatchIndex = _plan.AdvanceBatches.Count;
                return;
            }

            if (!_startAreaCleared)
            {
                system.ClearLavaStartObstacles(_plan.GridUid, grid, _plan);
                _startAreaCleared = true;
            }

            if (!_startAreaSeeded)
            {
                system.SeedLavaAtFlowStart(_plan.GridUid, grid, _plan);
                _startAreaSeeded = true;
            }

            if (!_primed)
            {
                _nextAdvanceAt = system._timing.CurTime;
                _primed = true;
            }

            while (_nextBatchIndex < _plan.AdvanceBatches.Count && system._timing.CurTime >= _nextAdvanceAt)
            {
                var remaining = _plan.AdvanceBatches.Count - _nextBatchIndex;
                var batchCount = Math.Min(_plan.Settings.TilesPerAdvance, remaining);
                for (var i = 0; i < batchCount; i++)
                {
                    foreach (var tile in _plan.AdvanceBatches[_nextBatchIndex])
                    {
                        system.ApplyLavaTile(_plan.GridUid, grid, tile, _plan.Settings, _plan.FlowId);
                    }

                    _nextBatchIndex++;
                }

                if (_nextBatchIndex >= _plan.AdvanceBatches.Count)
                    break;

                if (_advanceInterval <= TimeSpan.Zero)
                    continue;

                _nextAdvanceAt += _advanceInterval;
            }
        }

        public override bool IsComplete(WH40KCinematicSystem system)
        {
            return _nextBatchIndex >= _plan.AdvanceBatches.Count;
        }

        public override void ForceStop(WH40KCinematicSystem system)
        {
            _nextBatchIndex = _plan.AdvanceBatches.Count;
        }

        public override bool TryPromoteToPersistent(WH40KCinematicSystem system)
        {
            return _persistAfterCinematic && !IsComplete(system);
        }
    }

    private sealed record LavaMarkerRuntime(
        EntityUid Uid,
        EntityUid GridUid,
        MapGridComponent Grid,
        Vector2i Tile,
        WH40KCinematicLavaMarkerRole Role,
        int NodeIndex,
        WH40KCinematicLavaMarkerComponent Marker);

    private sealed record LavaFlowSettings(
        int Width,
        WH40KCinematicLavaWidthShape WidthShape,
        WH40KCinematicLavaObstacleMode ObstacleMode,
        bool PreserveExistingFloor,
        ProtoId<ContentTileDefinition> FloorTileId,
        ContentTileDefinition FloorTile,
        EntProtoId LavaPrototype,
        float AdvanceIntervalSeconds,
        int TilesPerAdvance,
        int StartClearRadius,
        HashSet<string> StartClearPrototypeIds,
        int StartFillRadius,
        WH40KCinematicLavaWidthShape StartFillShape);

    private sealed record LavaFlowPlan(
        string FlowId,
        EntityUid GridUid,
        MapGridComponent Grid,
        LavaFlowSettings Settings,
        int NodeCount,
        Vector2i StartTile,
        List<Vector2i> CenterTiles,
        List<List<Vector2i>> AdvanceBatches,
        List<Vector2i> Tiles,
        bool Truncated,
        string? TruncationReason);

    private sealed class LavaFlowBuildResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public LavaFlowPlan? Plan { get; set; }
        public bool Success => Errors.Count == 0;
        public bool Truncated { get; set; }
        public string? TruncationReason { get; set; }
    }
}

public readonly record struct WH40KCinematicLavaFlowDebugInfo(
    string FlowId,
    int NodeCount,
    int CenterTileCount,
    int TileCount,
    int Width,
    WH40KCinematicLavaWidthShape WidthShape,
    WH40KCinematicLavaObstacleMode ObstacleMode,
    bool Truncated,
    string? TruncationReason);
