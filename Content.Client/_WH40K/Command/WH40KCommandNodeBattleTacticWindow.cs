using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeBattleTacticWindow : FancyWindow
{
    private static readonly Color ImperiumColor = Color.FromHex("#F3C548");
    private static readonly Color ActiveColor = Color.FromHex("#7E88A6");

    private static readonly IReadOnlyList<WH40KCommandNodeTacticPreset> Presets = WH40KCommandNodeTactics.Presets;

    public event Action<string>? OnBattleTacticAssignRequested;

    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _teamLine;
    private readonly Label _activeBattleTacticLine;
    private readonly Label _selectedBattleTacticLine;
    private readonly Label _cooldownLine;
    private readonly RichTextLabel _selectedDescription;
    private readonly Button _assignButton;
    private readonly Dictionary<string, StyleBoxFlat> _rowStyles = new();
    private readonly Dictionary<string, Label> _rowTitleLabels = new();
    private readonly Dictionary<string, Button> _rowButtons = new();
    private Color _accent = ImperiumColor;
    private int _battleTacticCooldownSeconds;
    private string _activeBattleTacticId = WH40KCommandNodeTactics.DefaultTacticId;
    private string _selectedBattleTacticId = WH40KCommandNodeTactics.DefaultTacticId;

    public WH40KCommandNodeBattleTacticWindow()
    {
        Title = Loc.GetString("wh40k-command-node-battle-tactic-window-title");
        MinSize = SetSize = new Vector2(860, 560);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8
        };
        ContentsContainer.AddChild(root);

        var header = new PanelContainer
        {
            PanelOverride = _headerStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#2B3246"),
                BorderColor = ImperiumColor,
                BorderThickness = new Thickness(1)
            }
        };
        root.AddChild(header);

        var headerBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(8)
        };
        header.AddChild(headerBox);

        _teamLine = new Label();
        _activeBattleTacticLine = new Label();
        headerBox.AddChild(_teamLine);
        headerBox.AddChild(_activeBattleTacticLine);
        headerBox.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-battle-tactic-window-draft-note")
        });

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            VerticalExpand = true
        };
        root.AddChild(body);

        var listPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1F2433"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        body.AddChild(listPanel);

        var listBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = true
        };
        listPanel.AddChild(listBox);
        listBox.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-battle-tactic-window-list-header")
        });

        var listScroll = new ScrollContainer
        {
            VerticalExpand = true
        };
        listBox.AddChild(listScroll);

        var battleTacticRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        listScroll.AddChild(battleTacticRows);

        foreach (var preset in Presets)
        {
            var rowStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#232A3B"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            };

            var row = new PanelContainer
            {
                PanelOverride = rowStyle
            };
            battleTacticRows.AddChild(row);

            var rowBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 4,
                Margin = new Thickness(6)
            };
            row.AddChild(rowBox);

            var title = new Label
            {
                Text = Loc.GetString(preset.NameLocKey)
            };
            rowBox.AddChild(title);
            rowBox.AddChild(new Label
            {
                Text = Loc.GetString(preset.SummaryLocKey)
            });

            var selectButton = new Button
            {
                HorizontalExpand = true,
                Text = Loc.GetString("wh40k-command-node-battle-tactic-window-select-button")
            };
            selectButton.OnPressed += _ => SelectBattleTactic(preset.Id);
            rowBox.AddChild(selectButton);

            _rowStyles[preset.Id] = rowStyle;
            _rowTitleLabels[preset.Id] = title;
            _rowButtons[preset.Id] = selectButton;
        }

        var selectionPanel = new PanelContainer
        {
            MinWidth = 320,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#252A3A"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        body.AddChild(selectionPanel);

        var selectionBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = true
        };
        selectionPanel.AddChild(selectionBox);

        selectionBox.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-battle-tactic-window-selection-header")
        });

        _selectedBattleTacticLine = new Label();
        selectionBox.AddChild(_selectedBattleTacticLine);

        _cooldownLine = new Label();
        selectionBox.AddChild(_cooldownLine);

        _selectedDescription = new RichTextLabel
        {
            VerticalExpand = true
        };
        selectionBox.AddChild(_selectedDescription);

        _assignButton = new Button
        {
            HorizontalExpand = true
        };
        _assignButton.OnPressed += _ => AssignSelectedBattleTactic();
        selectionBox.AddChild(_assignButton);
    }

    public static (string Name, string Description) ResolveBattleTacticDisplay(string? tacticId)
    {
        var preset = FindTacticPreset(tacticId);
        return (Loc.GetString(preset.NameLocKey), Loc.GetString(preset.DescriptionLocKey));
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, ImperiumColor);
        _headerStyle.BorderColor = _accent;
        _teamLine.Text = Loc.GetString("wh40k-command-node-team", ("team", state.TeamName));

        _activeBattleTacticId = FindTacticPreset(state.ActiveBattleTacticId).Id;
        _battleTacticCooldownSeconds = Math.Max(0, state.BattleTacticCooldownSeconds);
        if (!_rowStyles.ContainsKey(_selectedBattleTacticId))
            _selectedBattleTacticId = _activeBattleTacticId;

        _activeBattleTacticLine.Text = Loc.GetString("wh40k-command-node-battle-tactic-window-active",
            ("tactic", ResolveBattleTacticDisplay(_activeBattleTacticId).Name));

        RefreshSelectionPreview();
        RefreshRows();
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
        _selectedBattleTacticLine.Text = Loc.GetString("wh40k-command-node-battle-tactic-window-selected",
            ("tactic", display.Name));
        _selectedDescription.SetMessage(
            FormattedMessage.FromMarkupPermissive(FormattedMessage.EscapeText(display.Description)),
            tagsAllowed: null,
            defaultColor: Color.White);

        var assigned = string.Equals(_selectedBattleTacticId, _activeBattleTacticId, StringComparison.OrdinalIgnoreCase);
        var cooldownActive = _battleTacticCooldownSeconds > 0;
        var time = FormatCooldownTime(_battleTacticCooldownSeconds);

        _cooldownLine.Text = cooldownActive
            ? Loc.GetString("wh40k-command-node-battle-tactic-window-cooldown-active", ("time", time))
            : Loc.GetString("wh40k-command-node-battle-tactic-window-cooldown-ready");
        _cooldownLine.ModulateSelfOverride = cooldownActive ? Color.FromHex("#E3A39D") : ActiveColor;

        _assignButton.Disabled = assigned || cooldownActive;
        _assignButton.Text = assigned
            ? Loc.GetString("wh40k-command-node-battle-tactic-window-assigned-button")
            : cooldownActive
                ? Loc.GetString("wh40k-command-node-battle-tactic-window-cooldown-button", ("time", time))
                : Loc.GetString("wh40k-command-node-battle-tactic-window-assign-button");
    }

    private void RefreshRows()
    {
        foreach (var preset in Presets)
        {
            var rowStyle = _rowStyles[preset.Id];
            var title = _rowTitleLabels[preset.Id];
            var button = _rowButtons[preset.Id];

            var isSelected = string.Equals(preset.Id, _selectedBattleTacticId, StringComparison.OrdinalIgnoreCase);
            var isActive = string.Equals(preset.Id, _activeBattleTacticId, StringComparison.OrdinalIgnoreCase);

            rowStyle.BackgroundColor = isSelected
                ? Color.FromHex("#2A344D")
                : isActive
                    ? Color.FromHex("#283043")
                    : Color.FromHex("#232A3B");
            rowStyle.BorderColor = isSelected
                ? _accent
                : isActive
                    ? ActiveColor
                    : Color.FromHex("#59617B");

            title.ModulateSelfOverride = isSelected ? _accent : Color.White;

            button.Text = isActive
                ? Loc.GetString("wh40k-command-node-battle-tactic-window-row-active-button")
                : isSelected
                    ? Loc.GetString("wh40k-command-node-battle-tactic-window-row-selected-button")
                    : Loc.GetString("wh40k-command-node-battle-tactic-window-select-button");
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
}
