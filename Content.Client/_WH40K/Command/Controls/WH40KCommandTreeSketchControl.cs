using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._WH40K.Command;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Content.Shared.Research.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Command.Controls;

/// <summary>
/// Visualizes the command-node progression tree and reflects authoritative backend state.
/// </summary>
public sealed class WH40KCommandTreeSketchControl : LayoutContainer
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("wh40k.command");
    private static readonly Color CanvasBackgroundColor = WH40KCommandUiStyles.PanelBackgroundAlt;
    private static readonly Color CanvasBorderColor = WH40KCommandUiStyles.StrongBorder;
    private static readonly Color DomainBackgroundColor = Color.FromHex("#0E1015");
    private static readonly Color DomainBackgroundAltColor = Color.FromHex("#101218");
    private static readonly Color DomainBorderColor = Color.FromHex("#4B3E25");
    private static readonly Color TierGuideColor = Color.FromHex("#4B3E25");
    private static readonly Color AvailableBackgroundColor = Color.FromHex("#17130D");
    private static readonly Color AvailableBorderColor = WH40KCommandUiStyles.DefaultAccent;
    private static readonly Color AvailableHoverBackgroundColor = Color.FromHex("#1E1810");
    private static readonly Color LockedBackgroundColor = Color.FromHex("#11131A");
    private static readonly Color LockedBorderColor = WH40KCommandUiStyles.MutedBorder;
    private static readonly Color LockedHoverBackgroundColor = Color.FromHex("#171A21");
    private static readonly Color PurchasedBackgroundColor = Color.FromHex("#162015");
    private static readonly Color PurchasedBorderColor = WH40KCommandUiStyles.ReadyBadge;
    private static readonly Color PurchasedHoverBackgroundColor = Color.FromHex("#1B2819");
    private static readonly Color PointLockBackgroundColor = Color.FromHex("#2F2417");
    private static readonly Color PointLockBorderColor = WH40KCommandUiStyles.WarningBadge;
    private static readonly Color PointLockHoverBackgroundColor = Color.FromHex("#3A2D1C");
    private static readonly Color InactiveBackgroundColor = Color.FromHex("#0E1015");
    private static readonly Color InactiveBorderColor = Color.FromHex("#3E3320");
    private static readonly Color InactiveHoverBackgroundColor = Color.FromHex("#15181F");
    private static readonly Color NodeInsetBackgroundColor = Color.FromHex("#0B0C10");
    private static readonly Vector2 NodeSize = new(160f, 78f);
    private const float HorizontalPadding = 18f;
    private const float DomainInnerPadding = 8f;
    private const float VerticalPaddingTop = 20f;
    private const float VerticalPaddingBottom = 20f;
    private const float HorizontalRankSpread = 1.05f;
    private const string CommandTreeTeamMapId = "WH40KCommandTreeTeamMap";
    private const string CommandTreeDefaultProfileId = "WH40KCommandTreeProfileDefault";

    private enum NodeVisualState
    {
        Available,
        LockedByParent,
        LockedByLevel,
        LockedByTime,
        Purchased,
        LockedByPoints,
        Inactive
    }

    public sealed record WH40KCommandTreeNodeInfo(
        string NodeId,
        string DomainId,
        string Title,
        string BadgeStatus,
        Color BadgeColor,
        string Status,
        string Cost,
        string Requirements,
        string ResearchUnlocks,
        string Effects,
        string Description,
        IReadOnlyList<string> ResearchUnlockEntries,
        int MinBaseLevel,
        bool Purchased,
        bool Available);

    public sealed record WH40KCommandTreeDomainSummary(
        string DomainId,
        int PurchasedCount,
        int AvailableCount,
        int TotalCount);

    private readonly record struct TreeNodeDefinition(
        string Id,
        string DomainId,
        string TitleKey,
        string DescriptionKey,
        string[] Parents,
        int Cost,
        int MinBaseLevel,
        int MinRoundTimeSeconds,
        string[] TechnologyUnlockIds,
        string[] LatheRecipeUnlockIds,
        string[] CargoProductUnlockIds,
        int ResearchPointGrant,
        int MachineSpeedBonusPercent,
        int MachineStorageBonus,
        int CargoDeliverySpeedBonusPercent,
        int CargoMaxItemsBonusPercent,
        int CargoPriceDiscountPercent,
        int ResearchTimeSpeedBonusPercent,
        int ResearchPointBonusPercent);

    private readonly record struct NodeLayout(int Tier, float Rank);

    private sealed class TreeNodeVisual
    {
        public TreeNodeDefinition Definition { get; }
        public ContainerButton Button { get; }
        public StyleBoxFlat Style { get; }
        public StyleBoxFlat InsetStyle { get; }
        public Label TitleLabel { get; }
        public Label MetaLabel { get; }
        public NodeVisualState State { get; set; }
        public bool Hovered { get; set; }

        public TreeNodeVisual(
            TreeNodeDefinition definition,
            ContainerButton button,
            StyleBoxFlat style,
            StyleBoxFlat insetStyle,
            Label titleLabel,
            Label metaLabel)
        {
            Definition = definition;
            Button = button;
            Style = style;
            InsetStyle = insetStyle;
            TitleLabel = titleLabel;
            MetaLabel = metaLabel;
        }
    }

    public event Action<WH40KCommandTreeNodeInfo>? OnNodeInfoChanged;
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
    private int _funds;
    private int _researchPoints;
    private int _baseLevel = 1;
    private int _roundElapsedSeconds;

    private string _teamId = string.Empty;
    private string _activeProfileId = string.Empty;
    private Color _accentColor = WH40KCommandUiStyles.DefaultAccent;
    private string? _focusedNodeId;
    private string _selectedDomainId = string.Empty;
    private WH40KCommandTreeNodeInfo? _currentNodeInfo;

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
    public WH40KCommandTreeNodeInfo? CurrentNodeInfo => _currentNodeInfo;

    public string SelectedDomainId
    {
        get => _selectedDomainId;
        set
        {
            var normalized = NormalizeKey(value);
            if (_selectedDomainId == normalized)
                return;

            _selectedDomainId = normalized;
            RefreshCanvasMetrics();
            RefreshVisualState();
        }
    }

    public WH40KCommandTreeSketchControl()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
        MinHeight = 680f;
        MinWidth = 560f;
        LoadTreeForTeam(string.Empty);
        EmitDefaultInfo();
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        var teamChanged = !string.Equals(_teamId, state.TeamId, StringComparison.OrdinalIgnoreCase);
        if (teamChanged)
        {
            _purchasedNodes.Clear();
            LoadTreeForTeam(state.TeamId);
        }

        _teamId = state.TeamId;
        _funds = Math.Max(0, state.Funds);
        _researchPoints = Math.Max(0, state.ResearchPoints);
        _baseLevel = Math.Max(1, state.BaseLevel);
        _roundElapsedSeconds = Math.Max(0, state.RoundElapsedSeconds);
        SyncPurchasedNodes(state.PurchasedTreeNodeIds);
        RefreshVisualState();
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        foreach (var node in _orderedNodes)
        {
            if (!IsNodeVisible(node.Definition))
                continue;

            var center = GetNodeCenter(node.Definition, finalSize);
            SetPosition(node.Button, center - NodeSize * 0.5f);
        }

        return base.ArrangeOverride(finalSize);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawRect(PixelSizeBox, CanvasBackgroundColor);
        handle.DrawRect(PixelSizeBox, CanvasBorderColor, false);
        DrawDomainColumns(handle);
        DrawTierGuides(handle);

        foreach (var (parentId, childId) in _connections)
        {
            if (!_nodes.TryGetValue(parentId, out var parent) ||
                !_nodes.TryGetValue(childId, out var child))
            {
                continue;
            }

            if (!IsNodeVisible(parent.Definition) || !IsNodeVisible(child.Definition))
                continue;

            var start = GetNodeAnchorBottom(parent);
            var end = GetNodeAnchorTop(child);
            var points = BuildConnectorPoints(start, end);

            DrawConnector(handle, points, GetConnectorColor(parent.State, child.State));
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

    private void DrawDomainColumns(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var usableWidth = MathF.Max(1f, size.X - HorizontalPadding * 2f);
        var domainCount = GetRenderedDomainCount();
        var domainWidth = usableWidth / domainCount;
        var top = VerticalPaddingTop * 0.5f;
        var bottom = MathF.Max(top + 1f, size.Y - VerticalPaddingBottom * 0.5f);

        for (var index = 0; index < domainCount; index++)
        {
            var left = HorizontalPadding + domainWidth * index;
            var right = left + domainWidth;
            var box = new UIBox2(left, top, right, bottom);
            var fill = index % 2 == 0 ? DomainBackgroundColor : DomainBackgroundAltColor;
            handle.DrawRect(box, fill);
            handle.DrawRect(box, DomainBorderColor.WithAlpha(0.75f), false);
        }
    }

    private void DrawTierGuides(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var left = HorizontalPadding;
        var right = MathF.Max(left + 1f, size.X - HorizontalPadding);
        var rowTop = VerticalPaddingTop + NodeSize.Y * 0.5f;
        var rowBottom = MathF.Max(rowTop + 1f, size.Y - VerticalPaddingBottom - NodeSize.Y * 0.5f);
        var maxTier = Math.Max(1, GetRenderedMaxTier());
        var rowStep = (rowBottom - rowTop) / maxTier;

        for (var tier = 0; tier <= maxTier; tier++)
        {
            var y = MathF.Round(rowTop + rowStep * tier);
            var alpha = tier == 0 ? 0.55f : 0.22f;
            handle.DrawLine(
                new Vector2(left, y),
                new Vector2(right, y),
                TierGuideColor.WithAlpha(alpha));
        }
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
            Sawmill.Error($"Missing command-tree profile prototype '{profileId}'.");
            return;
        }

        RebuildFromProfile(profile, teamId);
        _activeProfileId = profile.ID;
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

    private void RebuildFromProfile(WH40KCommandTreeProfilePrototype profile, string teamId)
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
                Math.Max(0, node.MinRoundTimeSeconds),
                ResolveTechnologyUnlockIds(node, teamId),
                ResolveLatheRecipeUnlockIds(node, teamId),
                ResolveCargoProductUnlockIds(node, teamId),
                Math.Max(0, node.ResearchPointGrant),
                Math.Max(0, node.MachineSpeedBonusPercent),
                Math.Max(0, node.MachineStorageBonus),
                Math.Max(0, node.CargoDeliverySpeedBonusPercent),
                Math.Max(0, node.CargoMaxItemsBonusPercent),
                Math.Max(0, node.CargoPriceDiscountPercent),
                Math.Max(0, node.ResearchTimeSpeedBonusPercent),
                Math.Max(0, node.ResearchPointBonusPercent)));
        }

        foreach (var node in _orderedNodes)
        {
            foreach (var parentId in node.Definition.Parents)
            {
                _connections.Add((parentId, node.Definition.Id));
            }
        }

        RecomputeAutomaticLayout();
        RefreshCanvasMetrics();
        RefreshVisualState();
    }

    private static string[] ResolveTechnologyUnlockIds(WH40KCommandTreeNodeConfig node, string teamId)
    {
        var resolved = new List<string>();
        resolved.AddRange(node.TechnologyUnlocks.Select(id => id.ToString()));

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            foreach (var (mappedTeamId, unlocks) in node.TeamTechnologyUnlocks)
            {
                if (!string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolved.AddRange(unlocks.Select(id => id.ToString()));
            }
        }

        return resolved
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ResolveLatheRecipeUnlockIds(WH40KCommandTreeNodeConfig node, string teamId)
    {
        var resolved = new List<string>();
        resolved.AddRange(node.LatheRecipeUnlocks.Select(id => id.ToString()));

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            foreach (var (mappedTeamId, unlocks) in node.TeamLatheRecipeUnlocks)
            {
                if (!string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolved.AddRange(unlocks.Select(id => id.ToString()));
            }
        }

        return resolved
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ResolveCargoProductUnlockIds(WH40KCommandTreeNodeConfig node, string teamId)
    {
        var resolved = new List<string>();
        resolved.AddRange(node.CargoProductUnlocks.Select(id => id.ToString()));

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            foreach (var (mappedTeamId, unlocks) in node.TeamCargoProductUnlocks)
            {
                if (!string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolved.AddRange(unlocks.Select(id => id.ToString()));
            }
        }

        return resolved
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void AddNode(TreeNodeDefinition definition)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = AvailableBackgroundColor,
            BorderColor = AvailableBorderColor,
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 6,
            ContentMarginTopOverride = 6,
            ContentMarginRightOverride = 6,
            ContentMarginBottomOverride = 6
        };

        var insetStyle = new StyleBoxFlat
        {
            BackgroundColor = NodeInsetBackgroundColor,
            BorderColor = AvailableBorderColor.WithAlpha(0.5f),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 6,
            ContentMarginTopOverride = 2,
            ContentMarginRightOverride = 6,
            ContentMarginBottomOverride = 2
        };

        var button = new ContainerButton
        {
            HorizontalExpand = false,
            VerticalExpand = false,
            SetSize = NodeSize,
            MinSize = NodeSize,
            StyleBoxOverride = style
        };

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        button.AddChild(content);

        var titleLabel = new Label
        {
            Align = Label.AlignMode.Center,
            VAlign = Label.VAlignMode.Center,
            ClipText = true,
            HorizontalExpand = true,
            VerticalExpand = true,
            StyleClasses = { "LabelSubText" }
        };
        content.AddChild(titleLabel);

        var metaPanel = new PanelContainer
        {
            PanelOverride = insetStyle,
            HorizontalExpand = true
        };
        var metaLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true,
            HorizontalExpand = true,
            StyleClasses = { "LabelSubText" }
        };
        metaPanel.AddChild(metaLabel);
        content.AddChild(metaPanel);

        var visual = new TreeNodeVisual(definition, button, style, insetStyle, titleLabel, metaLabel);
        _nodes[definition.Id] = visual;
        _orderedNodes.Add(visual);

        button.OnPressed += _ => OnNodePressed(visual);
        button.OnMouseEntered += _ => OnNodeMouseEntered(visual);
        button.OnMouseExited += _ => OnNodeMouseExited(visual);

        AddChild(button);
    }

    private void OnNodePressed(TreeNodeVisual node)
    {
        _focusedNodeId = node.Definition.Id;

        if (node.State != NodeVisualState.Available)
        {
            EmitNodeInfo(node);
            return;
        }

        OnPurchaseRequested?.Invoke(node.Definition.Id);
        EmitNodeInfo(node);
    }

    private void OnNodeMouseEntered(TreeNodeVisual node)
    {
        _focusedNodeId = node.Definition.Id;
        node.Hovered = true;
        ApplyNodeStyle(node);
        EmitNodeInfo(node);
    }

    private void OnNodeMouseExited(TreeNodeVisual node)
    {
        node.Hovered = false;
        ApplyNodeStyle(node);
    }

    private void RefreshVisualState()
    {
        var focusedNodeVisible = false;

        foreach (var node in _orderedNodes)
        {
            node.State = ResolveNodeState(node.Definition);
            ApplyNodeStyle(node);
            node.Button.Visible = IsNodeVisible(node.Definition);

            if (node.Button.Visible &&
                _focusedNodeId != null &&
                string.Equals(_focusedNodeId, node.Definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                focusedNodeVisible = true;
            }
        }

        if (_focusedNodeId != null &&
            focusedNodeVisible &&
            _nodes.TryGetValue(_focusedNodeId, out var focusedNode))
        {
            EmitNodeInfo(focusedNode);
        }
        else
        {
            EmitDefaultInfo();
        }

        InvalidateMeasure();
        InvalidateArrange();
    }

    private NodeVisualState ResolveNodeState(TreeNodeDefinition definition)
    {
        if (_purchasedNodes.Contains(definition.Id))
            return NodeVisualState.Purchased;

        if (definition.Cost <= 0)
            return NodeVisualState.Inactive;

        if (definition.Parents.Any(parentId => !_purchasedNodes.Contains(parentId)))
            return NodeVisualState.LockedByParent;

        if (_baseLevel < definition.MinBaseLevel)
            return NodeVisualState.LockedByLevel;

        if (!CanAffordNode(definition))
            return NodeVisualState.LockedByPoints;

        return NodeVisualState.Available;
    }

    private void ApplyNodeStyle(TreeNodeVisual node)
    {
        node.Button.ToolTip = null;
        Color titleColor;
        Color metaBorder;
        Color metaBackground;
        Color metaTextColor;

        switch (node.State)
        {
            case NodeVisualState.Purchased:
                node.Style.BackgroundColor = node.Hovered
                    ? PurchasedHoverBackgroundColor
                    : PurchasedBackgroundColor;
                node.Style.BorderColor = PurchasedBorderColor;
                node.Button.Disabled = true;
                titleColor = Color.FromHex("#E3F2D7");
                metaBorder = PurchasedBorderColor;
                metaBackground = PurchasedBorderColor.WithAlpha(0.18f);
                metaTextColor = PurchasedBorderColor;
                break;
            case NodeVisualState.LockedByPoints:
                node.Style.BackgroundColor = node.Hovered
                    ? PointLockHoverBackgroundColor
                    : PointLockBackgroundColor;
                node.Style.BorderColor = PointLockBorderColor;
                node.Button.Disabled = true;
                titleColor = WH40KCommandUiStyles.SoftText;
                metaBorder = LockedBorderColor;
                metaBackground = LockedBorderColor.WithAlpha(0.14f);
                metaTextColor = WH40KCommandUiStyles.MutedText;
                break;
            case NodeVisualState.Inactive:
                node.Style.BackgroundColor = node.Hovered
                    ? InactiveHoverBackgroundColor
                    : InactiveBackgroundColor;
                node.Style.BorderColor = InactiveBorderColor;
                node.Button.Disabled = true;
                titleColor = WH40KCommandUiStyles.MutedText;
                metaBorder = InactiveBorderColor;
                metaBackground = InactiveBorderColor.WithAlpha(0.14f);
                metaTextColor = WH40KCommandUiStyles.MutedText;
                break;
            case NodeVisualState.LockedByLevel:
            case NodeVisualState.LockedByTime:
            case NodeVisualState.LockedByParent:
                node.Style.BackgroundColor = node.Hovered
                    ? LockedHoverBackgroundColor
                    : LockedBackgroundColor;
                node.Style.BorderColor = LockedBorderColor;
                node.Button.Disabled = true;
                titleColor = WH40KCommandUiStyles.SoftText;
                metaBorder = LockedBorderColor;
                metaBackground = LockedBorderColor.WithAlpha(0.14f);
                metaTextColor = WH40KCommandUiStyles.MutedText;
                break;
            default:
                node.Style.BackgroundColor = node.Hovered
                    ? AvailableHoverBackgroundColor
                    : AvailableBackgroundColor;
                node.Style.BorderColor = node.Hovered ? AvailableBorderColor : _accentColor;
                node.Button.Disabled = false;
                titleColor = Color.White;
                metaBorder = _accentColor;
                metaBackground = _accentColor.WithAlpha(0.14f);
                metaTextColor = _accentColor;
                break;
        }

        node.InsetStyle.BorderColor = metaBorder;
        node.InsetStyle.BackgroundColor = metaBackground;
        node.TitleLabel.FontColorOverride = titleColor;
        node.MetaLabel.FontColorOverride = metaTextColor;
        node.TitleLabel.Text = BuildNodeTitle(node);
        node.MetaLabel.Text = BuildNodeMeta(node);
        node.Button.ToolTip = BuildNodeTooltip(node);
    }

    private void EmitNodeInfo(TreeNodeVisual node)
    {
        var info = BuildNodeInfo(node);
        _currentNodeInfo = info;
        OnNodeInfoChanged?.Invoke(info);
    }

    private void EmitDefaultInfo()
    {
        _currentNodeInfo = new WH40KCommandTreeNodeInfo(
            string.Empty,
            string.Empty,
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-title"),
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-state"),
            WH40KCommandUiStyles.ResolveMutedBorder(false),
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-state"),
            string.Empty,
            string.Empty,
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-research"),
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-effects"),
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-description"),
            Array.Empty<string>(),
            1,
            false,
            false);
        OnNodeInfoChanged?.Invoke(_currentNodeInfo);
    }

    public IReadOnlyList<WH40KCommandTreeNodeInfo> GetBranchIntel(string? domainId = null)
    {
        var normalizedDomainId = NormalizeKey(domainId);
        return _orderedNodes
            .Where(node => string.IsNullOrWhiteSpace(normalizedDomainId) ||
                           string.Equals(node.Definition.DomainId, normalizedDomainId, StringComparison.Ordinal))
            .OrderBy(node => GetDomainIndex(node.Definition.DomainId))
            .ThenBy(node => _nodeLayouts.TryGetValue(node.Definition.Id, out var layout) ? layout.Tier : 0)
            .ThenBy(node => _nodeLayouts.TryGetValue(node.Definition.Id, out var layout) ? layout.Rank : 0f)
            .Select(BuildNodeInfo)
            .ToArray();
    }

    private bool CanAffordNode(TreeNodeDefinition definition)
    {
        return _funds >= GetRuntimeFundsCost(definition) &&
               _researchPoints >= GetRuntimeResearchCost(definition);
    }

    private string BuildCostText(TreeNodeDefinition definition)
    {
        return Loc.GetString(
            "w40k-cmd-cost-funds-research",
            ("funds", WH40KCommandUiStyles.FormatThroneGelt(GetRuntimeFundsCost(definition))),
            ("research", WH40KCommandUiStyles.FormatResearch(GetRuntimeResearchCost(definition))));
    }

    private string BuildCompactCostText(TreeNodeDefinition definition)
    {
        return $"{WH40KCommandUiStyles.FormatThroneGelt(GetRuntimeFundsCost(definition))} / {WH40KCommandUiStyles.FormatResearch(GetRuntimeResearchCost(definition))}";
    }

    private static int GetRuntimeFundsCost(TreeNodeDefinition definition)
    {
        return WH40KCommandEconomyCalculator.GetCommandTreeFundsCost(Math.Max(0, definition.Cost));
    }

    private static int GetRuntimeResearchCost(TreeNodeDefinition definition)
    {
        return WH40KCommandEconomyCalculator.GetCommandTreeResearchCost(Math.Max(0, definition.Cost));
    }

    private string BuildRoundTimeLockText(int requiredRoundSeconds)
    {
        var required = Math.Max(0, requiredRoundSeconds);
        var remaining = Math.Max(0, required - _roundElapsedSeconds);
        return Loc.GetString(
            "w40k-cmd-upgrade-tree-tooltip-state-time-locked",
            ("time", FormatClock(required)),
            ("left", FormatClock(remaining)));
    }

    private string BuildNodeTitle(TreeNodeVisual node)
    {
        return SplitTitleIntoTwoLines(Loc.GetString(node.Definition.TitleKey), 14);
    }

    private string BuildNodeMeta(TreeNodeVisual node)
    {
        return node.State switch
        {
            NodeVisualState.Purchased => Loc.GetString("w40k-cmd-upgrade-tree-node-meta-purchased"),
            NodeVisualState.LockedByParent => Loc.GetString("w40k-cmd-upgrade-tree-node-meta-locked"),
            NodeVisualState.LockedByLevel => Loc.GetString("w40k-cmd-upgrade-tree-node-meta-locked"),
            NodeVisualState.LockedByTime => Loc.GetString("w40k-cmd-upgrade-tree-node-meta-locked"),
            NodeVisualState.LockedByPoints => BuildCompactCostText(node.Definition),
            NodeVisualState.Inactive => Loc.GetString("w40k-cmd-upgrade-tree-node-meta-locked"),
            _ => BuildCompactCostText(node.Definition)
        };
    }

    private string BuildNodeBadgeStatus(TreeNodeVisual node)
    {
        return node.State switch
        {
            NodeVisualState.Available => Loc.GetString("w40k-cmd-upgrade-tree-badge-available"),
            NodeVisualState.Purchased => Loc.GetString("w40k-cmd-upgrade-tree-badge-purchased"),
            NodeVisualState.LockedByParent => Loc.GetString("w40k-cmd-upgrade-tree-badge-parent"),
            NodeVisualState.LockedByLevel => Loc.GetString("w40k-cmd-upgrade-tree-badge-level"),
            NodeVisualState.LockedByTime => Loc.GetString("w40k-cmd-upgrade-tree-badge-time"),
            NodeVisualState.LockedByPoints => Loc.GetString("w40k-cmd-upgrade-tree-badge-budget"),
            NodeVisualState.Inactive => Loc.GetString("w40k-cmd-upgrade-tree-badge-inactive"),
            _ => Loc.GetString("w40k-cmd-upgrade-tree-badge-available")
        };
    }

    private Color ResolveBadgeColor(NodeVisualState state)
    {
        return state switch
        {
            NodeVisualState.Available => _accentColor,
            NodeVisualState.Purchased => PurchasedBorderColor,
            NodeVisualState.LockedByPoints => PointLockBorderColor,
            NodeVisualState.Inactive => InactiveBorderColor,
            _ => LockedBorderColor
        };
    }

    private string BuildNodeTooltip(TreeNodeVisual node)
    {
        return NormalizeMultiline(BuildNodeStatusText(node));
    }

    private string BuildNodeStatusText(TreeNodeVisual node)
    {
        return node.State switch
        {
            NodeVisualState.Available => Loc.GetString(
                "w40k-cmd-upgrade-tree-tooltip-state-available-cost",
                ("cost", BuildCostText(node.Definition))),
            NodeVisualState.Purchased => Loc.GetString("w40k-cmd-upgrade-tree-tooltip-state-purchased"),
            NodeVisualState.LockedByParent => Loc.GetString("w40k-cmd-upgrade-tree-tooltip-state-parent-locked"),
            NodeVisualState.LockedByLevel => Loc.GetString(
                "w40k-cmd-upgrade-tree-tooltip-state-level-locked",
                ("level", node.Definition.MinBaseLevel),
                ("current", _baseLevel)),
            NodeVisualState.LockedByTime => BuildRoundTimeLockText(node.Definition.MinRoundTimeSeconds),
            NodeVisualState.LockedByPoints => Loc.GetString(
                "w40k-cmd-upgrade-tree-tooltip-state-points-locked",
                ("cost", BuildCostText(node.Definition))),
            NodeVisualState.Inactive => Loc.GetString("w40k-cmd-upgrade-tree-tooltip-state-inactive"),
            _ => Loc.GetString("w40k-cmd-upgrade-tree-tooltip-state-available")
        };
    }

    private string BuildRequirementSummary(TreeNodeDefinition definition)
    {
        var parts = new List<string>();
        if (definition.Parents.Length > 0)
        {
            parts.Add(definition.Parents.Length == 1
                ? Loc.GetString("w40k-cmd-upgrade-tree-requirement-parent-single")
                : Loc.GetString(
                    "w40k-cmd-upgrade-tree-requirement-parent-multi",
                    ("count", definition.Parents.Length)));
        }

        if (definition.MinBaseLevel > 1)
        {
            parts.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-requirement-level",
                ("level", definition.MinBaseLevel)));
        }

        if (definition.MinRoundTimeSeconds > 0)
        {
            parts.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-requirement-time",
                ("time", FormatClock(definition.MinRoundTimeSeconds))));
        }

        return parts.Count == 0
            ? Loc.GetString("w40k-cmd-upgrade-tree-requirement-none")
            : string.Join("\n", parts.Select(part => $"- {part}"));
    }

    private string BuildEffectSummary(TreeNodeDefinition definition)
    {
        var effects = new List<string>();

        if (definition.MachineSpeedBonusPercent > 0)
        {
            effects.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-effect-machine-speed",
                ("value", definition.MachineSpeedBonusPercent)));
        }

        if (definition.MachineStorageBonus > 0)
        {
            effects.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-effect-machine-storage",
                ("value", definition.MachineStorageBonus)));
        }

        if (definition.CargoMaxItemsBonusPercent > 0)
        {
            effects.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-effect-logistics-cap",
                ("value", definition.CargoMaxItemsBonusPercent)));
        }

        if (definition.CargoDeliverySpeedBonusPercent > 0)
        {
            effects.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-effect-logistics-transit",
                ("value", definition.CargoDeliverySpeedBonusPercent)));
        }

        if (definition.CargoPriceDiscountPercent > 0)
        {
            effects.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-effect-logistics-discount",
                ("value", definition.CargoPriceDiscountPercent)));
        }

        if (definition.ResearchPointGrant > 0)
        {
            effects.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-effect-research-grant",
                ("value", WH40KCommandUiStyles.FormatResearch(definition.ResearchPointGrant))));
        }

        if (definition.ResearchPointBonusPercent > 0)
        {
            effects.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-effect-research-yield",
                ("value", definition.ResearchPointBonusPercent)));
        }

        if (definition.ResearchTimeSpeedBonusPercent > 0)
        {
            effects.Add(Loc.GetString(
                "w40k-cmd-upgrade-tree-effect-research-speed",
                ("value", definition.ResearchTimeSpeedBonusPercent)));
        }

        if (effects.Count == 0)
            return Loc.GetString("w40k-cmd-upgrade-tree-effect-none");

        return string.Join("\n", effects);
    }

    private WH40KCommandTreeNodeInfo BuildNodeInfo(TreeNodeVisual node)
    {
        var researchUnlockEntries = BuildResearchUnlockEntries(node.Definition);

        return new WH40KCommandTreeNodeInfo(
            node.Definition.Id,
            node.Definition.DomainId,
            Loc.GetString(node.Definition.TitleKey),
            BuildNodeBadgeStatus(node),
            ResolveBadgeColor(node.State),
            BuildNodeStatusText(node),
            BuildCostText(node.Definition),
            BuildRequirementSummary(node.Definition),
            BuildResearchUnlockSummary(researchUnlockEntries),
            BuildEffectSummary(node.Definition),
            NormalizeMultiline(Loc.GetString(node.Definition.DescriptionKey)),
            researchUnlockEntries,
            node.Definition.MinBaseLevel,
            node.State == NodeVisualState.Purchased,
            node.State == NodeVisualState.Available);
    }

    private string BuildResearchUnlockSummary(IReadOnlyList<string> entries)
    {
        return entries.Count == 0
            ? Loc.GetString("w40k-cmd-upgrade-tree-research-empty")
            : FormatBulletList(entries);
    }

    private List<string> BuildResearchUnlockEntries(TreeNodeDefinition definition)
    {
        return ResolveResearchUnlockNames(definition.TechnologyUnlockIds)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> ResolveResearchUnlockNames(IEnumerable<string> technologyIds)
    {
        var names = new List<string>();
        var allTechnologies = _prototype.EnumeratePrototypes<TechnologyPrototype>().ToArray();

        foreach (var technologyId in technologyIds)
        {
            if (!_prototype.TryIndex(technologyId, out TechnologyPrototype? technology))
                continue;

            if (!technology.Hidden)
                names.Add(Loc.GetString(technology.Name));

            foreach (var follower in allTechnologies)
            {
                if (follower.Hidden)
                    continue;

                if (!follower.TechnologyPrerequisites.Any(prereq =>
                        string.Equals(prereq.ToString(), technologyId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                names.Add(Loc.GetString(follower.Name));
            }
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatBulletList(IEnumerable<string> entries)
    {
        return string.Join("\n", entries.Select(entry => $"- {entry}"));
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

    public IReadOnlyList<WH40KCommandTreeDomainSummary> GetDomainSummaries()
    {
        var summaries = new List<WH40KCommandTreeDomainSummary>(_activeDomainIds.Count);

        foreach (var domainId in _activeDomainIds)
        {
            var domainNodes = _orderedNodes
                .Where(node => string.Equals(node.Definition.DomainId, domainId, StringComparison.Ordinal))
                .ToArray();

            var total = domainNodes.Length;
            var purchased = domainNodes.Count(node => node.State == NodeVisualState.Purchased);
            var available = domainNodes.Count(node => node.State == NodeVisualState.Available);
            summaries.Add(new WH40KCommandTreeDomainSummary(domainId, purchased, available, total));
        }

        return summaries;
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

    private void RefreshCanvasMetrics()
    {
        var domainCount = GetRenderedDomainCount();
        var width = HorizontalPadding * 2f + domainCount * (NodeSize.X + DomainInnerPadding * 2f + 20f);
        var height = VerticalPaddingTop + VerticalPaddingBottom + (GetRenderedMaxTier() + 1) * (NodeSize.Y + 36f);
        MinWidth = Math.Max(560f, width);
        MinHeight = Math.Max(680f, height);
        InvalidateMeasure();
    }

    private static string SplitTitleIntoTwoLines(string title, int targetLineLength)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
            return TrimToNodeLength(title, targetLineLength * 2 + 1);

        var firstLine = new StringBuilder();
        var secondLine = new StringBuilder();

        foreach (var word in words)
        {
            if (firstLine.Length == 0 ||
                firstLine.Length + 1 + word.Length <= targetLineLength ||
                secondLine.Length > 0)
            {
                if (secondLine.Length > 0)
                {
                    if (secondLine.Length > 0)
                        secondLine.Append(' ');
                    secondLine.Append(word);
                }
                else
                {
                    if (firstLine.Length > 0)
                        firstLine.Append(' ');
                    firstLine.Append(word);
                }
            }
            else
            {
                if (secondLine.Length > 0)
                    secondLine.Append(' ');
                secondLine.Append(word);
            }
        }

        if (secondLine.Length == 0)
            return TrimToNodeLength(firstLine.ToString(), targetLineLength * 2 + 1);

        return $"{TrimToNodeLength(firstLine.ToString(), targetLineLength)}\n{TrimToNodeLength(secondLine.ToString(), targetLineLength)}";
    }

    private static string TrimToNodeLength(string text, int maxLength)
    {
        if (maxLength <= 0 || text.Length <= maxLength)
            return text;

        if (maxLength <= 3)
            return text[..maxLength];

        return $"{text[..Math.Max(0, maxLength - 3)]}...";
    }

    private static string TrimToLength(string text, int maxLength)
    {
        if (maxLength <= 0 || text.Length <= maxLength)
            return text;

        return $"{text[..Math.Max(0, maxLength - 1)]}…";
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

    private bool IsNodeVisible(TreeNodeDefinition definition)
    {
        return string.IsNullOrWhiteSpace(_selectedDomainId) ||
               string.Equals(definition.DomainId, _selectedDomainId, StringComparison.Ordinal);
    }

    private int GetRenderedDomainCount()
    {
        return string.IsNullOrWhiteSpace(_selectedDomainId) ? Math.Max(1, _domainIndices.Count) : 1;
    }

    private int GetRenderedDomainIndex(string domainId)
    {
        return string.IsNullOrWhiteSpace(_selectedDomainId) ? GetDomainIndex(domainId) : 0;
    }

    private int GetRenderedMaxTier()
    {
        if (string.IsNullOrWhiteSpace(_selectedDomainId))
            return _maxComputedTier;

        var maxTier = 0;
        foreach (var node in _orderedNodes)
        {
            if (!string.Equals(node.Definition.DomainId, _selectedDomainId, StringComparison.Ordinal))
                continue;

            if (_nodeLayouts.TryGetValue(node.Definition.Id, out var layout))
                maxTier = Math.Max(maxTier, layout.Tier);
        }

        return Math.Max(1, maxTier);
    }

    private Vector2 GetNodeCenter(TreeNodeDefinition definition, Vector2 finalSize)
    {
        var safeWidth = MathF.Max(1f, finalSize.X);
        var safeHeight = MathF.Max(1f, finalSize.Y);

        var usableWidth = MathF.Max(1f, safeWidth - HorizontalPadding * 2f);
        var domainCount = GetRenderedDomainCount();
        var domainWidth = usableWidth / domainCount;
        var domainIndex = GetRenderedDomainIndex(definition.DomainId);
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
        var maxTier = GetRenderedMaxTier();
        var rowStep = (rowBottom - rowTop) / maxTier;
        var tier = Math.Clamp(layout.Tier, 0, maxTier);
        var centerY = rowTop + rowStep * tier;

        return SnapToPixel(new Vector2(centerX, centerY));
    }

    private Color GetConnectorColor(NodeVisualState parentState, NodeVisualState childState)
    {
        if (childState == NodeVisualState.Purchased)
            return PurchasedBorderColor;

        if (childState == NodeVisualState.Available)
            return _accentColor.WithAlpha(0.85f);

        return LockedBorderColor.WithAlpha(0.75f);
    }
}
