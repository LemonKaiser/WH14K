using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Localization;
using Content.Client.Viewport;
using Content.Shared._WH40K.Sentry.Laptop;
using Content.Shared.Turrets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Sentry.Laptop;

public sealed class WH40KSentryLaptopWindow : DefaultWindow, ILocalizedControl
{
    private readonly Label _summaryLabel;
    private readonly Button _refreshButton;
    private readonly Button _unlinkAllButton;
    private readonly Button _powerAllOnButton;
    private readonly Button _powerAllOffButton;
    private readonly Button _resetAllButton;
    private readonly Label _globalIffTitleLabel;
    private readonly BoxContainer _globalIffContainer;
    private readonly BoxContainer _turretList;
    private readonly Label _alertsTitleLabel;
    private readonly BoxContainer _alertList;
    private readonly PanelContainer _cameraPanel;
    private readonly Label _cameraTitle;
    private readonly Button _closeCameraButton;
    private WH40KSentryLaptopBuiState? _latestState;
    private bool _cameraEnabled;
    private string? _cameraTitleText;

    private Action? _onRefresh;
    private Action? _onUnlinkAll;
    private Action<bool>? _onSetPowerAll;
    private Action? _onResetTargetingAll;
    private Action<string, bool>? _onSetIffTeamAll;

    private Action<NetEntity>? _onUnlink;
    private Action<NetEntity>? _onTogglePower;
    private Action<NetEntity>? _onResetTargeting;
    private Action<NetEntity, string, bool>? _onSetIffTeam;
    private Action<NetEntity>? _onViewCamera;
    private Action? _onCloseCamera;

    public ScalingViewport CameraViewport { get; }

    public WH40KSentryLaptopWindow()
    {
        MinSize = SetSize = new Vector2(1220, 760);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var toolbar = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };

        _summaryLabel = new Label
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };

        _refreshButton = new Button
        {
            Text = Loc.GetString("wh40k-sentry-laptop-ui-refresh"),
        };
        _unlinkAllButton = new Button
        {
            Text = Loc.GetString("wh40k-sentry-laptop-ui-unlink-all"),
        };

        _refreshButton.OnPressed += _ => _onRefresh?.Invoke();
        _unlinkAllButton.OnPressed += _ => _onUnlinkAll?.Invoke();

        toolbar.AddChild(_summaryLabel);
        toolbar.AddChild(_refreshButton);
        toolbar.AddChild(_unlinkAllButton);
        root.AddChild(toolbar);

        var globalPanel = new PanelContainer
        {
            HorizontalExpand = true,
        };

        var globalRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6),
            HorizontalExpand = true,
        };

        var globalButtons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };

        _powerAllOnButton = new Button { Text = Loc.GetString("wh40k-sentry-laptop-ui-power-all-on") };
        _powerAllOffButton = new Button { Text = Loc.GetString("wh40k-sentry-laptop-ui-power-all-off") };
        _resetAllButton = new Button { Text = Loc.GetString("wh40k-sentry-laptop-ui-reset-all-targeting") };

        _powerAllOnButton.OnPressed += _ => _onSetPowerAll?.Invoke(true);
        _powerAllOffButton.OnPressed += _ => _onSetPowerAll?.Invoke(false);
        _resetAllButton.OnPressed += _ => _onResetTargetingAll?.Invoke();

        globalButtons.AddChild(_powerAllOnButton);
        globalButtons.AddChild(_powerAllOffButton);
        globalButtons.AddChild(_resetAllButton);
        globalRoot.AddChild(globalButtons);

        _globalIffTitleLabel = new Label
        {
            Text = Loc.GetString("wh40k-sentry-laptop-ui-global-iff"),
            Modulate = Color.LightGray,
        };
        globalRoot.AddChild(_globalIffTitleLabel);

        _globalIffContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };

        globalRoot.AddChild(_globalIffContainer);
        globalPanel.AddChild(globalRoot);
        root.AddChild(globalPanel);

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var turretScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1.7f,
        };

        _turretList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        turretScroll.AddChild(_turretList);
        content.AddChild(turretScroll);

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            MinWidth = 400,
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1.1f,
        };

        var alertPanel = new PanelContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 1f,
        };

        var alertRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _alertsTitleLabel = new Label { Text = Loc.GetString("wh40k-sentry-laptop-ui-alerts-title") };
        alertRoot.AddChild(_alertsTitleLabel);

        var alertScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _alertList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true,
        };
        alertScroll.AddChild(_alertList);
        alertRoot.AddChild(alertScroll);
        alertPanel.AddChild(alertRoot);
        side.AddChild(alertPanel);

        _cameraPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            SizeFlagsStretchRatio = 1.2f,
            Visible = false,
        };

        var cameraRoot = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var cameraHeader = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };

        _cameraTitle = new Label
        {
            Text = Loc.GetString("wh40k-sentry-laptop-ui-camera-title-idle"),
            HorizontalExpand = true,
            ClipText = true,
        };

        _closeCameraButton = new Button
        {
            Text = Loc.GetString("wh40k-sentry-laptop-ui-close-camera"),
        };
        _closeCameraButton.OnPressed += _ => _onCloseCamera?.Invoke();

        cameraHeader.AddChild(_cameraTitle);
        cameraHeader.AddChild(_closeCameraButton);
        cameraRoot.AddChild(cameraHeader);

        CameraViewport = new ScalingViewport
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinSize = new Vector2(340, 240),
        };
        cameraRoot.AddChild(CameraViewport);
        _cameraPanel.AddChild(cameraRoot);
        side.AddChild(_cameraPanel);

        content.AddChild(side);
        root.AddChild(content);
        ContentsContainer.AddChild(root);
        Relocalize();
    }

    public void Relocalize()
    {
        Title = Loc.GetString("wh40k-sentry-laptop-ui-title");
        _refreshButton.Text = Loc.GetString("wh40k-sentry-laptop-ui-refresh");
        _unlinkAllButton.Text = Loc.GetString("wh40k-sentry-laptop-ui-unlink-all");
        _powerAllOnButton.Text = Loc.GetString("wh40k-sentry-laptop-ui-power-all-on");
        _powerAllOffButton.Text = Loc.GetString("wh40k-sentry-laptop-ui-power-all-off");
        _resetAllButton.Text = Loc.GetString("wh40k-sentry-laptop-ui-reset-all-targeting");
        _globalIffTitleLabel.Text = Loc.GetString("wh40k-sentry-laptop-ui-global-iff");
        _alertsTitleLabel.Text = Loc.GetString("wh40k-sentry-laptop-ui-alerts-title");
        _closeCameraButton.Text = Loc.GetString("wh40k-sentry-laptop-ui-close-camera");
        _cameraTitle.Text = _cameraEnabled
            ? _cameraTitleText ?? Loc.GetString("wh40k-sentry-laptop-ui-camera-title-unavailable")
            : Loc.GetString("wh40k-sentry-laptop-ui-camera-title-idle");

        if (_latestState != null)
            ApplyState(_latestState);
    }

    public void BindActions(
        Action onRefresh,
        Action onUnlinkAll,
        Action<bool> onSetPowerAll,
        Action onResetTargetingAll,
        Action<string, bool> onSetIffTeamAll,
        Action<NetEntity> onUnlink,
        Action<NetEntity> onTogglePower,
        Action<NetEntity> onResetTargeting,
        Action<NetEntity, string, bool> onSetIffTeam,
        Action<NetEntity> onViewCamera,
        Action onCloseCamera)
    {
        _onRefresh = onRefresh;
        _onUnlinkAll = onUnlinkAll;
        _onSetPowerAll = onSetPowerAll;
        _onResetTargetingAll = onResetTargetingAll;
        _onSetIffTeamAll = onSetIffTeamAll;
        _onUnlink = onUnlink;
        _onTogglePower = onTogglePower;
        _onResetTargeting = onResetTargeting;
        _onSetIffTeam = onSetIffTeam;
        _onViewCamera = onViewCamera;
        _onCloseCamera = onCloseCamera;
    }

    public void ApplyState(WH40KSentryLaptopBuiState state)
    {
        _latestState = state;
        _summaryLabel.Text = Loc.GetString(
            "wh40k-sentry-laptop-ui-summary",
            ("linked", state.LinkedCount),
            ("max", state.MaxLinkedCount));
        _unlinkAllButton.Disabled = state.LinkedCount == 0;

        _globalIffContainer.DisposeAllChildren();
        foreach (var teamId in state.IffTeamOptions)
        {
            var allFriendly = state.LinkedTurrets.Count > 0 &&
                              state.LinkedTurrets.All(t => ContainsIgnoreCase(t.FriendlyTeams, teamId));

            var checkbox = new CheckBox
            {
                Text = Loc.GetString("wh40k-sentry-laptop-ui-iff-team-entry", ("team", teamId)),
                Pressed = allFriendly,
                Disabled = state.LinkedTurrets.Count == 0,
            };
            checkbox.OnToggled += ev => _onSetIffTeamAll?.Invoke(teamId, ev.Pressed);
            _globalIffContainer.AddChild(checkbox);
        }

        _turretList.DisposeAllChildren();
        if (state.LinkedTurrets.Count == 0)
        {
            _turretList.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-sentry-laptop-ui-empty"),
                Modulate = Color.LightGray,
            });
        }
        else
        {
            foreach (var turret in state.LinkedTurrets)
            {
                _turretList.AddChild(BuildTurretRow(turret, state.IffTeamOptions));
            }
        }

        _alertList.DisposeAllChildren();
        if (state.Alerts.Count == 0)
        {
            _alertList.AddChild(new Label
            {
                Text = Loc.GetString("wh40k-sentry-laptop-ui-alerts-empty"),
                Modulate = Color.LightGray,
            });
        }
        else
        {
            foreach (var alert in state.Alerts)
            {
                var color = alert.Severity switch
                {
                    WH40KSentryLaptopAlertSeverity.Critical => Color.IndianRed,
                    WH40KSentryLaptopAlertSeverity.Warning => Color.Goldenrod,
                    _ => Color.LightBlue,
                };

                _alertList.AddChild(new Label
                {
                    Text = Loc.GetString(
                        "wh40k-sentry-laptop-ui-alert-entry",
                        ("message", alert.Message),
                        ("age", alert.AgeSeconds)),
                    Modulate = color,
                });
            }
        }
    }

    public void SetCameraState(bool enabled, string title)
    {
        _cameraEnabled = enabled;
        _cameraTitleText = title;
        _cameraPanel.Visible = enabled;
        _cameraTitle.Text = title;

        if (!enabled)
            CameraViewport.Eye = null;
    }

    private PanelContainer BuildTurretRow(WH40KSentryLaptopTurretInfo turret, IReadOnlyList<string> iffTeamOptions)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 2),
        };

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true,
            Margin = new Thickness(6),
        };

        var header = new Label
        {
            Text = $"{turret.Name} [{GetStateText(turret.State)}]",
            HorizontalExpand = true,
            ClipText = true,
        };

        var details = new Label
        {
            Text = Loc.GetString(
                "wh40k-sentry-laptop-ui-row-details-extended",
                ("ammo", turret.Ammo),
                ("capacity", turret.AmmoCapacity),
                ("team", string.IsNullOrWhiteSpace(turret.TeamId) ? Loc.GetString("wh40k-sentry-laptop-ui-team-unknown") : turret.TeamId),
                ("power", turret.PowerEnabled
                    ? Loc.GetString("wh40k-sentry-laptop-ui-power-on-short")
                    : Loc.GetString("wh40k-sentry-laptop-ui-power-off-short"))),
            Modulate = turret.Broken ? Color.IndianRed : Color.LightGray,
        };

        var actions = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };

        var unlinkButton = new Button { Text = Loc.GetString("wh40k-sentry-laptop-ui-unlink") };
        unlinkButton.OnPressed += _ => _onUnlink?.Invoke(turret.Turret);
        actions.AddChild(unlinkButton);

        var powerButton = new Button
        {
            Text = turret.PowerEnabled
                ? Loc.GetString("wh40k-sentry-laptop-ui-power-off")
                : Loc.GetString("wh40k-sentry-laptop-ui-power-on"),
        };
        powerButton.OnPressed += _ => _onTogglePower?.Invoke(turret.Turret);
        actions.AddChild(powerButton);

        var resetButton = new Button { Text = Loc.GetString("wh40k-sentry-laptop-ui-reset-targeting") };
        resetButton.OnPressed += _ => _onResetTargeting?.Invoke(turret.Turret);
        actions.AddChild(resetButton);

        var cameraButton = new Button { Text = Loc.GetString("wh40k-sentry-laptop-ui-view-camera") };
        cameraButton.OnPressed += _ => _onViewCamera?.Invoke(turret.Turret);
        actions.AddChild(cameraButton);

        var iffRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };

        foreach (var teamId in iffTeamOptions)
        {
            var isFriendly = ContainsIgnoreCase(turret.FriendlyTeams, teamId);
            var checkbox = new CheckBox
            {
                Text = teamId,
                Pressed = isFriendly,
            };
            checkbox.OnToggled += ev => _onSetIffTeam?.Invoke(turret.Turret, teamId, ev.Pressed);
            iffRow.AddChild(checkbox);
        }

        row.AddChild(header);
        row.AddChild(details);
        row.AddChild(actions);
        row.AddChild(iffRow);
        panel.AddChild(row);
        return panel;
    }

    private static string GetStateText(DeployableTurretState state)
    {
        return state switch
        {
            DeployableTurretState.Deployed => Loc.GetString("wh40k-sentry-laptop-ui-state-deployed"),
            DeployableTurretState.Retracted => Loc.GetString("wh40k-sentry-laptop-ui-state-retracted"),
            DeployableTurretState.Deploying => Loc.GetString("wh40k-sentry-laptop-ui-state-deploying"),
            DeployableTurretState.Retracting => Loc.GetString("wh40k-sentry-laptop-ui-state-retracting"),
            DeployableTurretState.Firing => Loc.GetString("wh40k-sentry-laptop-ui-state-firing"),
            DeployableTurretState.Disabled => Loc.GetString("wh40k-sentry-laptop-ui-state-disabled"),
            DeployableTurretState.Broken => Loc.GetString("wh40k-sentry-laptop-ui-state-broken"),
            _ => state.ToString(),
        };
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string expected)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
