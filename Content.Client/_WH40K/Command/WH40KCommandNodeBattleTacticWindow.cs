using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.Localization;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeBattleTacticWindow : FancyWindow, ILocalizedControl
{
    private static readonly IReadOnlyList<WH40KCommandNodeTacticPreset> Presets = WH40KCommandNodeTactics.Presets;

    public event Action<string>? OnBattleTacticAssignRequested;

    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _headerTitleLabel;
    private readonly Label _headerDraftNoteLabel;
    private readonly Label _teamLine;
    private readonly Label _activeBattleTacticLine;
    private readonly PanelContainer _teamBadge;
    private readonly Label _teamBadgeLabel;
    private readonly PanelContainer _cooldownBadge;
    private readonly Label _cooldownBadgeLabel;
    private readonly PanelContainer _selectionPanel;
    private readonly Label _listSectionTitleLabel;
    private readonly Label _selectionSectionTitleLabel;
    private readonly Label _selectedBattleTacticLine;
    private readonly Label _cooldownLine;
    private readonly Label _selectedDescription;
    private readonly Button _assignButton;
    private readonly Dictionary<string, StyleBoxFlat> _rowStyles = new();
    private readonly Dictionary<string, Label> _rowTitleLabels = new();
    private readonly Dictionary<string, Label> _rowSummaryLabels = new();
    private readonly Dictionary<string, Button> _rowButtons = new();

    private Color _accent = WH40KCommandUiStyles.DefaultAccent;
    private int _battleTacticCooldownSeconds;
    private string _activeBattleTacticId = WH40KCommandNodeTactics.DefaultTacticId;
    private string _selectedBattleTacticId = WH40KCommandNodeTactics.DefaultTacticId;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;

    public WH40KCommandNodeBattleTacticWindow()
    {
        Title = Loc.GetString("w40k-cmd-battle-tactic-window-title");
        MinSize = SetSize = new Vector2(900, 560);

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
            Text = Loc.GetString("w40k-cmd-battle-tactic-window-title"),
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        headerInfo.AddChild(_headerTitleLabel);

        _headerDraftNoteLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-battle-tactic-window-draft-note"),
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_headerDraftNoteLabel);

        _teamLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _activeBattleTacticLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_teamLine);
        headerInfo.AddChild(_activeBattleTacticLine);

        var badgeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            VerticalAlignment = VAlignment.Center
        };
        headerBox.AddChild(badgeRow);

        _teamBadge = new PanelContainer();
        _teamBadgeLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true
        };
        _teamBadge.AddChild(_teamBadgeLabel);
        badgeRow.AddChild(_teamBadge);

        _cooldownBadge = new PanelContainer();
        _cooldownBadgeLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true
        };
        _cooldownBadge.AddChild(_cooldownBadgeLabel);
        badgeRow.AddChild(_cooldownBadge);

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        root.AddChild(body);

        var listPanel = CreateSection(
            Loc.GetString("w40k-cmd-battle-tactic-window-list-header"),
            out var listContent,
            out _listSectionTitleLabel,
            verticalExpand: true);
        listPanel.HorizontalExpand = true;
        listPanel.VerticalExpand = true;
        listPanel.SizeFlagsStretchRatio = 1.25f;
        body.AddChild(listPanel);

        var listScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true
        };
        listContent.AddChild(listScroll);

        var battleTacticRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        listScroll.AddChild(battleTacticRows);

        foreach (var preset in Presets)
        {
            var rowStyle = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackgroundAlt,
                WH40KCommandUiStyles.MutedBorder);

            var row = new PanelContainer
            {
                PanelOverride = rowStyle
            };
            battleTacticRows.AddChild(row);

            var rowBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 6
            };
            row.AddChild(rowBox);

            var title = new Label
            {
                Text = Loc.GetString(preset.NameLocKey),
                StyleClasses = { "LabelBig" },
                ClipText = true
            };
            var summary = new Label
            {
                Text = Loc.GetString(preset.SummaryLocKey),
                StyleClasses = { "LabelSubText" },
                ClipText = true
            };

            rowBox.AddChild(title);
            rowBox.AddChild(summary);
            rowBox.AddChild(new HSeparator());

            var selectButton = new Button
            {
                HorizontalExpand = true,
                Text = Loc.GetString("w40k-cmd-battle-tactic-window-select-button")
            };
            selectButton.OnPressed += _ => SelectBattleTactic(preset.Id);
            rowBox.AddChild(selectButton);

            _rowStyles[preset.Id] = rowStyle;
            _rowTitleLabels[preset.Id] = title;
            _rowSummaryLabels[preset.Id] = summary;
            _rowButtons[preset.Id] = selectButton;
        }

        _selectionPanel = CreateSection(
            Loc.GetString("w40k-cmd-battle-tactic-window-selection-header"),
            out var selectionContent,
            out _selectionSectionTitleLabel,
            verticalExpand: true);
        _selectionPanel.MinWidth = 280;
        _selectionPanel.VerticalExpand = true;
        body.AddChild(_selectionPanel);

        _selectedBattleTacticLine = new Label
        {
            StyleClasses = { "LabelBig" },
            ClipText = true
        };
        _cooldownLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        selectionContent.AddChild(_selectedBattleTacticLine);
        selectionContent.AddChild(_cooldownLine);

        var descriptionCard = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackgroundAlt,
                WH40KCommandUiStyles.MutedBorder)
        };
        selectionContent.AddChild(descriptionCard);

        _selectedDescription = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            StyleClasses = { "LabelSubText" }
        };
        descriptionCard.AddChild(_selectedDescription);

        _assignButton = new Button
        {
            HorizontalExpand = true
        };
        _assignButton.OnPressed += _ => AssignSelectedBattleTactic();
        selectionContent.AddChild(_assignButton);

        Relocalize();
    }

    public void Relocalize()
    {
        Title = Loc.GetString("w40k-cmd-battle-tactic-window-title");
        _headerTitleLabel.Text = Loc.GetString("w40k-cmd-battle-tactic-window-title");
        _headerDraftNoteLabel.Text = Loc.GetString("w40k-cmd-battle-tactic-window-draft-note");
        _listSectionTitleLabel.Text = Loc.GetString("w40k-cmd-battle-tactic-window-list-header");
        _selectionSectionTitleLabel.Text = Loc.GetString("w40k-cmd-battle-tactic-window-selection-header");

        RefreshRowText();

        if (_latestState != null)
        {
            UpdateState(_latestState);
            return;
        }

        RefreshSelectionPreview();
        RefreshRows();
    }

    public static (string Name, string Description) ResolveBattleTacticDisplay(string? tacticId)
    {
        var preset = FindTacticPreset(tacticId);
        return (Loc.GetString(preset.NameLocKey), Loc.GetString(preset.DescriptionLocKey));
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _latestState = state;
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, WH40KCommandUiStyles.DefaultAccent);
        _headerStyle.BorderColor = _accent;
        _headerTitleLabel.ModulateSelfOverride = _accent;

        var resolvedTeam = WH40KCommandUiStyles.ResolveLocalizedOrRaw(state.TeamName);
        _teamLine.Text = CompactLine(Loc.GetString("w40k-cmd-team", ("team", resolvedTeam)));
        _teamBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#203227".AsSpan()), _accent);
        _teamBadgeLabel.Text = string.IsNullOrWhiteSpace(state.TeamName) ? "?" : resolvedTeam.ToUpperInvariant();

        _activeBattleTacticId = FindTacticPreset(state.ActiveBattleTacticId).Id;
        _battleTacticCooldownSeconds = Math.Max(0, state.BattleTacticCooldownSeconds);
        if (!_rowStyles.ContainsKey(_selectedBattleTacticId))
            _selectedBattleTacticId = _activeBattleTacticId;

        _selectionPanel.PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
            WH40KCommandUiStyles.PanelBackground,
            _accent,
            2);

        _activeBattleTacticLine.Text = Loc.GetString("w40k-cmd-battle-tactic-window-active",
            ("tactic", ResolveBattleTacticDisplay(_activeBattleTacticId).Name));
        _activeBattleTacticLine.Text = CompactLine(_activeBattleTacticLine.Text);

        RefreshSelectionPreview();
        RefreshRows();
    }

    private PanelContainer CreateSection(string title, out BoxContainer content, out Label titleLabel, bool verticalExpand)
    {
        var section = new PanelContainer
        {
            VerticalExpand = verticalExpand,
            HorizontalExpand = true,
            PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
                WH40KCommandUiStyles.PanelBackgroundAlt,
                WH40KCommandUiStyles.StrongBorder,
                2)
        };

        var sectionRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            VerticalExpand = verticalExpand
        };
        section.AddChild(sectionRoot);

        var titleBar = new PanelContainer
        {
            PanelOverride = WH40KCommandUiStyles.CreateHeaderStripStyle(WH40KCommandUiStyles.MutedBorder)
        };
        titleLabel = new Label
        {
            Text = title,
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        titleBar.AddChild(titleLabel);
        sectionRoot.AddChild(titleBar);

        content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(10),
            VerticalExpand = verticalExpand
        };
        sectionRoot.AddChild(content);

        return section;
    }

    private void RefreshRowText()
    {
        foreach (var preset in Presets)
        {
            _rowTitleLabels[preset.Id].Text = Loc.GetString(preset.NameLocKey);
            _rowSummaryLabels[preset.Id].Text = CompactLine(Loc.GetString(preset.SummaryLocKey));
        }
    }

    private void SelectBattleTactic(string tacticId)
    {
        _selectedBattleTacticId = FindTacticPreset(tacticId).Id;
        RefreshSelectionPreview();
        RefreshRows();
    }

    private void AssignSelectedBattleTactic()
    {
        OnBattleTacticAssignRequested?.Invoke(_selectedBattleTacticId);
    }

    private void RefreshSelectionPreview()
    {
        var display = ResolveBattleTacticDisplay(_selectedBattleTacticId);
        _selectedBattleTacticLine.Text = CompactLine(Loc.GetString("w40k-cmd-battle-tactic-window-selected",
            ("tactic", display.Name)));
        _selectedBattleTacticLine.ModulateSelfOverride = _accent;

        _selectedDescription.Text = CompactLine(display.Description);

        var assigned = string.Equals(_selectedBattleTacticId, _activeBattleTacticId, StringComparison.OrdinalIgnoreCase);
        var cooldownActive = _battleTacticCooldownSeconds > 0;
        var time = FormatCooldownTime(_battleTacticCooldownSeconds);

        _cooldownLine.Text = cooldownActive
            ? Loc.GetString("w40k-cmd-battle-tactic-window-cooldown-active", ("time", time))
            : Loc.GetString("w40k-cmd-battle-tactic-window-cooldown-ready");
        _cooldownLine.ModulateSelfOverride = cooldownActive
            ? WH40KCommandUiStyles.WarningBadge
            : WH40KCommandUiStyles.ReadyBadge;

        _cooldownBadge.PanelOverride = cooldownActive
            ? WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#3A2E1D".AsSpan()), WH40KCommandUiStyles.WarningBadge)
            : WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#223B2F".AsSpan()), WH40KCommandUiStyles.ReadyBadge);
        _cooldownBadgeLabel.Text = cooldownActive ? time : Loc.GetString("w40k-cmd-status-badge-ready");

        _assignButton.Disabled = assigned || cooldownActive;
        _assignButton.Text = assigned
            ? Loc.GetString("w40k-cmd-battle-tactic-window-assigned-button")
            : cooldownActive
                ? Loc.GetString("w40k-cmd-battle-tactic-window-cooldown-button", ("time", time))
                : Loc.GetString("w40k-cmd-battle-tactic-window-assign-button");
    }

    private void RefreshRows()
    {
        foreach (var preset in Presets)
        {
            var rowStyle = _rowStyles[preset.Id];
            var title = _rowTitleLabels[preset.Id];
            var summary = _rowSummaryLabels[preset.Id];
            var button = _rowButtons[preset.Id];

            var isSelected = string.Equals(preset.Id, _selectedBattleTacticId, StringComparison.OrdinalIgnoreCase);
            var isActive = string.Equals(preset.Id, _activeBattleTacticId, StringComparison.OrdinalIgnoreCase);

            rowStyle.BackgroundColor = isSelected
                ? WH40KCommandUiStyles.CardBackground
                : isActive
                    ? WH40KCommandUiStyles.CardBackgroundMuted
                    : WH40KCommandUiStyles.CardBackgroundAlt;
            rowStyle.BorderColor = isSelected
                ? _accent
                : isActive
                    ? WH40KCommandUiStyles.ReadyBadge
                    : WH40KCommandUiStyles.MutedBorder;

            title.ModulateSelfOverride = isSelected ? _accent : Color.White;
            summary.ModulateSelfOverride = isActive
                ? WH40KCommandUiStyles.SoftText
                : WH40KCommandUiStyles.MutedText;

            button.Text = isActive
                ? Loc.GetString("w40k-cmd-battle-tactic-window-row-active-button")
                : isSelected
                    ? Loc.GetString("w40k-cmd-battle-tactic-window-row-selected-button")
                    : Loc.GetString("w40k-cmd-battle-tactic-window-select-button");
            button.Disabled = isActive;
        }
    }

    private static WH40KCommandNodeTacticPreset FindTacticPreset(string? tacticId)
    {
        return WH40KCommandNodeTactics.FindOrDefault(tacticId);
    }

    private static string FormatCooldownTime(int totalSeconds)
    {
        var safeSeconds = Math.Max(0, totalSeconds);
        var minutes = safeSeconds / 60;
        var seconds = safeSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private static string CompactLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var compact = text
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        return string.Join(' ', compact.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
