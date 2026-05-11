using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WH40K.Command.Controls;
using Content.Client.Localization;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.Tiers;
using Content.Shared.Roles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeUpgradeSketchWindow : FancyWindow, ILocalizedControl
{
    private const string BaseRoadmapTabId = "base-roadmap";
    private const string ReinforcementTeamMapId = "WH40KCommandReinforcementTeamMap";
    private const string OreExtractorTeamMapId = "WH40KCommandOreExtractorIntelTeamMap";
    private const string MachineProfileId = "WH40KTierMachineStandard";
    private const string LogisticsProfileId = "WH40KTierLogisticsStandard";

    private sealed record BaseRoadmapLevelIntel(
        int Level,
        int ThresholdXp,
        bool IsCurrent,
        bool IsUnlocked,
        List<string> BranchUnlocks,
        List<string> ResearchUnlocks,
        List<string> ReinforcementUnlocks,
        List<string> SystemUnlocks);

    public event Action<string>? OnTreeNodePurchaseRequested;

    private readonly IPrototypeManager _prototype = IoCManager.Resolve<IPrototypeManager>();

    private readonly StyleBoxFlat _headerStyle;
    private readonly PanelContainer _headerPanel;
    private readonly Label _headerTitleLabel;
    private readonly Label _headerSubtitleLabel;
    private readonly Label _headerResourceLabel;
    private readonly PanelContainer _teamBadge;
    private readonly Label _teamBadgeLabel;
    private readonly PanelContainer _phaseBadge;
    private readonly Label _phaseBadgeLabel;
    private readonly PanelContainer _levelBadge;
    private readonly Label _levelBadgeLabel;

    private readonly PanelContainer _focusCard;
    private readonly Label _focusCardTitleLabel;
    private readonly Label _focusTitleLabel;
    private readonly PanelContainer _focusDomainBadge;
    private readonly Label _focusDomainBadgeLabel;
    private readonly PanelContainer _focusStateBadge;
    private readonly Label _focusStateBadgeLabel;
    private readonly Label _focusCostLabel;
    private readonly RichTextLabel _focusRequirementLabel;
    private readonly Label _focusResearchTitleLabel;
    private readonly RichTextLabel _focusResearchLabel;
    private readonly Label _focusEffectsTitleLabel;
    private readonly RichTextLabel _focusEffectsLabel;
    private readonly Label _focusDescriptionTitleLabel;
    private readonly RichTextLabel _focusDescriptionLabel;

    private readonly PanelContainer _leftRailPanel;
    private readonly PanelContainer _treePanel;
    private readonly Label _treePanelTitleLabel;
    private readonly Label _treePanelSubtitleLabel;
    private readonly BoxContainer _domainDeck;
    private readonly PanelContainer _treeViewport;
    private readonly PanelContainer _roadmapViewport;
    private readonly BoxContainer _roadmapList;
    private readonly WH40KCommandTreeSketchControl _treeSketch;

    private Color _accent = WH40KCommandUiStyles.DefaultAccent;
    private bool _chaosTheme;
    private string _selectedTabId = string.Empty;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;

    public WH40KCommandNodeUpgradeSketchWindow()
    {
        Title = Loc.GetString("w40k-cmd-upgrade-sketch-window-title");
        MinSize = SetSize = new Vector2(1260f, 840f);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(8)
        };
        ContentsContainer.AddChild(root);

        _headerPanel = new PanelContainer
        {
            PanelOverride = _headerStyle = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.HeaderBackground,
                _accent,
                2)
        };
        root.AddChild(_headerPanel);

        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            Margin = new Thickness(12, 10),
            VerticalAlignment = VAlignment.Center
        };
        _headerPanel.AddChild(headerBox);

        var headerInfo = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            HorizontalExpand = true
        };
        headerBox.AddChild(headerInfo);

        _headerTitleLabel = new Label
        {
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        _headerSubtitleLabel = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _headerResourceLabel = new Label
        {
            ClipText = true
        };
        headerInfo.AddChild(_headerTitleLabel);
        headerInfo.AddChild(_headerSubtitleLabel);
        headerInfo.AddChild(_headerResourceLabel);

        var badgeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            VerticalAlignment = VAlignment.Center
        };
        headerBox.AddChild(badgeRow);

        _teamBadge = new PanelContainer();
        _teamBadgeLabel = new Label { Align = Label.AlignMode.Center };
        _teamBadge.AddChild(_teamBadgeLabel);
        badgeRow.AddChild(_teamBadge);

        _phaseBadge = new PanelContainer();
        _phaseBadgeLabel = new Label { Align = Label.AlignMode.Center };
        _phaseBadge.AddChild(_phaseBadgeLabel);
        badgeRow.AddChild(_phaseBadge);

        _levelBadge = new PanelContainer();
        _levelBadgeLabel = new Label { Align = Label.AlignMode.Center };
        _levelBadge.AddChild(_levelBadgeLabel);
        badgeRow.AddChild(_levelBadge);

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        root.AddChild(body);

        _leftRailPanel = new PanelContainer
        {
            HorizontalExpand = false,
            VerticalExpand = true,
            MinWidth = 360f,
            MaxWidth = 360f
        };
        body.AddChild(_leftRailPanel);

        var leftRailScroll = new ScrollContainer
        {
            HorizontalExpand = false,
            VerticalExpand = true,
            HScrollEnabled = false,
            HorizontalAlignment = HAlignment.Stretch,
            VerticalAlignment = VAlignment.Stretch
        };
        _leftRailPanel.AddChild(leftRailScroll);

        var leftRailContent = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true
        };
        leftRailScroll.AddChild(leftRailContent);

        _focusCard = new PanelContainer();
        leftRailContent.AddChild(_focusCard);

        var focusBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6
        };
        _focusCard.AddChild(focusBox);

        _focusCardTitleLabel = new Label
        {
            StyleClasses = { "LabelHeading" }
        };
        _focusTitleLabel = new Label
        {
            StyleClasses = { "LabelBig" },
            ClipText = true
        };
        focusBox.AddChild(_focusCardTitleLabel);
        focusBox.AddChild(_focusTitleLabel);

        var focusBadgeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6
        };
        focusBox.AddChild(focusBadgeRow);

        _focusDomainBadge = new PanelContainer();
        _focusDomainBadgeLabel = new Label { Align = Label.AlignMode.Center };
        _focusDomainBadge.AddChild(_focusDomainBadgeLabel);
        focusBadgeRow.AddChild(_focusDomainBadge);

        _focusStateBadge = new PanelContainer();
        _focusStateBadgeLabel = new Label { Align = Label.AlignMode.Center };
        _focusStateBadge.AddChild(_focusStateBadgeLabel);
        focusBadgeRow.AddChild(_focusStateBadge);

        _focusCostLabel = new Label { ClipText = true };
        _focusRequirementLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            MinHeight = 28f,
            MaxHeight = 96f,
            LineHeightScale = 0.95f
        };
        focusBox.AddChild(_focusCostLabel);
        focusBox.AddChild(_focusRequirementLabel);

        _focusResearchTitleLabel = new Label
        {
            StyleClasses = { "LabelSubText" }
        };
        _focusResearchLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            MinHeight = 56f,
            MaxHeight = 180f,
            LineHeightScale = 0.95f
        };
        focusBox.AddChild(_focusResearchTitleLabel);
        focusBox.AddChild(_focusResearchLabel);

        _focusEffectsTitleLabel = new Label
        {
            StyleClasses = { "LabelSubText" }
        };
        _focusEffectsLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            MinHeight = 48f,
            MaxHeight = 160f,
            LineHeightScale = 0.95f
        };
        focusBox.AddChild(_focusEffectsTitleLabel);
        focusBox.AddChild(_focusEffectsLabel);

        _focusDescriptionTitleLabel = new Label
        {
            StyleClasses = { "LabelSubText" }
        };
        _focusDescriptionLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            MinHeight = 72f,
            MaxHeight = 220f,
            LineHeightScale = 0.95f
        };
        focusBox.AddChild(_focusDescriptionTitleLabel);
        focusBox.AddChild(_focusDescriptionLabel);

        _treePanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        body.AddChild(_treePanel);

        var treePanelBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _treePanel.AddChild(treePanelBox);

        _treePanelTitleLabel = new Label
        {
            StyleClasses = { "LabelHeading" }
        };
        _treePanelSubtitleLabel = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        treePanelBox.AddChild(_treePanelTitleLabel);
        treePanelBox.AddChild(_treePanelSubtitleLabel);

        _domainDeck = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true
        };
        treePanelBox.AddChild(_domainDeck);

        _treeViewport = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        treePanelBox.AddChild(_treeViewport);

        var treeScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false
        };
        _treeViewport.AddChild(treeScroll);

        _treeSketch = new WH40KCommandTreeSketchControl
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        _treeSketch.OnNodeInfoChanged += info =>
        {
            if (_selectedTabId != BaseRoadmapTabId)
                ApplyNodeInfo(info);
        };
        _treeSketch.OnPurchaseRequested += OnTreeNodePurchaseRequestedInternal;
        treeScroll.AddChild(_treeSketch);

        _roadmapViewport = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false
        };
        treePanelBox.AddChild(_roadmapViewport);

        var roadmapScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false
        };
        _roadmapViewport.AddChild(roadmapScroll);

        _roadmapList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true
        };
        roadmapScroll.AddChild(_roadmapList);

        Relocalize();
        ApplyNodeInfo(_treeSketch.CurrentNodeInfo ?? BuildDefaultNodeInfo());
    }

    public void Relocalize()
    {
        Title = Loc.GetString("w40k-cmd-upgrade-sketch-window-title");
        _headerTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-sketch-window-title");
        _headerSubtitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-sketch-window-subtitle");
        _focusResearchTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-tree-focus-research-title");
        _focusEffectsTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-tree-focus-effects-title");
        _focusDescriptionTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-tree-focus-description-title");
        RebuildDomainDeck();

        if (_latestState != null)
            UpdateState(_latestState);
        else
            ApplyNodeInfo(BuildDefaultNodeInfo());
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _latestState = state;
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, WH40KCommandUiStyles.DefaultAccent);
        _chaosTheme = WH40KTeamIdentityClientResolver.UsesHereticsDoctrinePresentation(state.TeamId);

        _treeSketch.AccentColor = _accent;
        _treeSketch.UpdateState(state);

        EnsureSelectedTab();
        ApplyTheme();

        var resolvedTeam = WH40KCommandUiStyles.ResolveLocalizedOrRaw(state.TeamName);
        Title = Loc.GetString("w40k-cmd-upgrade-sketch-window-title-team", ("team", resolvedTeam));
        _headerTitleLabel.Text = Title;
        _headerResourceLabel.Text = Loc.GetString(
            "w40k-cmd-upgrade-sketch-window-resource-summary",
            ("funds", WH40KCommandUiStyles.FormatThroneGelt(state.Funds)),
            ("research", WH40KCommandUiStyles.FormatResearch(state.ResearchPoints)),
            ("influence", WH40KCommandUiStyles.FormatInfluence(state.CommandPoints)));

        _teamBadgeLabel.Text = resolvedTeam.ToUpperInvariant();
        _phaseBadgeLabel.Text = Loc.GetString(ResolvePhaseLocKey(state.Phase)).ToUpperInvariant();
        _levelBadgeLabel.Text = Loc.GetString("w40k-cmd-level-badge", ("level", state.BaseLevel));

        RebuildDomainDeck();
        RefreshActiveTabView();
    }

    private void EnsureSelectedTab()
    {
        if (_selectedTabId == BaseRoadmapTabId)
            return;

        var firstDomain = _treeSketch.GetDomainSummaries().FirstOrDefault()?.DomainId ?? string.Empty;
        var hasSelectedDomain = _treeSketch.GetDomainSummaries()
            .Any(summary => string.Equals(summary.DomainId, _selectedTabId, StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(_selectedTabId) || !hasSelectedDomain)
            _selectedTabId = firstDomain;
    }

    private void SetSelectedTab(string tabId)
    {
        if (string.Equals(_selectedTabId, tabId, StringComparison.Ordinal))
            return;

        _selectedTabId = tabId;
        RebuildDomainDeck();
        RefreshActiveTabView();
    }

    private void RefreshActiveTabView()
    {
        if (_latestState == null)
            return;

        var baseRoadmapActive = string.Equals(_selectedTabId, BaseRoadmapTabId, StringComparison.Ordinal);
        _leftRailPanel.Visible = !baseRoadmapActive;
        _treeViewport.Visible = !baseRoadmapActive;
        _roadmapViewport.Visible = baseRoadmapActive;

        if (baseRoadmapActive)
        {
            _treeSketch.SelectedDomainId = string.Empty;
            _treePanelTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-roadmap-title");
            _treePanelSubtitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-roadmap-subtitle");
            RefreshBaseRoadmap();
            return;
        }

        _treeSketch.SelectedDomainId = _selectedTabId;
        _treePanelTitleLabel.Text = Loc.GetString(
            "w40k-cmd-upgrade-tree-branch-title",
            ("domain", ResolveDomainLabel(_selectedTabId)));
        _treePanelSubtitleLabel.Text = Loc.GetString(
            "w40k-cmd-upgrade-tree-branch-subtitle",
            ("domain", ResolveDomainLabel(_selectedTabId)));

        var currentInfo = _treeSketch.CurrentNodeInfo;
        if (currentInfo != null &&
            !string.IsNullOrWhiteSpace(currentInfo.NodeId) &&
            string.Equals(currentInfo.DomainId, _selectedTabId, StringComparison.Ordinal))
        {
            ApplyNodeInfo(currentInfo);
            return;
        }

        var preferredNode = _treeSketch.GetBranchIntel(_selectedTabId)
            .OrderByDescending(node => node.Purchased)
            .ThenByDescending(node => node.Available)
            .ThenBy(node => node.MinBaseLevel)
            .FirstOrDefault();

        ApplyNodeInfo(preferredNode ?? BuildDefaultNodeInfo());
    }

    private void ApplyTheme()
    {
        _headerStyle.BackgroundColor = WH40KCommandUiStyles.ResolveHeaderBackground(_chaosTheme);
        _headerStyle.BorderColor = _accent;
        _headerTitleLabel.ModulateSelfOverride = _accent;
        _headerSubtitleLabel.ModulateSelfOverride = WH40KCommandUiStyles.ResolveMutedText(_chaosTheme);
        _headerResourceLabel.ModulateSelfOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme);

        _teamBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
            WH40KCommandUiStyles.ResolveBadgeBackground(_chaosTheme),
            _accent);
        _phaseBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
            WH40KCommandUiStyles.ResolveBadgeBackground(_chaosTheme),
            WH40KCommandUiStyles.ResolveMutedBorder(_chaosTheme));
        _levelBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
            WH40KCommandUiStyles.ResolveBadgeBackground(_chaosTheme),
            WH40KCommandUiStyles.ResolveMutedBorder(_chaosTheme));
        _teamBadgeLabel.FontColorOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme);
        _phaseBadgeLabel.FontColorOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme);
        _levelBadgeLabel.FontColorOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme);

        ApplyCardTheme(_focusCard, _accent, WH40KCommandUiStyles.ResolveCardBackground(_chaosTheme));
        ApplyCardTheme(_leftRailPanel, WH40KCommandUiStyles.ResolveStrongBorder(_chaosTheme), WH40KCommandUiStyles.ResolvePanelBackground(_chaosTheme));
        ApplyCardTheme(_treePanel, WH40KCommandUiStyles.ResolveStrongBorder(_chaosTheme), WH40KCommandUiStyles.ResolvePanelBackgroundAlt(_chaosTheme));
        ApplyCardTheme(_treeViewport, WH40KCommandUiStyles.ResolveMutedBorder(_chaosTheme), WH40KCommandUiStyles.ResolveCardBackground(_chaosTheme));
        ApplyCardTheme(_roadmapViewport, WH40KCommandUiStyles.ResolveMutedBorder(_chaosTheme), WH40KCommandUiStyles.ResolveCardBackground(_chaosTheme));

        _focusCardTitleLabel.ModulateSelfOverride = _accent;
        _focusTitleLabel.ModulateSelfOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme);
        _focusCostLabel.ModulateSelfOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme);
        _focusResearchTitleLabel.ModulateSelfOverride = _accent;
        _focusEffectsTitleLabel.ModulateSelfOverride = _accent;
        _focusDescriptionTitleLabel.ModulateSelfOverride = _accent;
        _treePanelTitleLabel.ModulateSelfOverride = _accent;
        _treePanelSubtitleLabel.ModulateSelfOverride = WH40KCommandUiStyles.ResolveMutedText(_chaosTheme);

        _focusDomainBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
            WH40KCommandUiStyles.ResolveBadgeBackground(_chaosTheme),
            WH40KCommandUiStyles.ResolveMutedBorder(_chaosTheme));
        _focusStateBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
            WH40KCommandUiStyles.ResolveBadgeBackground(_chaosTheme),
            _accent);
        _focusDomainBadgeLabel.FontColorOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme);
        _focusStateBadgeLabel.FontColorOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme);
        RebuildDomainDeck();
    }

    private void ApplyNodeInfo(WH40KCommandTreeSketchControl.WH40KCommandTreeNodeInfo info)
    {
        _focusCardTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-tree-focus-card-title");
        _focusResearchTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-tree-focus-research-title");
        _focusEffectsTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-tree-focus-effects-title");
        _focusDescriptionTitleLabel.Text = Loc.GetString("w40k-cmd-upgrade-tree-focus-description-title");
        _focusTitleLabel.Text = CompactLine(info.Title);
        _focusDomainBadgeLabel.Text = string.IsNullOrWhiteSpace(info.DomainId)
            ? Loc.GetString("w40k-cmd-upgrade-tree-focus-domain-empty")
            : ResolveDomainLabel(info.DomainId).ToUpperInvariant();
        _focusStateBadgeLabel.Text = CompactLine(info.BadgeStatus);
        _focusStateBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
            WH40KCommandUiStyles.ResolveBadgeBackground(_chaosTheme),
            info.BadgeColor);
        _focusCostLabel.Text = string.IsNullOrWhiteSpace(info.Cost)
            ? Loc.GetString("w40k-cmd-upgrade-tree-focus-cost-empty")
            : Loc.GetString("w40k-cmd-upgrade-tree-focus-cost", ("cost", info.Cost));
        WH40KCommandUiStyles.SetWrappedText(
            _focusRequirementLabel,
            string.IsNullOrWhiteSpace(info.Requirements)
                ? Loc.GetString("w40k-cmd-upgrade-tree-focus-requirements-empty")
                : Loc.GetString("w40k-cmd-upgrade-tree-focus-requirements", ("requirements", info.Requirements)),
            WH40KCommandUiStyles.ResolveMutedText(_chaosTheme));

        WH40KCommandUiStyles.SetWrappedText(
            _focusResearchLabel,
            string.IsNullOrWhiteSpace(info.ResearchUnlocks)
                ? Loc.GetString("w40k-cmd-upgrade-tree-info-default-research")
                : info.ResearchUnlocks,
            WH40KCommandUiStyles.ResolveSoftText(_chaosTheme));
        WH40KCommandUiStyles.SetWrappedText(
            _focusEffectsLabel,
            string.IsNullOrWhiteSpace(info.Effects)
                ? Loc.GetString("w40k-cmd-upgrade-tree-info-default-effects")
                : info.Effects,
            WH40KCommandUiStyles.ResolveSoftText(_chaosTheme));
        WH40KCommandUiStyles.SetWrappedText(
            _focusDescriptionLabel,
            string.IsNullOrWhiteSpace(info.Description)
                ? Loc.GetString("w40k-cmd-upgrade-tree-info-default-description")
                : info.Description,
            WH40KCommandUiStyles.ResolveSoftText(_chaosTheme));
    }

    private void RefreshBaseRoadmap()
    {
        if (_latestState == null)
            return;

        var levels = BuildBaseRoadmap(_latestState);
        _roadmapList.RemoveAllChildren();

        foreach (var level in levels)
        {
            AppendRoadmapCard(level);
        }
    }

    private void RebuildDomainDeck()
    {
        _domainDeck.RemoveAllChildren();

        foreach (var summary in _treeSketch.GetDomainSummaries())
        {
            var isSelected = string.Equals(_selectedTabId, summary.DomainId, StringComparison.Ordinal);
            var color = summary.AvailableCount > 0
                ? _accent
                : summary.PurchasedCount > 0
                    ? WH40KCommandUiStyles.ReadyBadge
                    : WH40KCommandUiStyles.ResolveMutedBorder(_chaosTheme);

            _domainDeck.AddChild(CreateTabButton(
                summary.DomainId,
                ResolveDomainLabel(summary.DomainId),
                Loc.GetString(
                    "w40k-cmd-upgrade-tree-tab-domain-meta",
                    ("purchased", summary.PurchasedCount),
                    ("total", summary.TotalCount)),
                color,
                isSelected));
        }

        var levelCount = Math.Max(1, (_latestState?.LevelThresholds.Length ?? 0) + 1);
        _domainDeck.AddChild(CreateTabButton(
            BaseRoadmapTabId,
            Loc.GetString("w40k-cmd-upgrade-tree-tab-base-title"),
            Loc.GetString("w40k-cmd-upgrade-tree-tab-base-meta", ("levels", levelCount)),
            WH40KCommandUiStyles.InfoBadge,
            string.Equals(_selectedTabId, BaseRoadmapTabId, StringComparison.Ordinal)));
    }

    private ContainerButton CreateTabButton(string tabId, string title, string meta, Color accent, bool selected)
    {
        var button = new ContainerButton
        {
            HorizontalExpand = true,
            MinHeight = 60f,
            StyleBoxOverride = selected
                ? WH40KCommandUiStyles.CreatePrimaryButtonStyle(accent, false, _chaosTheme)
                : WH40KCommandUiStyles.CreateSecondaryButtonStyle(accent, false, _chaosTheme)
        };

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        button.AddChild(box);

        box.AddChild(new Label
        {
            Text = title.ToUpperInvariant(),
            ClipText = true,
            FontColorOverride = selected
                ? WH40KCommandUiStyles.ResolveSoftText(_chaosTheme)
                : accent
        });

        box.AddChild(new Label
        {
            Text = meta,
            ClipText = true,
            StyleClasses = { "LabelSubText" },
            FontColorOverride = WH40KCommandUiStyles.ResolveMutedText(_chaosTheme)
        });

        button.OnPressed += _ => SetSelectedTab(tabId);
        return button;
    }

    private IReadOnlyList<BaseRoadmapLevelIntel> BuildBaseRoadmap(WH40KCommandNodeBoundUserInterfaceState state)
    {
        var branchUnlocks = new Dictionary<int, List<string>>();
        var researchUnlocks = new Dictionary<int, List<string>>();

        foreach (var node in _treeSketch.GetBranchIntel())
        {
            var level = Math.Max(1, node.MinBaseLevel);
            var line = $"{ResolveDomainLabel(node.DomainId)}: {CompactLine(node.Title)}";
            AddGroupedEntry(branchUnlocks, level, line);
            AddGroupedEntries(researchUnlocks, level, node.ResearchUnlockEntries);
        }

        var reinforcementUnlocks = BuildReinforcementUnlocksByLevel(state.TeamId);
        var systemUnlocks = BuildSystemUnlocksByLevel(state.TeamId);

        var maxLevel = new[]
        {
            1,
            state.LevelThresholds.Length + 1,
            branchUnlocks.Keys.DefaultIfEmpty(1).Max(),
            researchUnlocks.Keys.DefaultIfEmpty(1).Max(),
            reinforcementUnlocks.Keys.DefaultIfEmpty(1).Max(),
            systemUnlocks.Keys.DefaultIfEmpty(1).Max()
        }.Max();

        var levels = new List<BaseRoadmapLevelIntel>(maxLevel);
        for (var level = 1; level <= maxLevel; level++)
        {
            levels.Add(new BaseRoadmapLevelIntel(
                level,
                GetThresholdForLevel(state.LevelThresholds, level),
                level == state.BaseLevel,
                level <= state.BaseLevel,
                branchUnlocks.GetValueOrDefault(level, new List<string>()),
                researchUnlocks.GetValueOrDefault(level, new List<string>()),
                reinforcementUnlocks.GetValueOrDefault(level, new List<string>()),
                systemUnlocks.GetValueOrDefault(level, new List<string>())));
        }

        return levels;
    }

    private Dictionary<int, List<string>> BuildReinforcementUnlocksByLevel(string teamId)
    {
        var grouped = new Dictionary<int, List<string>>();
        if (!TryResolveReinforcementProfile(teamId, out var profile))
            return grouped;

        foreach (var option in profile.Options)
        {
            if (!_prototype.TryIndex(option.Job, out JobPrototype? job))
                continue;

            AddGroupedEntry(
                grouped,
                Math.Max(1, option.MinBaseLevel),
                job.LocalizedName);
        }

        return grouped;
    }

    private Dictionary<int, List<string>> BuildSystemUnlocksByLevel(string teamId)
    {
        var grouped = new Dictionary<int, List<string>>();

        if (_prototype.TryIndex(MachineProfileId, out WH40KTierMachineProfilePrototype? machineProfile) &&
            machineProfile.ThresholdProfile != null &&
            _prototype.TryIndex(machineProfile.ThresholdProfile.Value, out WH40KTierThresholdProfilePrototype? machineThresholds))
        {
            AddGroupedEntry(
                grouped,
                Math.Max(1, machineThresholds.Tier1MinBaseLevel),
                Loc.GetString("w40k-cmd-upgrade-roadmap-system-machine", ("tier", 1), ("seconds", machineProfile.MinProcessSecondsTier1)));
            AddGroupedEntry(
                grouped,
                Math.Max(1, machineThresholds.Tier2MinBaseLevel),
                Loc.GetString("w40k-cmd-upgrade-roadmap-system-machine", ("tier", 2), ("seconds", machineProfile.MinProcessSecondsTier2)));
            AddGroupedEntry(
                grouped,
                Math.Max(1, machineThresholds.Tier3MinBaseLevel),
                Loc.GetString("w40k-cmd-upgrade-roadmap-system-machine", ("tier", 3), ("seconds", machineProfile.MinProcessSecondsTier3)));
        }

        if (_prototype.TryIndex(LogisticsProfileId, out WH40KTierLogisticsProfilePrototype? logisticsProfile) &&
            logisticsProfile.ThresholdProfile != null &&
            _prototype.TryIndex(logisticsProfile.ThresholdProfile.Value, out WH40KTierThresholdProfilePrototype? logisticsThresholds))
        {
            AddGroupedEntry(
                grouped,
                Math.Max(1, logisticsThresholds.Tier1MinBaseLevel),
                Loc.GetString(
                    "w40k-cmd-upgrade-roadmap-system-logistics",
                    ("tier", 1),
                    ("items", logisticsProfile.Tier1MaxItemsBonus),
                    ("minutes", logisticsProfile.Tier1DeliveryMinutesReduction)));
            AddGroupedEntry(
                grouped,
                Math.Max(1, logisticsThresholds.Tier2MinBaseLevel),
                Loc.GetString(
                    "w40k-cmd-upgrade-roadmap-system-logistics",
                    ("tier", 2),
                    ("items", logisticsProfile.Tier2MaxItemsBonus),
                    ("minutes", logisticsProfile.Tier2DeliveryMinutesReduction)));
            AddGroupedEntry(
                grouped,
                Math.Max(1, logisticsThresholds.Tier3MinBaseLevel),
                Loc.GetString(
                    "w40k-cmd-upgrade-roadmap-system-logistics",
                    ("tier", 3),
                    ("items", logisticsProfile.Tier3MaxItemsBonus),
                    ("minutes", logisticsProfile.Tier3DeliveryMinutesReduction)));
        }

        if (TryResolveOreExtractorProfile(teamId, out var oreProfile))
        {
            AddGroupedEntry(
                grouped,
                Math.Max(1, oreProfile.Tier1MinBaseLevel),
                Loc.GetString(
                    "w40k-cmd-upgrade-roadmap-system-ore",
                    ("tier", 1),
                    ("count", oreProfile.SpawnCountTier1),
                    ("seconds", oreProfile.SpawnIntervalTier1)));
            AddGroupedEntry(
                grouped,
                Math.Max(1, oreProfile.Tier2MinBaseLevel),
                Loc.GetString(
                    "w40k-cmd-upgrade-roadmap-system-ore",
                    ("tier", 2),
                    ("count", oreProfile.SpawnCountTier2),
                    ("seconds", oreProfile.SpawnIntervalTier2)));
            AddGroupedEntry(
                grouped,
                Math.Max(1, oreProfile.Tier3MinBaseLevel),
                Loc.GetString(
                    "w40k-cmd-upgrade-roadmap-system-ore",
                    ("tier", 3),
                    ("count", oreProfile.SpawnCountTier3),
                    ("seconds", oreProfile.SpawnIntervalTier3)));
        }

        return grouped;
    }

    private bool TryResolveReinforcementProfile(string teamId, out WH40KCommandReinforcementProfilePrototype profile)
    {
        profile = default!;
        if (!_prototype.TryIndex(ReinforcementTeamMapId, out WH40KCommandReinforcementTeamMapPrototype? map))
            return false;

        var profileId = map.DefaultProfile.ToString();
        foreach (var (mappedTeamId, mappedProfileId) in map.TeamProfiles)
        {
            if (!string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            profileId = mappedProfileId.ToString();
            break;
        }

        if (!_prototype.TryIndex(profileId, out WH40KCommandReinforcementProfilePrototype? resolvedProfile) ||
            resolvedProfile == null)
        {
            return false;
        }

        profile = resolvedProfile;
        return true;
    }

    private bool TryResolveOreExtractorProfile(string teamId, out WH40KCommandOreExtractorIntelProfilePrototype profile)
    {
        profile = default!;
        if (!_prototype.TryIndex(OreExtractorTeamMapId, out WH40KCommandOreExtractorIntelTeamMapPrototype? map))
            return false;

        var profileId = map.DefaultProfile.ToString();
        foreach (var (mappedTeamId, mappedProfileId) in map.TeamProfiles)
        {
            if (!string.Equals(mappedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            profileId = mappedProfileId.ToString();
            break;
        }

        if (!_prototype.TryIndex(profileId, out WH40KCommandOreExtractorIntelProfilePrototype? resolvedProfile) ||
            resolvedProfile == null)
        {
            return false;
        }

        profile = resolvedProfile;
        return true;
    }

    private void AppendRoadmapCard(BaseRoadmapLevelIntel level)
    {
        var border = level.IsCurrent
            ? _accent
            : level.IsUnlocked
                ? WH40KCommandUiStyles.ReadyBadge
                : WH40KCommandUiStyles.ResolveMutedBorder(_chaosTheme);
        var background = level.IsCurrent
            ? WH40KCommandUiStyles.ResolveCardBackground(_chaosTheme)
            : WH40KCommandUiStyles.ResolveCardBackgroundAlt(_chaosTheme);

        var card = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(background, border)
        };

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6
        };
        card.AddChild(box);

        var headerRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8
        };
        box.AddChild(headerRow);

        headerRow.AddChild(new Label
        {
            Text = Loc.GetString("w40k-cmd-upgrade-roadmap-card-level", ("level", level.Level)),
            StyleClasses = { "LabelHeading" },
            FontColorOverride = border,
            HorizontalExpand = true
        });

        var badge = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
                WH40KCommandUiStyles.ResolveBadgeBackground(_chaosTheme),
                border)
        };
        badge.AddChild(new Label
        {
            Text = Loc.GetString(level.IsCurrent
                ? "w40k-cmd-upgrade-roadmap-card-status-current"
                : level.IsUnlocked
                    ? "w40k-cmd-upgrade-roadmap-card-status-unlocked"
                    : "w40k-cmd-upgrade-roadmap-card-status-upcoming")
                .ToUpperInvariant(),
            FontColorOverride = WH40KCommandUiStyles.ResolveSoftText(_chaosTheme)
        });
        headerRow.AddChild(badge);

        box.AddChild(new Label
        {
            Text = level.Level <= 1
                ? Loc.GetString("w40k-cmd-upgrade-roadmap-card-threshold-start")
                : Loc.GetString(
                    "w40k-cmd-upgrade-roadmap-card-threshold-xp",
                    ("xp", WH40KCommandUiStyles.FormatExperience(level.ThresholdXp))),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = WH40KCommandUiStyles.ResolveMutedText(_chaosTheme)
        });

        AddRoadmapSection(box, "w40k-cmd-upgrade-roadmap-section-branches", level.BranchUnlocks, border);
        AddRoadmapSection(box, "w40k-cmd-upgrade-roadmap-section-research", level.ResearchUnlocks, border);
        AddRoadmapSection(box, "w40k-cmd-upgrade-roadmap-section-reinforcements", level.ReinforcementUnlocks, border);
        AddRoadmapSection(box, "w40k-cmd-upgrade-roadmap-section-systems", level.SystemUnlocks, border);

        if (level.BranchUnlocks.Count == 0 &&
            level.ResearchUnlocks.Count == 0 &&
            level.ReinforcementUnlocks.Count == 0 &&
            level.SystemUnlocks.Count == 0)
        {
            box.AddChild(new Label
            {
                Text = Loc.GetString("w40k-cmd-upgrade-roadmap-card-empty"),
                StyleClasses = { "LabelSubText" },
                FontColorOverride = WH40KCommandUiStyles.ResolveMutedText(_chaosTheme)
            });
        }

        _roadmapList.AddChild(card);
    }

    private void AddRoadmapSection(BoxContainer parent, string titleKey, IReadOnlyList<string> entries, Color accent)
    {
        if (entries.Count == 0)
            return;

        parent.AddChild(new Label
        {
            Text = Loc.GetString(titleKey),
            FontColorOverride = accent
        });

        var body = new RichTextLabel
        {
            HorizontalExpand = true,
            MinHeight = 18f
        };
        WH40KCommandUiStyles.SetWrappedText(
            body,
            string.Join("\n", entries.Select(entry => $"- {entry}")),
            WH40KCommandUiStyles.ResolveSoftText(_chaosTheme));
        parent.AddChild(body);
    }

    private static void AddGroupedEntry(IDictionary<int, List<string>> grouped, int level, string line)
    {
        if (!grouped.TryGetValue(level, out var list))
        {
            list = new List<string>();
            grouped[level] = list;
        }

        if (!list.Contains(line, StringComparer.OrdinalIgnoreCase))
            list.Add(line);
    }

    private static void AddGroupedEntries(IDictionary<int, List<string>> grouped, int level, IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            AddGroupedEntry(grouped, level, line);
        }
    }

    private static int GetThresholdForLevel(IReadOnlyList<int> thresholds, int level)
    {
        if (level <= 1 || thresholds.Count == 0)
            return 0;

        var index = Math.Clamp(level - 2, 0, thresholds.Count - 1);
        return thresholds[index];
    }


    private void OnTreeNodePurchaseRequestedInternal(string nodeId)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
            OnTreeNodePurchaseRequested?.Invoke(nodeId);
    }

    private static void ApplyCardTheme(PanelContainer panel, Color border, Color background)
    {
        panel.PanelOverride = WH40KCommandUiStyles.CreateCardStyle(background, border);
    }

    private static string ResolveDomainLabel(string domainId)
    {
        return Loc.GetString($"w40k-cmd-upgrade-sketch-domain-{domainId}");
    }

    private static string ResolvePhaseLocKey(WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => "wh40k-phase-preparation-name",
            WH40KBattlePhase.Assault => "wh40k-phase-assault-name",
            WH40KBattlePhase.Apocalypse => "wh40k-phase-apocalypse-name",
            _ => "wh40k-phase-preparation-name"
        };
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

    private static WH40KCommandTreeSketchControl.WH40KCommandTreeNodeInfo BuildDefaultNodeInfo()
    {
        return new WH40KCommandTreeSketchControl.WH40KCommandTreeNodeInfo(
            string.Empty,
            string.Empty,
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-title"),
            Loc.GetString("w40k-cmd-upgrade-tree-info-default-state"),
            WH40KCommandUiStyles.MutedBorder,
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
    }
}
