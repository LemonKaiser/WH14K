using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Input;
using Content.Shared._WH40K.TacticalMap;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WH40K.TacticalMap.UI;

/// <summary>
/// Dedicated tactical-map viewport.
/// Fits the battlefield into the real control size,
/// keeps local drafts separate from the snapshot, and handles its own pan/zoom input.
/// </summary>
public sealed class WH40KTacticalMapControl : Control
{
    private const float LiveRefreshCaptureDelaySeconds = 0.75f;
    private const float DefaultAnnotationThickness = 1.5f;
    private const float MinAnnotationThickness = 0.25f;
    private const float MaxAnnotationThickness = 6f;
    private const int MaxLocalStrokePoints = 2048;
    private const float DefaultZoom = 1f;
    private const float MinZoom = 1f;
    private const float MaxZoom = 6f;
    private const float ViewMarginPixels = 16f;
    private const float AllyMarkerHoverRadius = 12f;
    private static readonly Color TacticalBlackoutColor = Color.Black;

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SharedTransformSystem _transformSystem;
    private readonly SpriteSystem _sprite;
    private readonly SharedWH40KTacticalMapBlackoutSystem _blackoutSystem;
    private readonly Font _headerFont;
    private readonly Font _detailFont;
    private readonly Texture _fogNoiseTexture;
    private readonly OwnedTexture? _ownedFogNoiseTexture;
    private readonly EntityQuery<WH40KTacticalMapBlackoutComponent> _blackoutQuery;

    private OwnedTexture? _snapshotTexture;
    private IClydeViewport? _refreshViewport;
    private ResPath? _loadedTexturePath;

    private MapGridComponent? _grid;
    private TransformComponent? _gridTransform;
    private WH40KTacticalMapBlackoutComponent? _blackout;
    private EntityUid? _currentGridUid;

    private readonly List<(EntityUid Uid, bool WasVisible)> _temporarilyHiddenSprites = new();
    private readonly HashSet<Vector2i> _revealedFogChunks = new();
    private readonly List<WH40KTacticalMapAnnotationStroke> _savedAnnotations = new();
    private readonly List<WH40KTacticalMapAnnotationStroke> _workingAnnotations = new();
    private readonly List<Vector2> _activeStrokePoints = new();
    private readonly List<WH40KTacticalMapAllyMarker> _alliedMarkers = new();
    private readonly List<WH40KTacticalMapCapturePointMarker> _capturePointMarkers = new();

    private int _queuedLiveRefreshRevision = -1;
    private int _capturedLiveRefreshRevision = -1;
    private int _liveRefreshRevision = -1;
    private TimeSpan _captureNotBefore = TimeSpan.Zero;
    private LiveRefreshChunk? _pendingLiveRefresh;
    private EntityUid? _liveRefreshEyeUid;
    private Vector2i _liveRefreshChunkOrigin = Vector2i.Zero;
    private Vector2i _liveRefreshChunkSizeActual = Vector2i.Zero;
    private bool _liveRefreshActive;
    private bool _liveRefreshEnabled;

    private WH40KTacticalMapAnnotationTool _annotationTool = WH40KTacticalMapAnnotationTool.Pan;
    private Color _annotationColor = Color.Red;
    private float _annotationThickness = DefaultAnnotationThickness;
    private bool _annotationDirty;
    private bool _remoteAnnotationDirty;
    private bool _savePending;
    private WH40KTacticalMapAnnotationStroke[]? _pendingSaveSnapshot;
    private int _savedAnnotationRevision = -1;

    private BoundKeyFunction? _activePointerFunction;
    private bool _isPanning;
    private bool _isAnnotating;
    private Vector2 _lastPointerPixel;
    private Vector2 _lastAnnotationPixel;
    private Vector2 _viewCenter;
    private bool _viewInitialized;
    private bool _showChunkGrid;
    private float _zoom = DefaultZoom;
    private float _lastFitScale = 1f;
    private bool _showAllies = true;
    private bool _showAllyNames;
    private bool _annotationsEnabled = true;
    private NetEntity? _hoveredAllyEntity;

    public event Action? AnnotationStateChanged;
    public event Action? ViewChanged;

    public EntityUid? MapUid;
    public EntityUid? TacticalMapUid;
    public ResPath SnapshotTexturePath = new("/Textures/_WH40K/Interface/TacticalMap/battlefield40k_snapshot.png");
    public Dictionary<EntityCoordinates, (bool Visible, Color Color)> TrackedCoordinates = new();
    public bool FogEnabled = true;
    public int FogChunkSize = 8;
    public string TeamId = string.Empty;
    public bool HasUnsavedAnnotations => _annotationDirty;
    public bool HasRemoteAnnotationChanges => _remoteAnnotationDirty;
    public bool HasSavedAnnotations => _savedAnnotations.Count > 0;
    public bool HasAnyWorkingAnnotations => _workingAnnotations.Count > 0;
    public bool IsSavePending => _savePending;
    public bool CanSaveAnnotations => _annotationsEnabled && !string.IsNullOrWhiteSpace(TeamId);
    public int AlliedMarkerCount => _alliedMarkers.Count;
    public int CapturePointCount => _capturePointMarkers.Count;
    public float ZoomFactor => _zoom;
    public WH40KTacticalMapAnnotationTool AnnotationTool => _annotationTool;
    public bool AnnotationsEnabled
    {
        get => _annotationsEnabled;
        set
        {
            if (_annotationsEnabled == value)
                return;

            _annotationsEnabled = value;
            CancelInteraction(clearDraftPreview: true);

            if (!_annotationsEnabled)
            {
                _annotationTool = WH40KTacticalMapAnnotationTool.Pan;
                _savePending = false;
                _pendingSaveSnapshot = null;
                _remoteAnnotationDirty = false;

                if (_annotationDirty)
                    ReplaceWorkingAnnotations(_savedAnnotations, false);
            }

            AnnotationStateChanged?.Invoke();
            InvalidateArrange();
        }
    }
    public bool ShowAllies
    {
        get => _showAllies;
        set
        {
            if (_showAllies == value)
                return;

            _showAllies = value;
            if (!_showAllies)
                SetHoveredAlly(null);
            InvalidateArrange();
        }
    }
    public bool ShowChunkGrid
    {
        get => _showChunkGrid;
        set
        {
            if (_showChunkGrid == value)
                return;

            _showChunkGrid = value;
            InvalidateArrange();
        }
    }
    public bool ShowAllyNames
    {
        get => _showAllyNames;
        set
        {
            if (_showAllyNames == value)
                return;

            _showAllyNames = value;
            InvalidateArrange();
        }
    }

    private readonly record struct ViewState(Box2 Bounds, Vector2 ScreenCenter, float Scale, Vector2 ViewCenter)
    {
        public Vector2 WorldToScreen(Vector2 localPosition)
        {
            return new Vector2(
                ScreenCenter.X + (localPosition.X - ViewCenter.X) * Scale,
                ScreenCenter.Y + (ViewCenter.Y - localPosition.Y) * Scale);
        }

        public Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            return new Vector2(
                ViewCenter.X + (screenPosition.X - ScreenCenter.X) / Scale,
                ViewCenter.Y - (screenPosition.Y - ScreenCenter.Y) / Scale);
        }
    }

    private readonly record struct LiveRefreshChunk(
        int Revision,
        EntityUid EyeUid,
        Vector2i PixelOrigin,
        Vector2i PixelSize,
        Box2 LocalBounds);

    public WH40KTacticalMapControl()
    {
        IoCManager.InjectDependencies(this);
        _transformSystem = _entMan.System<SharedTransformSystem>();
        _sprite = _entMan.System<SpriteSystem>();
        _blackoutSystem = _entMan.System<SharedWH40KTacticalMapBlackoutSystem>();
        _blackoutQuery = _entMan.GetEntityQuery<WH40KTacticalMapBlackoutComponent>();

        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
        MinWidth = 320f;
        MinHeight = 240f;

        var cache = IoCManager.Resolve<IResourceCache>();
        _headerFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf"), 15);
        _detailFont = new VectorFont(cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"), 11);

        try
        {
            using var stream = _resources.ContentFileRead(new ResPath("/Textures/Parallaxes/noise.png"));
            _ownedFogNoiseTexture = _clyde.LoadTextureFromPNGStream(
                stream,
                "wh40k-tactical-fog-noise",
                new TextureLoadParameters
                {
                    SampleParameters = new TextureSampleParameters
                    {
                        Filter = true,
                        WrapMode = TextureWrapMode.MirroredRepeat,
                    },
                    Srgb = true,
                    Preload = false,
                });
            _fogNoiseTexture = _ownedFogNoiseTexture;
        }
        catch
        {
            _fogNoiseTexture = cache.GetResource<TextureResource>("/Textures/Parallaxes/noise.png").Texture;
        }
    }

    public void ForceSnapshotUpdate()
    {
        _entMan.TryGetComponent(MapUid, out _grid);
        _entMan.TryGetComponent(MapUid, out _gridTransform);
        _blackout = null;

        if (MapUid is { } mapUid)
            _blackoutQuery.TryComp(mapUid, out _blackout);

        if (_currentGridUid != MapUid)
        {
            _currentGridUid = MapUid;
            _viewInitialized = false;
        }
    }

    public void ResetView()
    {
        ForceSnapshotUpdate();

        if (_grid == null)
            return;

        var bounds = _grid.LocalAABB;
        _viewCenter = GetDefaultViewCenter(bounds);
        _zoom = DefaultZoom;
        _viewInitialized = true;
        ViewChanged?.Invoke();
        InvalidateArrange();
    }

    public void CenterToCoordinates(EntityCoordinates coordinates)
    {
        if (_gridTransform == null || _grid == null)
            return;

        var mapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
        if (mapCoordinates.MapId == MapId.Nullspace)
            return;

        var localPosition = Vector2.Transform(mapCoordinates.Position, _transformSystem.GetInvWorldMatrix(_gridTransform));
        _viewCenter = ClampViewCenter(localPosition, _grid.LocalAABB);
        _viewInitialized = true;
        ViewChanged?.Invoke();
        InvalidateArrange();
    }

    public void ApplyState(WH40KTacticalMapBuiState state)
    {
        var previousTeamId = TeamId;
        var previousDirty = _annotationDirty;
        var previousRemoteDirty = _remoteAnnotationDirty;
        var previousSavePending = _savePending;

        AnnotationsEnabled = state.CanAnnotate;
        TeamId = state.TeamId;
        FogEnabled = state.FogEnabled;
        FogChunkSize = Math.Max(1, state.FogChunkSize);
        _liveRefreshEnabled = state.LiveRefreshEnabled;
        _revealedFogChunks.Clear();

        foreach (var chunk in state.RevealedChunks)
        {
            _revealedFogChunks.Add(chunk);
        }

        _alliedMarkers.Clear();
        _alliedMarkers.AddRange(state.AlliedMarkers);
        _capturePointMarkers.Clear();
        _capturePointMarkers.AddRange(state.CapturePoints);

        var incomingAnnotations = CloneAnnotations(state.AnnotationStrokes);
        var remoteChanged = state.AnnotationRevision != _savedAnnotationRevision ||
                            !AreAnnotationsEquivalent(_savedAnnotations, incomingAnnotations);
        var matchesPendingSave = _savePending &&
                                 _pendingSaveSnapshot != null &&
                                 AreAnnotationsEquivalent(_pendingSaveSnapshot, incomingAnnotations);
        var teamChanged = !string.Equals(previousTeamId, TeamId, StringComparison.Ordinal);

        _savedAnnotations.Clear();
        _savedAnnotations.AddRange(incomingAnnotations);
        _savedAnnotationRevision = state.AnnotationRevision;

        if (teamChanged || !_annotationDirty || matchesPendingSave)
        {
            ReplaceWorkingAnnotations(incomingAnnotations, false);
            _remoteAnnotationDirty = false;
            _savePending = false;
            _pendingSaveSnapshot = null;
        }
        else if (remoteChanged)
        {
            _remoteAnnotationDirty = true;
        }

        if (teamChanged)
            CancelInteraction(clearDraftPreview: true);

        if (previousDirty != _annotationDirty ||
            previousRemoteDirty != _remoteAnnotationDirty ||
            previousSavePending != _savePending ||
            !string.Equals(previousTeamId, TeamId, StringComparison.Ordinal))
        {
            AnnotationStateChanged?.Invoke();
        }

        InvalidateArrange();
    }

    public void ApplyOverlayState(WH40KTacticalMapOverlayState state)
    {
        _alliedMarkers.Clear();
        _alliedMarkers.AddRange(state.AlliedMarkers);
        _capturePointMarkers.Clear();
        _capturePointMarkers.AddRange(state.CapturePoints);
        InvalidateArrange();
    }

    public void ApplyLiveRefreshState(WH40KTacticalMapLiveRefreshState state)
    {
        _liveRefreshActive = state.Active;
        _liveRefreshRevision = state.Revision;
        _liveRefreshEyeUid = state.Active ? _entMan.GetEntity(state.Eye) : null;
        _liveRefreshChunkOrigin = state.TileOrigin;
        _liveRefreshChunkSizeActual = state.TileSize;

        if (!state.Active)
        {
            _pendingLiveRefresh = null;
            _queuedLiveRefreshRevision = -1;
        }

        InvalidateArrange();
    }

    public void SetAnnotationTool(WH40KTacticalMapAnnotationTool tool)
    {
        if (!_annotationsEnabled)
            tool = WH40KTacticalMapAnnotationTool.Pan;

        if (_annotationTool == tool)
            return;

        CancelInteraction(clearDraftPreview: true);
        _annotationTool = tool;
        InvalidateArrange();
    }

    public void SetAnnotationColor(Color color)
    {
        if (!_annotationsEnabled)
            return;

        _annotationColor = color;
        InvalidateArrange();
    }

    public void SetAnnotationThickness(float thickness)
    {
        if (!_annotationsEnabled)
            return;

        _annotationThickness = Math.Clamp(thickness, MinAnnotationThickness, MaxAnnotationThickness);
        InvalidateArrange();
    }

    public void ClearWorkingAnnotations()
    {
        if (!_annotationsEnabled)
            return;

        CancelInteraction(clearDraftPreview: true);

        if (_workingAnnotations.Count == 0)
            return;

        _workingAnnotations.Clear();
        SetAnnotationDirty(true);
        InvalidateArrange();
    }

    public void ReloadSavedAnnotations()
    {
        if (!_annotationsEnabled)
            return;

        CancelInteraction(clearDraftPreview: true);
        ReplaceWorkingAnnotations(_savedAnnotations, false);
        _remoteAnnotationDirty = false;
        _savePending = false;
        _pendingSaveSnapshot = null;
        AnnotationStateChanged?.Invoke();
        InvalidateArrange();
    }

    public void MarkSaveRequested()
    {
        if (!_annotationsEnabled)
            return;

        _savePending = true;
        _pendingSaveSnapshot = CloneAnnotations(_workingAnnotations);
        AnnotationStateChanged?.Invoke();
    }

    public WH40KTacticalMapSaveAnnotationsMessage BuildSaveMessage()
    {
        return new WH40KTacticalMapSaveAnnotationsMessage(
            _annotationsEnabled
                ? CloneAnnotations(_workingAnnotations)
                : Array.Empty<WH40KTacticalMapAnnotationStroke>());
    }

    protected override void ExitedTree()
    {
        CancelInteraction(clearDraftPreview: true);
        DisposeRefreshViewport();
        DisposeSnapshotTexture();
        base.ExitedTree();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (MapUid != null)
            InvalidateArrange();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == ContentKeyFunctions.MouseMiddle)
        {
            if (!TryGetViewState(out _))
                return;

            if (_isAnnotating)
                CommitActiveAnnotation();

            SetHoveredAlly(null);
            _activePointerFunction = args.Function;
            _isPanning = true;
            _lastPointerPixel = args.RelativePixelPosition;
            _viewInitialized = true;
            args.Handle();
            return;
        }

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (!_annotationsEnabled)
        {
            if (!TryGetViewState(out _))
                return;

            SetHoveredAlly(null);
            _activePointerFunction = args.Function;
            _isPanning = true;
            _lastPointerPixel = args.RelativePixelPosition;
            _viewInitialized = true;
            args.Handle();
            return;
        }

        if (_annotationTool == WH40KTacticalMapAnnotationTool.Pan)
        {
            if (!TryGetViewState(out _))
                return;

            SetHoveredAlly(null);
            _activePointerFunction = args.Function;
            _isPanning = true;
            _lastPointerPixel = args.RelativePixelPosition;
            _viewInitialized = true;
            args.Handle();
            return;
        }

        if (!TryGetDrawableLocalPosition(args.RelativePixelPosition, out var localPosition))
            return;

        _activePointerFunction = args.Function;
        _isAnnotating = true;
        _lastPointerPixel = args.RelativePixelPosition;
        _lastAnnotationPixel = args.RelativePixelPosition;
        _viewInitialized = true;
        SetHoveredAlly(null);
        _activeStrokePoints.Clear();
        AppendActivePoint(localPosition, args.RelativePixelPosition);

        if (_annotationTool == WH40KTacticalMapAnnotationTool.Eraser)
            ApplyEraserAt(localPosition);

        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (_activePointerFunction != args.Function)
            return;

        if (_isAnnotating)
        {
            CommitActiveAnnotation();
            args.Handle();
            return;
        }

        if (_isPanning)
        {
            CancelInteraction(clearDraftPreview: false);
            args.Handle();
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_isPanning)
        {
            if (!TryGetViewState(out var view))
                return;

            SetHoveredAlly(null);
            var delta = args.RelativePixelPosition - _lastPointerPixel;
            _lastPointerPixel = args.RelativePixelPosition;
            _viewCenter = ClampViewCenter(
                _viewCenter - new Vector2(delta.X, -delta.Y) / MathF.Max(0.001f, view.Scale),
                view.Bounds);
            ViewChanged?.Invoke();
            InvalidateArrange();
            args.Handle();
            return;
        }

        if (_isAnnotating)
        {
            _lastPointerPixel = args.RelativePixelPosition;

            if (TryGetDrawableLocalPosition(args.RelativePixelPosition, out var localPosition, clampToBounds: true))
            {
                AppendActivePoint(localPosition, args.RelativePixelPosition);

                if (_annotationTool == WH40KTacticalMapAnnotationTool.Eraser)
                    ApplyEraserAt(localPosition);
            }

            InvalidateArrange();
            args.Handle();
            SetHoveredAlly(null);
            return;
        }

        UpdateHoveredAlly(args.RelativePixelPosition);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        SetHoveredAlly(null);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (!TryGetViewState(out var view))
            return;

        var before = view.ScreenToWorld(args.RelativePixelPosition);
        var zoomFactor = MathF.Pow(1.15f, args.Delta.Y);
        _zoom = Math.Clamp(_zoom * zoomFactor, MinZoom, MaxZoom);
        _viewInitialized = true;

        if (TryGetViewState(out var newView))
        {
            var after = newView.ScreenToWorld(args.RelativePixelPosition);
            _viewCenter = ClampViewCenter(_viewCenter + (before - after), newView.Bounds);
        }

        ViewChanged?.Invoke();
        InvalidateArrange();
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        ForceSnapshotUpdate();
        DrawBackground(handle);

        if (!TryGetViewState(out var view) || _grid == null || _gridTransform == null)
        {
            DrawLocalizedNoSignal(handle);
            return;
        }

        if (!TryEnsureSnapshotTexture())
        {
            DrawLocalizedNoSignal(handle);
            return;
        }

        MaybeApplyLiveRefresh();

        var mapRect = GetMapRect(view, _grid.LocalAABB);
        handle.DrawTextureRect(_snapshotTexture!, mapRect);
        DrawFog(handle, view, _grid.LocalAABB);
        DrawChunkGrid(handle, view, mapRect, _grid.LocalAABB);
        DrawAnnotations(handle, view);
        DrawCapturePoints(handle, view);
        DrawAlliedMarkers(handle, view);
        DrawTrackedEntities(handle, view);
        DrawTacticalBlackoutMasks(handle, view, _grid.LocalAABB);
        DrawMapFrame(handle, mapRect);
    }

    private bool TryGetViewState(out ViewState view)
    {
        view = default;
        ForceSnapshotUpdate();

        if (_grid == null || PixelWidth <= 0 || PixelHeight <= 0)
            return false;

        var bounds = _grid.LocalAABB;
        var usableWidth = Math.Max(1f, PixelWidth - ViewMarginPixels * 2f);
        var usableHeight = Math.Max(1f, PixelHeight - ViewMarginPixels * 2f);
        var fitScale = MathF.Min(usableWidth / Math.Max(1f, bounds.Width), usableHeight / Math.Max(1f, bounds.Height));
        if (!float.IsFinite(fitScale) || fitScale <= 0f)
            fitScale = 1f;

        _lastFitScale = fitScale;

        if (!_viewInitialized)
        {
            _viewCenter = GetDefaultViewCenter(bounds);
            _zoom = DefaultZoom;
            _viewInitialized = true;
            ViewChanged?.Invoke();
        }

        view = new ViewState(
            bounds,
            new Vector2(PixelWidth * 0.5f, PixelHeight * 0.5f),
            fitScale * _zoom,
            _viewCenter);

        return true;
    }

    private Vector2 GetDefaultViewCenter(Box2 bounds)
    {
        if (TryGetTrackedLocalPosition(out var tracked))
            return ClampViewCenter(tracked, bounds);

        return new Vector2(
            (bounds.Left + bounds.Right) * 0.5f,
            (bounds.Bottom + bounds.Top) * 0.5f);
    }

    private Vector2 ClampViewCenter(Vector2 center, Box2 bounds)
    {
        var extraX = Math.Max(8f, bounds.Width * 0.2f);
        var extraY = Math.Max(8f, bounds.Height * 0.2f);

        return new Vector2(
            Math.Clamp(center.X, bounds.Left - extraX, bounds.Right + extraX),
            Math.Clamp(center.Y, bounds.Bottom - extraY, bounds.Top + extraY));
    }

    private bool TryGetTrackedLocalPosition(out Vector2 localPosition)
    {
        localPosition = default;

        if (_gridTransform == null)
            return false;

        foreach (var (coordinates, _) in TrackedCoordinates)
        {
            var mapPosition = _transformSystem.ToMapCoordinates(coordinates);
            if (mapPosition.MapId == MapId.Nullspace)
                continue;

            localPosition = Vector2.Transform(mapPosition.Position, _transformSystem.GetInvWorldMatrix(_gridTransform));
            return true;
        }

        return false;
    }

    private void DrawBackground(DrawingHandleScreen handle)
    {
        handle.DrawRect(PixelSizeBox, Color.FromHex("#080B10".AsSpan()));
        handle.DrawRect(PixelSizeBox, Color.FromHex("#26374A".AsSpan()), filled: false);
    }

    private void DrawLocalizedNoSignal(DrawingHandleScreen handle)
    {
        var title = Loc.GetString("wh40k-tactical-map-no-signal-title");
        var detail = Loc.GetString("wh40k-tactical-map-no-signal-detail");
        var titleSize = handle.GetDimensions(_headerFont, title.AsSpan(), 1f);
        var detailSize = handle.GetDimensions(_detailFont, detail.AsSpan(), 1f);
        var center = new Vector2(PixelWidth * 0.5f, PixelHeight * 0.5f);

        handle.DrawString(_headerFont, center - new Vector2(titleSize.X * 0.5f, titleSize.Y + 4f), title, Color.FromHex("#D3AE72".AsSpan()));
        handle.DrawString(_detailFont, center - new Vector2(detailSize.X * 0.5f, -8f), detail, Color.FromHex("#8996A7".AsSpan()));
    }

    private void DrawNoSignal(DrawingHandleScreen handle)
    {
        var title = "НЕТ КАНАЛА КАРТЫ";
        var detail = "Тактическая карта ожидает корректную боевую сетку.";

        var titleSize = handle.GetDimensions(_headerFont, title.AsSpan(), 1f);
        var detailSize = handle.GetDimensions(_detailFont, detail.AsSpan(), 1f);
        var center = new Vector2(PixelWidth * 0.5f, PixelHeight * 0.5f);

        handle.DrawString(_headerFont, center - new Vector2(titleSize.X * 0.5f, titleSize.Y + 4f), title, Color.FromHex("#D3AE72".AsSpan()));
        handle.DrawString(_detailFont, center - new Vector2(detailSize.X * 0.5f, -8f), detail, Color.FromHex("#8996A7".AsSpan()));
    }

    private UIBox2 GetMapRect(ViewState view, Box2 bounds)
    {
        var topLeft = view.WorldToScreen(new Vector2(bounds.Left, bounds.Top));
        var bottomRight = view.WorldToScreen(new Vector2(bounds.Right, bounds.Bottom));
        return new UIBox2(topLeft, bottomRight);
    }

    private void DrawMapFrame(DrawingHandleScreen handle, UIBox2 mapRect)
    {
        handle.DrawRect(mapRect, Color.FromHex("#4B5F78".AsSpan()), filled: false);
        handle.DrawRect(new UIBox2(mapRect.Left + 1f, mapRect.Top + 1f, mapRect.Right - 1f, mapRect.Bottom - 1f),
            Color.FromHex("#182330".AsSpan()), filled: false);
    }

    private void DrawFog(DrawingHandleScreen handle, ViewState view, Box2 bounds)
    {
        if (!FogEnabled)
            return;

        var mapRect = GetMapRect(view, bounds);
        DrawHiddenFogField(handle, mapRect, bounds);

        if (_snapshotTexture == null)
            return;

        var chunkSize = Math.Max(1, FogChunkSize);
        foreach (var chunk in _revealedFogChunks)
        {
            if (!TryGetFogChunkRectangles(view, bounds, chunk, chunkSize, out var chunkRect, out var sourceRect))
                continue;

            handle.DrawTextureRectRegion(_snapshotTexture, chunkRect, sourceRect);
        }
    }

    private void DrawHiddenFogField(DrawingHandleScreen handle, UIBox2 mapRect, Box2 bounds)
    {
        var fogBackdrop = Color.FromHex("#04070B".AsSpan()).WithAlpha(0.92f);
        handle.DrawRect(mapRect, fogBackdrop);
        DrawFogFieldLayer(handle, _fogNoiseTexture, mapRect, bounds, 11, 0.92f, Color.FromHex("#94A4B6".AsSpan()).WithAlpha(0.26f));
        DrawFogFieldLayer(handle, _fogNoiseTexture, mapRect, bounds, 29, 0.62f, Color.FromHex("#CAD3DD".AsSpan()).WithAlpha(0.12f));
        DrawFogFieldLayer(handle, _fogNoiseTexture, mapRect, bounds, 47, 0.38f, Color.FromHex("#566373".AsSpan()).WithAlpha(0.18f));
        handle.DrawRect(mapRect, Color.Black.WithAlpha(0.20f));
    }

    private void DrawFogFieldLayer(
        DrawingHandleScreen handle,
        Texture texture,
        UIBox2 mapRect,
        Box2 bounds,
        int salt,
        float sampleScale,
        Color tint)
    {
        if (_ownedFogNoiseTexture != null)
        {
            DrawRepeatedFogFieldLayer(handle, texture, mapRect, bounds, salt, sampleScale, tint);
            return;
        }

        var sourceRect = SampleFogFieldRegion(texture, salt, sampleScale);
        handle.DrawTextureRectRegion(texture, mapRect, sourceRect, tint);
    }

    private static void DrawRepeatedFogFieldLayer(
        DrawingHandleScreen handle,
        Texture texture,
        UIBox2 mapRect,
        Box2 bounds,
        int salt,
        float sampleScale,
        Color tint)
    {
        if (mapRect.Width <= 1f || mapRect.Height <= 1f || bounds.Width <= 0f || bounds.Height <= 0f)
            return;

        var hash = FogSaltHash(salt);
        var offset = new Vector2(
            ((hash & 0xFF) / 255f) * 3.5f,
            (((hash >> 8) & 0xFF) / 255f) * 3.5f);
        var repeatDensity = 0.02f + sampleScale * 0.075f;
        var repeatX = Math.Max(1.25f, bounds.Width * repeatDensity);
        var repeatY = Math.Max(1.25f, bounds.Height * repeatDensity);

        var vertices = new[]
        {
            new DrawVertexUV2D(new Vector2(mapRect.Left, mapRect.Top), new Vector2(offset.X, offset.Y)),
            new DrawVertexUV2D(new Vector2(mapRect.Right, mapRect.Top), new Vector2(offset.X + repeatX, offset.Y)),
            new DrawVertexUV2D(new Vector2(mapRect.Left, mapRect.Bottom), new Vector2(offset.X, offset.Y + repeatY)),
            new DrawVertexUV2D(new Vector2(mapRect.Right, mapRect.Bottom), new Vector2(offset.X + repeatX, offset.Y + repeatY)),
        };

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, texture, vertices, tint);
    }

    private bool TryGetFogChunkRectangles(
        ViewState view,
        Box2 bounds,
        Vector2i chunk,
        int chunkSize,
        out UIBox2 chunkRect,
        out UIBox2 sourceRect)
    {
        chunkRect = default;
        sourceRect = default;

        if (_snapshotTexture == null || bounds.Width <= 0f || bounds.Height <= 0f)
            return false;

        var left = MathF.Max(bounds.Left, chunk.X * chunkSize);
        var right = MathF.Min(bounds.Right, (chunk.X + 1) * chunkSize);
        var bottom = MathF.Max(bounds.Bottom, chunk.Y * chunkSize);
        var top = MathF.Min(bounds.Top, (chunk.Y + 1) * chunkSize);

        if (right <= left || top <= bottom)
            return false;

        var topLeft = view.WorldToScreen(new Vector2(left, top));
        var bottomRight = view.WorldToScreen(new Vector2(right, bottom));
        chunkRect = new UIBox2(topLeft, bottomRight);

        var textureWidth = _snapshotTexture.Width;
        var textureHeight = _snapshotTexture.Height;
        var sourceLeft = (left - bounds.Left) / bounds.Width * textureWidth;
        var sourceRight = (right - bounds.Left) / bounds.Width * textureWidth;
        var sourceTop = (bounds.Top - top) / bounds.Height * textureHeight;
        var sourceBottom = (bounds.Top - bottom) / bounds.Height * textureHeight;
        sourceRect = new UIBox2(sourceLeft, sourceTop, sourceRight, sourceBottom);
        return true;
    }

    private static UIBox2 SampleFogFieldRegion(Texture texture, int salt, float sampleScale)
    {
        var clampedScale = Math.Clamp(sampleScale, 0.15f, 1f);
        var regionWidth = MathF.Max(8f, texture.Width * clampedScale);
        var regionHeight = MathF.Max(8f, texture.Height * clampedScale);
        var maxLeft = MathF.Max(0f, texture.Width - regionWidth);
        var maxTop = MathF.Max(0f, texture.Height - regionHeight);
        var hash = FogSaltHash(salt);
        var left = maxLeft <= 0f ? 0f : ((hash & 0xFF) / 255f) * maxLeft;
        var top = maxTop <= 0f ? 0f : (((hash >> 8) & 0xFF) / 255f) * maxTop;
        return new UIBox2(left, top, left + regionWidth, top + regionHeight);
    }

    private static int FogSaltHash(int salt)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + salt;
            hash ^= hash << 13;
            hash ^= (int) ((uint) hash >> 17);
            hash ^= hash << 5;
            return hash & int.MaxValue;
        }
    }

    private void DrawChunkGrid(DrawingHandleScreen handle, ViewState view, UIBox2 mapRect, Box2 bounds)
    {
        if (!ShowChunkGrid)
            return;

        var chunkSize = Math.Max(1, FogChunkSize);
        var chunkScreenSize = chunkSize * view.Scale;
        var innerAlpha = chunkScreenSize < 10f ? 0.34f : chunkScreenSize < 18f ? 0.44f : 0.56f;
        var innerWidth = chunkScreenSize >= 18f ? 2f : 1f;
        var shadowWidth = innerWidth + 2f;
        var shadowColor = Color.FromHex("#06101A".AsSpan()).WithAlpha(0.78f);
        var lineColor = Color.FromHex("#89B4DF".AsSpan()).WithAlpha(innerAlpha);

        var firstVertical = MathF.Floor(bounds.Left / chunkSize) * chunkSize;
        var lastVertical = MathF.Ceiling(bounds.Right / chunkSize) * chunkSize;
        for (var x = firstVertical; x <= lastVertical; x += chunkSize)
        {
            var screenX = view.WorldToScreen(new Vector2(x, bounds.Top)).X;
            DrawVerticalGridLine(handle, screenX, mapRect.Top, mapRect.Bottom, shadowWidth, shadowColor);
            DrawVerticalGridLine(handle, screenX, mapRect.Top, mapRect.Bottom, innerWidth, lineColor);
        }

        var firstHorizontal = MathF.Floor(bounds.Bottom / chunkSize) * chunkSize;
        var lastHorizontal = MathF.Ceiling(bounds.Top / chunkSize) * chunkSize;
        for (var y = firstHorizontal; y <= lastHorizontal; y += chunkSize)
        {
            var screenY = view.WorldToScreen(new Vector2(bounds.Left, y)).Y;
            DrawHorizontalGridLine(handle, screenY, mapRect.Left, mapRect.Right, shadowWidth, shadowColor);
            DrawHorizontalGridLine(handle, screenY, mapRect.Left, mapRect.Right, innerWidth, lineColor);
        }
    }

    private static List<Box2> BuildMergedTileRects(HashSet<Vector2i> tiles, float tileSize)
    {
        var rects = new List<Box2>();
        if (tiles.Count == 0)
            return rects;

        var remaining = new HashSet<Vector2i>(tiles);
        while (remaining.Count > 0)
        {
            var start = default(Vector2i);
            foreach (var tile in remaining)
            {
                start = tile;
                break;
            }

            var maxX = start.X;
            while (remaining.Contains(new Vector2i(maxX + 1, start.Y)))
            {
                maxX++;
            }

            var height = 1;
            while (true)
            {
                var nextY = start.Y + height;
                var fullRow = true;
                for (var x = start.X; x <= maxX; x++)
                {
                    if (remaining.Contains(new Vector2i(x, nextY)))
                        continue;

                    fullRow = false;
                    break;
                }

                if (!fullRow)
                    break;

                height++;
            }

            for (var x = start.X; x <= maxX; x++)
            {
                for (var y = start.Y; y < start.Y + height; y++)
                {
                    remaining.Remove(new Vector2i(x, y));
                }
            }

            rects.Add(new Box2(
                start.X * tileSize,
                start.Y * tileSize,
                (maxX + 1) * tileSize,
                (start.Y + height) * tileSize));
        }

        return rects;
    }

    private static Box2 GetTileBounds(Vector2i tile, float tileSize)
    {
        var left = tile.X * tileSize;
        var bottom = tile.Y * tileSize;
        return new Box2(left, bottom, left + tileSize, bottom + tileSize);
    }

    private static UIBox2 GetWorldScreenRect(ViewState view, Box2 bounds)
    {
        var topLeft = view.WorldToScreen(new Vector2(bounds.Left, bounds.Top));
        var bottomRight = view.WorldToScreen(new Vector2(bounds.Right, bounds.Bottom));
        return new UIBox2(topLeft, bottomRight);
    }

    private static void DrawWorldRect(
        DrawingHandleScreen handle,
        ViewState view,
        Box2 bounds,
        Color color,
        bool filled = true,
        float expandPixels = 0f)
    {
        var screenRect = GetWorldScreenRect(view, bounds);
        if (filled && expandPixels > 0f)
        {
            screenRect = new UIBox2(
                screenRect.Left - expandPixels,
                screenRect.Top - expandPixels,
                screenRect.Right + expandPixels,
                screenRect.Bottom + expandPixels);
        }

        handle.DrawRect(screenRect, color, filled);
    }

    private void DrawTacticalBlackoutMasks(DrawingHandleScreen handle, ViewState view, Box2 bounds)
    {
        if (_grid == null || _blackout == null || MapUid is not { } mapUid)
            return;

        var tileSize = MathF.Max(0.001f, _grid.TileSize);
        var maskedTiles = new HashSet<Vector2i>();
        var minTileX = (int) MathF.Floor(bounds.Left / tileSize);
        var maxTileX = (int) MathF.Ceiling(bounds.Right / tileSize) - 1;
        var minTileY = (int) MathF.Floor(bounds.Bottom / tileSize);
        var maxTileY = (int) MathF.Ceiling(bounds.Top / tileSize) - 1;

        for (var tileX = minTileX; tileX <= maxTileX; tileX++)
        {
            for (var tileY = minTileY; tileY <= maxTileY; tileY++)
            {
                var tile = new Vector2i(tileX, tileY);
                if (!_blackoutSystem.IsBlackedOut((mapUid, _grid, _blackout), tile))
                    continue;

                // Keep mapper blackout under unrevealed fog, but preserve hard masking in explored territory.
                if (FogEnabled && !IsTileRevealed(tile, tileSize))
                    continue;

                maskedTiles.Add(tile);
            }
        }

        foreach (var maskRect in BuildMergedTileRects(maskedTiles, tileSize))
        {
            DrawWorldRect(handle, view, maskRect, TacticalBlackoutColor, expandPixels: 0.65f);
        }
    }

    private static void DrawVerticalGridLine(
        DrawingHandleScreen handle,
        float centerX,
        float top,
        float bottom,
        float width,
        Color color)
    {
        var halfWidth = width * 0.5f;
        handle.DrawRect(new UIBox2(centerX - halfWidth, top, centerX + halfWidth, bottom), color);
    }

    private static void DrawHorizontalGridLine(
        DrawingHandleScreen handle,
        float centerY,
        float left,
        float right,
        float width,
        Color color)
    {
        var halfWidth = width * 0.5f;
        handle.DrawRect(new UIBox2(left, centerY - halfWidth, right, centerY + halfWidth), color);
    }

    private void DrawAnnotations(DrawingHandleScreen handle, ViewState view)
    {
        foreach (var stroke in _workingAnnotations)
        {
            DrawStroke(handle, stroke.Points, stroke.Color, stroke.Thickness, view);
        }

        if (_annotationTool == WH40KTacticalMapAnnotationTool.Brush && _activeStrokePoints.Count > 0)
        {
            DrawStroke(handle, _activeStrokePoints, _annotationColor.WithAlpha(0.88f), _annotationThickness, view);
        }
        else if (_annotationTool == WH40KTacticalMapAnnotationTool.Eraser && _activeStrokePoints.Count > 0)
        {
            var screenPosition = view.WorldToScreen(_activeStrokePoints[^1]);
            var radius = MathF.Max(4f, _annotationThickness * view.Scale * 0.45f);
            handle.DrawCircle(screenPosition, radius, Color.White.WithAlpha(0.22f));
        }
    }

    private void DrawStroke(
        DrawingHandleScreen handle,
        IReadOnlyList<Vector2> points,
        Color color,
        float thickness,
        ViewState view)
    {
        if (points.Count == 0)
            return;

        var radius = MathF.Max(1.2f, thickness * view.Scale * 0.28f);
        var lastScreen = view.WorldToScreen(points[0]);
        var segment = new Vector2[4];
        handle.DrawCircle(lastScreen, radius, color);

        for (var i = 1; i < points.Count; i++)
        {
            var currentScreen = view.WorldToScreen(points[i]);
            var delta = currentScreen - lastScreen;
            var lengthSquared = delta.LengthSquared();

            if (lengthSquared > 0.0001f)
            {
                var normal = new Vector2(-delta.Y, delta.X) / MathF.Sqrt(lengthSquared) * radius;
                segment[0] = lastScreen + normal;
                segment[1] = lastScreen - normal;
                segment[2] = currentScreen + normal;
                segment[3] = currentScreen - normal;
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, segment, color);
            }

            handle.DrawCircle(currentScreen, radius, color);
            lastScreen = currentScreen;
        }
    }

    private void DrawTrackedEntities(DrawingHandleScreen handle, ViewState view)
    {
        if (_gridTransform == null)
            return;

        foreach (var (coordinates, value) in TrackedCoordinates)
        {
            if (!value.Visible)
                continue;

            var mapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
            if (mapCoordinates.MapId == MapId.Nullspace)
                continue;

            var localPosition = Vector2.Transform(mapCoordinates.Position, _transformSystem.GetInvWorldMatrix(_gridTransform));
            if (!IsLocalPositionVisible(localPosition))
                continue;

            var position = view.WorldToScreen(localPosition);
            handle.DrawCircle(position, 5f, value.Color);
            handle.DrawCircle(position, 9f, value.Color.WithAlpha(0.25f));
        }
    }

    private void DrawAlliedMarkers(DrawingHandleScreen handle, ViewState view)
    {
        if (!ShowAllies)
            return;

        foreach (var ally in _alliedMarkers)
        {
            if (!IsLocalPositionVisible(ally.Position))
                continue;

            var position = view.WorldToScreen(ally.Position);
            handle.DrawCircle(position, 4f, ally.Color);
            handle.DrawCircle(position, 7f, ally.Color.WithAlpha(0.18f));
            handle.DrawCircle(position, 4.8f, Color.White.WithAlpha(0.6f), false);

            if (ShowAllyNames || _hoveredAllyEntity == ally.Entity)
                DrawMapLabel(handle, ally.Label, position + new Vector2(10f, -15f), ally.Color);
        }
    }

    private void DrawCapturePoints(DrawingHandleScreen handle, ViewState view)
    {
        foreach (var point in _capturePointMarkers)
        {
            if (!IsCapturePointVisible(point.Position))
                continue;

            var ownerColor = string.IsNullOrWhiteSpace(point.OwnerTeamId)
                ? Color.FromHex("#7F8790".AsSpan())
                : point.OwnerColor;
            var position = view.WorldToScreen(point.Position);
            var radius = 9f;

            handle.DrawCircle(position, radius + 2f, Color.Black.WithAlpha(0.35f), true);
            handle.DrawCircle(position, radius, ownerColor.WithAlpha(0.2f), true);
            handle.DrawCircle(position, radius, ownerColor.WithAlpha(0.95f), false);

            if (!string.IsNullOrWhiteSpace(point.CapturingTeamId) && point.CaptureProgress > 0f)
            {
                var captureRadius = MathF.Max(2.5f, radius * point.CaptureProgress);
                handle.DrawCircle(position, captureRadius, point.CapturingColor.WithAlpha(0.28f), true);
                handle.DrawCircle(position, captureRadius, point.CapturingColor.WithAlpha(0.92f), false);
            }

            if (point.Contested)
                handle.DrawCircle(position, radius * 0.55f, point.CapturingColor.WithAlpha(0.88f), false);

            var title = WH40KTacticalMapLoc.LocalizeCaptureLabel(point.Callsign, point.Label).ToUpperInvariant();
            DrawMapLabel(handle, title, position + new Vector2(12f, -16f), ownerColor);
        }
    }

    private void DrawMapLabel(DrawingHandleScreen handle, string text, Vector2 topLeft, Color color)
    {
        handle.DrawString(_detailFont, topLeft + new Vector2(1f, 1f), text, Color.Black.WithAlpha(0.9f));
        handle.DrawString(_detailFont, topLeft, text, color.WithAlpha(0.98f));
    }

    private void UpdateHoveredAlly(Vector2 relativePixelPosition)
    {
        if (!ShowAllies || !TryGetViewState(out var view))
        {
            SetHoveredAlly(null);
            return;
        }

        var bestDistanceSquared = AllyMarkerHoverRadius * AllyMarkerHoverRadius;
        NetEntity? hoveredEntity = null;

        foreach (var ally in _alliedMarkers)
        {
            if (!IsLocalPositionVisible(ally.Position))
                continue;

            var position = view.WorldToScreen(ally.Position);
            var distanceSquared = Vector2.DistanceSquared(position, relativePixelPosition);
            if (distanceSquared > bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            hoveredEntity = ally.Entity;
        }

        SetHoveredAlly(hoveredEntity);
    }

    private void SetHoveredAlly(NetEntity? allyEntity)
    {
        if (_hoveredAllyEntity == allyEntity)
            return;

        _hoveredAllyEntity = allyEntity;
        InvalidateArrange();
    }

    private bool TryGetDrawableLocalPosition(Vector2 relativePixelPosition, out Vector2 localPosition, bool clampToBounds = false)
    {
        localPosition = default;

        if (!TryGetViewState(out var view) || _grid == null)
            return false;

        localPosition = view.ScreenToWorld(relativePixelPosition);
        var bounds = _grid.LocalAABB;

        if (bounds.Contains(localPosition))
            return true;

        if (!clampToBounds)
            return false;

        localPosition = new Vector2(
            Math.Clamp(localPosition.X, bounds.Left, bounds.Right),
            Math.Clamp(localPosition.Y, bounds.Bottom, bounds.Top));
        return true;
    }

    private bool IsLocalPositionVisible(Vector2 localPosition)
    {
        return IsLocalPositionRevealed(localPosition) && !IsLocalPositionBlackedOut(localPosition);
    }

    private bool IsCapturePointVisible(Vector2 localPosition)
    {
        return IsLocalPositionOnMap(localPosition) && !IsLocalPositionBlackedOut(localPosition);
    }

    private bool IsLocalPositionOnMap(Vector2 localPosition)
    {
        return _grid != null && _grid.LocalAABB.Contains(localPosition);
    }

    private bool IsLocalPositionRevealed(Vector2 localPosition)
    {
        if (!FogEnabled)
            return true;

        var chunkSize = Math.Max(1, FogChunkSize);
        var chunk = new Vector2i(
            (int) MathF.Floor(localPosition.X / chunkSize),
            (int) MathF.Floor(localPosition.Y / chunkSize));

        return _revealedFogChunks.Contains(chunk);
    }

    private bool IsTileRevealed(Vector2i tile, float tileSize)
    {
        var localPosition = new Vector2((tile.X + 0.5f) * tileSize, (tile.Y + 0.5f) * tileSize);
        return IsLocalPositionRevealed(localPosition);
    }

    private bool IsLocalPositionBlackedOut(Vector2 localPosition)
    {
        if (_grid == null || _blackout == null || MapUid is not { } mapUid)
            return false;

        var tileSize = MathF.Max(0.001f, _grid.TileSize);
        var tile = new Vector2i(
            (int) MathF.Floor(localPosition.X / tileSize),
            (int) MathF.Floor(localPosition.Y / tileSize));
        return _blackoutSystem.IsBlackedOut((mapUid, _grid, _blackout), tile);
    }

    private void AppendActivePoint(Vector2 localPosition, Vector2 relativePixelPosition)
    {
        if (_annotationTool == WH40KTacticalMapAnnotationTool.Eraser)
        {
            if (_activeStrokePoints.Count == 0)
                _activeStrokePoints.Add(localPosition);
            else
                _activeStrokePoints[0] = localPosition;

            _lastAnnotationPixel = relativePixelPosition;
            return;
        }

        if (_activeStrokePoints.Count == 0)
        {
            _activeStrokePoints.Add(localPosition);
            _lastAnnotationPixel = relativePixelPosition;
            return;
        }

        var lastPoint = _activeStrokePoints[^1];
        var worldDistance = Vector2.Distance(lastPoint, localPosition);
        var pixelDistance = Vector2.Distance(_lastAnnotationPixel, relativePixelPosition);
        var worldSpacing = MathF.Max(0.02f, _annotationThickness * 0.04f);
        var pixelSpacing = MathF.Max(1.25f, _annotationThickness * 0.35f);

        if (worldDistance < 0.0001f)
            return;

        if (worldDistance < worldSpacing && pixelDistance < pixelSpacing)
            return;

        var steps = Math.Clamp((int) MathF.Ceiling(MathF.Max(worldDistance / worldSpacing, pixelDistance / pixelSpacing)), 1, 32);
        for (var i = 1; i <= steps; i++)
        {
            var sample = Vector2.Lerp(lastPoint, localPosition, i / (float) steps);
            if (Vector2.DistanceSquared(_activeStrokePoints[^1], sample) < 0.0001f)
                continue;

            FlushActiveBrushSegmentIfNeeded();
            _activeStrokePoints.Add(sample);
        }

        _lastAnnotationPixel = relativePixelPosition;
    }

    private void FlushActiveBrushSegmentIfNeeded()
    {
        if (_annotationTool != WH40KTacticalMapAnnotationTool.Brush ||
            _activeStrokePoints.Count < MaxLocalStrokePoints)
        {
            return;
        }

        if (_activeStrokePoints.Count > 1)
        {
            _workingAnnotations.Add(new WH40KTacticalMapAnnotationStroke(
                _activeStrokePoints.ToArray(),
                _annotationColor,
                _annotationThickness));
            SetAnnotationDirty(true);
        }

        var lastPoint = _activeStrokePoints[^1];
        _activeStrokePoints.Clear();
        _activeStrokePoints.Add(lastPoint);
    }

    private void ApplyEraserAt(Vector2 center)
    {
        if (_workingAnnotations.Count == 0)
            return;

        var changed = false;
        var next = new List<WH40KTacticalMapAnnotationStroke>(_workingAnnotations.Count);

        foreach (var stroke in _workingAnnotations)
        {
            var effectiveRadius = _annotationThickness + stroke.Thickness * 0.5f;
            var radiusSquared = effectiveRadius * effectiveRadius;

            if (!StrokeTouchesRadius(stroke, center, radiusSquared))
            {
                next.Add(stroke);
                continue;
            }

            changed = true;
            SplitStrokeByRadius(stroke, center, radiusSquared, next);
        }

        if (!changed)
            return;

        _workingAnnotations.Clear();
        _workingAnnotations.AddRange(next);
        SetAnnotationDirty(true);
    }

    private void ReplaceWorkingAnnotations(IReadOnlyList<WH40KTacticalMapAnnotationStroke> source, bool dirty)
    {
        CancelInteraction(clearDraftPreview: true);
        _workingAnnotations.Clear();
        _workingAnnotations.AddRange(CloneAnnotations(source));
        SetAnnotationDirty(dirty);
    }

    private void CommitActiveAnnotation()
    {
        if (!_isAnnotating)
            return;

        if (_annotationTool == WH40KTacticalMapAnnotationTool.Brush && _activeStrokePoints.Count > 0)
        {
            _workingAnnotations.Add(new WH40KTacticalMapAnnotationStroke(
                _activeStrokePoints.ToArray(),
                _annotationColor,
                _annotationThickness));
            SetAnnotationDirty(true);
        }

        CancelInteraction(clearDraftPreview: true);
    }

    private void CancelInteraction(bool clearDraftPreview)
    {
        _activePointerFunction = null;
        _isPanning = false;
        _isAnnotating = false;
        _lastAnnotationPixel = Vector2.Zero;

        if (clearDraftPreview)
            _activeStrokePoints.Clear();
    }

    protected override void ControlFocusExited()
    {
        base.ControlFocusExited();

        if (_isAnnotating)
        {
            CommitActiveAnnotation();
            return;
        }

        if (_isPanning)
            CancelInteraction(clearDraftPreview: false);
    }

    private void SetAnnotationDirty(bool dirty)
    {
        if (_annotationDirty == dirty)
            return;

        _annotationDirty = dirty;
        AnnotationStateChanged?.Invoke();
    }

    private static bool StrokeTouchesRadius(WH40KTacticalMapAnnotationStroke stroke, Vector2 center, float radiusSquared)
    {
        foreach (var point in stroke.Points)
        {
            if (Vector2.DistanceSquared(point, center) <= radiusSquared)
                return true;
        }

        return false;
    }

    private static void SplitStrokeByRadius(
        WH40KTacticalMapAnnotationStroke stroke,
        Vector2 center,
        float radiusSquared,
        List<WH40KTacticalMapAnnotationStroke> output)
    {
        var retained = new List<Vector2>(stroke.Points.Length);

        void FlushSegment()
        {
            if (retained.Count == 0)
                return;

            output.Add(new WH40KTacticalMapAnnotationStroke(retained.ToArray(), stroke.Color, stroke.Thickness));
            retained.Clear();
        }

        foreach (var point in stroke.Points)
        {
            if (Vector2.DistanceSquared(point, center) <= radiusSquared)
            {
                FlushSegment();
                continue;
            }

            retained.Add(point);
        }

        FlushSegment();
    }

    private bool TryEnsureSnapshotTexture()
    {
        var path = SnapshotTexturePath;

        if (_snapshotTexture != null && _loadedTexturePath == path)
            return true;

        DisposeSnapshotTexture();

        try
        {
            using var stream = _resources.ContentFileRead(path);
            _snapshotTexture = _clyde.LoadTextureFromPNGStream(stream, $"wh40k-tactical-map:{path}");
            _loadedTexturePath = path;
            _queuedLiveRefreshRevision = -1;
            _capturedLiveRefreshRevision = -1;
            _pendingLiveRefresh = null;
            return true;
        }
        catch
        {
            _loadedTexturePath = null;
            return false;
        }
    }

    private void MaybeApplyLiveRefresh()
    {
        if (_snapshotTexture == null || _grid == null || _gridTransform == null)
            return;

        if (!_liveRefreshEnabled ||
            !_liveRefreshActive ||
            _liveRefreshEyeUid is not { } eyeUid ||
            _liveRefreshChunkSizeActual == Vector2i.Zero)
        {
            _pendingLiveRefresh = null;
            return;
        }

        if (_queuedLiveRefreshRevision != _liveRefreshRevision)
        {
            var chunk = BuildLiveRefreshChunk(_liveRefreshRevision, eyeUid, _liveRefreshChunkOrigin, _liveRefreshChunkSizeActual);
            if (chunk != null)
            {
                _pendingLiveRefresh = chunk.Value;
                _queuedLiveRefreshRevision = chunk.Value.Revision;
                _captureNotBefore = _timing.RealTime + TimeSpan.FromSeconds(LiveRefreshCaptureDelaySeconds);
            }
        }

        if (_pendingLiveRefresh is not { } pending ||
            pending.Revision <= _capturedLiveRefreshRevision ||
            _timing.RealTime < _captureNotBefore)
        {
            return;
        }

        CaptureLiveRefreshChunk(pending);
    }

    private LiveRefreshChunk? BuildLiveRefreshChunk(int revision, EntityUid eyeUid, Vector2i tileOrigin, Vector2i tileSize)
    {
        if (_snapshotTexture == null || _grid == null)
            return null;

        if (tileSize.X <= 0 || tileSize.Y <= 0)
            return null;

        var bounds = _grid.LocalAABB;
        var minTileX = (int) MathF.Floor(bounds.Left);
        var minTileY = (int) MathF.Floor(bounds.Bottom);
        var maxTileX = (int) MathF.Ceiling(bounds.Right);
        var maxTileY = (int) MathF.Ceiling(bounds.Top);
        var totalTilesX = Math.Max(1, maxTileX - minTileX);
        var totalTilesY = Math.Max(1, maxTileY - minTileY);
        var pixelsPerTileX = Math.Max(1, _snapshotTexture.Width / totalTilesX);
        var pixelsPerTileY = Math.Max(1, _snapshotTexture.Height / totalTilesY);

        var pixelOrigin = new Vector2i(
            (tileOrigin.X - minTileX) * pixelsPerTileX,
            (maxTileY - (tileOrigin.Y + tileSize.Y)) * pixelsPerTileY);

        var pixelSize = new Vector2i(
            tileSize.X * pixelsPerTileX,
            tileSize.Y * pixelsPerTileY);

        var localBounds = new Box2(
            tileOrigin.X,
            tileOrigin.Y,
            tileOrigin.X + tileSize.X,
            tileOrigin.Y + tileSize.Y);

        return new LiveRefreshChunk(revision, eyeUid, pixelOrigin, pixelSize, localBounds);
    }

    private void CaptureLiveRefreshChunk(LiveRefreshChunk chunk)
    {
        if (_snapshotTexture == null || _gridTransform == null)
            return;

        if (!_entMan.TryGetComponent(chunk.EyeUid, out EyeComponent? eyeComp) || eyeComp.Eye == null)
            return;

        EnsureRefreshViewport(chunk.PixelSize);

        if (_refreshViewport == null)
            return;

        _refreshViewport.Eye = eyeComp.Eye;
        _refreshViewport.ClearColor = _gridTransform.MapUid is { } mapUid
            ? _clyde.GetClearColor(mapUid)
            : Color.Black;

        HideTransientSprites(chunk.LocalBounds);

        try
        {
            _refreshViewport.Render();
            _refreshViewport.RenderTarget.CopyPixelsToMemory<Rgba32>(image =>
            {
                _snapshotTexture?.SetSubImage(chunk.PixelOrigin, image);
            });

            _capturedLiveRefreshRevision = chunk.Revision;
            _pendingLiveRefresh = null;
        }
        finally
        {
            RestoreTransientSprites();
        }
    }

    private void EnsureRefreshViewport(Vector2i size)
    {
        if (_refreshViewport != null && _refreshViewport.Size == size)
            return;

        DisposeRefreshViewport();
        _refreshViewport = _clyde.CreateViewport(
            size,
            new TextureSampleParameters { Filter = false },
            name: "WH40KTacticalMapLiveRefresh");
    }

    private void HideTransientSprites(Box2 localBounds)
    {
        if (_gridTransform == null)
            return;

        _temporarilyHiddenSprites.Clear();
        var invGridMatrix = _transformSystem.GetInvWorldMatrix(_gridTransform);
        var query = _entMan.EntityQueryEnumerator<SpriteComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var sprite, out var xform))
        {
            if (!sprite.Visible || xform.Anchored || xform.MapID != _gridTransform.MapID)
                continue;

            var worldPosition = _transformSystem.GetWorldPosition(xform);
            var localPosition = Vector2.Transform(worldPosition, invGridMatrix);
            if (!localBounds.Contains(localPosition))
                continue;

            _temporarilyHiddenSprites.Add((uid, true));
            _sprite.SetVisible((uid, sprite), false);
        }
    }

    private void RestoreTransientSprites()
    {
        foreach (var (uid, wasVisible) in _temporarilyHiddenSprites)
        {
            if (!_entMan.TryGetComponent(uid, out SpriteComponent? sprite))
                continue;

            _sprite.SetVisible((uid, sprite), wasVisible);
        }

        _temporarilyHiddenSprites.Clear();
    }

    private void DisposeRefreshViewport()
    {
        _refreshViewport?.Dispose();
        _refreshViewport = null;
    }

    private void DisposeSnapshotTexture()
    {
        _snapshotTexture?.Dispose();
        _snapshotTexture = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeRefreshViewport();
            DisposeSnapshotTexture();
            _ownedFogNoiseTexture?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static WH40KTacticalMapAnnotationStroke[] CloneAnnotations(IReadOnlyList<WH40KTacticalMapAnnotationStroke> strokes)
    {
        var cloned = new WH40KTacticalMapAnnotationStroke[strokes.Count];
        for (var i = 0; i < strokes.Count; i++)
        {
            var stroke = strokes[i];
            var points = new Vector2[stroke.Points.Length];
            Array.Copy(stroke.Points, points, stroke.Points.Length);
            cloned[i] = new WH40KTacticalMapAnnotationStroke(points, stroke.Color, stroke.Thickness);
        }

        return cloned;
    }

    private static bool AreAnnotationsEquivalent(
        IReadOnlyList<WH40KTacticalMapAnnotationStroke> left,
        IReadOnlyList<WH40KTacticalMapAnnotationStroke> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            var leftStroke = left[i];
            var rightStroke = right[i];

            if (!leftStroke.Color.Equals(rightStroke.Color) ||
                MathF.Abs(leftStroke.Thickness - rightStroke.Thickness) > 0.0001f ||
                leftStroke.Points.Length != rightStroke.Points.Length)
            {
                return false;
            }

            for (var pointIndex = 0; pointIndex < leftStroke.Points.Length; pointIndex++)
            {
                if (Vector2.DistanceSquared(leftStroke.Points[pointIndex], rightStroke.Points[pointIndex]) > 0.0001f)
                    return false;
            }
        }

        return true;
    }
}
