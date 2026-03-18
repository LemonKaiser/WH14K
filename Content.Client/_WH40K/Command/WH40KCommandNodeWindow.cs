using System;
using System.Linq;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WH40K.Command;
using Content.Shared._WH40K.GameMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Command;

public sealed class WH40KCommandNodeWindow : FancyWindow
{
    private static readonly Color ImperiumColor = Color.FromHex("#F3C548");

    public event Action? OnUpgradePressed;
    public event Action? OnReinforcementPressed;
    public event Action? OnTeamCompositionPressed;
    public event Action? OnUpgradeSketchPressed;
    public event Action? OnMissionBoardPressed;
    public event Action? OnTacticalBonusesPressed;
    public event Action? OnDoctrinePressed;
    public event Action? OnBattleTacticPressed;

    private readonly Label _teamLine;
    private readonly Label _phaseLine;
    private readonly Label _levelLine;
    private readonly Label _pointsLine;
    private readonly Label _developmentPointsLine;
    private readonly Label _nextLine;
    private readonly Label _thresholdsLine;
    private readonly Label _baseProgressLine;
    private readonly ProgressBar _baseProgressBar;
    private readonly Label _battleTacticLine;
    private readonly RichTextLabel _battleTacticDescriptionLine;
    private readonly Label _reinforcementStatusLine;
    private readonly Label _focusLine;
    private readonly Label _upgradeLine;
    private readonly Label _reinforcementLine;
    private readonly Button _upgradeButton;
    private readonly Button _reinforcementButton;
    private readonly Button _teamCompositionButton;
    private readonly Button _upgradeSketchButton;
    private readonly Button _missionBoardButton;
    private readonly Button _tacticalBonusesButton;
    private readonly Button _doctrineButton;
    private readonly Button _battleTacticButton;
    private readonly StyleBoxFlat _headerStyle;
    private readonly StyleBoxFlat _infoStyle;
    private readonly StyleBoxFlat _actionStyle;
    private readonly StyleBoxFlat _progressForegroundStyle;
    private string _activeBattleTacticName = string.Empty;
    private string _activeBattleTacticDescription = string.Empty;

    public WH40KCommandNodeWindow()
    {
        Title = Loc.GetString("wh40k-command-node-window-title");

        MinSize = SetSize = new Vector2(940, 620);

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
        _phaseLine = new Label();
        headerBox.AddChild(_teamLine);
        headerBox.AddChild(_phaseLine);

        var progressPanel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#252A3A"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        root.AddChild(progressPanel);

        var progressBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(8)
        };
        progressPanel.AddChild(progressBox);

        progressBox.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-base-progress-header")
        });

        _baseProgressBar = new ProgressBar
        {
            MinValue = 0f,
            MaxValue = 1f,
            Value = 0f,
            SetHeight = 18f,
            HorizontalExpand = true,
            BackgroundStyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#646464")
            },
            ForegroundStyleBoxOverride = _progressForegroundStyle = new StyleBoxFlat
            {
                BackgroundColor = ImperiumColor
            }
        };
        progressBox.AddChild(_baseProgressBar);

        _baseProgressLine = new Label();
        progressBox.AddChild(_baseProgressLine);

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            VerticalExpand = true
        };
        root.AddChild(body);

        var infoPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = _infoStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1F2433"),
                BorderColor = Color.FromHex("#56607A"),
                BorderThickness = new Thickness(1)
            }
        };
        body.AddChild(infoPanel);

        var infoScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true
        };
        infoPanel.AddChild(infoScroll);

        var info = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            VerticalExpand = true
        };
        infoScroll.AddChild(info);

        _levelLine = new Label();
        _pointsLine = new Label();
        _developmentPointsLine = new Label();
        _nextLine = new Label();
        _thresholdsLine = new Label();
        _thresholdsLine.ClipText = true;
        _thresholdsLine.HorizontalExpand = true;
        info.AddChild(_levelLine);
        info.AddChild(_pointsLine);
        info.AddChild(_developmentPointsLine);
        info.AddChild(_nextLine);
        info.AddChild(_thresholdsLine);
        info.AddChild(new HSeparator());

        var briefingPanel = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#232A3B"),
                BorderColor = Color.FromHex("#59617B"),
                BorderThickness = new Thickness(1)
            }
        };
        info.AddChild(briefingPanel);

        var briefing = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            Margin = new Thickness(8),
            VerticalExpand = true
        };
        briefingPanel.AddChild(briefing);

        briefing.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-briefing-header")
        });

        _battleTacticLine = new Label();
        briefing.AddChild(_battleTacticLine);

        _battleTacticDescriptionLine = new RichTextLabel
        {
            VerticalExpand = true
        };
        briefing.AddChild(_battleTacticDescriptionLine);

        briefing.AddChild(new HSeparator());

        _reinforcementStatusLine = new Label();
        _focusLine = new Label();
        briefing.AddChild(_reinforcementStatusLine);
        briefing.AddChild(_focusLine);
        briefing.AddChild(new Control
        {
            VerticalExpand = true
        });

        briefing.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-actions-sketch-note")
        });

        var actionPanel = new PanelContainer
        {
            MinWidth = 330,
            VerticalExpand = true,
            PanelOverride = _actionStyle = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#252A3A"),
                BorderColor = Color.FromHex("#56607A"),
                BorderThickness = new Thickness(1)
            }
        };
        body.AddChild(actionPanel);

        var actionScroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true
        };
        actionPanel.AddChild(actionScroll);

        var action = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(8),
            HorizontalExpand = true,
            VerticalExpand = true
        };
        actionScroll.AddChild(action);

        action.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-actions-header")
        });
        _upgradeLine = new Label
        {
            ClipText = true,
            HorizontalExpand = true
        };
        _reinforcementLine = new Label
        {
            ClipText = true,
            HorizontalExpand = true
        };
        action.AddChild(_upgradeLine);
        _upgradeButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-upgrade-button")
        };
        _upgradeButton.OnPressed += _ => OnUpgradePressed?.Invoke();
        action.AddChild(_upgradeButton);

        action.AddChild(new HSeparator());
        action.AddChild(_reinforcementLine);
        _reinforcementButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-reinforcement-button")
        };
        _reinforcementButton.OnPressed += _ => OnReinforcementPressed?.Invoke();
        action.AddChild(_reinforcementButton);
        action.AddChild(new HSeparator());

        action.AddChild(new Label
        {
            Text = Loc.GetString("wh40k-command-node-actions-windows-header")
        });

        _teamCompositionButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-team-composition-open-button")
        };
        _teamCompositionButton.OnPressed += _ => OnTeamCompositionPressed?.Invoke();
        action.AddChild(_teamCompositionButton);

        _upgradeSketchButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-upgrade-sketch-open-button")
        };
        _upgradeSketchButton.OnPressed += _ => OnUpgradeSketchPressed?.Invoke();
        action.AddChild(_upgradeSketchButton);

        _missionBoardButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-mission-board-open-button")
        };
        _missionBoardButton.OnPressed += _ => OnMissionBoardPressed?.Invoke();
        action.AddChild(_missionBoardButton);

        _tacticalBonusesButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-tactical-bonuses-open-button")
        };
        _tacticalBonusesButton.OnPressed += _ => OnTacticalBonusesPressed?.Invoke();
        action.AddChild(_tacticalBonusesButton);

        _doctrineButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-doctrine-open-button")
        };
        _doctrineButton.OnPressed += _ => OnDoctrinePressed?.Invoke();
        action.AddChild(_doctrineButton);

        _battleTacticButton = new Button
        {
            HorizontalExpand = true,
            Text = Loc.GetString("wh40k-command-node-battle-tactic-open-button")
        };
        _battleTacticButton.OnPressed += _ => OnBattleTacticPressed?.Invoke();
        action.AddChild(_battleTacticButton);
    }

    public void UpdateState(WH40KCommandNodeBoundUserInterfaceState state)
    {
        ApplyFactionTheme(state.TeamId);

        _teamLine.Text = Loc.GetString("wh40k-command-node-team", ("team", state.TeamName));
        _phaseLine.Text = Loc.GetString("wh40k-command-node-phase", ("phase", Loc.GetString(GetPhaseKey(state.Phase))));
        _levelLine.Text = Loc.GetString("wh40k-command-node-level", ("level", state.BaseLevel));
        _pointsLine.Text = Loc.GetString("wh40k-command-node-points", ("points", state.FrontPoints));
        _developmentPointsLine.Text = Loc.GetString("wh40k-command-node-development-points", ("points", state.CommandPoints));
        _nextLine.Text = state.PointsToNextLevel is { } toNext
            ? Loc.GetString("wh40k-command-node-next", ("points", toNext))
            : Loc.GetString("wh40k-command-node-next-max");

        var (progress, segmentCurrent, segmentTotal) = CalculateBaseProgress(state);
        _baseProgressBar.Value = progress;
        _baseProgressLine.Text = state.PointsToNextLevel is { } pointsLeft
            ? Loc.GetString("wh40k-command-node-base-progress-current",
                ("current", segmentCurrent),
                ("total", segmentTotal),
                ("left", pointsLeft))
            : Loc.GetString("wh40k-command-node-base-progress-max");

        var thresholds = state.LevelThresholds.Length == 0
            ? "-"
            : string.Join(", ", state.LevelThresholds.Select(x => x.ToString()));
        _thresholdsLine.Text = Loc.GetString("wh40k-command-node-thresholds", ("thresholds", thresholds));

        _upgradeLine.Text = Loc.GetString("wh40k-command-node-upgrade-line",
            ("level", state.UpgradeLevel),
            ("cost", state.UpgradeCost));
        _reinforcementLine.Text = Loc.GetString("wh40k-command-node-reinforcement-line",
            ("cost", state.ReinforcementCost),
            ("cooldown", state.ReinforcementCooldownSeconds));

        UpdateBriefing(state);

        _upgradeButton.Disabled = state.UpgradeCost <= 0 || state.CommandPoints < state.UpgradeCost;
        _reinforcementButton.Disabled = state.Phase != WH40KBattlePhase.Assault ||
                                        state.ReinforcementCost <= 0 ||
                                        state.CommandPoints < state.ReinforcementCost ||
                                        state.ReinforcementCooldownSeconds > 0;
    }

    public void SetBattleTacticPreview(string tacticName, string tacticDescription)
    {
        _activeBattleTacticName = tacticName;
        _activeBattleTacticDescription = tacticDescription;
        ApplyBattleTacticPreviewText();
    }

    private void ApplyFactionTheme(string teamId)
    {
        var accent = WH40KTeamIdentityClientResolver.ResolveAccentColor(teamId, ImperiumColor);
        _headerStyle.BorderColor = accent;
        _infoStyle.BorderColor = accent;
        _actionStyle.BorderColor = accent;
        _progressForegroundStyle.BackgroundColor = accent;
        _teamLine.ModulateSelfOverride = accent;
        _phaseLine.ModulateSelfOverride = accent;
        _battleTacticLine.ModulateSelfOverride = accent;
    }

    private static (float Value, int SegmentCurrent, int SegmentTotal) CalculateBaseProgress(
        WH40KCommandNodeBoundUserInterfaceState state)
    {
        if (state.PointsToNextLevel == null)
            return (1f, 1, 1);

        if (state.LevelThresholds.Length == 0)
        {
            var total = Math.Max(1, state.PointsToNextLevel.Value);
            return (0f, 0, total);
        }

        var level = Math.Max(1, state.BaseLevel);
        var thresholds = state.LevelThresholds;

        var segmentStart = 0;
        if (level > 1)
        {
            var previousThresholdIndex = Math.Clamp(level - 2, 0, thresholds.Length - 1);
            segmentStart = thresholds[previousThresholdIndex];
        }

        var currentThresholdIndex = Math.Clamp(level - 1, 0, thresholds.Length - 1);
        var segmentEnd = thresholds[currentThresholdIndex];

        if (segmentEnd <= segmentStart)
        {
            var fallbackTotal = Math.Max(1, state.PointsToNextLevel.Value + Math.Max(0, state.FrontPoints - segmentStart));
            var fallbackCurrent = Math.Clamp(fallbackTotal - state.PointsToNextLevel.Value, 0, fallbackTotal);
            return ((float) fallbackCurrent / fallbackTotal, fallbackCurrent, fallbackTotal);
        }

        var segmentTotal = segmentEnd - segmentStart;
        var segmentCurrent = Math.Clamp(state.FrontPoints - segmentStart, 0, segmentTotal);
        var value = Math.Clamp((float) segmentCurrent / segmentTotal, 0f, 1f);
        return (value, segmentCurrent, segmentTotal);
    }

    private string GetPhaseKey(WH40KBattlePhase phase)
    {
        return phase switch
        {
            WH40KBattlePhase.Preparation => "wh40k-phase-preparation-name",
            WH40KBattlePhase.Assault => "wh40k-phase-assault-name",
            WH40KBattlePhase.Apocalypse => "wh40k-phase-apocalypse-name",
            _ => "wh40k-phase-preparation-name"
        };
    }

    private void UpdateBriefing(WH40KCommandNodeBoundUserInterfaceState state)
    {
        if (string.IsNullOrWhiteSpace(_activeBattleTacticName))
        {
            _activeBattleTacticName = Loc.GetString("wh40k-command-node-battle-tactic-default-name");
            _activeBattleTacticDescription = Loc.GetString("wh40k-command-node-battle-tactic-default-description");
        }

        ApplyBattleTacticPreviewText();

        _reinforcementStatusLine.Text = state.ReinforcementCooldownSeconds > 0
            ? Loc.GetString("wh40k-command-node-reinforcement-readiness-cooldown",
                ("seconds", state.ReinforcementCooldownSeconds))
            : state.Phase < WH40KBattlePhase.Assault
                ? Loc.GetString("wh40k-command-node-reinforcement-readiness-phase-lock")
                : state.Phase >= WH40KBattlePhase.Apocalypse
                    ? Loc.GetString("wh40k-command-node-reinforcement-readiness-apocalypse-lock")
                    : state.CommandPoints < state.ReinforcementCost
                        ? Loc.GetString("wh40k-command-node-reinforcement-readiness-budget-lock",
                            ("cost", state.ReinforcementCost))
                        : Loc.GetString("wh40k-command-node-reinforcement-readiness-ready");

        var upgradeHint = state.CommandPoints >= state.UpgradeCost
            ? Loc.GetString("wh40k-command-node-focus-upgrade-ready", ("cost", state.UpgradeCost))
            : Loc.GetString("wh40k-command-node-focus-upgrade-wait", ("cost", state.UpgradeCost));

        _focusLine.Text = state.PointsToNextLevel is { } pointsLeft
            ? Loc.GetString("wh40k-command-node-focus-next-level", ("points", pointsLeft), ("upgrade", upgradeHint))
            : Loc.GetString("wh40k-command-node-focus-max-level", ("upgrade", upgradeHint));
    }

    private void ApplyBattleTacticPreviewText()
    {
        _battleTacticLine.Text = Loc.GetString("wh40k-command-node-battle-tactic-active", ("tactic", _activeBattleTacticName));
        _battleTacticDescriptionLine.SetMessage(
            FormattedMessage.FromMarkupPermissive(FormattedMessage.EscapeText(_activeBattleTacticDescription)),
            tagsAllowed: null,
            defaultColor: Color.White);
    }
}
