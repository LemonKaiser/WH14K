using System;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeMissionBoardWindow : FancyWindow
{
    private static readonly Color ImperiumColor = Color.FromHex("#F3C548");

    private readonly StyleBoxFlat _headerStyle;
    private readonly Label _teamLine;
    private readonly Label _phaseLine;
    private readonly Label _activeStatusLine;
    private readonly Label _activeMissionTitleLine;
    private readonly Label _activeMissionTimerLine;
    private readonly Label _activeMissionRewardLine;
    private readonly RichTextLabel _activeMissionDescription;
    private readonly Button _pinpointerSyncButton;
    private readonly BoxContainer _systemRows;
    private readonly BoxContainer _selectableRows;

    private Color _accent = ImperiumColor;

    public event Action<string>? OnTaskSelected;
    public event Action? OnPinpointerSyncRequested;

    public WH40KCommandNodeMissionBoardWindow()
    {
        Title = Loc.GetString("wh40k-command-node-mission-board-window-title");
        MinSize = SetSize = new Vector2(920, 670);

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
            SeparationOverride = 3,
            Margin = new Thickness(8)
        };
        header.AddChild(headerBox);

        _teamLine = new Label();
        _phaseLine = new Label();
        headerBox.AddChild(_teamLine);
        headerBox.AddChild(_phaseLine);

        var topRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            VerticalExpand = true
        };
        root.AddChild(topRow);

        var activeMissionSection = CreateSection(
            Loc.GetString("wh40k-command-node-mission-board-active-header"),
            out var activeMissionBox,
            verticalExpand: true);
        activeMissionSection.HorizontalExpand = true;
        activeMissionSection.VerticalExpand = true;
        activeMissionSection.SizeFlagsStretchRatio = 1.1f;
        topRow.AddChild(activeMissionSection);

        var activeMissionScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true
        };
        activeMissionBox.AddChild(activeMissionScroll);

        var activeMissionContent = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true,
            HorizontalExpand = true
        };
        activeMissionScroll.AddChild(activeMissionContent);

        _activeStatusLine = new Label();
        _activeMissionTitleLine = new Label();
        _activeMissionTimerLine = new Label();
        _activeMissionRewardLine = new Label();
        _activeMissionDescription = new RichTextLabel
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        activeMissionContent.AddChild(_activeStatusLine);
        activeMissionContent.AddChild(_activeMissionTitleLine);
        activeMissionContent.AddChild(_activeMissionTimerLine);
        activeMissionContent.AddChild(_activeMissionRewardLine);
        activeMissionContent.AddChild(new HSeparator());
        activeMissionContent.AddChild(_activeMissionDescription);
        activeMissionContent.AddChild(_pinpointerSyncButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-mission-board-pinpointer-sync-button")
        });
        _pinpointerSyncButton.OnPressed += _ => OnPinpointerSyncRequested?.Invoke();

        var systemSection = CreateSection(
            Loc.GetString("wh40k-command-node-mission-board-system-header"),
            out var systemBox,
            verticalExpand: true);
        systemSection.MinWidth = 320;
        systemSection.SizeFlagsStretchRatio = 0.9f;
        systemSection.HorizontalExpand = true;
        systemSection.VerticalExpand = true;
        topRow.AddChild(systemSection);

        var systemScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true
        };
        systemBox.AddChild(systemScroll);

        _systemRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        systemScroll.AddChild(_systemRows);

        var selectableSection = CreateSection(
            Loc.GetString("wh40k-command-node-mission-board-selectable-header"),
            out var selectableBox,
            verticalExpand: false);
        root.AddChild(selectableSection);

        var selectableScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SetHeight = 200f
        };
        selectableBox.AddChild(selectableScroll);

        _selectableRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true
        };
        selectableScroll.AddChild(_selectableRows);
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, ImperiumColor);
        _headerStyle.BorderColor = _accent;
        _teamLine.Text = Loc.GetString("wh40k-command-node-team", ("team", state.TeamName));
        _phaseLine.Text = Loc.GetString("wh40k-command-node-phase",
            ("phase", Loc.GetString(GetPhaseKey(state.Phase))));

        var mission = state.MissionBoard;
        var runtimeMission = state.TeamMissionRuntime.IsActive
            ? state.TeamMissionRuntime
            : state.GlobalMissionRuntime;

        if (runtimeMission.IsActive)
        {
            var scopeLabel = GetScopeLabel(runtimeMission);
            _activeStatusLine.Text = Loc.GetString("wh40k-command-node-mission-board-active-status",
                ("status", Loc.GetString("wh40k-command-node-mission-board-status-active")));
            _activeMissionTitleLine.Text = ResolveLocalizedOrRaw(runtimeMission.MissionTitle);
            _activeMissionTimerLine.Text = Loc.GetString("wh40k-command-node-mission-board-active-timer",
                ("timer", $"{scopeLabel}: {FormatDuration(runtimeMission.RemainingSeconds)}"));
            _activeMissionRewardLine.Text = Loc.GetString("wh40k-command-node-mission-board-runtime-reward",
                ("major", runtimeMission.RewardMajorDevelopmentPoints),
                ("minor", runtimeMission.RewardMinorDevelopmentPoints),
                ("tempo", runtimeMission.RewardTempoBonusPercent),
                ("token", string.IsNullOrWhiteSpace(runtimeMission.RewardTokenId) ? "-" : ResolveLocalizedOrRaw(runtimeMission.RewardTokenId)));
            ApplyWrappedText(_activeMissionDescription, BuildRuntimeMissionDescription(runtimeMission));
            _pinpointerSyncButton.Disabled = false;
        }
        else
        {
            _activeStatusLine.Text = Loc.GetString("wh40k-command-node-mission-board-active-status",
                ("status", Loc.GetString("wh40k-command-node-mission-board-status-pending")));
            _activeMissionTitleLine.Text = Loc.GetString("wh40k-command-node-mission-board-no-active-title");
            _activeMissionTimerLine.Text = Loc.GetString("wh40k-command-node-mission-board-active-timer",
                ("timer", Loc.GetString("wh40k-command-node-mission-board-no-active-timer")));
            _activeMissionRewardLine.Text = Loc.GetString("wh40k-command-node-mission-board-no-active-reward");
            ApplyWrappedText(_activeMissionDescription, Loc.GetString("wh40k-command-node-mission-board-no-active-description"));
            _pinpointerSyncButton.Disabled = true;
        }

        RebuildSystemRows(mission);
        RebuildSelectableTasks(mission);
    }

    private PanelContainer CreateSection(string title, out BoxContainer content, bool verticalExpand)
    {
        var section = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1F2433"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
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
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#2B3246"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            }
        };
        sectionRoot.AddChild(titleBar);
        titleBar.AddChild(new Label
        {
            Margin = new Thickness(6, 4),
            Text = title
        });

        content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = verticalExpand
        };
        sectionRoot.AddChild(content);

        return section;
    }

    private static string BuildRuntimeMissionDescription(WH40KCommandMissionRuntimeState mission)
    {
        var token = string.IsNullOrWhiteSpace(mission.RewardTokenId)
            ? "-"
            : ResolveLocalizedOrRaw(mission.RewardTokenId);
        return Loc.GetString("wh40k-command-node-mission-board-runtime-description",
            ("description", ResolveLocalizedOrRaw(mission.MissionDescription)),
            ("timeout", mission.RewardTimeoutDevelopmentPoints),
            ("failure", mission.RewardFailureDevelopmentPoints),
            ("token", token),
            ("token_time", FormatDuration(mission.RewardTokenDurationSeconds)));
    }

    private static string GetScopeLabel(WH40KCommandMissionRuntimeState mission)
    {
        return mission.Scope switch
        {
            WH40KCommandDynamicMissionScope.Global => Loc.GetString("wh40k-command-node-mission-board-scope-global"),
            WH40KCommandDynamicMissionScope.Faction => Loc.GetString("wh40k-command-node-mission-board-scope-faction"),
            _ => Loc.GetString("wh40k-command-node-mission-board-scope-global")
        };
    }

    private void RebuildSystemRows(WH40KCommandMissionBoardState mission)
    {
        _systemRows.RemoveAllChildren();

        if (mission.SystemTasks.Length == 0)
        {
            _systemRows.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-command-node-mission-board-system-empty"),
                ModulateSelfOverride = Color.FromHex("#AAB3CC")
            });
            return;
        }

        foreach (var task in mission.SystemTasks)
        {
            var status = Loc.GetString(GetStatusKey(task.Status));
            AddSystemRow(task, status, emphasized: task.Status == WH40KCommandMissionBoardTaskStatus.Active);
        }
    }

    private void RebuildSelectableTasks(WH40KCommandMissionBoardState mission)
    {
        _selectableRows.RemoveAllChildren();

        if (mission.SelectableTasks.Length == 0)
        {
            _selectableRows.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-command-node-mission-board-selectable-empty"),
                ModulateSelfOverride = Color.FromHex("#AAB3CC")
            });
            return;
        }

        var hasSelectedTask = !string.IsNullOrWhiteSpace(mission.SelectedTaskId);
        foreach (var task in mission.SelectableTasks)
        {
            var selected = string.Equals(task.Id, mission.SelectedTaskId, StringComparison.Ordinal);
            var locked = hasSelectedTask && !selected;
            AddSelectableTaskCard(task, selected, locked);
        }
    }

    private void AddSystemRow(WH40KCommandMissionBoardSystemTaskState task, string status, bool emphasized)
    {
        var row = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = emphasized ? Color.FromHex("#2A344D") : Color.FromHex("#232A3B"),
                BorderColor = emphasized ? _accent : Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        _systemRows.AddChild(row);

        var rowBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Margin = new Thickness(6)
        };
        row.AddChild(rowBox);

        rowBox.AddChild(new Label
        {
            Text = ResolveLocalizedOrRaw(task.TitleKey),
            ModulateSelfOverride = emphasized ? _accent : Color.White
        });
        rowBox.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-mission-board-row-status", ("status", status))
        });
        rowBox.AddChild(new Label
        {
            Text = ResolveLocalizedOrRaw(task.RewardKey),
            ModulateSelfOverride = _accent
        });

        var descriptionLabel = new RichTextLabel
        {
            HorizontalExpand = true
        };
        ApplyWrappedText(descriptionLabel, ResolveLocalizedOrRaw(task.DescriptionKey));
        rowBox.AddChild(descriptionLabel);
    }

    private void AddSelectableTaskCard(WH40KCommandMissionBoardSelectableTaskState task, bool selected, bool locked)
    {
        var card = new PanelContainer
        {
            MinWidth = 280,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = selected ? Color.FromHex("#2A344D") : Color.FromHex("#232A3B"),
                BorderColor = selected
                    ? _accent
                    : locked
                        ? Color.FromHex("#78809A")
                        : Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        _selectableRows.AddChild(card);

        var cardBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6),
            VerticalExpand = true
        };
        card.AddChild(cardBox);

        cardBox.AddChild(new Label
        {
            Text = ResolveLocalizedOrRaw(task.TitleKey),
            ModulateSelfOverride = _accent
        });
        cardBox.AddChild(new Label
        {
            Text = ResolveLocalizedOrRaw(task.RewardKey)
        });
        cardBox.AddChild(new Label
        {
            Text = ResolveLocalizedOrRaw(task.DurationKey),
            ModulateSelfOverride = Color.FromHex("#AAB3CC")
        });

        var description = new RichTextLabel
        {
            HorizontalExpand = true,
            VerticalExpand = true
        };
        ApplyWrappedText(description, ResolveLocalizedOrRaw(task.DescriptionKey));
        cardBox.AddChild(description);

        var buttonText = selected
            ? Loc.GetString("wh40k-command-node-mission-board-selectable-selected-button")
            : locked
                ? Loc.GetString("wh40k-command-node-mission-board-selectable-locked-button")
                : Loc.GetString("wh40k-command-node-mission-board-selectable-select-button");

        var button = new Button
        {
            HorizontalExpand = true,
            Text = buttonText,
            Disabled = selected || locked
        };
        button.OnPressed += _ => OnTaskSelected?.Invoke(task.Id);
        cardBox.AddChild(button);
    }

    private static void ApplyWrappedText(RichTextLabel label, string text)
    {
        var normalized = text.Replace("\\n", "\n", StringComparison.Ordinal);
        label.SetMessage(
            FormattedMessage.FromMarkupPermissive(FormattedMessage.EscapeText(normalized)),
            tagsAllowed: null,
            defaultColor: Color.White);
    }

    private static string GetStatusKey(WH40KCommandMissionBoardTaskStatus status)
    {
        return status switch
        {
            WH40KCommandMissionBoardTaskStatus.Pending => "wh40k-command-node-mission-board-status-pending",
            WH40KCommandMissionBoardTaskStatus.Active => "wh40k-command-node-mission-board-status-active",
            WH40KCommandMissionBoardTaskStatus.Queued => "wh40k-command-node-mission-board-status-queued",
            _ => "wh40k-command-node-mission-board-status-pending"
        };
    }

    private static string GetPhaseKey(WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => "wh40k-phase-preparation-name",
            WH40KBattlePhase.Assault => "wh40k-phase-assault-name",
            WH40KBattlePhase.Apocalypse => "wh40k-phase-apocalypse-name",
            _ => "wh40k-phase-preparation-name"
        };
    }

    private static string FormatDuration(int seconds)
    {
        var clamped = Math.Max(0, seconds);
        var minutes = clamped / 60;
        var secs = clamped % 60;
        return $"{minutes:00}:{secs:00}";
    }

    private static string ResolveLocalizedOrRaw(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (Loc.TryGetString(value, out var localized) && !string.IsNullOrWhiteSpace(localized))
            return localized!;

        return value;
    }
}
