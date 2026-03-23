using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.PowerCell;
using Content.Shared.Station;
using Content.Shared._WH40K.Influence;
using Content.Shared._WH40K.TacticalMap;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.TacticalMap;

public sealed class WH40KTacticalMapSystem : SharedWH40KTacticalMapSystem
{
    private const string ScannerPrototype = "WH40KTacticalMapScannerEye";
    private const bool LiveRefreshRuntimeEnabled = false;
    private const int DefaultFogChunkSize = 8;
    private const int DefaultFogRevealRadiusChunks = 0;
    private const int MaxAnnotationStrokes = 256;
    private const int MaxAnnotationPointsPerStroke = 512;
    private const int MaxAnnotationPointsTotal = 8192;
    private const float MinAnnotationThickness = 0.25f;
    private const float MaxAnnotationThickness = 6f;
    private static readonly TimeSpan FogRevealUpdateInterval = TimeSpan.FromSeconds(0.75);
    private static readonly TimeSpan StateSyncInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan StateSyncNoTeamInterval = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan OverlayRefreshInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan OverlayRefreshIntervalNoTeam = TimeSpan.FromSeconds(1.0);
    private static readonly Color NeutralMarkerColor = Color.FromHex("#7F8790".AsSpan());

    private readonly record struct TeamMapKey(EntityUid GridUid, string TeamId);

    private sealed class FogGridConfig
    {
        public int ChunkSize;
        public int RevealRadiusChunks;
    }

    private sealed class TeamFogState
    {
        public int ChunkSize;
        public int Revision;
        public readonly HashSet<Vector2i> RevealedChunks = new();
    }

    private sealed class TeamAnnotationState
    {
        public int Revision;
        public readonly List<WH40KTacticalMapAnnotationStroke> Strokes = new();
    }

    private sealed class TeamOverlayState
    {
        public bool Initialized;
        public TimeSpan NextRefreshAt;
        public WH40KTacticalMapAllyMarker[] AlliedMarkers = Array.Empty<WH40KTacticalMapAllyMarker>();
        public WH40KTacticalMapCapturePointMarker[] CapturePoints = Array.Empty<WH40KTacticalMapCapturePointMarker>();
    }

    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PowerCellSystem _cell = default!;
    [Dependency] private readonly SharedStationSystem _station = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly ViewSubscriberSystem _viewSubscribers = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, FogGridConfig> _fogGridConfigs = new();
    private readonly Dictionary<TeamMapKey, TeamFogState> _teamFogStates = new();
    private readonly Dictionary<TeamMapKey, TeamAnnotationState> _teamAnnotationStates = new();
    private readonly Dictionary<TeamMapKey, TeamOverlayState> _teamOverlayStates = new();
    private TimeSpan _nextFogRevealUpdateAt = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WH40KTacticalMapComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KTacticalMapUserComponent, EntParentChangedMessage>(OnUserParentChanged);
        SubscribeLocalEvent<WH40KTacticalMapUserComponent, ComponentShutdown>(OnUserShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        Subs.BuiEvents<WH40KTacticalMapComponent>(WH40KTacticalMapUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnMapOpened);
            subs.Event<BoundUIClosedEvent>(OnMapClosed);
            subs.Event<WH40KTacticalMapSaveAnnotationsMessage>(OnSaveAnnotations);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextFogRevealUpdateAt)
        {
            AdvanceFogRevealRuntime();
            _nextFogRevealUpdateAt = now + FogRevealUpdateInterval;
        }

        var query = EntityQueryEnumerator<WH40KTacticalMapUserComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var user, out var actor))
        {
            if (!TryComp(user.Map, out WH40KTacticalMapComponent? map))
                continue;

            if (now >= user.NextStateSyncAt)
            {
                SyncStateToViewer((uid, user), (user.Map, map), actor);
                user.NextStateSyncAt = now + ResolveStateSyncInterval(user.TeamId);
            }

            if (user.Scanner is not { } scanner || Deleted(scanner))
                continue;

            if (!IsLiveRefreshEnabled(map))
            {
                ClearLiveRefreshRuntime((uid, user), (user.Map, map));
                continue;
            }

            if (now < user.NextRefreshAt)
                continue;

            AdvanceLiveRefreshScanner((uid, user), (user.Map, map));
            user.NextRefreshAt = now + TimeSpan.FromSeconds(Math.Max(0.5f, map.LiveRefreshInterval));
        }
    }

    private void OnMapInit(Entity<WH40KTacticalMapComponent> ent, ref MapInitEvent args)
    {
        _ui.SetUiState(ent.Owner, WH40KTacticalMapUiKey.Key, null);

        if (!ent.Comp.InitializeWithStation)
            return;

        var station = _station.GetStationInMap(_xform.GetMapId(ent.Owner));
        if (station != null)
        {
            ent.Comp.TargetGrid = _station.GetLargestGrid((station.Value, null));
            EnsureFogGridConfig(ent, ent.Comp.TargetGrid);
            return;
        }

        var xform = Transform(ent.Owner);
        if (xform.GridUid is not { } gridUid ||
            !HasComp<MapGridComponent>(gridUid))
        {
            return;
        }

        ent.Comp.TargetGrid = gridUid;
        EnsureFogGridConfig(ent, gridUid);
    }

    private void OnMapClosed(EntityUid uid, WH40KTacticalMapComponent component, BoundUIClosedEvent args)
    {
        if (!Equals(args.UiKey, WH40KTacticalMapUiKey.Key))
            return;

        _ui.SetUiState(uid, WH40KTacticalMapUiKey.Key, null);

        if (TryComp(args.Actor, out WH40KTacticalMapUserComponent? user))
            ClearLiveRefreshRuntime((args.Actor, user), (uid, component));

        RemCompDeferred<WH40KTacticalMapUserComponent>(args.Actor);
    }

    private void OnUserParentChanged(EntityUid uid, WH40KTacticalMapUserComponent component, ref EntParentChangedMessage args)
    {
        if (TryComp(component.Map, out WH40KTacticalMapComponent? map))
            ClearLiveRefreshRuntime((uid, component), (component.Map, map));

        _ui.CloseUi(component.Map, WH40KTacticalMapUiKey.Key, uid);
    }

    private void OnUserShutdown(EntityUid uid, WH40KTacticalMapUserComponent component, ref ComponentShutdown args)
    {
        if (TryComp(component.Map, out WH40KTacticalMapComponent? map))
            ClearLiveRefreshRuntime((uid, component), (component.Map, map));
        else if (component.Scanner is { } scanner && !Deleted(scanner))
            QueueDel(scanner);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _fogGridConfigs.Clear();
        _teamFogStates.Clear();
        _teamAnnotationStates.Clear();
        _teamOverlayStates.Clear();
        _nextFogRevealUpdateAt = TimeSpan.Zero;
    }

    private void OnMapOpened(EntityUid uid, WH40KTacticalMapComponent component, BoundUIOpenedEvent args)
    {
        if (!_cell.TryUseActivatableCharge(uid))
            return;

        _ui.SetUiState(uid, WH40KTacticalMapUiKey.Key, null);

        var user = EnsureComp<WH40KTacticalMapUserComponent>(args.Actor);
        user.Map = uid;
        user.LastFogRevision = -1;
        user.LastAnnotationRevision = -1;
        user.LastOverlayRevision = -1;
        user.LastLiveRefreshRevision = -1;
        user.NextStateSyncAt = TimeSpan.Zero;

        if (ResolveTargetGrid((uid, component)) is { } targetGrid)
            EnsureFogGridConfig((uid, component), targetGrid);

        if (TryComp(args.Actor, out ActorComponent? actor))
            SyncStateToViewer((args.Actor, user), (uid, component), actor);

        if (!IsLiveRefreshEnabled(component))
            return;

        EnsureLiveRefreshRuntime((uid, component), (args.Actor, user));
    }

    private void OnSaveAnnotations(EntityUid uid, WH40KTacticalMapComponent component, WH40KTacticalMapSaveAnnotationsMessage args)
    {
        if (!component.CanAnnotate)
            return;

        if (!TryComp(args.Actor, out ActorComponent? actor))
            return;

        if (!ResolveViewerTeamId(args.Actor, actor, out var teamId) ||
            string.IsNullOrWhiteSpace(teamId) ||
            ResolveTargetGrid((uid, component)) is not { } gridUid)
        {
            return;
        }

        var sanitized = SanitizeAnnotationStrokes(args.Strokes, gridUid);
        var state = GetOrCreateAnnotationState(gridUid, teamId);
        if (AreAnnotationsEquivalent(state.Strokes, sanitized))
            return;

        state.Strokes.Clear();
        state.Strokes.AddRange(sanitized);
        state.Revision++;

        var totalPoints = 0;
        foreach (var stroke in sanitized)
        {
            totalPoints += stroke.Points.Length;
        }
    }

    private void EnsureLiveRefreshRuntime(
        Entity<WH40KTacticalMapComponent> map,
        Entity<WH40KTacticalMapUserComponent> user)
    {
        if (map.Comp.TargetGrid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out _))
        {
            return;
        }

        if (user.Comp.Scanner is not { } scanner || Deleted(scanner))
        {
            scanner = Spawn(ScannerPrototype, MapCoordinates.Nullspace);
            user.Comp.Scanner = scanner;
            user.Comp.ScanIndex = 0;

            if (TryComp(user.Owner, out ActorComponent? actor))
                _viewSubscribers.AddViewSubscriber(scanner, actor.PlayerSession);
        }

        if (!TryComp(scanner, out EyeComponent? eyeComp))
            return;

        _eye.SetDrawFov(scanner, false, eyeComp);
        _eye.SetDrawLight((scanner, eyeComp), false);
        _eye.SetPvsScale((scanner, eyeComp), MathF.Max(0.1f, map.Comp.LiveRefreshPvsScale));

        user.Comp.NextRefreshAt = TimeSpan.Zero;
        AdvanceLiveRefreshScanner(user, map);
        user.Comp.NextRefreshAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0.5f, map.Comp.LiveRefreshInterval));
    }

    private void AdvanceFogRevealRuntime()
    {
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (!_teamRule.TryGetTeamIdFromEntity(uid, out var teamId) ||
                string.IsNullOrWhiteSpace(teamId) ||
                xform.GridUid is not { } gridUid ||
                !TryComp<MapGridComponent>(gridUid, out _))
            {
                continue;
            }

            var config = ResolveFogGridConfig(gridUid);
            var fog = GetOrCreateFogState(gridUid, teamId, config);
            var gridXform = Transform(gridUid);
            var localPosition = Vector2.Transform(_xform.GetWorldPosition(xform), _xform.GetInvWorldMatrix(gridXform));
            var centerChunk = WorldToFogChunk(localPosition, fog.ChunkSize);
            var radius = Math.Max(0, config.RevealRadiusChunks);
            var changed = false;

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    changed |= fog.RevealedChunks.Add(new Vector2i(centerChunk.X + dx, centerChunk.Y + dy));
                }
            }

            if (changed)
                fog.Revision++;
        }
    }

    private void AdvanceLiveRefreshScanner(
        Entity<WH40KTacticalMapUserComponent> user,
        Entity<WH40KTacticalMapComponent> map)
    {
        if (user.Comp.Scanner is not { } scanner ||
            Deleted(scanner) ||
            map.Comp.TargetGrid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        var chunkTiles = Math.Max(4, map.Comp.LiveRefreshChunkSize);
        var bounds = grid.LocalAABB;

        var minX = (int) MathF.Floor(bounds.Left);
        var minY = (int) MathF.Floor(bounds.Bottom);
        var maxX = (int) MathF.Ceiling(bounds.Right);
        var maxY = (int) MathF.Ceiling(bounds.Top);

        var totalTilesX = Math.Max(0, maxX - minX);
        var totalTilesY = Math.Max(0, maxY - minY);

        if (totalTilesX == 0 || totalTilesY == 0)
            return;

        var chunksX = (int) Math.Ceiling(totalTilesX / (float) chunkTiles);
        var chunksY = (int) Math.Ceiling(totalTilesY / (float) chunkTiles);
        var totalChunks = Math.Max(1, chunksX * chunksY);

        var index = Math.Abs(user.Comp.ScanIndex) % totalChunks;
        var chunkX = index % chunksX;
        var chunkY = index / chunksX;

        var originX = minX + chunkX * chunkTiles;
        var originY = minY + chunkY * chunkTiles;
        var sizeX = Math.Min(chunkTiles, maxX - originX);
        var sizeY = Math.Min(chunkTiles, maxY - originY);

        if (sizeX <= 0 || sizeY <= 0)
        {
            user.Comp.ScanIndex = (index + 1) % totalChunks;
            return;
        }

        var center = new Vector2(originX + sizeX / 2f, originY + sizeY / 2f);
        _xform.SetCoordinates(scanner, new EntityCoordinates(gridUid, center));

        if (TryComp(user.Owner, out ActorComponent? actor))
            SyncLiveRefreshToViewer(user, map, actor, scanner, new Vector2i(originX, originY), new Vector2i(sizeX, sizeY));

        user.Comp.ScanIndex = (index + 1) % totalChunks;
    }

    private void ClearLiveRefreshRuntime(
        Entity<WH40KTacticalMapUserComponent> user,
        Entity<WH40KTacticalMapComponent?> map)
    {
        if (user.Comp.Scanner is { } scanner && !Deleted(scanner))
        {
            if (TryComp(user.Owner, out ActorComponent? actor))
                _viewSubscribers.RemoveViewSubscriber(scanner, actor.PlayerSession);

            QueueDel(scanner);
        }

        user.Comp.Scanner = null;
        user.Comp.ScanIndex = 0;
        user.Comp.NextRefreshAt = TimeSpan.Zero;
        user.Comp.LastLiveRefreshRevision = -1;

        if (map.Comp == null)
            return;

        if (TryComp(user.Owner, out ActorComponent? ownerActor))
            ClearLiveRefreshForViewer(user, (map.Owner, map.Comp), ownerActor);
    }

    private void SyncStateToViewer(
        Entity<WH40KTacticalMapUserComponent> user,
        Entity<WH40KTacticalMapComponent> map,
        ActorComponent actor)
    {
        var teamId = string.Empty;
        ResolveViewerTeamId(user.Owner, actor, out teamId);

        if (!string.Equals(user.Comp.TeamId, teamId, StringComparison.Ordinal))
        {
            user.Comp.TeamId = teamId;
            user.Comp.LastFogRevision = -1;
            user.Comp.LastAnnotationRevision = -1;
            user.Comp.LastOverlayRevision = -1;
        }

        var targetGrid = ResolveTargetGrid(map);

        var fogEnabled = map.Comp.FogEnabled;
        var targetGridNet = targetGrid is { } targetGridUid ? GetNetEntity(targetGridUid) : NetEntity.Invalid;
        var gridName = targetGrid is { } gridForName && TryComp(gridForName, out MetaDataComponent? targetMeta)
            ? targetMeta.EntityName
            : string.Empty;
        var snapshotTexturePath = map.Comp.SnapshotTexture.ToString();
        var trackedEntity = map.Comp.ShowLocation ? GetNetEntity(user.Owner) : NetEntity.Invalid;
        var fogChunkSize = Math.Max(1, map.Comp.FogChunkSize);
        var fogRevision = 0;
        TeamFogState? fogState = null;

        if (fogEnabled &&
            !string.IsNullOrWhiteSpace(teamId) &&
            targetGrid is { } fogGridUid)
        {
            EnsureFogGridConfig(map, fogGridUid);
            fogState = GetOrCreateFogState(fogGridUid, teamId, ResolveFogGridConfig(fogGridUid));
            fogChunkSize = fogState.ChunkSize;
            fogRevision = fogState.Revision;
        }

        var annotationRevision = 0;
        TeamAnnotationState? annotationState = null;

        if (!string.IsNullOrWhiteSpace(teamId) &&
            targetGrid is { } annotationGridUid)
        {
            annotationState = GetOrCreateAnnotationState(annotationGridUid, teamId);
            annotationRevision = annotationState.Revision;
        }

        var overlayRevision = 0;
        var alliedMarkers = Array.Empty<WH40KTacticalMapAllyMarker>();
        var capturePoints = Array.Empty<WH40KTacticalMapCapturePointMarker>();

        if (targetGrid is { } overlayGridUid)
        {
            var overlayState = GetOrRefreshOverlayState(overlayGridUid, teamId);
            alliedMarkers = FilterAlliedMarkersForViewer(overlayState.AlliedMarkers, user.Owner);
            capturePoints = overlayState.CapturePoints;
            overlayRevision = ComputeOverlayRevision(alliedMarkers, capturePoints);
        }

        var fogChanged = user.Comp.LastFogRevision != fogRevision;
        var annotationChanged = user.Comp.LastAnnotationRevision != annotationRevision;
        var overlayChanged = user.Comp.LastOverlayRevision != overlayRevision;

        if (!fogChanged &&
            !annotationChanged &&
            !overlayChanged)
        {
            return;
        }

        if (!fogChanged &&
            !annotationChanged &&
            overlayChanged)
        {
            var overlayState = new WH40KTacticalMapOverlayState(
                overlayRevision,
                alliedMarkers,
                capturePoints);

            RaiseNetworkEvent(new WH40KTacticalMapOverlayEvent(GetNetEntity(map.Owner), overlayState), actor.PlayerSession);
            user.Comp.LastOverlayRevision = overlayRevision;
            return;
        }

        var revealedChunks = fogState != null
            ? fogState.RevealedChunks.ToArray()
            : Array.Empty<Vector2i>();
        var annotationStrokes = annotationState != null
            ? annotationState.Strokes.ToArray()
            : Array.Empty<WH40KTacticalMapAnnotationStroke>();

        var state = new WH40KTacticalMapBuiState(
            targetGridNet,
            gridName,
            snapshotTexturePath,
            trackedEntity,
            map.Comp.CanAnnotate,
            IsLiveRefreshEnabled(map.Comp),
            teamId,
            fogEnabled,
            fogChunkSize,
            fogRevision,
            revealedChunks,
            annotationRevision,
            annotationStrokes,
            overlayRevision,
            alliedMarkers,
            capturePoints);

        RaiseNetworkEvent(new WH40KTacticalMapStateEvent(GetNetEntity(map.Owner), state), actor.PlayerSession);
        user.Comp.LastFogRevision = fogRevision;
        user.Comp.LastAnnotationRevision = annotationRevision;
        user.Comp.LastOverlayRevision = overlayRevision;
    }

    private void SyncLiveRefreshToViewer(
        Entity<WH40KTacticalMapUserComponent> user,
        Entity<WH40KTacticalMapComponent> map,
        ActorComponent actor,
        EntityUid scanner,
        Vector2i tileOrigin,
        Vector2i tileSize)
    {
        if (!IsLiveRefreshEnabled(map.Comp))
        {
            ClearLiveRefreshForViewer(user, map, actor);
            return;
        }

        var revision = user.Comp.LastLiveRefreshRevision + 1;
        user.Comp.LastLiveRefreshRevision = revision;

        var state = new WH40KTacticalMapLiveRefreshState(
            true,
            revision,
            GetNetEntity(scanner),
            tileOrigin,
            tileSize);

        RaiseNetworkEvent(new WH40KTacticalMapLiveRefreshEvent(GetNetEntity(map.Owner), state), actor.PlayerSession);
    }

    private void ClearLiveRefreshForViewer(
        Entity<WH40KTacticalMapUserComponent> user,
        Entity<WH40KTacticalMapComponent> map,
        ActorComponent actor)
    {
        var revision = Math.Max(0, user.Comp.LastLiveRefreshRevision + 1);
        user.Comp.LastLiveRefreshRevision = revision;

        var state = new WH40KTacticalMapLiveRefreshState(
            false,
            revision,
            NetEntity.Invalid,
            Vector2i.Zero,
            Vector2i.Zero);

        RaiseNetworkEvent(new WH40KTacticalMapLiveRefreshEvent(GetNetEntity(map.Owner), state), actor.PlayerSession);
    }

    private static bool IsLiveRefreshEnabled(WH40KTacticalMapComponent component)
    {
        return LiveRefreshRuntimeEnabled && component.LiveRefreshEnabled;
    }

    private TeamOverlayState GetOrRefreshOverlayState(EntityUid gridUid, string teamId)
    {
        var key = new TeamMapKey(gridUid, teamId);
        if (!_teamOverlayStates.TryGetValue(key, out var state))
        {
            state = new TeamOverlayState();
            _teamOverlayStates[key] = state;
        }

        var now = _timing.CurTime;
        if (state.Initialized && now < state.NextRefreshAt)
            return state;

        state.AlliedMarkers = BuildAlliedMarkers(gridUid, teamId);
        state.CapturePoints = BuildCapturePointMarkers(gridUid);
        state.Initialized = true;
        state.NextRefreshAt = now + ResolveOverlayRefreshInterval(teamId);
        return state;
    }

    private WH40KTacticalMapAllyMarker[] FilterAlliedMarkersForViewer(
        IReadOnlyList<WH40KTacticalMapAllyMarker> alliedMarkers,
        EntityUid viewer)
    {
        if (alliedMarkers.Count == 0)
            return Array.Empty<WH40KTacticalMapAllyMarker>();

        var viewerNetEntity = GetNetEntity(viewer);
        var count = 0;

        foreach (var ally in alliedMarkers)
        {
            if (ally.Entity != viewerNetEntity)
                count++;
        }

        if (count == alliedMarkers.Count && alliedMarkers is WH40KTacticalMapAllyMarker[] cachedMarkers)
            return cachedMarkers;

        if (count == 0)
            return Array.Empty<WH40KTacticalMapAllyMarker>();

        var filtered = new WH40KTacticalMapAllyMarker[count];
        var index = 0;

        foreach (var ally in alliedMarkers)
        {
            if (ally.Entity == viewerNetEntity)
                continue;

            filtered[index++] = ally;
        }

        return filtered;
    }

    private WH40KTacticalMapAllyMarker[] BuildAlliedMarkers(EntityUid gridUid, string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return Array.Empty<WH40KTacticalMapAllyMarker>();

        var results = new List<WH40KTacticalMapAllyMarker>();
        var gridXform = Transform(gridUid);
        var invGridMatrix = _xform.GetInvWorldMatrix(gridXform);
        var query = EntityQueryEnumerator<WH40KTeamMemberComponent, TransformComponent, MetaDataComponent>();

        while (query.MoveNext(out var uid, out var member, out var xform, out var meta))
        {
            if (xform.GridUid != gridUid ||
                !string.Equals(member.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!HasComp<ActorComponent>(uid) &&
                !HasComp<WH40KTacticalMapTrackedComponent>(uid))
            {
                continue;
            }

            if (TryComp<MobStateComponent>(uid, out var mobState) &&
                _mobState.IsDead(uid, mobState))
            {
                continue;
            }

            var localPosition = Vector2.Transform(_xform.GetWorldPosition(xform), invGridMatrix);
            results.Add(new WH40KTacticalMapAllyMarker(
                GetNetEntity(uid),
                meta.EntityName,
                localPosition,
                ResolveTeamColor(member.TeamId)));
        }

        results.Sort(static (left, right) => string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase));
        return results.ToArray();
    }

    private WH40KTacticalMapCapturePointMarker[] BuildCapturePointMarkers(EntityUid gridUid)
    {
        var results = new List<WH40KTacticalMapCapturePointMarker>();
        var gridXform = Transform(gridUid);
        var invGridMatrix = _xform.GetInvWorldMatrix(gridXform);
        var query = EntityQueryEnumerator<WH40KInfluencePointComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            var localPosition = Vector2.Transform(_xform.GetWorldPosition(xform), invGridMatrix);
            var ownerTeamId = point.OwnerTeamId ?? string.Empty;
            var capturingTeamId = point.CapturingTeamId ?? string.Empty;
            var captureProgress = string.IsNullOrWhiteSpace(capturingTeamId)
                ? 0f
                : Math.Clamp(point.CaptureProgressSeconds / Math.Max(1f, point.CaptureTimeSeconds), 0f, 1f);
            var contested = !string.IsNullOrWhiteSpace(ownerTeamId) &&
                            !string.IsNullOrWhiteSpace(capturingTeamId) &&
                            !string.Equals(ownerTeamId, capturingTeamId, StringComparison.OrdinalIgnoreCase);

            results.Add(new WH40KTacticalMapCapturePointMarker(
                GetNetEntity(uid),
                BuildLocalizedCapturePointLabel(uid, point),
                point.Callsign ?? string.Empty,
                localPosition,
                ownerTeamId,
                ResolveTeamDisplayName(ownerTeamId, "Neutral"),
                ResolveTeamColor(ownerTeamId),
                capturingTeamId,
                ResolveTeamDisplayName(capturingTeamId, string.Empty),
                ResolveTeamColor(capturingTeamId),
                captureProgress,
                Math.Max(0, point.FrontPointsPerInterval),
                contested));
        }

        results.Sort(static (left, right) => string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase));
        return results.ToArray();
    }

    private int ComputeOverlayRevision(
        IReadOnlyList<WH40KTacticalMapAllyMarker> alliedMarkers,
        IReadOnlyList<WH40KTacticalMapCapturePointMarker> capturePoints)
    {
        var hash = new HashCode();
        hash.Add(alliedMarkers.Count);

        foreach (var ally in alliedMarkers)
        {
            hash.Add(ally.Entity);
            hash.Add(ally.Label, StringComparer.OrdinalIgnoreCase);
            hash.Add((int) MathF.Round(ally.Position.X));
            hash.Add((int) MathF.Round(ally.Position.Y));
            hash.Add(ally.Color.ToArgb());
        }

        hash.Add(capturePoints.Count);

        foreach (var point in capturePoints)
        {
            hash.Add(point.Entity);
            hash.Add(point.Label, StringComparer.OrdinalIgnoreCase);
            hash.Add(point.Callsign, StringComparer.OrdinalIgnoreCase);
            hash.Add((int) MathF.Round(point.Position.X));
            hash.Add((int) MathF.Round(point.Position.Y));
            hash.Add(point.OwnerTeamId, StringComparer.OrdinalIgnoreCase);
            hash.Add(point.CapturingTeamId, StringComparer.OrdinalIgnoreCase);
            hash.Add((int) MathF.Round(point.CaptureProgress * 100f));
            hash.Add(point.FrontReward);
            hash.Add(point.Contested);
        }

        return hash.ToHashCode();
    }

    private static TimeSpan ResolveStateSyncInterval(string teamId)
    {
        return string.IsNullOrWhiteSpace(teamId)
            ? StateSyncNoTeamInterval
            : StateSyncInterval;
    }

    private static TimeSpan ResolveOverlayRefreshInterval(string teamId)
    {
        return string.IsNullOrWhiteSpace(teamId)
            ? OverlayRefreshIntervalNoTeam
            : OverlayRefreshInterval;
    }

    private Color ResolveTeamColor(string? teamId)
    {
        if (!string.IsNullOrWhiteSpace(teamId) &&
            _teamRule.TryGetTeamColor(teamId, out var teamColor))
        {
            return teamColor;
        }

        return NeutralMarkerColor;
    }

    private string ResolveTeamDisplayName(string? teamId, string fallback)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return fallback;

        return _teamRule.TryGetTeamDisplayName(teamId, out var displayName)
            ? displayName
            : teamId;
    }

    private string ResolveCapturePointLabel(EntityUid uid, WH40KInfluencePointComponent point)
    {
        if (!string.IsNullOrWhiteSpace(point.Callsign))
            return $"Точка {point.Callsign}";

        return TryComp(uid, out MetaDataComponent? meta)
            ? meta.EntityName
            : "Точка захвата";
    }

    private string BuildLocalizedCapturePointLabel(EntityUid uid, WH40KInfluencePointComponent point)
    {
        if (!string.IsNullOrWhiteSpace(point.Callsign))
        {
            return Loc.GetString(
                "wh40k-tactical-map-capture-label",
                ("callsign", LocalizeCallsign(point.Callsign)));
        }

        return TryComp(uid, out MetaDataComponent? meta)
            ? meta.EntityName
            : Loc.GetString("wh40k-tactical-map-capture-fallback");
    }

    private string LocalizeCallsign(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return string.Empty;

        var trimmed = callsign.Trim();
        var separator = trimmed.IndexOf('-', StringComparison.Ordinal);
        var baseToken = separator >= 0 ? trimmed[..separator] : trimmed;
        var suffix = separator >= 0 ? trimmed[separator..] : string.Empty;
        var key = $"wh40k-tactical-map-callsign-{baseToken.ToLowerInvariant()}";

        var localized = Loc.TryGetString(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : baseToken;

        return $"{localized}{suffix}";
    }

    public bool TryGetFogSnapshot(EntityUid gridUid, string teamId, out int chunkSize, out int revision, out Vector2i[] revealedChunks)
    {
        chunkSize = DefaultFogChunkSize;
        revision = 0;
        revealedChunks = Array.Empty<Vector2i>();

        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        if (!_teamFogStates.TryGetValue(new TeamMapKey(gridUid, teamId), out var fogState))
            return false;

        chunkSize = fogState.ChunkSize;
        revision = fogState.Revision;
        revealedChunks = fogState.RevealedChunks.ToArray();
        return true;
    }

    public int RevealFogChunks(EntityUid gridUid, string teamId, IEnumerable<Vector2i> chunks)
    {
        if (string.IsNullOrWhiteSpace(teamId))
            return 0;

        var fogState = GetOrCreateFogState(gridUid, teamId, ResolveFogGridConfig(gridUid));
        var changed = false;

        foreach (var chunk in chunks)
        {
            changed |= fogState.RevealedChunks.Add(chunk);
        }

        if (changed)
            fogState.Revision++;

        return fogState.Revision;
    }

    private bool ResolveViewerTeamId(EntityUid viewer, ActorComponent actor, out string teamId)
    {
        if (_teamRule.TryGetTeamIdFromEntity(viewer, out teamId))
            return true;

        return _teamRule.TryGetTeamIdForUser(actor.PlayerSession.UserId, out teamId);
    }

    private EntityUid? ResolveTargetGrid(Entity<WH40KTacticalMapComponent> map)
    {
        if (map.Comp.TargetGrid is { } targetGrid &&
            HasComp<MapGridComponent>(targetGrid))
        {
            return targetGrid;
        }

        var xform = Transform(map.Owner);
        return xform.GridUid is { } gridUid && HasComp<MapGridComponent>(gridUid)
            ? gridUid
            : null;
    }

    private void EnsureFogGridConfig(Entity<WH40KTacticalMapComponent> map, EntityUid? gridUid)
    {
        if (gridUid == null)
            return;

        _fogGridConfigs[gridUid.Value] = new FogGridConfig
        {
            ChunkSize = Math.Max(1, map.Comp.FogChunkSize),
            RevealRadiusChunks = Math.Max(0, map.Comp.FogRevealRadiusChunks),
        };
    }

    private FogGridConfig ResolveFogGridConfig(EntityUid gridUid)
    {
        if (_fogGridConfigs.TryGetValue(gridUid, out var config))
            return config;

        config = new FogGridConfig
        {
            ChunkSize = DefaultFogChunkSize,
            RevealRadiusChunks = DefaultFogRevealRadiusChunks,
        };

        _fogGridConfigs[gridUid] = config;
        return config;
    }

    private TeamFogState GetOrCreateFogState(EntityUid gridUid, string teamId, FogGridConfig config)
    {
        var key = new TeamMapKey(gridUid, teamId);
        if (_teamFogStates.TryGetValue(key, out var existing))
        {
            if (existing.ChunkSize == config.ChunkSize)
                return existing;

            _teamFogStates.Remove(key);
        }

        var created = new TeamFogState
        {
            ChunkSize = Math.Max(1, config.ChunkSize),
        };

        _teamFogStates[key] = created;
        return created;
    }

    private TeamAnnotationState GetOrCreateAnnotationState(EntityUid gridUid, string teamId)
    {
        var key = new TeamMapKey(gridUid, teamId);
        if (_teamAnnotationStates.TryGetValue(key, out var existing))
            return existing;

        var created = new TeamAnnotationState();
        _teamAnnotationStates[key] = created;
        return created;
    }

    private WH40KTacticalMapAnnotationStroke[] SanitizeAnnotationStrokes(
        IReadOnlyList<WH40KTacticalMapAnnotationStroke> incoming,
        EntityUid gridUid)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return Array.Empty<WH40KTacticalMapAnnotationStroke>();

        var bounds = grid.LocalAABB;
        var result = new List<WH40KTacticalMapAnnotationStroke>(Math.Min(incoming.Count, MaxAnnotationStrokes));
        var totalPoints = 0;

        foreach (var stroke in incoming)
        {
            if (result.Count >= MaxAnnotationStrokes || totalPoints >= MaxAnnotationPointsTotal)
                break;

            var remainingPointBudget = Math.Min(MaxAnnotationPointsPerStroke, MaxAnnotationPointsTotal - totalPoints);
            if (remainingPointBudget <= 0)
                break;

            var sanitizedPoints = new List<Vector2>(Math.Min(stroke.Points.Length, remainingPointBudget));
            for (var i = 0; i < stroke.Points.Length && sanitizedPoints.Count < remainingPointBudget; i++)
            {
                var point = stroke.Points[i];
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                    continue;

                var clamped = new Vector2(
                    Math.Clamp(point.X, bounds.Left, bounds.Right),
                    Math.Clamp(point.Y, bounds.Bottom, bounds.Top));

                if (sanitizedPoints.Count > 0 && Vector2.DistanceSquared(sanitizedPoints[^1], clamped) < 0.0001f)
                    continue;

                sanitizedPoints.Add(clamped);
            }

            if (sanitizedPoints.Count == 0)
                continue;

            var thickness = Math.Clamp(stroke.Thickness, MinAnnotationThickness, MaxAnnotationThickness);
            result.Add(new WH40KTacticalMapAnnotationStroke(sanitizedPoints.ToArray(), stroke.Color, thickness));
            totalPoints += sanitizedPoints.Count;
        }

        return result.ToArray();
    }

    private static bool AreAnnotationsEquivalent(
        IReadOnlyList<WH40KTacticalMapAnnotationStroke> existing,
        IReadOnlyList<WH40KTacticalMapAnnotationStroke> incoming)
    {
        if (existing.Count != incoming.Count)
            return false;

        for (var i = 0; i < existing.Count; i++)
        {
            var left = existing[i];
            var right = incoming[i];

            if (!left.Color.Equals(right.Color) || MathF.Abs(left.Thickness - right.Thickness) > 0.0001f)
                return false;

            if (left.Points.Length != right.Points.Length)
                return false;

            for (var pointIndex = 0; pointIndex < left.Points.Length; pointIndex++)
            {
                if (Vector2.DistanceSquared(left.Points[pointIndex], right.Points[pointIndex]) > 0.0001f)
                    return false;
            }
        }

        return true;
    }

    private static Vector2i WorldToFogChunk(Vector2 localPosition, int chunkSize)
    {
        return new Vector2i(
            (int) MathF.Floor(localPosition.X / chunkSize),
            (int) MathF.Floor(localPosition.Y / chunkSize));
    }
}
