using System;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.Localization;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeMissionBoardWindow : FancyWindow, ILocalizedControl
{
    private readonly Label _headerTitleLabel;
    private readonly Label _headerSubtitleLabel;
    private readonly Label _teamLine;
    private readonly Label _phaseLine;
    private readonly PanelContainer _teamBadge;
    private readonly Label _teamBadgeLabel;
    private readonly PanelContainer _phaseBadge;
    private readonly Label _phaseBadgeLabel;
    private readonly StyleBoxFlat _headerStyle;
    private readonly PanelContainer _activeMissionSection;
    private readonly PanelContainer _missionStatusBadge;
    private readonly Label _missionStatusBadgeLabel;
    private readonly Label _activeStatusLine;
    private readonly Label _activeMissionTitleLine;
    private readonly Label _activeMissionTimerLine;
    private readonly Label _activeMissionRewardLine;
    private readonly Label _activeMissionDescription;
    private readonly Button _pinpointerSyncButton;
    private readonly Label _activeSectionTitleLabel;
    private readonly Label _systemSectionTitleLabel;
    private readonly Label _selectableSectionTitleLabel;
    private readonly BoxContainer _systemRows;
    private readonly BoxContainer _selectableRows;

    private Color _accent = WH40KCommandUiStyles.DefaultAccent;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;

    public event Action<string>? OnTaskSelected;
    public event Action? OnPinpointerSyncRequested;

    public WH40KCommandNodeMissionBoardWindow()
    {
        Title = Loc.GetString("w40k-cmd-mission-board-window-title");
        MinSize = SetSize = new Vector2(1012, 660);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(6)
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
            SeparationOverride = 10,
            Margin = new Thickness(10, 8),
            VerticalAlignment = VAlignment.Center
        };
        header.AddChild(headerBox);

        var headerInfo = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            HorizontalExpand = true
        };
        headerBox.AddChild(headerInfo);

        _headerTitleLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-mission-board-window-title"),
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        headerInfo.AddChild(_headerTitleLabel);
        _headerSubtitleLabel = new Label
        {
            Text = Loc.GetString("w40k-cmd-mission-board-active-description"),
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_headerSubtitleLabel);

        _teamLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _phaseLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        headerInfo.AddChild(_teamLine);
        headerInfo.AddChild(_phaseLine);

        var badgeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
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

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            VerticalExpand = true
        };
        root.AddChild(body);

        var topRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 5,
            VerticalExpand = true
        };
        body.AddChild(topRow);

        _activeMissionSection = CreateSection(
            Loc.GetString("w40k-cmd-mission-board-active-header"),
            out var activeMissionBox,
            out _activeSectionTitleLabel,
            verticalExpand: true);
        _activeMissionSection.HorizontalExpand = true;
        _activeMissionSection.VerticalExpand = true;
        _activeMissionSection.SizeFlagsStretchRatio = 1.05f;
        topRow.AddChild(_activeMissionSection);

        var activeMissionContent = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            VerticalExpand = true
        };
        activeMissionBox.AddChild(activeMissionContent);

        var missionBadgeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            VerticalAlignment = VAlignment.Center
        };
        activeMissionContent.AddChild(missionBadgeRow);

        _missionStatusBadge = new PanelContainer();
        _missionStatusBadgeLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true
        };
        _missionStatusBadge.AddChild(_missionStatusBadgeLabel);
        missionBadgeRow.AddChild(_missionStatusBadge);

        missionBadgeRow.AddChild(new Control
        {
            HorizontalExpand = true
        });

        _pinpointerSyncButton = new Button
        {
            Text = Loc.GetString("w40k-cmd-mission-board-pinpointer-sync-button")
        };
        _pinpointerSyncButton.OnPressed += _ => OnPinpointerSyncRequested?.Invoke();
        missionBadgeRow.AddChild(_pinpointerSyncButton);

        _activeStatusLine = new Label
        {
            StyleClasses = { "LabelSubText" },
            ClipText = true
        };
        _activeMissionTitleLine = new Label
        {
            StyleClasses = { "LabelBig" },
            ClipText = true
        };
        _activeMissionTimerLine = new Label { ClipText = true };
        _activeMissionRewardLine = new Label { ClipText = true };
        _activeMissionDescription = new Label
        {
            HorizontalExpand = true,
            ClipText = true
        };

        activeMissionContent.AddChild(_activeStatusLine);
        activeMissionContent.AddChild(_activeMissionTitleLine);
        activeMissionContent.AddChild(_activeMissionTimerLine);
        activeMissionContent.AddChild(_activeMissionRewardLine);
        activeMissionContent.AddChild(new HSeparator());

        var missionDescriptionFrame = new PanelContainer
        {
            SetHeight = 86f,
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackgroundAlt,
                WH40KCommandUiStyles.MutedBorder)
        };
        missionDescriptionFrame.AddChild(_activeMissionDescription);
        activeMissionContent.AddChild(missionDescriptionFrame);

        var systemSection = CreateSection(
            Loc.GetString("w40k-cmd-mission-board-system-header"),
            out var systemBox,
            out _systemSectionTitleLabel,
            verticalExpand: true);
        systemSection.MinWidth = 300;
        systemSection.SizeFlagsStretchRatio = 0.95f;
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
            SeparationOverride = 8,
            VerticalExpand = true
        };
        systemScroll.AddChild(_systemRows);

        var selectableSection = CreateSection(
            Loc.GetString("w40k-cmd-mission-board-selectable-header"),
            out var selectableBox,
            out _selectableSectionTitleLabel,
            verticalExpand: false);
        body.AddChild(selectableSection);

        var selectableScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = true,
            SetHeight = 170f
        };
        selectableBox.AddChild(selectableScroll);

        _selectableRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            HorizontalExpand = true
        };
        selectableScroll.AddChild(_selectableRows);

        Relocalize();
    }

    public void Relocalize()
    {
        Title = Loc.GetString("w40k-cmd-mission-board-window-title");
        _headerTitleLabel.Text = Loc.GetString("w40k-cmd-mission-board-window-title");
        _headerSubtitleLabel.Text = Loc.GetString("w40k-cmd-mission-board-active-description");
        _activeSectionTitleLabel.Text = Loc.GetString("w40k-cmd-mission-board-active-header");
        _systemSectionTitleLabel.Text = Loc.GetString("w40k-cmd-mission-board-system-header");
        _selectableSectionTitleLabel.Text = Loc.GetString("w40k-cmd-mission-board-selectable-header");
        _pinpointerSyncButton.Text = Loc.GetString("w40k-cmd-mission-board-pinpointer-sync-button");

        if (_latestState != null)
            UpdateState(_latestState);
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        _latestState = state;
        _accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(state.TeamId, WH40KCommandUiStyles.DefaultAccent);
        _headerStyle.BorderColor = _accent;
        _headerTitleLabel.ModulateSelfOverride = _accent;

        var resolvedTeam = WH40KCommandUiStyles.ResolveLocalizedOrRaw(state.TeamName);

        _teamLine.Text = Loc.GetString("w40k-cmd-team", ("team", resolvedTeam));
        _phaseLine.Text = Loc.GetString("w40k-cmd-phase",
            ("phase", Loc.GetString(GetPhaseKey(state.Phase))));

        _teamBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#203227".AsSpan()), _accent);
        _teamBadgeLabel.Text = string.IsNullOrWhiteSpace(resolvedTeam)
            ? "?"
            : resolvedTeam.ToUpperInvariant();

        _phaseBadge.PanelOverride = ResolvePhaseBadgeStyle(state.Phase);
        _phaseBadgeLabel.Text = Loc.GetString(GetPhaseKey(state.Phase));

        var runtimeMission = state.TeamMissionRuntime.IsActive
            ? state.TeamMissionRuntime
            : state.GlobalMissionRuntime;

        var missionIsActive = runtimeMission.IsActive;
        _activeMissionSection.PanelOverride = WH40KCommandUiStyles.CreateBorderPanelStyle(
            WH40KCommandUiStyles.PanelBackground,
            missionIsActive ? _accent : WH40KCommandUiStyles.StrongBorder,
            2);

        if (missionIsActive)
        {
            var scopeLabel = GetScopeLabel(runtimeMission);
            _missionStatusBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#223B2F".AsSpan()),
                WH40KCommandUiStyles.ReadyBadge);
            _missionStatusBadgeLabel.Text = Loc.GetString("w40k-cmd-mission-board-status-active");

            _activeStatusLine.Text = Loc.GetString("w40k-cmd-mission-board-active-status",
                ("status", $"{scopeLabel} / {Loc.GetString("w40k-cmd-mission-board-status-active")}"));
            _activeMissionTitleLine.Text = WH40KCommandUiStyles.ResolveLocalizedOrRaw(runtimeMission.MissionTitle);
            _activeMissionTitleLine.ModulateSelfOverride = _accent;
            _activeMissionTimerLine.Text = Loc.GetString("w40k-cmd-mission-board-active-timer",
                ("timer", $"{scopeLabel}: {FormatDuration(runtimeMission.RemainingSeconds)}"));
            _activeMissionRewardLine.Text = Loc.GetString("w40k-cmd-mission-board-runtime-reward",
                ("major", runtimeMission.RewardMajorDevelopmentPoints),
                ("minor", runtimeMission.RewardMinorDevelopmentPoints),
                ("tempo", runtimeMission.RewardTempoBonusPercent),
                ("token", string.IsNullOrWhiteSpace(runtimeMission.RewardTokenId)
                    ? "-"
                    : WH40KCommandUiStyles.ResolveLocalizedOrRaw(runtimeMission.RewardTokenId)));
            _activeMissionDescription.Text = CompactText(BuildRuntimeMissionDescription(runtimeMission), 120);
            _pinpointerSyncButton.Disabled = false;
        }
        else
        {
            _missionStatusBadge.PanelOverride = WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#22313B".AsSpan()),
                WH40KCommandUiStyles.InfoBadge);
            _missionStatusBadgeLabel.Text = Loc.GetString("w40k-cmd-mission-board-status-pending");

            _activeStatusLine.Text = Loc.GetString("w40k-cmd-mission-board-active-status",
                ("status", Loc.GetString("w40k-cmd-mission-board-status-pending")));
            _activeMissionTitleLine.Text = Loc.GetString("w40k-cmd-mission-board-no-active-title");
            _activeMissionTitleLine.ModulateSelfOverride = Color.White;
            _activeMissionTimerLine.Text = Loc.GetString("w40k-cmd-mission-board-active-timer",
                ("timer", Loc.GetString("w40k-cmd-mission-board-no-active-timer")));
            _activeMissionRewardLine.Text = Loc.GetString("w40k-cmd-mission-board-no-active-reward");
            _activeMissionDescription.Text = CompactText(
                Loc.GetString("w40k-cmd-mission-board-no-active-description"),
                120);
            _pinpointerSyncButton.Disabled = true;
        }

        RebuildSystemRows(state.MissionBoard);
        RebuildSelectableTasks(state.MissionBoard);
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
        sectionRoot.AddChild(titleBar);
        titleLabel = new Label
        {
            Text = title,
            StyleClasses = { "LabelHeading" },
            ClipText = true
        };
        titleBar.AddChild(titleLabel);

        content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(10, 10, 10, 10),
            VerticalExpand = verticalExpand
        };
        sectionRoot.AddChild(content);

        return section;
    }

    private void RebuildSystemRows(WH40KCommandMissionBoardState mission)
    {
        _systemRows.RemoveAllChildren();

        if (mission.SystemTasks.Length == 0)
        {
            _systemRows.AddChild(CreateEmptyCard(Loc.GetString("w40k-cmd-mission-board-system-empty")));
            return;
        }

        foreach (var task in mission.SystemTasks)
        {
            var emphasized = task.Status == WH40KCommandMissionBoardTaskStatus.Active;
            var card = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                    emphasized ? WH40KCommandUiStyles.CardBackground : WH40KCommandUiStyles.CardBackgroundAlt,
                    emphasized ? _accent : WH40KCommandUiStyles.MutedBorder)
            };
            _systemRows.AddChild(card);

            var rowBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 4
            };
            card.AddChild(rowBox);

            var header = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 8
            };
            rowBox.AddChild(header);

            header.AddChild(new Label
            {
                Text = WH40KCommandUiStyles.ResolveLocalizedOrRaw(task.TitleKey),
                StyleClasses = { "LabelBig" },
                HorizontalExpand = true,
                ModulateSelfOverride = emphasized ? _accent : Color.White,
                ClipText = true
            });

            var statusBadge = new PanelContainer
            {
                PanelOverride = ResolveTaskStatusStyle(task.Status, emphasized)
            };
            statusBadge.AddChild(new Label
            {
                Text = Loc.GetString(GetStatusKey(task.Status))
            });
            header.AddChild(statusBadge);

            rowBox.AddChild(new Label
            {
                Text = WH40KCommandUiStyles.ResolveLocalizedOrRaw(task.RewardKey),
                ModulateSelfOverride = _accent,
                ClipText = true
            });

            rowBox.AddChild(new Label
            {
                Text = CompactText(WH40KCommandUiStyles.ResolveLocalizedOrRaw(task.DescriptionKey), 110),
                StyleClasses = { "LabelSubText" },
                HorizontalExpand = true,
                ClipText = true
            });
        }
    }

    private void RebuildSelectableTasks(WH40KCommandMissionBoardState mission)
    {
        _selectableRows.RemoveAllChildren();

        if (mission.SelectableTasks.Length == 0)
        {
            _selectableRows.AddChild(CreateEmptyCard(
                Loc.GetString("w40k-cmd-mission-board-selectable-empty"),
                minWidth: 320));
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

    private void AddSelectableTaskCard(WH40KCommandMissionBoardSelectableTaskState task, bool selected, bool locked)
    {
        var border = selected
            ? _accent
            : locked
                ? WH40KCommandUiStyles.StrongBorder
                : WH40KCommandUiStyles.MutedBorder;

        var background = selected
            ? WH40KCommandUiStyles.CardBackground
            : WH40KCommandUiStyles.CardBackgroundAlt;

        var card = new PanelContainer
        {
            MinWidth = 260,
            MaxWidth = 292,
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(background, border)
        };
        _selectableRows.AddChild(card);

        var cardBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6
        };
        card.AddChild(cardBox);

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8
        };
        cardBox.AddChild(header);

        header.AddChild(new Label
        {
            Text = WH40KCommandUiStyles.ResolveLocalizedOrRaw(task.TitleKey),
            StyleClasses = { "LabelBig" },
            HorizontalExpand = true,
            ModulateSelfOverride = selected ? _accent : Color.White,
            ClipText = true
        });

        var stateBadge = new PanelContainer
        {
            PanelOverride = selected
                ? WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#223B2F".AsSpan()), WH40KCommandUiStyles.ReadyBadge)
                : locked
                    ? WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#2A2F39".AsSpan()), WH40KCommandUiStyles.WarningBadge)
                    : WH40KCommandUiStyles.CreateBadgeStyle(Color.FromHex("#22313B".AsSpan()), _accent)
        };
        stateBadge.AddChild(new Label
        {
            Text = selected
                ? Loc.GetString("w40k-cmd-mission-board-selectable-selected-button")
                : locked
                    ? Loc.GetString("w40k-cmd-mission-board-selectable-locked-button")
                    : Loc.GetString("w40k-cmd-mission-board-selectable-select-button")
        });
        header.AddChild(stateBadge);

        cardBox.AddChild(new Label
        {
            Text = WH40KCommandUiStyles.ResolveLocalizedOrRaw(task.RewardKey),
            ModulateSelfOverride = _accent,
            ClipText = true
        });
        cardBox.AddChild(new Label
        {
            Text = WH40KCommandUiStyles.ResolveLocalizedOrRaw(task.DurationKey),
            StyleClasses = { "LabelSubText" },
            ClipText = true
        });

        cardBox.AddChild(new Label
        {
            Text = CompactText(WH40KCommandUiStyles.ResolveLocalizedOrRaw(task.DescriptionKey), 100),
            StyleClasses = { "LabelSubText" },
            HorizontalExpand = true,
            ClipText = true
        });

        var buttonText = selected
            ? Loc.GetString("w40k-cmd-mission-board-selectable-selected-button")
            : locked
                ? Loc.GetString("w40k-cmd-mission-board-selectable-locked-button")
                : Loc.GetString("w40k-cmd-mission-board-selectable-select-button");

        var button = new Button
        {
            HorizontalExpand = true,
            Text = buttonText,
            Disabled = selected || locked
        };
        button.OnPressed += _ => OnTaskSelected?.Invoke(task.Id);
        cardBox.AddChild(button);
    }

    private PanelContainer CreateEmptyCard(string text, float minWidth = 0f)
    {
        var card = new PanelContainer
        {
            MinWidth = minWidth,
            PanelOverride = WH40KCommandUiStyles.CreateCardStyle(
                WH40KCommandUiStyles.CardBackgroundMuted,
                WH40KCommandUiStyles.MutedBorder)
        };

        card.AddChild(new Label
        {
            Text = text,
            StyleClasses = { "LabelSubText" },
            ClipText = true
        });

        return card;
    }

    private StyleBoxFlat ResolveTaskStatusStyle(WH40KCommandMissionBoardTaskStatus status, bool emphasized)
    {
        if (emphasized)
        {
            return WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#223B2F".AsSpan()),
                WH40KCommandUiStyles.ReadyBadge);
        }

        return status switch
        {
            WH40KCommandMissionBoardTaskStatus.Queued => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#3A2E1D".AsSpan()),
                WH40KCommandUiStyles.WarningBadge),
            _ => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#22313B".AsSpan()),
                WH40KCommandUiStyles.InfoBadge)
        };
    }

    private static string BuildRuntimeMissionDescription(WH40KCommandMissionRuntimeState mission)
    {
        var token = string.IsNullOrWhiteSpace(mission.RewardTokenId)
            ? "-"
            : WH40KCommandUiStyles.ResolveLocalizedOrRaw(mission.RewardTokenId);
        return $"{CompactText(WH40KCommandUiStyles.ResolveLocalizedOrRaw(mission.MissionDescription), 42)}"
               + $" | +{mission.RewardTimeoutDevelopmentPoints}/-{mission.RewardFailureDevelopmentPoints}"
               + $" | {token}"
               + $" | {FormatDuration(mission.RewardTokenDurationSeconds)}";
    }

    private static string CompactText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var compact = text.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        while (compact.Contains("  ", StringComparison.Ordinal))
        {
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (compact.Length <= maxLength)
            return compact;

        return compact[..Math.Max(0, maxLength - 1)].TrimEnd() + "...";
    }

    private static string GetScopeLabel(WH40KCommandMissionRuntimeState mission)
    {
        return mission.Scope switch
        {
            WH40KCommandDynamicMissionScope.Global => Loc.GetString("w40k-cmd-mission-board-scope-global"),
            WH40KCommandDynamicMissionScope.Faction => Loc.GetString("w40k-cmd-mission-board-scope-faction"),
            _ => Loc.GetString("w40k-cmd-mission-board-scope-global")
        };
    }

    private static string GetStatusKey(WH40KCommandMissionBoardTaskStatus status)
    {
        return status switch
        {
            WH40KCommandMissionBoardTaskStatus.Pending => "w40k-cmd-mission-board-status-pending",
            WH40KCommandMissionBoardTaskStatus.Active => "w40k-cmd-mission-board-status-active",
            WH40KCommandMissionBoardTaskStatus.Queued => "w40k-cmd-mission-board-status-queued",
            _ => "w40k-cmd-mission-board-status-pending"
        };
    }

    private static StyleBoxFlat ResolvePhaseBadgeStyle(WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#26314A".AsSpan()),
                WH40KCommandUiStyles.InfoBadge),
            WH40KBattlePhase.Assault => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#3A2E1D".AsSpan()),
                WH40KCommandUiStyles.WarningBadge),
            WH40KBattlePhase.Apocalypse => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#3A2A2A".AsSpan()),
                WH40KCommandUiStyles.DangerBadge),
            _ => WH40KCommandUiStyles.CreateBadgeStyle(
                Color.FromHex("#26314A".AsSpan()),
                WH40KCommandUiStyles.InfoBadge)
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
}
