using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WH40K.Psyker;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Psyker.UI;

public sealed class WH40KPsykerAstralOverlay : LayoutContainer
{
    private static readonly Color HeaderTextColor = Color.FromHex("#EEF7FF");
    private static readonly Color PanelBackgroundColor = Color.FromHex("#08111A").WithAlpha(0.34f);
    private static readonly Color PanelBorderColor = Color.FromHex("#6FB7EF").WithAlpha(0.38f);
    private static readonly Color ButtonBackgroundColor = Color.FromHex("#102335").WithAlpha(0.66f);
    private static readonly Color ButtonBorderColor = Color.FromHex("#8EC8F0").WithAlpha(0.78f);
    private static readonly Color PurchaseReadyColor = Color.FromHex("#D6F6FF");
    private static readonly Color PurchaseLockedColor = Color.FromHex("#7E9AB3");
    private static readonly Color PurchaseUnlockedColor = Color.FromHex("#F5E9B4");
    private static readonly Color DescriptionColor = Color.FromHex("#C9D9E8");

    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Texture _warpTexture;
    private readonly BoxContainer _content;
    private readonly WH40KPsykerAstralConstellationControl _constellation;
    private readonly BoxContainer _footerLane;
    private readonly PanelContainer _footerPanel;
    private readonly StyleBoxFlat _footerPanelStyle = BuildPanelStyle(PanelBackgroundColor, PanelBorderColor, 1f);
    private readonly StyleBoxFlat _purchaseButtonStyle = BuildPanelStyle(ButtonBackgroundColor, ButtonBorderColor, 1f);
    private readonly StyleBoxFlat _exitButtonStyle = BuildPanelStyle(Color.FromHex("#0C1825").WithAlpha(0.68f), Color.FromHex("#5F86A7").WithAlpha(0.62f), 1f);
    private readonly Label _nodeName;
    private readonly Label _nodeMeta;
    private readonly RichTextLabel _nodeDescription;
    private readonly Label _nodeStatus;
    private readonly Button _purchaseButton;
    private readonly Button _exitButton;
    private readonly List<WH40KPsykerDisciplineNodePrototype> _nodes = new();
    private readonly Dictionary<string, WH40KPsykerDisciplineNodePrototype> _nodeById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeAvailabilitySnapshot> _nodeAvailability = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unlockedNodeIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _availableNodeIds = new(StringComparer.Ordinal);

    private string? _rootNodeId;

    private float _fade;
    private float _instabilityFraction;
    private float _strainFraction;
    private WH40KPsykerAstralOverlayViewState _lastState;
    private bool _hasState;

    public event Action? ExitRequested;
    public event Action<string>? PurchaseRequested;
    public event Action<int>? CollectibleStarRequested;

    public WH40KPsykerAstralOverlay()
    {
        IoCManager.InjectDependencies(this);

        HorizontalExpand = true;
        VerticalExpand = true;
        MouseFilter = MouseFilterMode.Stop;

        _warpTexture = _resourceCache.GetResource<TextureResource>("/Textures/_WH40K/Parallaxes/blue.png").Texture;

        _content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(18, 18, 18, 18)
        };
        AddChild(_content);
        SetAnchorPreset(_content, LayoutPreset.Wide);

        _constellation = new WH40KPsykerAstralConstellationControl
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _constellation.FocusChanged += OnConstellationFocusChanged;
        _constellation.CollectibleStarRequested += OnCollectibleStarRequested;
        _content.AddChild(_constellation);

        _footerLane = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = false,
            SeparationOverride = 0
        };
        AddChild(_footerLane);
        SetAnchorPreset(_footerLane, LayoutPreset.BottomWide);
        SetMarginLeft(_footerLane, 18f);
        SetMarginRight(_footerLane, -18f);
        SetMarginTop(_footerLane, -304f);
        SetMarginBottom(_footerLane, -108f);
        _footerLane.AddChild(new Control { HorizontalExpand = true });

        _footerPanel = new PanelContainer
        {
            PanelOverride = _footerPanelStyle,
            RectClipContent = true,
            MinHeight = 188f,
            MaxHeight = 220f
        };
        _footerLane.AddChild(_footerPanel);
        _footerLane.AddChild(new Control { HorizontalExpand = true });

        var footer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            Margin = new Thickness(12, 10)
        };
        _footerPanel.AddChild(footer);

        _nodeName = new Label
        {
            StyleClasses = { "LabelBig" },
            FontColorOverride = HeaderTextColor,
            HorizontalExpand = true,
            ClipText = true
        };
        footer.AddChild(_nodeName);

        _nodeMeta = new Label
        {
            FontColorOverride = PurchaseReadyColor,
            HorizontalExpand = true,
            ClipText = true
        };
        footer.AddChild(_nodeMeta);

        _nodeDescription = new RichTextLabel
        {
            HorizontalExpand = true,
            MaxWidth = 470f,
            MinHeight = 46f,
            LineHeightScale = 0.96f
        };
        footer.AddChild(_nodeDescription);

        _nodeStatus = new Label
        {
            FontColorOverride = PurchaseLockedColor,
            HorizontalExpand = true,
            ClipText = false
        };
        footer.AddChild(_nodeStatus);

        var footerActions = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true
        };
        footer.AddChild(footerActions);

        _purchaseButton = new Button
        {
            Text = Loc.GetString("wh40k-psyker-astral-node-button-locked"),
            StyleBoxOverride = _purchaseButtonStyle,
            HorizontalExpand = true,
            MinWidth = 188f,
            MinHeight = 40f
        };
        _purchaseButton.OnPressed += _ => RequestFocusedNodePurchase();
        footerActions.AddChild(_purchaseButton);

        _exitButton = new Button
        {
            Text = Loc.GetString("wh40k-psyker-astral-exit"),
            StyleBoxOverride = _exitButtonStyle,
            HorizontalExpand = true,
            MinWidth = 188f,
            MinHeight = 40f,
            ClipText = true
        };
        _exitButton.OnPressed += _ => ExitRequested?.Invoke();
        footerActions.AddChild(_exitButton);

        ReloadNodes();
        RefreshFocusedNodeInfo();
    }

    public void ApplyState(WH40KPsykerAstralOverlayViewState state)
    {
        var layoutChanged = !_hasState ||
                            !string.Equals(_lastState.ConstellationLayoutId, state.ConstellationLayoutId, StringComparison.Ordinal);

        _hasState = true;
        _lastState = state;
        _fade = Math.Clamp(state.Fade, 0f, 1f);
        _instabilityFraction = Math.Clamp(state.InstabilityFraction, 0f, 1f);
        _strainFraction = Math.Clamp(state.AstralStrain / WH40KPsykerAstralMath.MaxAstralStrain, 0f, 1f);
        Visible = state.Visible && _fade > 0.001f;
        Modulate = Color.White.WithAlpha(_fade);
        _exitButton.Disabled = !state.CanExit;
        RefreshNodeStates(state);
        if (layoutChanged)
            ApplyConstellationLayout(state.ConstellationLayoutId);
        _constellation.SetFade(_fade);
        _constellation.SetWarpTension(_instabilityFraction, _strainFraction);
        _constellation.SetCollectibleStars(state.CollectibleStars);
        RefreshFocusedNodeInfo();
        InvalidateMeasure();
    }

    public void Relocalize()
    {
        _exitButton.Text = Loc.GetString("wh40k-psyker-astral-exit");
        ReloadNodes();

        if (_hasState)
            ApplyState(_lastState);
        else
            RefreshFocusedNodeInfo();
    }

    protected override void Resized()
    {
        base.Resized();
        UpdateFooterWidth();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawRect(PixelSizeBox, Color.Black.WithAlpha(0.92f));

        if (PixelWidth <= 0 || PixelHeight <= 0)
            return;

        var textureSize = new Vector2(_warpTexture.Size.X, _warpTexture.Size.Y);
        var scale = MathF.Max(PixelWidth / MathF.Max(1f, textureSize.X), PixelHeight / MathF.Max(1f, textureSize.Y));
        var time = (float) _timing.CurTime.TotalSeconds;
        var distortion = Math.Clamp(_instabilityFraction * 0.78f + _strainFraction * 0.52f, 0f, 1f);
        var pulse = 0.024f * MathF.Sin(time * 0.32f) + distortion * 0.022f * MathF.Sin(time * 2.6f);
        var drawSize = textureSize * scale * (1.05f + pulse);
        var drift = new Vector2(MathF.Sin(time * 0.08f) * 14f, MathF.Cos(time * 0.07f) * 10f) * _fade * (1f + distortion * 1.9f);
        drift += new Vector2(MathF.Sin(time * 3.2f), MathF.Cos(time * 2.7f)) * distortion * 6f;
        var origin = (PixelSize - drawSize) * 0.5f + drift;
        var tint = Color.White.WithAlpha(0.18f + _fade * 0.62f + distortion * 0.08f);
        handle.DrawTextureRect(_warpTexture, UIBox2.FromDimensions(origin, drawSize), tint);
        handle.DrawRect(PixelSizeBox, Color.FromHex("#02070D").WithAlpha(0.54f - _fade * 0.18f));
        handle.DrawRect(PixelSizeBox, Color.FromHex("#1A0E22").WithAlpha(distortion * 0.18f));
    }

    private void ReloadNodes()
    {
        _nodes.Clear();
        _nodeById.Clear();
        _nodes.AddRange(_prototype.EnumeratePrototypes<WH40KPsykerDisciplineNodePrototype>()
            .OrderBy(node => node.Tier)
            .ThenBy(node => node.Discipline)
            .ThenBy(node => node.ID));

        foreach (var node in _nodes)
        {
            _nodeById[node.ID] = node;
        }

        _rootNodeId = _nodes
            .FirstOrDefault(node => node.Cost <= 0 && node.Requires.Count == 0)?.ID
            ?? _nodes.FirstOrDefault()?.ID;

        _constellation.SetNodes(_nodes, _rootNodeId);
        ApplyConstellationLayout(_hasState ? _lastState.ConstellationLayoutId : string.Empty);
        RefreshFocusedNodeInfo();
    }

    private void ApplyConstellationLayout(string layoutId)
    {
        if (string.IsNullOrWhiteSpace(layoutId) ||
            !_prototype.TryIndex<WH40KPsykerAstralLayoutPrototype>(layoutId, out var layout))
        {
            _constellation.SetNodeLayout(null);
            return;
        }

        var positions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        foreach (var node in layout.Positions)
        {
            if (string.IsNullOrWhiteSpace(node.Node))
                continue;

            positions[node.Node] = new Vector2(node.X, node.Y);
        }

        _constellation.SetNodeLayout(positions);
    }

    private void RefreshNodeStates(WH40KPsykerAstralOverlayViewState state)
    {
        _unlockedNodeIds.Clear();
        _availableNodeIds.Clear();
        _nodeAvailability.Clear();

        foreach (var nodeId in state.UnlockedNodes)
        {
            _unlockedNodeIds.Add(nodeId);
        }

        foreach (var node in _nodes)
        {
            var availability = ResolveAvailability(node, state, out var prerequisiteName);
            var statusText = ResolveAvailabilityText(availability, node, prerequisiteName);
            _nodeAvailability[node.ID] = new NodeAvailabilitySnapshot(availability, statusText);

            if (availability == WH40KPsykerAstralNodeAvailability.Available)
                _availableNodeIds.Add(node.ID);
        }

        _constellation.SetProgression(_unlockedNodeIds, _availableNodeIds);
    }

    private WH40KPsykerAstralNodeAvailability ResolveAvailability(
        WH40KPsykerDisciplineNodePrototype node,
        WH40KPsykerAstralOverlayViewState state,
        out string prerequisiteName)
    {
        prerequisiteName = string.Empty;

        if (_unlockedNodeIds.Contains(node.ID))
            return WH40KPsykerAstralNodeAvailability.Unlocked;

        if (!state.CanPurchase)
            return WH40KPsykerAstralNodeAvailability.FadePending;

        if (node.RequiredLevel > state.Level)
            return WH40KPsykerAstralNodeAvailability.LevelLocked;

        foreach (var requiredId in node.Requires)
        {
            if (_unlockedNodeIds.Contains(requiredId))
                continue;

            prerequisiteName = requiredId;
            if (_prototype.TryIndex<WH40KPsykerDisciplineNodePrototype>(requiredId, out var prerequisite))
                prerequisiteName = Loc.GetString(prerequisite.Name);

            return WH40KPsykerAstralNodeAvailability.PrerequisiteLocked;
        }

        if (state.DisciplinePoints < node.Cost)
            return WH40KPsykerAstralNodeAvailability.NotEnoughPoints;

        return WH40KPsykerAstralNodeAvailability.Available;
    }

    private void RequestFocusedNodePurchase()
    {
        if (GetFocusedNode() is not { } node)
            return;

        if (!_nodeAvailability.TryGetValue(node.ID, out var snapshot) ||
            snapshot.Availability != WH40KPsykerAstralNodeAvailability.Available)
        {
            return;
        }

        PurchaseRequested?.Invoke(node.ID);
    }

    private void RefreshFocusedNodeInfo()
    {
        UpdateFooterWidth();

        if (GetFocusedNode() is not { } node)
        {
            _nodeName.Text = Loc.GetString("wh40k-psyker-astral-title");
            _nodeMeta.Text = string.Empty;
            _nodeDescription.SetMessage(Loc.GetString("wh40k-psyker-astral-subtitle"), DescriptionColor);
            _nodeStatus.Text = string.Empty;
            _purchaseButton.Text = Loc.GetString("wh40k-psyker-astral-node-button-locked");
            _purchaseButton.Disabled = true;
            return;
        }

        var accent = ResolveDisciplineColor(node.Discipline);
        var snapshot = _nodeAvailability.TryGetValue(node.ID, out var availability)
            ? availability
            : new NodeAvailabilitySnapshot(
                _unlockedNodeIds.Contains(node.ID)
                    ? WH40KPsykerAstralNodeAvailability.Unlocked
                    : WH40KPsykerAstralNodeAvailability.FadePending,
                string.Empty);

        _footerPanelStyle.BorderColor = accent.WithAlpha(snapshot.Availability == WH40KPsykerAstralNodeAvailability.Available ? 0.82f : 0.55f);
        _footerPanelStyle.BackgroundColor = Blend(PanelBackgroundColor, accent.WithAlpha(0.12f), 0.18f);

        _purchaseButtonStyle.BorderColor = snapshot.Availability switch
        {
            WH40KPsykerAstralNodeAvailability.Available => accent.WithAlpha(0.9f),
            WH40KPsykerAstralNodeAvailability.Unlocked => PurchaseUnlockedColor.WithAlpha(0.78f),
            _ => ButtonBorderColor.WithAlpha(0.56f)
        };
        _purchaseButtonStyle.BackgroundColor = snapshot.Availability switch
        {
            WH40KPsykerAstralNodeAvailability.Available => Blend(ButtonBackgroundColor, accent.WithAlpha(0.24f), 0.45f),
            WH40KPsykerAstralNodeAvailability.Unlocked => Blend(ButtonBackgroundColor, PurchaseUnlockedColor.WithAlpha(0.16f), 0.32f),
            _ => ButtonBackgroundColor.WithAlpha(0.56f)
        };

        _nodeName.Text = Loc.GetString(node.Name);
        _nodeMeta.Text = Loc.GetString(
            "wh40k-psyker-astral-node-meta",
            ("discipline", Loc.GetString(GetDisciplineLocKey(node.Discipline))),
            ("level", node.RequiredLevel),
            ("cost", node.Cost));
        _nodeMeta.FontColorOverride = accent;
        _nodeDescription.SetMessage(Loc.GetString(node.Description), DescriptionColor);
        _nodeStatus.Text = snapshot.StatusText;
        _nodeStatus.FontColorOverride = ResolveStatusColor(snapshot.Availability);
        _purchaseButton.Text = snapshot.Availability switch
        {
            WH40KPsykerAstralNodeAvailability.Available => Loc.GetString("wh40k-psyker-astral-node-button-purchase"),
            WH40KPsykerAstralNodeAvailability.Unlocked => Loc.GetString("wh40k-psyker-astral-node-button-unlocked"),
            _ => Loc.GetString("wh40k-psyker-astral-node-button-locked")
        };
        _purchaseButton.Disabled = snapshot.Availability != WH40KPsykerAstralNodeAvailability.Available;
    }

    private WH40KPsykerDisciplineNodePrototype? GetFocusedNode()
    {
        var activeId = _constellation.HoveredNodeId
            ?? _constellation.SelectedNodeId
            ?? _rootNodeId;

        if (activeId == null || !_nodeById.TryGetValue(activeId, out var node))
            return null;

        return node;
    }

    private void OnConstellationFocusChanged(string? _, string? __)
    {
        RefreshFocusedNodeInfo();
    }

    private void UpdateFooterWidth()
    {
        var preferredWidth = Math.Clamp(Size.X * 0.44f, 560f, 760f);
        var maxWidth = Math.Max(360f, Size.X - 96f);
        _footerPanel.SetWidth = Math.Clamp(preferredWidth, 360f, maxWidth);
        _nodeDescription.MaxWidth = Math.Max(300f, _footerPanel.SetWidth - 28f);

        var laneHeight = 196f;
        const float bottomInset = 108f;
        _footerPanel.MinHeight = laneHeight;
        _footerPanel.MaxHeight = laneHeight;
        SetMarginLeft(_footerLane, 18f);
        SetMarginRight(_footerLane, -18f);
        SetMarginTop(_footerLane, -(bottomInset + laneHeight));
        SetMarginBottom(_footerLane, -bottomInset);
    }

    private void OnCollectibleStarRequested(int starId)
    {
        CollectibleStarRequested?.Invoke(starId);
    }

    private static string ResolveAvailabilityText(
        WH40KPsykerAstralNodeAvailability availability,
        WH40KPsykerDisciplineNodePrototype node,
        string prerequisiteName)
    {
        return availability switch
        {
            WH40KPsykerAstralNodeAvailability.Unlocked => Loc.GetString("wh40k-psyker-astral-node-status-unlocked"),
            WH40KPsykerAstralNodeAvailability.Available => Loc.GetString("wh40k-psyker-astral-node-status-available"),
            WH40KPsykerAstralNodeAvailability.FadePending => Loc.GetString("wh40k-psyker-astral-node-status-fade"),
            WH40KPsykerAstralNodeAvailability.LevelLocked => string.Empty,
            WH40KPsykerAstralNodeAvailability.PrerequisiteLocked => Loc.GetString("wh40k-psyker-astral-node-status-prerequisite", ("node", prerequisiteName)),
            WH40KPsykerAstralNodeAvailability.NotEnoughPoints => Loc.GetString("wh40k-psyker-astral-node-status-points"),
            _ => string.Empty,
        };
    }

    private static Color ResolveStatusColor(WH40KPsykerAstralNodeAvailability availability)
    {
        return availability switch
        {
            WH40KPsykerAstralNodeAvailability.Unlocked => PurchaseUnlockedColor,
            WH40KPsykerAstralNodeAvailability.Available => PurchaseReadyColor,
            _ => PurchaseLockedColor
        };
    }

    private static StyleBoxFlat BuildPanelStyle(Color background, Color border, float borderThickness)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(borderThickness),
            ContentMarginLeftOverride = 0,
            ContentMarginTopOverride = 0,
            ContentMarginRightOverride = 0,
            ContentMarginBottomOverride = 0
        };
    }

    internal static Color ResolveDisciplineColor(string discipline)
    {
        return discipline switch
        {
            "Sanctioning" => Color.FromHex("#8FC6FF"),
            "Telekinesis" => Color.FromHex("#B8D95E"),
            "Aegis" => Color.FromHex("#F1CF7A"),
            "Divination" => Color.FromHex("#72D0B5"),
            "Biomancy" => Color.FromHex("#F29C8A"),
            _ => Color.FromHex("#D7ECFF"),
        };
    }

    internal static string GetDisciplineLocKey(string discipline)
    {
        return discipline switch
        {
            "Sanctioning" => "wh40k-psyker-astral-discipline-sanctioning",
            "Telekinesis" => "wh40k-psyker-astral-discipline-telekinesis",
            "Aegis" => "wh40k-psyker-astral-discipline-aegis",
            "Divination" => "wh40k-psyker-astral-discipline-divination",
            "Biomancy" => "wh40k-psyker-astral-discipline-biomancy",
            _ => "wh40k-psyker-astral-discipline-unknown",
        };
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        var blend = MathHelper.Clamp01(amount);
        return new Color(
            MathHelper.Lerp(from.R, to.R, blend),
            MathHelper.Lerp(from.G, to.G, blend),
            MathHelper.Lerp(from.B, to.B, blend),
            MathHelper.Lerp(from.A, to.A, blend));
    }

    private readonly record struct NodeAvailabilitySnapshot(
        WH40KPsykerAstralNodeAvailability Availability,
        string StatusText);
}

public sealed class WH40KPsykerAstralConstellationControl : Control
{
    private const float ViewMarginPixels = 12f;
    private const float MinZoom = 1f;
    private const float MaxZoom = 4.6f;
    private const float DefaultZoom = 1f;
    private const float DragThreshold = 7f;

    private readonly List<WH40KPsykerDisciplineNodePrototype> _nodes = new();
    private readonly Dictionary<string, WH40KPsykerDisciplineNodePrototype> _nodeById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector2> _nodePositions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unlockedNodeIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _availableNodeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _selectionBursts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _unlockBursts = new(StringComparer.Ordinal);
    private readonly List<WH40KPsykerAstralCollectibleStar> _collectibleStars = new();
    private readonly Dictionary<int, float> _starBursts = new();

    private string? _rootNodeId;
    private string? _hoveredNodeId;
    private string? _selectedNodeId;
    private Vector2 _viewCenter;
    private Vector2 _targetViewCenter;
    private Vector2 _pointerPixel;
    private Vector2 _dragLastPointerPixel;
    private float _zoom = DefaultZoom;
    private float _targetZoom = DefaultZoom;
    private float _fade;
    private float _instabilityFraction;
    private float _strainFraction;
    private float _time;
    private float _dragDistanceAccumulator;
    private bool _pointerInside;
    private bool _pointerDown;
    private bool _dragging;
    private bool _viewInitialized;
    private bool _progressionInitialized;

    public string? HoveredNodeId => _hoveredNodeId;
    public string? SelectedNodeId => _selectedNodeId;

    public event Action<string?, string?>? FocusChanged;
    public event Action<int>? CollectibleStarRequested;

    public WH40KPsykerAstralConstellationControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 480f;
        MinWidth = 520f;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void SetNodes(IEnumerable<WH40KPsykerDisciplineNodePrototype> nodes, string? rootNodeId = null)
    {
        _nodes.Clear();
        _nodeById.Clear();

        foreach (var node in nodes)
        {
            _nodes.Add(node);
            _nodeById[node.ID] = node;
        }

        _rootNodeId = rootNodeId;
        if (_rootNodeId == null || !_nodeById.ContainsKey(_rootNodeId))
            _rootNodeId = _nodes.FirstOrDefault(node => node.Cost <= 0 && node.Requires.Count == 0)?.ID ?? _nodes.FirstOrDefault()?.ID;

        if (_selectedNodeId == null || !_nodeById.ContainsKey(_selectedNodeId))
        {
            _selectedNodeId = _rootNodeId;
            EmitFocusChanged();
        }

        _viewInitialized = false;
        InvalidateArrange();
    }

    public void SetNodeLayout(IReadOnlyDictionary<string, Vector2>? positions)
    {
        _nodePositions.Clear();

        if (positions != null)
        {
            foreach (var (nodeId, position) in positions)
            {
                if (string.IsNullOrWhiteSpace(nodeId))
                    continue;

                _nodePositions[nodeId] = new Vector2(
                    Math.Clamp(position.X, 0.02f, 0.98f),
                    Math.Clamp(position.Y, 0.04f, 0.96f));
            }
        }

        _viewInitialized = false;
        InvalidateArrange();
    }

    public void SetProgression(IEnumerable<string> unlockedNodeIds, IEnumerable<string> availableNodeIds)
    {
        var newlyUnlocked = new List<string>();

        if (_progressionInitialized)
        {
            foreach (var nodeId in unlockedNodeIds)
            {
                if (!_unlockedNodeIds.Contains(nodeId))
                    newlyUnlocked.Add(nodeId);
            }
        }

        _unlockedNodeIds.Clear();
        _availableNodeIds.Clear();

        foreach (var nodeId in unlockedNodeIds)
        {
            _unlockedNodeIds.Add(nodeId);
        }

        foreach (var nodeId in availableNodeIds)
        {
            _availableNodeIds.Add(nodeId);
        }

        foreach (var nodeId in newlyUnlocked)
        {
            _unlockBursts[nodeId] = 1f;
        }

        _progressionInitialized = true;
        InvalidateArrange();
    }

    public void SetFade(float fade)
    {
        _fade = Math.Clamp(fade, 0f, 1f);
    }

    public void SetCollectibleStars(IEnumerable<WH40KPsykerAstralCollectibleStar> collectibleStars)
    {
        _collectibleStars.Clear();
        _collectibleStars.AddRange(collectibleStars);
        InvalidateArrange();
    }

    public void SetWarpTension(float instabilityFraction, float strainFraction)
    {
        _instabilityFraction = Math.Clamp(instabilityFraction, 0f, 1f);
        _strainFraction = Math.Clamp(strainFraction, 0f, 1f);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _pointerDown = true;
        _dragging = false;
        _dragDistanceAccumulator = 0f;
        _dragLastPointerPixel = args.RelativePixelPosition;
        _pointerPixel = args.RelativePixelPosition;
        _pointerInside = true;
        UpdateHoveredNode(args.RelativePixelPosition);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        var wasDragging = _dragging;
        _pointerDown = false;
        _dragging = false;

        if (!wasDragging)
            TrySelectNode(args.RelativePixelPosition);

        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        _pointerInside = true;
        _pointerPixel = args.RelativePixelPosition;

        if (_pointerDown && TryGetViewState(out var view))
        {
            var delta = args.RelativePixelPosition - _dragLastPointerPixel;
            _dragLastPointerPixel = args.RelativePixelPosition;
            _dragDistanceAccumulator += delta.Length();

            if (!_dragging && _dragDistanceAccumulator >= DragThreshold)
            {
                _dragging = true;
                SetHoveredNode(null);
            }

            if (_dragging)
            {
                _targetViewCenter = ClampViewCenter(
                    _targetViewCenter - delta / MathF.Max(0.001f, view.Scale),
                    view.Bounds);
                return;
            }
        }

        UpdateHoveredNode(args.RelativePixelPosition);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (!TryGetViewState(out var view))
            return;

        var before = view.ScreenToWorld(args.RelativePixelPosition);
        var zoomFactor = MathF.Pow(1.16f, args.Delta.Y);
        var nextZoom = Math.Clamp(_targetZoom * zoomFactor, MinZoom, MaxZoom);

        if (!TryBuildView(view.Bounds, _targetViewCenter, nextZoom, out var nextView))
            return;

        var after = nextView.ScreenToWorld(args.RelativePixelPosition);
        _targetZoom = nextZoom;
        _targetViewCenter = ClampViewCenter(_targetViewCenter + (before - after), nextView.Bounds);
        args.Handle();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _pointerInside = false;
        if (!_dragging)
            SetHoveredNode(null);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var delta = args.DeltaSeconds;
        if (delta <= 0f)
            return;

        _time += delta;
        var changed = false;

        var blend = MathHelper.Clamp01(delta * 10f);
        if (_viewCenter != _targetViewCenter)
        {
            var nextCenter = Vector2.Lerp(_viewCenter, _targetViewCenter, blend);
            if ((nextCenter - _viewCenter).LengthSquared() > 0.0001f)
            {
                _viewCenter = nextCenter;
                changed = true;
            }
            else
            {
                _viewCenter = _targetViewCenter;
            }
        }

        var nextZoom = MathHelper.Lerp(_zoom, _targetZoom, blend);
        if (MathF.Abs(nextZoom - _zoom) > 0.0005f)
        {
            _zoom = nextZoom;
            changed = true;
        }
        else if (!MathHelper.CloseTo(_zoom, _targetZoom))
        {
            _zoom = _targetZoom;
        }

        changed |= AdvancePulseMap(_selectionBursts, delta * 2.6f);
        changed |= AdvancePulseMap(_unlockBursts, delta * 2.2f);
        changed |= AdvancePulseMap(_starBursts, delta * 1.9f);

        if (changed)
        {
            InvalidateArrange();
            if (_pointerInside && !_dragging)
                UpdateHoveredNode(_pointerPixel);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, Color.FromHex("#03080E").WithAlpha(0.18f));

        if (!TryGetViewState(out var view) || _nodes.Count == 0)
            return;

        DrawBackdrop(handle, view);
        DrawCollectibleStars(handle, view);
        DrawConnectors(handle, view);
        foreach (var node in _nodes.OrderBy(node => node.Tier)
                     .ThenBy(node => node.ID == _selectedNodeId || node.ID == _hoveredNodeId))
        {
            DrawNode(handle, view, node);
        }

    }

    private void DrawBackdrop(DrawingHandleScreen handle, AstralViewState view)
    {
        var center = view.WorldToScreen(view.Bounds.Center);
        var span = MathF.Min(PixelWidth, PixelHeight);
        var sigilAlpha = 0.05f + _fade * 0.05f;

        DrawAstralPetal(
            handle,
            center,
            span * 0.15f,
            span * 0.055f,
            _time * 0.08f,
            Color.FromHex("#D6F7FF").WithAlpha(sigilAlpha * 0.54f));
        DrawAstralPetal(
            handle,
            center,
            span * 0.24f,
            span * 0.075f,
            _time * -0.06f + MathF.Tau / 6f,
            Color.FromHex("#75C4FF").WithAlpha(sigilAlpha * 0.42f));
        DrawAstralPetal(
            handle,
            center,
            span * 0.33f,
            span * 0.09f,
            _time * 0.04f + MathF.Tau / 3f,
            Color.FromHex("#4B87C7").WithAlpha(sigilAlpha * 0.30f));
    }

    private void DrawCollectibleStars(DrawingHandleScreen handle, AstralViewState view)
    {
        if (_collectibleStars.Count == 0)
            return;

        foreach (var star in _collectibleStars)
        {
            var position = view.WorldToScreen(new Vector2(star.X, star.Y));
            var nearestNodePosition = GetNearestNodeScreenPosition(view, position);
            var phase = 0.5f + 0.5f * MathF.Sin(_time * (1.6f + star.Variant * 0.16f) + star.Id * 0.77f);
            var radius = (4.2f + star.Scale * 2.5f) * (1f + phase * 0.16f);
            var burst = _starBursts.GetValueOrDefault(star.Id);
            var tint = star.Variant switch
            {
                1 => Color.FromHex("#C9EEFF"),
                2 => Color.FromHex("#E8F8FF"),
                3 => Color.FromHex("#9BE5FF"),
                _ => Color.FromHex("#BFE8FF")
            };

            if (nearestNodePosition != null)
            {
                var tether = tint.WithAlpha(0.08f + phase * 0.08f + burst * 0.12f);
                handle.DrawLine(position, nearestNodePosition.Value, tether);
            }

            DrawCollectibleBeacon(handle, position, tint, phase, burst);
            handle.DrawCircle(position, radius * (1.9f + burst * 0.9f), tint.WithAlpha(0.08f + phase * 0.05f + burst * 0.12f));
            DrawSparkGlyph(handle, position, radius * (1.5f + burst * 0.6f), tint.WithAlpha(0.58f + phase * 0.22f + burst * 0.2f), 0.44f);
            DrawSparkGlyph(handle, position, radius * (0.8f + burst * 0.25f), Color.White.WithAlpha(0.48f + phase * 0.24f + burst * 0.18f), 0.28f);
        }
    }

    private void DrawCollectibleBeacon(
        DrawingHandleScreen handle,
        Vector2 position,
        Color tint,
        float phase,
        float burst)
    {
        var markerRadius = 18f + phase * 6f + burst * 10f;
        var pulseAlpha = 0.08f + phase * 0.06f + burst * 0.12f;
        handle.DrawCircle(position, markerRadius, tint.WithAlpha(pulseAlpha), false);
        handle.DrawCircle(position, markerRadius * 1.34f, tint.WithAlpha(pulseAlpha * 0.72f), false);

        for (var i = 0; i < 4; i++)
        {
            var angle = _time * 0.32f + i * MathF.Tau / 4f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var orbitCenter = position + direction * (markerRadius + 16f + phase * 4f);
            var orbitColor = tint.WithAlpha(0.30f + phase * 0.16f + burst * 0.18f);
            DrawSparkGlyph(handle, orbitCenter, 5.5f + phase * 1.8f + burst * 2f, orbitColor, 0.34f);
            handle.DrawLine(
                orbitCenter,
                position + direction * (markerRadius * 0.45f),
                tint.WithAlpha(0.12f + phase * 0.08f + burst * 0.12f));
        }
    }

    private void DrawConnectors(DrawingHandleScreen handle, AstralViewState view)
    {
        foreach (var node in _nodes)
        {
            if (node.Requires.Count == 0)
                continue;

            var nodePosition = view.WorldToScreen(ResolveNodeWorldPosition(node));
            var nodeSelected = node.ID == _selectedNodeId || node.ID == _hoveredNodeId;

            foreach (var requiredId in node.Requires)
            {
                if (!_nodeById.TryGetValue(requiredId, out var required))
                    continue;

                var requiredSelected = requiredId == _selectedNodeId || requiredId == _hoveredNodeId;
                var activeLink = _unlockedNodeIds.Contains(requiredId) &&
                                 (_unlockedNodeIds.Contains(node.ID) || _availableNodeIds.Contains(node.ID));
                var selectionBoost = nodeSelected || requiredSelected ? 0.24f : 0f;
                var color = WH40KPsykerAstralOverlay.ResolveDisciplineColor(node.Discipline)
                    .WithAlpha(activeLink ? 0.30f + _fade * 0.3f + selectionBoost : 0.08f + _fade * 0.08f + selectionBoost * 0.5f);

                DrawConnector(handle, view.WorldToScreen(ResolveNodeWorldPosition(required)), nodePosition, color, node.Tier);
            }
        }
    }

    private void DrawNode(DrawingHandleScreen handle, AstralViewState view, WH40KPsykerDisciplineNodePrototype node)
    {
        var worldPosition = ResolveNodeWorldPosition(node);
        var position = view.WorldToScreen(worldPosition);
        var accent = WH40KPsykerAstralOverlay.ResolveDisciplineColor(node.Discipline);
        var phase = 0.5f + 0.5f * MathF.Sin(_time * 1.7f + worldPosition.X * 5.3f + worldPosition.Y * 4.1f);
        var zoomFactor = 1f + MathF.Max(0f, _zoom - 1f) * 0.22f;
        var radius = (7f + Math.Clamp(node.Tier, 0, 4) * 2f) * zoomFactor;
        var glowRadius = radius + 8f + phase * 2.2f;
        var unlocked = _unlockedNodeIds.Contains(node.ID);
        var available = _availableNodeIds.Contains(node.ID);
        var hovered = node.ID == _hoveredNodeId;
        var selected = node.ID == _selectedNodeId;
        var selectionBurst = _selectionBursts.GetValueOrDefault(node.ID);
        var unlockBurst = _unlockBursts.GetValueOrDefault(node.ID);
        var distortion = Math.Clamp(_instabilityFraction * 0.65f + _strainFraction * 0.5f, 0f, 1f);
        var fillAlpha = unlocked
            ? 0.46f + _fade * 0.42f
            : available
                ? 0.2f + _fade * 0.26f
                : 0.08f + _fade * 0.12f;
        var ringAlpha = unlocked
            ? 0.34f + _fade * 0.32f
            : available
                ? 0.22f + _fade * 0.24f
                : 0.09f + _fade * 0.14f;

        if (hovered)
        {
            fillAlpha += 0.1f;
            ringAlpha += 0.12f;
            glowRadius += 3f;
        }

        if (selected)
        {
            fillAlpha += 0.14f;
            ringAlpha += 0.16f;
            glowRadius += 5f;
        }

        DrawSparkGlyph(handle, position, glowRadius + selectionBurst * 6f, accent.WithAlpha(fillAlpha * (0.30f + selectionBurst * 0.18f)), 0.22f);
        handle.DrawCircle(position, radius + unlockBurst * 4f, accent.WithAlpha(fillAlpha));
        DrawSparkGlyph(handle, position, radius + 4f, Color.White.WithAlpha(ringAlpha), 0.16f);

        if (available || hovered)
        {
            var haloRadius = radius + 8f + MathF.Max(0f, phase) * 4f + selectionBurst * 6f;
            DrawSparkGlyph(handle, position, haloRadius, accent.WithAlpha(0.16f + MathF.Max(0f, phase) * 0.1f + selectionBurst * 0.18f), 0.14f);
        }

        if (selected || selectionBurst > 0f)
        {
            DrawIgnitionBurst(handle, position, accent, radius, selectionBurst, distortion);
            handle.DrawCircle(position, radius + 10f + selectionBurst * 10f, Color.White.WithAlpha(0.2f + selectionBurst * 0.32f), false);
        }

        if (unlocked)
        {
            handle.DrawCircle(position, MathF.Max(2f, radius - 3f), Color.White.WithAlpha(0.28f + _fade * 0.28f));
            if (unlockBurst > 0f)
            {
                handle.DrawCircle(position, radius + 16f + unlockBurst * 18f, accent.WithAlpha(0.2f + unlockBurst * 0.26f), false);
                handle.DrawCircle(position, radius + 6f + unlockBurst * 9f, Color.White.WithAlpha(0.12f + unlockBurst * 0.2f), false);
            }
        }

        if (node.Cost <= 0)
        {
            DrawSparkGlyph(handle, position, radius + 12f, Color.FromHex("#FFFFFF").WithAlpha(0.16f + _fade * 0.16f), 0.12f);
            DrawSparkGlyph(handle, position, radius + 20f, Color.FromHex("#8FC6FF").WithAlpha(0.1f + _fade * 0.12f), 0.10f);
        }
    }

    private void DrawIgnitionBurst(
        DrawingHandleScreen handle,
        Vector2 position,
        Color accent,
        float radius,
        float selectionBurst,
        float distortion)
    {
        var pulse = MathF.Max(selectionBurst, 0.28f + 0.22f * MathF.Sin(_time * 2.4f));
        for (var i = 0; i < 6; i++)
        {
            var angle = _time * 1.2f + i * MathF.Tau / 6f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var inner = position + direction * (radius + 2f);
            var outer = position + direction * (radius + 10f + pulse * 10f + distortion * 4f);
            handle.DrawLine(inner, outer, Color.White.WithAlpha(0.12f + pulse * 0.36f));
            DrawSparkGlyph(handle, outer, 1.6f + pulse * 1.6f, accent.WithAlpha(0.18f + pulse * 0.22f), 0.18f);
        }
    }

    private bool TrySelectNode(Vector2 localPosition)
    {
        if (TryGetCollectibleStarAt(localPosition, out var star))
        {
            _starBursts[star.Id] = 1f;
            CollectibleStarRequested?.Invoke(star.Id);
            InvalidateArrange();
            return true;
        }

        if (!TryGetNodeAt(localPosition, out var node))
            return false;

        SelectNode(node.ID, focus: true, animate: true);
        return true;
    }

    private void SelectNode(string nodeId, bool focus, bool animate)
    {
        if (!_nodeById.TryGetValue(nodeId, out var node))
            return;

        var changed = _selectedNodeId != nodeId;
        _selectedNodeId = nodeId;
        _selectionBursts[nodeId] = 1f;

        if (focus)
            FocusNode(node);

        if (changed)
            EmitFocusChanged();

        InvalidateArrange();
    }

    private void FocusNode(WH40KPsykerDisciplineNodePrototype node)
    {
        if (!TryGetWorldBounds(out var bounds))
            return;

        EnsureViewInitialized(bounds);
        _targetViewCenter = ClampViewCenter(ResolveNodeWorldPosition(node), bounds);
        _targetZoom = Math.Clamp(MathF.Max(_targetZoom, 1.34f + node.Tier * 0.28f), MinZoom, MaxZoom);
    }

    private void UpdateHoveredNode(Vector2 localPosition)
    {
        if (_dragging)
        {
            SetHoveredNode(null);
            return;
        }

        if (TryGetNodeAt(localPosition, out var node))
        {
            SetHoveredNode(node.ID);
            return;
        }

        SetHoveredNode(null);
    }

    private void SetHoveredNode(string? nodeId)
    {
        if (_hoveredNodeId == nodeId)
            return;

        _hoveredNodeId = nodeId;
        EmitFocusChanged();
        InvalidateArrange();
    }

    private void EmitFocusChanged()
    {
        FocusChanged?.Invoke(_hoveredNodeId, _selectedNodeId);
    }

    private bool TryGetNodeAt(Vector2 localPosition, out WH40KPsykerDisciplineNodePrototype node)
    {
        node = default!;

        if (!TryGetViewState(out var view))
            return false;

        var bestDistanceSquared = float.MaxValue;
        WH40KPsykerDisciplineNodePrototype? bestNode = null;

        foreach (var candidate in _nodes)
        {
            var position = view.WorldToScreen(ResolveNodeWorldPosition(candidate));
            var hitRadius = GetNodeRadius(candidate) + 12f;
            var distanceSquared = Vector2.DistanceSquared(position, localPosition);
            if (distanceSquared > hitRadius * hitRadius || distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestNode = candidate;
        }

        if (bestNode == null)
            return false;

        node = bestNode;
        return true;
    }

    private bool TryGetViewState(out AstralViewState view)
    {
        view = default;

        if (!TryGetWorldBounds(out var bounds) || PixelWidth <= 0 || PixelHeight <= 0)
            return false;

        EnsureViewInitialized(bounds);
        return TryBuildView(bounds, _viewCenter, _zoom, out view);
    }

    private bool TryBuildView(WorldRect bounds, Vector2 center, float zoom, out AstralViewState view)
    {
        view = default;
        if (PixelWidth <= 0 || PixelHeight <= 0)
            return false;

        var usableWidth = Math.Max(1f, PixelWidth - ViewMarginPixels * 2f);
        var usableHeight = Math.Max(1f, PixelHeight - ViewMarginPixels * 2f);
        var fitScale = MathF.Min(
            usableWidth / MathF.Max(0.001f, bounds.Width),
            usableHeight / MathF.Max(0.001f, bounds.Height));

        if (!float.IsFinite(fitScale) || fitScale <= 0f)
            return false;

        view = new AstralViewState(bounds, new Vector2(PixelWidth * 0.5f, PixelHeight * 0.5f), fitScale * zoom, center);
        return true;
    }

    private bool TryGetWorldBounds(out WorldRect bounds)
    {
        bounds = default;
        if (_nodes.Count == 0)
            return false;

        var left = float.MaxValue;
        var top = float.MaxValue;
        var right = float.MinValue;
        var bottom = float.MinValue;

        foreach (var node in _nodes)
        {
            var position = ResolveNodeWorldPosition(node);
            left = MathF.Min(left, position.X);
            top = MathF.Min(top, position.Y);
            right = MathF.Max(right, position.X);
            bottom = MathF.Max(bottom, position.Y);
        }

        var paddingX = MathF.Max(0.12f, (right - left) * 0.18f);
        var paddingY = MathF.Max(0.12f, (bottom - top) * 0.18f);
        bounds = new WorldRect(left - paddingX, top - paddingY, right + paddingX, bottom + paddingY);
        return true;
    }

    private void EnsureViewInitialized(WorldRect bounds)
    {
        if (_viewInitialized)
            return;

        _viewCenter = GetDefaultViewCenter(bounds);
        _targetViewCenter = _viewCenter;
        _zoom = DefaultZoom;
        _targetZoom = DefaultZoom;
        _viewInitialized = true;
    }

    private Vector2 GetDefaultViewCenter(WorldRect bounds)
    {
        if (_selectedNodeId != null &&
            _selectedNodeId != _rootNodeId &&
            _nodeById.TryGetValue(_selectedNodeId, out var selected))
        {
            return ClampViewCenter(ResolveNodeWorldPosition(selected), bounds);
        }

        return bounds.Center;
    }

    private static Vector2 ClampViewCenter(Vector2 center, WorldRect bounds)
    {
        var extraX = MathF.Max(0.08f, bounds.Width * 0.14f);
        var extraY = MathF.Max(0.08f, bounds.Height * 0.14f);
        return new Vector2(
            Math.Clamp(center.X, bounds.Left - extraX, bounds.Right + extraX),
            Math.Clamp(center.Y, bounds.Top - extraY, bounds.Bottom + extraY));
    }

    private static bool AdvancePulseMap<TKey>(Dictionary<TKey, float> pulses, float decay) where TKey : notnull
    {
        if (pulses.Count == 0)
            return false;

        var changed = false;
        var toRemove = new List<TKey>();
        foreach (var (key, value) in pulses)
        {
            var next = value - decay;
            if (next <= 0f)
            {
                toRemove.Add(key);
                changed = true;
                continue;
            }

            pulses[key] = next;
            changed = true;
        }

        foreach (var key in toRemove)
        {
            pulses.Remove(key);
        }

        return changed;
    }

    private Vector2 ResolveNodeWorldPosition(WH40KPsykerDisciplineNodePrototype node)
    {
        if (_nodePositions.TryGetValue(node.ID, out var position))
            return position;

        return new Vector2(
            Math.Clamp(node.X, 0.02f, 0.98f),
            Math.Clamp(node.Y, 0.04f, 0.96f));
    }

    private float GetNodeRadius(WH40KPsykerDisciplineNodePrototype node)
    {
        return (7f + Math.Clamp(node.Tier, 0, 4) * 2f) * (1f + MathF.Max(0f, _zoom - 1f) * 0.22f);
    }

    private bool TryGetCollectibleStarAt(Vector2 localPosition, out WH40KPsykerAstralCollectibleStar star)
    {
        star = default;

        if (_collectibleStars.Count == 0 || !TryGetViewState(out var view))
            return false;

        var bestDistanceSquared = float.MaxValue;
        WH40KPsykerAstralCollectibleStar? bestStar = null;

        foreach (var candidate in _collectibleStars)
        {
            var position = view.WorldToScreen(new Vector2(candidate.X, candidate.Y));
            var radius = 13f + candidate.Scale * 3f;
            var distanceSquared = Vector2.DistanceSquared(position, localPosition);
            if (distanceSquared > radius * radius || distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestStar = candidate;
        }

        if (bestStar == null)
            return false;

        star = bestStar.Value;
        return true;
    }

    private static void DrawConnector(DrawingHandleScreen handle, Vector2 from, Vector2 to, Color color, int tier)
    {
        var midpoint = (from + to) * 0.5f;
        var direction = to - from;
        if (direction.LengthSquared() < 0.001f)
        {
            handle.DrawLine(from, to, color);
            return;
        }

        var tangent = Vector2.Normalize(direction);
        var normal = new Vector2(-tangent.Y, tangent.X);
        var bendStrength = Math.Clamp(12f + tier * 4f, 10f, 24f);
        var bend = midpoint + normal * bendStrength * MathF.Sin((from.X + to.Y) * 0.02f);

        handle.DrawLine(from, bend, color);
        handle.DrawLine(bend, to, color);
    }

    private Vector2? GetNearestNodeScreenPosition(AstralViewState view, Vector2 screenPosition)
    {
        if (_nodes.Count == 0)
            return null;

        Vector2? bestPosition = null;
        var bestDistanceSquared = float.MaxValue;

        foreach (var node in _nodes)
        {
            var candidate = view.WorldToScreen(ResolveNodeWorldPosition(node));
            var distanceSquared = Vector2.DistanceSquared(candidate, screenPosition);
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestPosition = candidate;
        }

        return bestPosition;
    }

    private static void DrawAstralPetal(
        DrawingHandleScreen handle,
        Vector2 center,
        float width,
        float height,
        float rotation,
        Color color)
    {
        var points = new[]
        {
            new Vector2(0f, -height),
            new Vector2(width * 0.5f, -height * 0.32f),
            new Vector2(width, 0f),
            new Vector2(width * 0.5f, height * 0.32f),
            new Vector2(0f, height),
            new Vector2(-width * 0.5f, height * 0.32f),
            new Vector2(-width, 0f),
            new Vector2(-width * 0.5f, -height * 0.32f)
        };

        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        var first = RotateAndTranslate(points[0], center, cos, sin);
        var previous = first;

        for (var i = 1; i < 8; i++)
        {
            var current = RotateAndTranslate(points[i], center, cos, sin);
            handle.DrawLine(previous, current, color);
            previous = current;
        }

        handle.DrawLine(previous, first, color);
    }

    private static void DrawSparkGlyph(DrawingHandleScreen handle, Vector2 center, float radius, Color color, float diagonalScale)
    {
        var cardinal = radius;
        var diagonal = radius * Math.Max(0.12f, diagonalScale);
        handle.DrawLine(center + new Vector2(-cardinal, 0f), center + new Vector2(cardinal, 0f), color);
        handle.DrawLine(center + new Vector2(0f, -cardinal), center + new Vector2(0f, cardinal), color);
        handle.DrawLine(center + new Vector2(-diagonal, -diagonal), center + new Vector2(diagonal, diagonal), color.WithAlpha(color.A * 0.72f));
        handle.DrawLine(center + new Vector2(-diagonal, diagonal), center + new Vector2(diagonal, -diagonal), color.WithAlpha(color.A * 0.72f));
    }

    private static Vector2 RotateAndTranslate(Vector2 point, Vector2 center, float cos, float sin)
    {
        return new Vector2(
            center.X + point.X * cos - point.Y * sin,
            center.Y + point.X * sin + point.Y * cos);
    }

    private readonly record struct AstralViewState(WorldRect Bounds, Vector2 ScreenCenter, float Scale, Vector2 ViewCenter)
    {
        public Vector2 WorldToScreen(Vector2 worldPosition)
        {
            return ScreenCenter + (worldPosition - ViewCenter) * Scale;
        }

        public Vector2 ScreenToWorld(Vector2 screenPosition)
        {
            return ViewCenter + (screenPosition - ScreenCenter) / Scale;
        }
    }

    private readonly record struct WorldRect(float Left, float Top, float Right, float Bottom)
    {
        public float Width => Right - Left;
        public float Height => Bottom - Top;
        public Vector2 Center => new((Left + Right) * 0.5f, (Top + Bottom) * 0.5f);
    }
}

public readonly record struct WH40KPsykerAstralOverlayViewState(
    bool Visible,
    float Fade,
    int Level,
    string WarpChargeText,
    string WarpInstabilityText,
    bool CanExit,
    bool CanPurchase,
    int DisciplinePoints,
    int TotalDisciplinePointsEarned,
    int AstralDepth,
    float InstabilityFraction,
    float AstralStrain,
    string ConstellationLayoutId,
    IReadOnlyList<string> UnlockedNodes,
    IReadOnlyList<WH40KPsykerAstralCollectibleStar> CollectibleStars);

internal enum WH40KPsykerAstralNodeAvailability : byte
{
    Unlocked,
    Available,
    FadePending,
    LevelLocked,
    PrerequisiteLocked,
    NotEnoughPoints
}
