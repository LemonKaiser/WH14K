using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._WH40K.Command.Controls;
using Content.Client.Localization;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeUpgradeSketchWindow : FancyWindow, ILocalizedControl
{
    public event Action<string>? OnTreeNodePurchaseRequested;

    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _headerTitleLabel;
    private readonly Label _headerDraftNoteLabel;
    private readonly Label _teamLine;
    private readonly Label _upgradePointsLine;
    private readonly Label _doctrineLine;
    private readonly PanelContainer _teamBadge;
    private readonly Label _teamBadgeLabel;
    private readonly PanelContainer _hoverPanel;
    private readonly BoxContainer _domainHeaderRow;
    private readonly WH40KCommandTreeSketchControl _treeSketch;
    private readonly Label _hoverTitleLine;
    private readonly Label _hoverDescriptionLine;

    private Color _accent = WH40KCommandUiStyles.DefaultAccent;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;
    private string _latestDoctrineId = string.Empty;

    public WH40KCommandNodeUpgradeSketchWindow()
    {
        Title = Loc.GetString("w40k-cmd-upgrade-sketch-window-title");
        MinSize = SetSize = new Vector2(960, 660);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8)
        };
        ContentsContainer.AddChild(root);

        var header = new PanelContainer
        {
            PanelOverride = _headerStyle = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.HeaderBackground,
                _accent,
                2)
        };
        root.AddChild(header);

        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(10, 8),
            VerticalAlignment = VAlignment.Center
        };
        header.AddChild(headerBox);

        var headerInfo = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true
        };
        headerBox.AddChild(headerInfo);

        _headerTitleLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-upgrade-sketch-window-title"),
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        _teamLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _upgradePointsLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _doctrineLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_headerTitleLabel);

        _headerDraftNoteLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-upgrade-sketch-window-draft-note"),
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_headerDraftNoteLabel);
        headerInfo.AddChild(_teamLine);
        headerInfo.AddChild(_upgradePointsLine);
        headerInfo.AddChild(_doctrineLine);

        _teamBadge = new PanelContainer();
        _teamBadgeLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true
        };
        _teamBadge.AddChild(_teamBadgeLabel);
        headerBox.AddChild(_teamBadge);

        _hoverPanel = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackground,
                _accent)
        };
        root.AddChild(_hoverPanel);

        var hoverBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4
        };
        _hoverPanel.AddChild(hoverBox);

        _hoverTitleLine = new Label
        {
            StyleClasses = { "LabelBig" }
        };
        _hoverDescriptionLine = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            StyleClasses = { "LabelSubText" }
        };
        hoverBox.AddChild(_hoverTitleLine);
        hoverBox.AddChild(_hoverDescriptionLine);

        var canvasFrame = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.PanelBackgroundAlt,
                WH40KCommandUiStyles.StrongBorder,
                2)
        };
        root.AddChild(canvasFrame);

        var treeArea = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            VerticalExpand = true
        };
        canvasFrame.AddChild(treeArea);

        var domainsPanel = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateHeaderStripStyle(WH40KCommandUiStyles.MutedBorder)
        };
        treeArea.AddChild(domainsPanel);

        _domainHeaderRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(0)
        };
        domainsPanel.AddChild(_domainHeaderRow);

        var treeScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            HScrollEnabled = true
        };
        treeArea.AddChild(treeScroll);

        _treeSketch = new WH40KCommandTreeSketchControl
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinSize = new Vector2(700f, 540f)
        };
        _treeSketch.OnNodeInfoChanged += OnNodeInfoChanged;
        _treeSketch.OnPurchaseRequested += OnTreeNodePurchaseRequestedInternal;
        treeScroll.AddChild(_treeSketch);
        RebuildDomainHeaders(_treeSketch.ActiveDomainIds);

        ApplyDefaultHoverInfo();
        Relocalize();
    }

    public void Relocalize()
    {
        Title = Loc.GetString("w40k-cmd-upgrade-sketch-window-title");
        _headerTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-sketch-window-title");
        _headerDraftNoteLabel.Text = Loc.GetString("w40k-cmd-upgrade-sketch-window-draft-note");
        ApplyDefaultHoverInfo();
        RebuildDomainHeaders(_treeSketch.ActiveDomainIds);

        if (_latestState != null)
            UpdateState(_latestState, _latestDoctrineId);
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state, string activeDoctrineId)
    {
        _latestState = state;
        _latestDoctrineId = activeDoctrineId;
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, WH40KCommandUiStyles.DefaultAccent);
        _headerStyle.BorderColor = _accent;
        _headerTitleLabel.ModulateSelfOverride = _accent;
        _hoverPanel.PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
            WH40KCommandUiStyles.CardBackground,
            _accent);

        _treeSketch.AccentColor = _accent;

        var resolvedTeam = WH40KCommandUiStyles.ResolveLocalizedOrRaw(state.TeamName);
        Title = Loc.GetString("w40k-cmd-upgrade-sketch-window-title-team", ("team", resolvedTeam));
        _teamLine.Text = CompactLine(Loc.GetString("w40k-cmd-team", ("team", resolvedTeam)));
        _upgradePointsLine.Text = CompactLine(Loc.GetString(
            "w40k-cmd-upgrade-sketch-window-points",
            ("funds", state.Funds),
            ("research", state.ResearchPoints)));
        _doctrineLine.Text = CompactLine(string.IsNullOrWhiteSpace(activeDoctrineId)
            ? Loc.GetString("w40k-cmd-upgrade-sketch-window-doctrine-none")
            : Loc.GetString("w40k-cmd-upgrade-sketch-window-doctrine-active",
                ("doctrine", WH40KCommandNodeDoctrineWindow.ResolveDoctrineDisplay(activeDoctrineId, state.TeamId).Name)));

        _teamBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#203227".AsSpan()), _accent);
        _teamBadgeLabel.Text = CompactLine(string.IsNullOrWhiteSpace(state.TeamName) ? "?" : resolvedTeam.ToUpperInvariant());

        _treeSketch.UpdateState(state, activeDoctrineId);
        RebuildDomainHeaders(_treeSketch.ActiveDomainIds);
    }

    private void OnNodeInfoChanged(string title, string description)
    {
        _hoverTitleLine.Text = title;
        _hoverTitleLine.ModulateSelfOverride = _accent;
        _hoverDescriptionLine.Text = CompactLine(description);
    }

    private void ApplyDefaultHoverInfo()
    {
        OnNodeInfoChanged(
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-title"),
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-description"));
    }

    private static string CompactLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var compact = text
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        return string.Join(' ', compact.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private void RebuildDomainHeaders(IReadOnlyList<string> domainIds)
    {
        _domainHeaderRow.RemoveAllChildren();

        foreach (var domainId in domainIds)
        {
            var badge = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
                    Color.FromHex("#22313B".AsSpan()),
                    _accent)
            };
            badge.AddChild(new Label
            {
                HorizontalExpand = true,
                HorizontalAlignment = HAlignment.Center,
                ClipText = true,
                Text = ResolveDomainLabel(domainId)
            });
            _domainHeaderRow.AddChild(badge);
        }
    }

    private static string ResolveDomainLabel(string domainId)
    {
        return Loc.GetString($"w40k-cmd-upgrade-sketch-domain-{domainId}");
    }

    private void OnTreeNodePurchaseRequestedInternal(string nodeId)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
            OnTreeNodePurchaseRequested?.Invoke(nodeId);
    }
}
