using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WH40K.Command;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Command.Controls;

/// <summary>
/// Visualizes the command-node progression tree and reflects authoritative backend state.
/// </summary>
public sealed class WH40KCommandTreeSketchControl : LayoutContainer
{
    private static readonly Color CanvasBackgroundColor = Color.FromHex("#21242E");
    private static readonly Color CanvasBorderColor = Color.FromHex("#4D5670");
    private static readonly Color AvailableBackgroundColor = Color.FromHex("#3A3A3A");
    private static readonly Color AvailableBorderColor = Color.FromHex("#DADADA");
    private static readonly Color AvailableHoverBackgroundColor = Color.FromHex("#474A53");
    private static readonly Color LockedBackgroundColor = Color.FromHex("#2B2D33");
    private static readonly Color LockedBorderColor = Color.FromHex("#616773");
    private static readonly Color LockedHoverBackgroundColor = Color.FromHex("#353944");
    private static readonly Color PurchasedBackgroundColor = Color.FromHex("#1F3D2A");
    private static readonly Color PurchasedBorderColor = Color.FromHex("#63C285");
    private static readonly Color PurchasedHoverBackgroundColor = Color.FromHex("#2A4A35");
    private static readonly Color DoctrineLockBackgroundColor = Color.FromHex("#392528");
    private static readonly Color DoctrineLockBorderColor = Color.FromHex("#D46A6A");
    private static readonly Color DoctrineLockHoverBackgroundColor = Color.FromHex("#4A2F33");
    private static readonly Color PointLockBackgroundColor = Color.FromHex("#40311D");
    private static readonly Color PointLockBorderColor = Color.FromHex("#D99B47");
    private static readonly Color PointLockHoverBackgroundColor = Color.FromHex("#533F24");
    private static readonly Color InactiveBackgroundColor = Color.FromHex("#24262D");
    private static readonly Color InactiveBorderColor = Color.FromHex("#515766");
    private static readonly Color InactiveHoverBackgroundColor = Color.FromHex("#2E323C");
    private static readonly Vector2 NodeSize = new(108f, 40f);
    private const int MarqueeVisibleChars = 13;
    private const float MarqueeTickSeconds = 0.12f;
    private const float MarqueePauseSeconds = 0.65f;
    private const float HorizontalPadding = 24f;
    private const float DomainInnerPadding = 10f;
    private const float VerticalPaddingTop = 20f;
    private const float VerticalPaddingBottom = 20f;
    private const float HorizontalRankSpread = 1.6f;
    private const string CommandTreeTeamMapId = "WH40KCommandTreeTeamMap";
    private const string CommandTreeDefaultProfileId = "WH40KCommandTreeProfileDefault";
    private const string CommandTreeCostTeamMapId = "WH40KCommandTreeCostTeamMap";
    private const string CommandTreeCostDefaultProfileId = "WH40KCommandTreeCostProfileDefault";

    private enum NodeVisualState
    {
        Available,
        LockedByParent,
        LockedByLevel,
        LockedByTime,
        Purchased,
        LockedByDoctrine,
        LockedByPoints,
        Inactive
    }

    private readonly record struct TreeNodeDefinition(
        string Id,
        string DomainId,
        string TitleKey,
        string DescriptionKey,
        string[] Parents,
        int Cost,
        int MinBaseLevel,
        int MinRoundTimeSeconds);

    private readonly record struct NodeLayout(int Tier, float Rank);

    private sealed class TreeNodeVisual
    {
        public TreeNodeDefinition Definition { get; }
        public Button Button { get; }
        public StyleBoxFlat Style { get; }
        public NodeVisualState State { get; set; }
        public bool Hovered { get; set; }
        public string FullCaption { get; set; } = string.Empty;
        public int MarqueeOffset { get; set; }
        public float MarqueeTickAccumulator { get; set; }
        public float MarqueePause { get; set; }

        public TreeNodeVisual(TreeNodeDefinition definition, Button button, StyleBoxFlat style)
        {
            Definition = definition;
            Button = button;
            Style = style;
        }
    }

    public event Action<string, string>? OnNodeInfoChanged;
    public event Action<string>? OnPurchaseRequested;

    private readonly Dictionary<string, TreeNodeVisual> _nodes = new();
    private readonly List<TreeNodeVisual> _orderedNodes = new();
    private readonly List<(string ParentId, string ChildId)> _connections = new();
    private readonly Dictionary<string, NodeLayout> _nodeLayouts = new();
    private readonly Dictionary<string, int> _domainIndices = new();
    private readonly List<string> _activeDomainIds = new();
    private readonly HashSet<string> _purchasedNodes = new();
    private readonly IPrototypeManager _prototype = IoCManager.Resolve<IPrototypeManager>();
    private int _maxComputedTier;
    private int _commandPoints;
    private int _baseLevel = 1;
    private int _roundElapsedSeconds;
    private WH40KBattlePhase _phase = WH40KBattlePhase.Preparation;

    private string _teamId = string.Empty;
    private string _activeProfileId = string.Empty;
    private string _activeCostProfileId = string.Empty;
    private string _activeDoctrineId = string.Empty;
    private string _lockedDomainId = string.Empty;
    private Color _accentColor = Color.FromHex("#F3C548");
    private WH40KCommandTreeCostProfilePrototype? _activeCostProfile;

    public Color AccentColor
    {
        get => _accentColor;
        set
        {
            _accentColor = value;
            RefreshVisualState();
        }
    }

    public IReadOnlyList<string> ActiveDomainIds => _activeDomainIds;

    public WH40KCommandTreeSketchControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 640f;
        MinWidth = 760f;
        LoadTreeForTeam(string.Empty);
        LoadCostProfileForTeam(string.Empty);
        EmitDefaultInfo();
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state, string activeDoctrineId)
    {
        var teamChanged = !string.Equals(_teamId, state.TeamId, StringComparison.OrdinalIgnoreCase);
        if (teamChanged)
        {
            _purchasedNodes.Clear();
            LoadTreeForTeam(state.TeamId);
            LoadCostProfileForTeam(state.TeamId);
        }

        _teamId = state.TeamId;
        _commandPoints = Math.Max(0, state.CommandPoints);
        _baseLevel = Math.Max(1, state.BaseLevel);
        _roundElapsedSeconds = Math.Max(0, state.RoundElapsedSeconds);
        _phase = state.Phase;
        _activeDoctrineId = activeDoctrineId ?? string.Empty;
        _lockedDomainId = NormalizeKey(WH40KCommandNodeDoctrineWindow.ResolveDoctrineLockedDomainId(_activeDoctrineId, _teamId));
        SyncPurchasedNodes(state.PurchasedTreeNodeIds);
        RefreshVisualState();
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        foreach (var node in _orderedNodes)
        {
            var center = GetNodeCenter(node.Definition, finalSize);
            SetPosition(node.Button, center - NodeSize * 0.5f);
        }

        return base.ArrangeOverride(finalSize);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawRect(PixelSizeBox, CanvasBackgroundColor);
        handle.DrawRect(PixelSizeBox, CanvasBorderColor, false);

        foreach (var (parentId, childId) in _connections)
        {
            if (!_nodes.TryGetValue(parentId, out var parent) ||
                !_nodes.TryGetValue(childId, out var child))
            {
                continue;
            }

            var start = GetNodeAnchorBottom(parent);
            var end = GetNodeAnchorTop(child);
            var points = BuildConnectorPoints(start, end);

            DrawConnector(handle, points, GetConnectorColor(parent.State, child.State));
        }

        foreach (var node in _orderedNodes)
        {
            if (node.State != NodeVisualState.LockedByDoctrine)
                continue;

            var box = GetNodeDrawBox(node);
            handle.DrawLine(box.TopLeft, box.BottomRight, DoctrineLockBorderColor);
            handle.DrawLine(new Vector2(box.Left, box.Bottom), new Vector2(box.Right, box.Top), DoctrineLockBorderColor);
        }
    }

    private static (Vector2 P0, Vector2 P1, Vector2 P2, Vector2 P3) BuildConnectorPoints(Vector2 start, Vector2 end)
    {
        var exitY = start.Y + 14f;
        if (exitY >= end.Y - 6f)
            exitY = (start.Y + end.Y) * 0.5f;

        var p0 = SnapToPixel(start);
        var p1 = SnapToPixel(new Vector2(start.X, exitY));
        var p2 = SnapToPixel(new Vector2(end.X, exitY));
        var p3 = SnapToPixel(end);

        return (p0, p1, p2, p3);
    }

    private static void DrawConnector(
        DrawingHandleScreen handle,
        (Vector2 P0, Vector2 P1, Vector2 P2, Vector2 P3) points,
        Color color)
    {
        DrawSegment(handle, points.P0, points.P1, color);
        DrawSegment(handle, points.P1, points.P2, color);
        DrawSegment(handle, points.P2, points.P3, color);
    }

    private static void DrawSegment(DrawingHandleScreen handle, Vector2 from, Vector2 to, Color color)
    {
        if (from == to)
            return;

        handle.DrawLine(from, to, color);
    }

    private static Vector2 SnapToPixel(Vector2 point)
    {
        return new Vector2(MathF.Round(point.X), MathF.Round(point.Y));
    }

    private void LoadTreeForTeam(string teamId)
    {
        var profileId = ResolveProfileForTeam(teamId);
        if (_orderedNodes.Count > 0 &&
            string.Equals(_activeProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_prototype.TryIndex(profileId, out WH40KCommandTreeProfilePrototype? profile))
        {
            Logger.ErrorS("wh40k.command", $"Missing command-tree profile prototype '{profileId}'.");
            return;
        }

        RebuildFromProfile(profile);
        _activeProfileId = profile.ID;
    }

    private void LoadCostProfileForTeam(string teamId)
    {
        var profileId = ResolveCostProfileForTeam(teamId);
        if (_activeCostProfile is not null &&
            string.Equals(_activeCostProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_prototype.TryIndex(profileId, out WH40KCommandTreeCostProfilePrototype? profile))
        {
            Logger.ErrorS("wh40k.command", $"Missing command-tree cost profile prototype '{profileId}'.");
            _activeCostProfile = null;
            _activeCostProfileId = string.Empty;
            return;
        }

        _activeCostProfile = profile;
        _activeCostProfileId = profile.ID;
    }

    private string ResolveProfileForTeam(string teamId)
    {
        if (!_prototype.TryIndex(CommandTreeTeamMapId, out WH40KCommandTreeTeamMapPrototype? teamMap))
            return CommandTreeDefaultProfileId;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            if (teamMap.TeamProfiles.TryGetValue(teamId, out var directProfile))
                return directProfile;

            foreach (var (mappedTeamId, mappedProfile) in teamMap.TeamProfiles)
            {
                if (string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    return mappedProfile;
            }
        }

        return teamMap.DefaultProfile;
    }

    private string ResolveCostProfileForTeam(string teamId)
    {
        if (!_prototype.TryIndex(CommandTreeCostTeamMapId, out WH40KCommandTreeCostTeamMapPrototype? teamMap))
            return CommandTreeCostDefaultProfileId;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            if (teamMap.TeamProfiles.TryGetValue(teamId, out var directProfile))
                return directProfile;

            foreach (var (mappedTeamId, mappedProfile) in teamMap.TeamProfiles)
            {
                if (string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    return mappedProfile;
            }
        }

        return teamMap.DefaultProfile;
    }

    private void RebuildFromProfile(WH40KCommandTreeProfilePrototype profile)
    {
        RemoveAllChildren();
        _nodes.Clear();
        _orderedNodes.Clear();
        _connections.Clear();
        _nodeLayouts.Clear();
        _domainIndices.Clear();
        _activeDomainIds.Clear();
        _maxComputedTier = 0;

        for (var i = 0; i < profile.Domains.Count; i++)
        {
            var domainId = NormalizeKey(profile.Domains[i].Id);
            if (string.IsNullOrWhiteSpace(domainId))
                continue;

            if (_domainIndices.ContainsKey(domainId))
                continue;

            _domainIndices[domainId] = _domainIndices.Count;
            _activeDomainIds.Add(domainId);
        }

        foreach (var node in profile.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) ||
                string.IsNullOrWhiteSpace(node.Domain) ||
                string.IsNullOrWhiteSpace(node.TitleKey) ||
                string.IsNullOrWhiteSpace(node.DescriptionKey))
            {
                continue;
            }

            var domainId = NormalizeKey(node.Domain);
            if (!_domainIndices.ContainsKey(domainId))
                continue;

            if (_nodes.ContainsKey(node.Id))
                continue;

            var parents = node.Parents
                .Where(parentId => !string.IsNullOrWhiteSpace(parentId))
                .Distinct()
                .ToArray();

            AddNode(new TreeNodeDefinition(
                node.Id,
                domainId,
                node.TitleKey,
                node.DescriptionKey,
                parents,
                Math.Max(0, node.Cost),
                Math.Max(1, node.MinBaseLevel),
                Math.Max(0, node.MinRoundTimeSeconds)));
        }

        foreach (var node in _orderedNodes)
        {
            foreach (var parentId in node.Definition.Parents)
            {
                _connections.Add((parentId, node.Definition.Id));
            }
        }

        RecomputeAutomaticLayout();
        RefreshVisualState();
    }

    private void AddNode(TreeNodeDefinition definition)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = AvailableBackgroundColor,
            BorderColor = AvailableBorderColor,
            BorderThickness = new Thickness(1)
        };

        var button = new Button
        {
            ClipText = true,
            TextAlign = Label.AlignMode.Left,
            HorizontalExpand = false,
            VerticalExpand = false,
            SetSize = NodeSize,
            MinSize = NodeSize,
            StyleBoxOverride = style
        };

        var visual = new TreeNodeVisual(definition, button, style);
        _nodes[definition.Id] = visual;
        _orderedNodes.Add(visual);

        button.OnPressed += _ => OnNodePressed(visual);
        button.OnMouseEntered += _ => OnNodeMouseEntered(visual);
        button.OnMouseExited += _ => OnNodeMouseExited(visual);

        AddChild(button);
    }

    private void OnNodePressed(TreeNodeVisual node)
    {
        if (node.State != NodeVisualState.Available)
            return;

        OnPurchaseRequested?.Invoke(node.Definition.Id);
        EmitNodeInfo(node);
    }

    private void OnNodeMouseEntered(TreeNodeVisual node)
    {
        node.Hovered = true;
        ApplyNodeStyle(node);
        EmitNodeInfo(node);
    }

    private void OnNodeMouseExited(TreeNodeVisual node)
    {
        node.Hovered = false;
        ApplyNodeStyle(node);
        EmitDefaultInfo();
    }

    private void RefreshVisualState()
    {
        foreach (var node in _orderedNodes)
        {
            node.State = ResolveNodeState(node.Definition);
            ApplyNodeStyle(node);
        }
    }

    private NodeVisualState ResolveNodeState(TreeNodeDefinition definition)
    {
        if (_purchasedNodes.Contains(definition.Id))
            return NodeVisualState.Purchased;

        if (definition.Cost <= 0)
            return NodeVisualState.Inactive;

        if (!string.IsNullOrWhiteSpace(_lockedDomainId) &&
            string.Equals(_lockedDomainId, definition.DomainId, StringComparison.Ordinal))
        {
            return NodeVisualState.LockedByDoctrine;
        }

        if (definition.Parents.Any(parentId => !_purchasedNodes.Contains(parentId)))
            return NodeVisualState.LockedByParent;

        if (_baseLevel < definition.MinBaseLevel)
            return NodeVisualState.LockedByLevel;

        if (_roundElapsedSeconds < definition.MinRoundTimeSeconds)
            return NodeVisualState.LockedByTime;

        var cost = GetRuntimeCost(definition);
        if (_commandPoints < cost)
            return NodeVisualState.LockedByPoints;

        return NodeVisualState.Available;
    }

    private void ApplyNodeStyle(TreeNodeVisual node)
    {
        node.Button.ToolTip = null;

        switch (node.State)
        {
            case NodeVisualState.Purchased:
                node.Style.BackgroundColor = node.Hovered
                    ? PurchasedHoverBackgroundColor
                    : PurchasedBackgroundColor;
                node.Style.BorderColor = PurchasedBorderColor;
                node.Button.Disabled = true;
                break;
            case NodeVisualState.LockedByDoctrine:
                node.Style.BackgroundColor = node.Hovered
                    ? DoctrineLockHoverBackgroundColor
                    : DoctrineLockBackgroundColor;
                node.Style.BorderColor = DoctrineLockBorderColor;
                node.Button.Disabled = true;
                break;
            case NodeVisualState.LockedByPoints:
                node.Style.BackgroundColor = node.Hovered
                    ? PointLockHoverBackgroundColor
                    : PointLockBackgroundColor;
                node.Style.BorderColor = PointLockBorderColor;
                node.Button.Disabled = true;
                break;
            case NodeVisualState.Inactive:
                node.Style.BackgroundColor = node.Hovered
                    ? InactiveHoverBackgroundColor
                    : InactiveBackgroundColor;
                node.Style.BorderColor = InactiveBorderColor;
                node.Button.Disabled = true;
                break;
            case NodeVisualState.LockedByLevel:
            case NodeVisualState.LockedByTime:
            case NodeVisualState.LockedByParent:
                node.Style.BackgroundColor = node.Hovered
                    ? LockedHoverBackgroundColor
                    : LockedBackgroundColor;
                node.Style.BorderColor = LockedBorderColor;
                node.Button.Disabled = true;
                break;
            default:
                node.Style.BackgroundColor = node.Hovered
                    ? AvailableHoverBackgroundColor
                    : AvailableBackgroundColor;
                node.Style.BorderColor = node.Hovered ? AvailableBorderColor : _accentColor;
                node.Button.Disabled = false;
                break;
        }

        var caption = BuildNodeCaption(node);
        if (!string.Equals(node.FullCaption, caption, StringComparison.Ordinal))
        {
            node.FullCaption = caption;
            node.MarqueeOffset = 0;
            node.MarqueeTickAccumulator = 0f;
            node.MarqueePause = MarqueePauseSeconds;
        }

        UpdateNodeCaption(node);
    }

    private void EmitNodeInfo(TreeNodeVisual node)
    {
        OnNodeInfoChanged?.Invoke(
            Loc.GetString(node.Definition.TitleKey),
            BuildNodeDetail(node));
    }

    private void EmitDefaultInfo()
    {
        OnNodeInfoChanged?.Invoke(
            Loc.GetString("wh40k-command-node-upgrade-tree-info-default-title"),
            Loc.GetString("wh40k-command-node-upgrade-tree-info-default-description"));
    }

    private string BuildNodeCaption(TreeNodeVisual node)
    {
        var title = Loc.GetString(node.Definition.TitleKey);
        return node.State switch
        {
            NodeVisualState.Purchased => Loc.GetString("wh40k-command-node-upgrade-tree-node-text-purchased",
                ("title", title)),
            NodeVisualState.LockedByDoctrine => Loc.GetString("wh40k-command-node-upgrade-tree-node-text-doctrine-locked",
                ("title", title)),
            NodeVisualState.LockedByParent => Loc.GetString("wh40k-command-node-upgrade-tree-node-text-parent-locked",
                ("title", title)),
            _ => title
        };
    }

    private string BuildNodeDetail(TreeNodeVisual node)
    {
        var cost = GetRuntimeCost(node.Definition);
        var statusLine = node.State switch
        {
            NodeVisualState.Available => Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-state-available-cost",
                ("cost", cost)),
            NodeVisualState.Purchased => Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-state-purchased"),
            NodeVisualState.LockedByParent => Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-state-parent-locked"),
            NodeVisualState.LockedByLevel => Loc.GetString(
                "wh40k-command-node-upgrade-tree-tooltip-state-level-locked",
                ("level", node.Definition.MinBaseLevel),
                ("current", _baseLevel)),
            NodeVisualState.LockedByTime => BuildRoundTimeLockText(node.Definition.MinRoundTimeSeconds),
            NodeVisualState.LockedByDoctrine => BuildDoctrineLockStateText(),
            NodeVisualState.LockedByPoints => Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-state-points-locked",
                ("cost", cost)),
            NodeVisualState.Inactive => Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-state-inactive"),
            _ => Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-state-available")
        };

        var detail = Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-template",
            ("description", Loc.GetString(node.Definition.DescriptionKey)),
            ("status", statusLine));
        return NormalizeMultiline(detail);
    }

    private int GetRuntimeCost(TreeNodeDefinition definition)
    {
        return WH40KCommandTreeCostCalculator.GetEffectiveNodeCost(
            definition.Cost,
            _commandPoints,
            _baseLevel,
            _phase,
            _activeCostProfile);
    }

    private string BuildDoctrineLockStateText()
    {
        if (string.IsNullOrWhiteSpace(_activeDoctrineId))
            return Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-state-doctrine-locked");

        var doctrine = WH40KCommandNodeDoctrineWindow.ResolveDoctrineDisplay(_activeDoctrineId, _teamId);
        return Loc.GetString("wh40k-command-node-upgrade-tree-tooltip-state-doctrine-locked-with-name",
            ("doctrine", doctrine.Name));
    }

    private string BuildRoundTimeLockText(int requiredRoundSeconds)
    {
        var required = Math.Max(0, requiredRoundSeconds);
        var remaining = Math.Max(0, required - _roundElapsedSeconds);
        return Loc.GetString(
            "wh40k-command-node-upgrade-tree-tooltip-state-time-locked",
            ("time", FormatClock(required)),
            ("left", FormatClock(remaining)));
    }

    private Vector2 GetNodeAnchorBottom(TreeNodeVisual node)
    {
        var box = GetNodeDrawBox(node);
        return new Vector2(box.Center.X, box.Bottom);
    }

    private Vector2 GetNodeAnchorTop(TreeNodeVisual node)
    {
        var box = GetNodeDrawBox(node);
        return new Vector2(box.Center.X, box.Top);
    }

    private static UIBox2 GetNodeDrawBox(TreeNodeVisual node)
    {
        var position = node.Button.PixelPosition;
        var size = node.Button.PixelSize;
        return new UIBox2(position.X, position.Y, position.X + size.X, position.Y + size.Y);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var delta = (float) args.DeltaSeconds;
        if (delta <= 0f)
            return;

        foreach (var node in _orderedNodes)
        {
            AdvanceNodeCaption(node, delta);
        }
    }

    private void AdvanceNodeCaption(TreeNodeVisual node, float deltaSeconds)
    {
        if (node.FullCaption.Length <= MarqueeVisibleChars)
            return;

        if (node.MarqueePause > 0f)
        {
            node.MarqueePause -= deltaSeconds;
            return;
        }

        node.MarqueeTickAccumulator += deltaSeconds;
        if (node.MarqueeTickAccumulator < MarqueeTickSeconds)
            return;

        node.MarqueeTickAccumulator -= MarqueeTickSeconds;
        node.MarqueeOffset++;

        var cycleLength = node.FullCaption.Length + 3;
        if (node.MarqueeOffset >= cycleLength)
        {
            node.MarqueeOffset = 0;
            node.MarqueePause = MarqueePauseSeconds;
        }

        UpdateNodeCaption(node);
    }

    private static void UpdateNodeCaption(TreeNodeVisual node)
    {
        if (node.FullCaption.Length <= MarqueeVisibleChars)
        {
            node.Button.Text = node.FullCaption;
            return;
        }

        var tape = $"{node.FullCaption}   {node.FullCaption}";
        var offset = Math.Clamp(node.MarqueeOffset, 0, node.FullCaption.Length + 2);
        var visible = tape.Substring(offset, Math.Min(MarqueeVisibleChars, tape.Length - offset));
        node.Button.Text = visible;
    }

    private static string NormalizeMultiline(string text)
    {
        return text.Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private static string FormatClock(int totalSeconds)
    {
        var safe = Math.Max(0, totalSeconds);
        var minutes = safe / 60;
        var seconds = safe % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private static string NormalizeKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text.Trim().ToLowerInvariant();
    }

    private void SyncPurchasedNodes(IReadOnlyList<string> purchasedNodeIds)
    {
        _purchasedNodes.Clear();

        foreach (var nodeId in purchasedNodeIds)
        {
            var normalized = NormalizeKey(nodeId);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            _purchasedNodes.Add(normalized);
        }
    }

    /// <summary>
    /// Sugiyama-style layered layout:
    /// 1) assign tiers by longest parent path;
    /// 2) reduce crossings with barycentric sweeps;
    /// 3) map order in each tier to rank [0..1] inside domain column.
    /// </summary>
    private void RecomputeAutomaticLayout()
    {
        _nodeLayouts.Clear();
        _maxComputedTier = 0;

        var orderedDomains = _domainIndices
            .OrderBy(entry => entry.Value)
            .Select(entry => entry.Key);

        foreach (var domainId in orderedDomains)
        {
            var domainNodes = _orderedNodes
                .Where(n => string.Equals(n.Definition.DomainId, domainId, StringComparison.Ordinal))
                .Select(n => n.Definition.Id)
                .ToList();

            if (domainNodes.Count == 0)
                continue;

            var canonicalIds = new Dictionary<string, string>(domainNodes.Count);
            foreach (var id in domainNodes)
            {
                var key = NormalizeKey(id);
                if (!canonicalIds.ContainsKey(key))
                    canonicalIds[key] = id;
            }

            var parentMap = new Dictionary<string, List<string>>(domainNodes.Count);
            var childMap = new Dictionary<string, List<string>>(domainNodes.Count);

            foreach (var id in domainNodes)
            {
                parentMap[id] = _nodes[id].Definition.Parents
                    .Select(parentId =>
                    {
                        canonicalIds.TryGetValue(NormalizeKey(parentId), out var canonicalParentId);
                        return canonicalParentId;
                    })
                    .Where(canonicalParentId => canonicalParentId is not null)
                    .Select(canonicalParentId => canonicalParentId!)
                    .ToList();
                childMap[id] = new List<string>();
            }

            foreach (var (parentId, childId) in _connections)
            {
                if (!canonicalIds.TryGetValue(NormalizeKey(parentId), out var canonicalParentId) ||
                    !canonicalIds.TryGetValue(NormalizeKey(childId), out var canonicalChildId))
                    continue;

                childMap[canonicalParentId].Add(canonicalChildId);
            }

            var tierMap = ComputeTierMap(parentMap, domainNodes);
            var tierLists = BuildTierLists(tierMap);
            var orderMap = CreateInitialOrder(tierLists);

            for (var i = 0; i < 4; i++)
            {
                SweepDown(tierLists, parentMap, orderMap);
                SweepUp(tierLists, childMap, orderMap);
            }

            foreach (var (tier, ids) in tierLists)
            {
                _maxComputedTier = Math.Max(_maxComputedTier, tier);
                var count = ids.Count;
                if (count == 0)
                    continue;

                for (var index = 0; index < count; index++)
                {
                    var id = ids[index];
                    var rank = count == 1
                        ? 0.5f
                        : (index + 1f) / (count + 1f);
                    _nodeLayouts[id] = new NodeLayout(tier, rank);
                }
            }
        }
    }

    private static Dictionary<string, int> ComputeTierMap(
        IReadOnlyDictionary<string, List<string>> parentMap,
        IReadOnlyList<string> domainNodes)
    {
        var tiers = new Dictionary<string, int>(domainNodes.Count);
        var visiting = new HashSet<string>();

        int ResolveTier(string id)
        {
            if (tiers.TryGetValue(id, out var cachedTier))
                return cachedTier;

            if (!visiting.Add(id))
                return 0;

            var tier = 0;
            if (parentMap.TryGetValue(id, out var parents))
            {
                foreach (var parent in parents)
                {
                    tier = Math.Max(tier, ResolveTier(parent) + 1);
                }
            }

            visiting.Remove(id);
            tiers[id] = tier;
            return tier;
        }

        foreach (var id in domainNodes)
        {
            ResolveTier(id);
        }

        return tiers;
    }

    private static SortedDictionary<int, List<string>> BuildTierLists(
        IReadOnlyDictionary<string, int> tierMap)
    {
        var tiers = new SortedDictionary<int, List<string>>();
        foreach (var (id, tier) in tierMap)
        {
            if (!tiers.TryGetValue(tier, out var list))
            {
                list = new List<string>();
                tiers[tier] = list;
            }

            list.Add(id);
        }

        foreach (var (_, list) in tiers)
        {
            list.Sort(static (a, b) => string.Compare(a, b, StringComparison.Ordinal));
        }

        return tiers;
    }

    private static Dictionary<string, float> CreateInitialOrder(
        IReadOnlyDictionary<int, List<string>> tierLists)
    {
        var order = new Dictionary<string, float>();
        foreach (var (_, ids) in tierLists)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                order[ids[i]] = i;
            }
        }

        return order;
    }

    private static void SweepDown(
        IReadOnlyDictionary<int, List<string>> tierLists,
        IReadOnlyDictionary<string, List<string>> parentMap,
        IDictionary<string, float> order)
    {
        foreach (var (tier, ids) in tierLists)
        {
            if (tier <= 0 || ids.Count <= 1)
                continue;

            ids.Sort((a, b) => CompareByBarycenter(a, b, parentMap, order));
            ReindexOrder(ids, order);
        }
    }

    private static void SweepUp(
        IReadOnlyDictionary<int, List<string>> tierLists,
        IReadOnlyDictionary<string, List<string>> childMap,
        IDictionary<string, float> order)
    {
        var keys = tierLists.Keys.OrderByDescending(x => x).ToList();
        foreach (var tier in keys)
        {
            if (!tierLists.TryGetValue(tier, out var ids))
                continue;

            if (ids.Count <= 1)
                continue;

            ids.Sort((a, b) => CompareByBarycenter(a, b, childMap, order));
            ReindexOrder(ids, order);
        }
    }

    private static int CompareByBarycenter(
        string a,
        string b,
        IReadOnlyDictionary<string, List<string>> adjacency,
        IDictionary<string, float> order)
    {
        var baryA = ComputeBarycenter(a, adjacency, order);
        var baryB = ComputeBarycenter(b, adjacency, order);
        var delta = baryA - baryB;
        if (MathF.Abs(delta) > 0.001f)
            return delta < 0f ? -1 : 1;

        var orderA = order.TryGetValue(a, out var oa) ? oa : 0f;
        var orderB = order.TryGetValue(b, out var ob) ? ob : 0f;
        var orderDelta = orderA - orderB;
        if (MathF.Abs(orderDelta) > 0.001f)
            return orderDelta < 0f ? -1 : 1;

        return string.Compare(a, b, StringComparison.Ordinal);
    }

    private static float ComputeBarycenter(
        string id,
        IReadOnlyDictionary<string, List<string>> adjacency,
        IDictionary<string, float> order)
    {
        if (!adjacency.TryGetValue(id, out var neighbors) || neighbors.Count == 0)
            return order.TryGetValue(id, out var own) ? own : 0f;

        var sum = 0f;
        var count = 0;
        foreach (var neighbor in neighbors)
        {
            if (!order.TryGetValue(neighbor, out var neighborOrder))
                continue;

            sum += neighborOrder;
            count++;
        }

        if (count == 0)
            return order.TryGetValue(id, out var own) ? own : 0f;

        return sum / count;
    }

    private static void ReindexOrder(
        IReadOnlyList<string> ids,
        IDictionary<string, float> order)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            order[ids[i]] = i;
        }
    }

    private int GetDomainIndex(string domainId)
    {
        return _domainIndices.TryGetValue(NormalizeKey(domainId), out var index)
            ? index
            : 0;
    }

    private Vector2 GetNodeCenter(TreeNodeDefinition definition, Vector2 finalSize)
    {
        var safeWidth = MathF.Max(1f, finalSize.X);
        var safeHeight = MathF.Max(1f, finalSize.Y);

        var usableWidth = MathF.Max(1f, safeWidth - HorizontalPadding * 2f);
        var domainCount = Math.Max(1, _domainIndices.Count);
        var domainWidth = usableWidth / domainCount;
        var domainIndex = GetDomainIndex(definition.DomainId);
        var domainLeft = HorizontalPadding + domainWidth * domainIndex;
        var domainRight = domainLeft + domainWidth;

        var layout = _nodeLayouts.TryGetValue(definition.Id, out var computedLayout)
            ? computedLayout
            : new NodeLayout(0, 0.5f);

        var rank = Math.Clamp(layout.Rank, 0f, 1f);
        rank = Math.Clamp(0.5f + (rank - 0.5f) * HorizontalRankSpread, 0f, 1f);
        var centerX = domainLeft + domainWidth * rank;
        var minCenterX = domainLeft + DomainInnerPadding + NodeSize.X * 0.5f;
        var maxCenterX = domainRight - DomainInnerPadding - NodeSize.X * 0.5f;
        centerX = minCenterX <= maxCenterX
            ? Math.Clamp(centerX, minCenterX, maxCenterX)
            : (domainLeft + domainRight) * 0.5f;

        var rowTop = VerticalPaddingTop + NodeSize.Y * 0.5f;
        var rowBottom = MathF.Max(rowTop + 1f, safeHeight - VerticalPaddingBottom - NodeSize.Y * 0.5f);
        var maxTier = Math.Max(1, _maxComputedTier);
        var rowStep = (rowBottom - rowTop) / maxTier;
        var tier = Math.Clamp(layout.Tier, 0, maxTier);
        var centerY = rowTop + rowStep * tier;

        return SnapToPixel(new Vector2(centerX, centerY));
    }

    private Color GetConnectorColor(NodeVisualState parentState, NodeVisualState childState)
    {
        if (childState == NodeVisualState.Purchased)
            return PurchasedBorderColor;

        if (childState == NodeVisualState.LockedByDoctrine || parentState == NodeVisualState.LockedByDoctrine)
            return DoctrineLockBorderColor.WithAlpha(0.75f);

        if (childState == NodeVisualState.Available)
            return _accentColor.WithAlpha(0.85f);

        return LockedBorderColor.WithAlpha(0.75f);
    }
}
