using System;
using System.Linq;
using Content.Client.Eye;
using Content.Shared._WH40K.Sentry.Laptop;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Localization;

namespace Content.Client._WH40K.Sentry.Laptop;

[UsedImplicitly]
public sealed class WH40KSentryLaptopBoundUserInterface : BoundUserInterface
{
    private readonly EyeLerpingSystem _eyeLerping = default!;

    private WH40KSentryLaptopWindow? _window;
    private WH40KSentryLaptopBuiState? _latestState;
    private NetEntity? _selectedCameraTurret;
    private EntityUid? _currentCameraTurret;

    public WH40KSentryLaptopBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _eyeLerping = EntMan.System<EyeLerpingSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WH40KSentryLaptopWindow>();
        _window.BindActions(
            onRefresh: () => SendMessage(new WH40KSentryLaptopRefreshBuiMsg()),
            onUnlinkAll: () => SendMessage(new WH40KSentryLaptopUnlinkAllBuiMsg()),
            onSetPowerAll: enabled => SendMessage(new WH40KSentryLaptopSetPowerAllBuiMsg(enabled)),
            onResetTargetingAll: () => SendMessage(new WH40KSentryLaptopResetTargetingAllBuiMsg()),
            onSetIffTeamAll: (team, allowed) => SendMessage(new WH40KSentryLaptopSetIffTeamAllBuiMsg(team, allowed)),
            onUnlink: turret => SendMessage(new WH40KSentryLaptopUnlinkBuiMsg(turret)),
            onTogglePower: turret => SendMessage(new WH40KSentryLaptopTogglePowerBuiMsg(turret)),
            onResetTargeting: turret => SendMessage(new WH40KSentryLaptopResetTargetingBuiMsg(turret)),
            onSetIffTeam: (turret, team, allowed) => SendMessage(new WH40KSentryLaptopSetIffTeamBuiMsg(turret, team, allowed)),
            onViewCamera: OnViewCameraRequested,
            onCloseCamera: OnCloseCameraRequested);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KSentryLaptopBuiState cast)
            return;

        _latestState = cast;
        _window.ApplyState(cast);

        if (_selectedCameraTurret is { } selected &&
            !cast.LinkedTurrets.Any(t => t.Turret == selected))
        {
            _selectedCameraTurret = null;
            ClearCameraTarget();
            _window.SetCameraState(false, Loc.GetString("wh40k-sentry-laptop-ui-camera-title-idle"));
            return;
        }

        UpdateCameraPreview();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ClearCameraTarget();
    }

    private void OnViewCameraRequested(NetEntity turret)
    {
        _selectedCameraTurret = turret;
        SendMessage(new WH40KSentryLaptopViewCameraBuiMsg(turret));
        UpdateCameraPreview();
    }

    private void OnCloseCameraRequested()
    {
        _selectedCameraTurret = null;
        SendMessage(new WH40KSentryLaptopCloseCameraBuiMsg());
        ClearCameraTarget();

        if (_window != null)
            _window.SetCameraState(false, Loc.GetString("wh40k-sentry-laptop-ui-camera-title-idle"));
    }

    private void UpdateCameraPreview()
    {
        if (_window == null)
            return;

        if (_selectedCameraTurret is not { } selected)
        {
            ClearCameraTarget();
            _window.SetCameraState(false, Loc.GetString("wh40k-sentry-laptop-ui-camera-title-idle"));
            return;
        }

        var turretInfo = _latestState?.LinkedTurrets.FirstOrDefault(t => t.Turret == selected);
        if (turretInfo == null)
        {
            ClearCameraTarget();
            _window.CameraViewport.Eye = null;
            _window.SetCameraState(true, Loc.GetString("wh40k-sentry-laptop-ui-camera-title-unavailable"));
            return;
        }

        if (!EntMan.TryGetEntity(selected, out var turretUid))
        {
            ClearCameraTarget();
            _window.CameraViewport.Eye = null;
            _window.SetCameraState(true,
                Loc.GetString("wh40k-sentry-laptop-ui-camera-title-no-feed", ("turret", turretInfo.Name)));
            return;
        }

        SwitchCameraTarget(turretUid.Value);

        if (EntMan.TryGetComponent<EyeComponent>(turretUid.Value, out var eyeComp) && eyeComp.Eye != null)
        {
            _window.CameraViewport.Eye = eyeComp.Eye;
            _window.SetCameraState(true,
                Loc.GetString("wh40k-sentry-laptop-ui-camera-title-active", ("turret", turretInfo.Name)));
            return;
        }

        _window.CameraViewport.Eye = null;
        _window.SetCameraState(true,
            Loc.GetString("wh40k-sentry-laptop-ui-camera-title-no-feed", ("turret", turretInfo.Name)));
    }

    private void SwitchCameraTarget(EntityUid turretUid)
    {
        if (_currentCameraTurret == turretUid)
            return;

        if (_currentCameraTurret is { } previous)
            _eyeLerping.RemoveEye(previous);

        _currentCameraTurret = turretUid;
        _eyeLerping.AddEye(turretUid);
    }

    private void ClearCameraTarget()
    {
        if (_currentCameraTurret is not { } turret)
            return;

        _eyeLerping.RemoveEye(turret);
        _currentCameraTurret = null;
    }
}
