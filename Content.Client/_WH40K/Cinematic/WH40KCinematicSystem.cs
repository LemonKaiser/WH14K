using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared._WH40K.Cinematic;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Noise;
using Robust.Shared.Random;

namespace Content.Client._WH40K.Cinematic;

public sealed class WH40KCinematicSystem : EntitySystem
{
    private const float ShakeBaseDurationSeconds = 0.70f;
    private const float ShakeDurationScaleSeconds = 0.45f;
    private const float ShakeMaxDurationSeconds = 1.40f;
    private const float ShakeAttackFraction = 0.12f;
    private const float ShakeReleaseExponent = 1.60f;
    private const float ShakePositionScale = 0.18f;
    private const float ShakeMaxPositionAmplitude = 0.30f;
    private const float ShakeRotationScaleDegrees = 1.15f;
    private const float ShakeMaxRotationDegrees = 1.65f;
    private const float ShakeBaseFrequency = 10f;
    private const float ShakeFrequencyScale = 8f;
    private const float ShakeMaxFrequency = 18f;
    private const float ShakeNoiseFrequency = 1f;
    private const float OverlayShakeNoiseTimeOffset = 137.5f;

    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IClientNetManager _net = default!;

    public WH40KCinematicNetState? ActiveState { get; private set; }
    public WH40KCinematicStoppedEvent? LastStoppedEvent { get; private set; }
    public bool IsCinematicModeActive => _cinematicModeActive;

    private FixedEye? _cinematicEye;
    private WH40KCinematicOverlay? _overlay;
    private ClientUiRestoreState? _uiRestoreState;
    private CinematicBlendState? _blendState;
    private IEye? _restoreEye;
    private readonly FastNoiseLite _shakePositionXNoise = new();
    private readonly FastNoiseLite _shakePositionYNoise = new();
    private readonly FastNoiseLite _shakeRotationNoise = new();
    private Vector2 _shakeOffset;
    private float _shakeElapsed;
    private float _shakeDuration;
    private float _shakeFrequency;
    private float _shakePositionAmplitude;
    private float _shakeRotationAmplitude;
    private Vector2 _overlayShakeOffset;
    private float _overlayShakeElapsed;
    private float _overlayShakeDuration;
    private float _overlayShakeFrequency;
    private float _overlayShakePositionAmplitude;
    private float _overlayShakeRotationAmplitude;
    private float _baseRotationDegrees;
    private float _screenShakeIntensity = 1f;
    private bool _restoreDrawFov = true;
    private bool _restoreDrawLight = true;
    private string? _appliedShotKey;
    private bool _cinematicModeActive;
    private bool _audienceShotShakeActive;
    private int _latestStoppedRunSerial;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WH40KCinematicStateEvent>(OnStateEvent);
        SubscribeNetworkEvent<WH40KCinematicStoppedEvent>(OnStoppedEvent);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        _net.Disconnect += OnClientDisconnect;

        InitializeShakeNoise(_shakePositionXNoise);
        InitializeShakeNoise(_shakePositionYNoise);
        InitializeShakeNoise(_shakeRotationNoise);
        ReseedShakeNoise();

        Subs.CVar(_cfg, CCVars.ScreenShakeIntensity, value => _screenShakeIntensity = value, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_cinematicModeActive)
            return;

        ApplyCinematicUIMode();

        if (_cinematicEye == null)
            return;

        if (!ReferenceEquals(_eyeManager.CurrentEye, _cinematicEye))
            _eyeManager.CurrentEye = _cinematicEye;

        UpdateBlend(frameTime);
        UpdateShake(frameTime);
    }

    private void OnStateEvent(WH40KCinematicStateEvent ev, EntitySessionEventArgs args)
    {
        if (ev.State.RunSerial <= _latestStoppedRunSerial)
            return;

        if (ActiveState != null && ev.State.RunSerial < ActiveState.RunSerial)
            return;

        ActiveState = ev.State;
        LastStoppedEvent = null;

        var shouldEnterCinematicMode = ev.State.AudienceLocked || ev.State.ActiveShot != null;
        if (!shouldEnterCinematicMode)
        {
            _appliedShotKey = null;
            ExitCinematicMode();
            return;
        }

        EnterCinematicMode();
        ApplyAudienceShotShake(ev.State);

        if (ev.State.ActiveShot == null)
            return;

        EnsureCinematicEye();
        var shotKey = $"{ev.State.CinematicId}:{ev.State.ActiveStepIndex}";
        if (shotKey == _appliedShotKey)
        {
            if (_cinematicEye != null && !ReferenceEquals(_eyeManager.CurrentEye, _cinematicEye))
                _eyeManager.CurrentEye = _cinematicEye;
            return;
        }

        ApplyShot(ev.State.ActiveShot);
        if (_cinematicEye != null && !ReferenceEquals(_eyeManager.CurrentEye, _cinematicEye))
            _eyeManager.CurrentEye = _cinematicEye;
        _appliedShotKey = shotKey;
    }

    private void OnStoppedEvent(WH40KCinematicStoppedEvent ev, EntitySessionEventArgs args)
    {
        if (ev.RunSerial < _latestStoppedRunSerial)
            return;

        _latestStoppedRunSerial = Math.Max(_latestStoppedRunSerial, ev.RunSerial);

        if (ActiveState != null && ActiveState.RunSerial > ev.RunSerial)
            return;

        ActiveState = null;
        LastStoppedEvent = ev;
        _appliedShotKey = null;
        ExitCinematicMode();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev, EntitySessionEventArgs args)
    {
        ResetRuntimeState();
    }

    private void OnClientDisconnect(object? sender, NetDisconnectedArgs args)
    {
        ResetRuntimeState();
    }

    private void ResetRuntimeState()
    {
        ActiveState = null;
        LastStoppedEvent = null;
        _appliedShotKey = null;
        _latestStoppedRunSerial = 0;
        ExitCinematicMode();
    }

    private void EnterCinematicMode()
    {
        if (!_cinematicModeActive)
        {
            _cinematicModeActive = true;
            _restoreEye = _eyeManager.CurrentEye;
            _uiRestoreState = CaptureUiRestoreState();

            EnsureOverlay();
        }

        ApplyCinematicUIMode();
    }

    private void EnsureCinematicEye()
    {
        if (_cinematicEye != null)
            return;

        _cinematicEye = new FixedEye();
        if (_restoreEye != null)
        {
            _cinematicEye.Position = _restoreEye.Position;
            _cinematicEye.Zoom = _restoreEye.Zoom;
            _cinematicEye.Rotation = _restoreEye.Rotation;
            _cinematicEye.DrawFov = _restoreEye.DrawFov;
            _cinematicEye.DrawLight = _restoreEye.DrawLight;
            _baseRotationDegrees = (float) _restoreEye.Rotation.Degrees;
            _restoreDrawFov = _restoreEye.DrawFov;
            _restoreDrawLight = _restoreEye.DrawLight;
        }
    }

    private void ExitCinematicMode()
    {
        if (!_cinematicModeActive)
            return;

        _cinematicModeActive = false;
        _blendState = null;
        _shakeOffset = Vector2.Zero;
        _shakeElapsed = 0f;
        _shakeDuration = 0f;
        _shakeFrequency = ShakeBaseFrequency;
        _shakePositionAmplitude = 0f;
        _shakeRotationAmplitude = 0f;
        ResetAudienceShotShake();
        _baseRotationDegrees = 0f;

        if (_cinematicEye != null)
        {
            _cinematicEye.Offset = Vector2.Zero;
            _cinematicEye = null;
        }

        if (_restoreEye != null)
            _eyeManager.CurrentEye = _restoreEye;
        else
            _eyeManager.ClearCurrentEye();

        if (_uiRestoreState != null)
            RestoreUiState(_uiRestoreState);

        if (_overlay != null)
            _overlay.Visible = false;

        _uiRestoreState = null;
        _restoreEye = null;
        _restoreDrawFov = true;
        _restoreDrawLight = true;
    }

    private void ApplyShot(WH40KCinematicShotNetState shot)
    {
        if (_cinematicEye == null)
            return;

        var entityCoordinates = GetCoordinates(shot.Coordinates);
        var mapCoordinates = _transform.ToMapCoordinates(entityCoordinates);
        var targetPose = new CinematicPose(mapCoordinates, shot.Zoom, shot.RotationDegrees);
        var currentPose = GetCurrentPose();

        if (shot.TransitionMode == WH40KCinematicCameraTransitionMode.Blend &&
            shot.BlendDurationSeconds > 0f &&
            currentPose.Coordinates.MapId != MapId.Nullspace &&
            currentPose.Coordinates.MapId == targetPose.Coordinates.MapId)
        {
            _blendState = new CinematicBlendState(
                currentPose,
                targetPose,
                TimeSpan.FromSeconds(shot.BlendDurationSeconds),
                shot.TransitionEasing);
        }
        else
        {
            _blendState = null;
            ApplyPose(targetPose);
        }

        ApplyShotVisualOverrides(shot);

        if (shot.ShakeIntensity > 0f && _screenShakeIntensity > 0f)
            StartShakeImpulse(shot.ShakeIntensity);
    }

    private void UpdateBlend(float frameTime)
    {
        if (_cinematicEye == null || _blendState == null)
            return;

        _blendState.Elapsed += TimeSpan.FromSeconds(frameTime);
        var durationSeconds = Math.Max(0.0001f, (float) _blendState.Duration.TotalSeconds);
        var progress = Math.Clamp((float) _blendState.Elapsed.TotalSeconds / durationSeconds, 0f, 1f);
        var smoothed = ApplyBlendEasing(progress, _blendState.Easing);

        if (_blendState.From.Coordinates.MapId != _blendState.To.Coordinates.MapId)
        {
            ApplyPose(_blendState.To);
            _blendState = null;
            return;
        }

        var from = _blendState.From.Coordinates.Position;
        var to = _blendState.To.Coordinates.Position;
        var pos = Vector2.Lerp(from, to, smoothed);
        var zoom = MathHelper.Lerp(_blendState.From.Zoom, _blendState.To.Zoom, smoothed);
        var rotation = LerpDegrees(_blendState.From.RotationDegrees, _blendState.To.RotationDegrees, smoothed);

        ApplyPose(new CinematicPose(new MapCoordinates(pos, _blendState.To.Coordinates.MapId), zoom, rotation));

        if (progress >= 1f)
            _blendState = null;
    }

    private void UpdateShake(float frameTime)
    {
        if (_cinematicEye == null)
            return;

        var hasMainShake = EvaluateShakeChannel(
            ref _shakeElapsed,
            ref _shakeDuration,
            ref _shakePositionAmplitude,
            ref _shakeRotationAmplitude,
            _shakeFrequency,
            frameTime,
            0f,
            out var mainOffset,
            out var mainRotationOffset);

        var hasOverlayShake = EvaluateShakeChannel(
            ref _overlayShakeElapsed,
            ref _overlayShakeDuration,
            ref _overlayShakePositionAmplitude,
            ref _overlayShakeRotationAmplitude,
            _overlayShakeFrequency,
            frameTime,
            OverlayShakeNoiseTimeOffset,
            out var overlayOffset,
            out var overlayRotationOffset);

        if (!hasMainShake && !hasOverlayShake)
        {
            _shakeOffset = Vector2.Zero;
            _overlayShakeOffset = Vector2.Zero;
            _cinematicEye.Offset = Vector2.Zero;
            _cinematicEye.Rotation = Angle.FromDegrees(_baseRotationDegrees);
            return;
        }

        _shakeOffset = mainOffset;
        _overlayShakeOffset = overlayOffset;
        _cinematicEye.Offset = mainOffset + overlayOffset;
        _cinematicEye.Rotation = Angle.FromDegrees(_baseRotationDegrees + mainRotationOffset + overlayRotationOffset);
    }

    private CinematicPose GetCurrentPose()
    {
        var eye = _eyeManager.CurrentEye;
        return new CinematicPose(
            eye.Position,
            eye.Zoom.X,
            (float) eye.Rotation.Degrees);
    }

    private void ApplyPose(CinematicPose pose)
    {
        if (_cinematicEye == null)
            return;

        _cinematicEye.Position = pose.Coordinates;
        _cinematicEye.Zoom = new Vector2(pose.Zoom, pose.Zoom);
        _baseRotationDegrees = pose.RotationDegrees;
        _cinematicEye.Rotation = Angle.FromDegrees(_baseRotationDegrees);
    }

    private void ApplyShotVisualOverrides(WH40KCinematicShotNetState shot)
    {
        if (_cinematicEye == null)
            return;

        _cinematicEye.DrawFov = shot.DrawFovOverride ?? _restoreDrawFov;
        _cinematicEye.DrawLight = shot.DrawLightOverride ?? _restoreDrawLight;
    }

    private void StartShakeImpulse(float intensity)
    {
        var scaledIntensity = Math.Max(0f, intensity * _screenShakeIntensity);
        if (scaledIntensity <= 0f)
            return;

        var carry = _shakeDuration <= 0.0001f
            ? 0f
            : 1f - Math.Clamp(_shakeElapsed / Math.Max(_shakeDuration, 0.0001f), 0f, 1f);

        var newPositionAmplitude = Math.Clamp(scaledIntensity * ShakePositionScale, 0f, ShakeMaxPositionAmplitude);
        var newRotationAmplitude = Math.Clamp(scaledIntensity * ShakeRotationScaleDegrees, 0f, ShakeMaxRotationDegrees);

        _shakePositionAmplitude = Math.Clamp(Math.Max(_shakePositionAmplitude * carry, newPositionAmplitude), 0f, ShakeMaxPositionAmplitude);
        _shakeRotationAmplitude = Math.Clamp(Math.Max(_shakeRotationAmplitude * carry, newRotationAmplitude), 0f, ShakeMaxRotationDegrees);
        _shakeDuration = Math.Clamp(
            ShakeBaseDurationSeconds + scaledIntensity * ShakeDurationScaleSeconds,
            ShakeBaseDurationSeconds,
            ShakeMaxDurationSeconds);
        _shakeFrequency = Math.Clamp(
            ShakeBaseFrequency + scaledIntensity * ShakeFrequencyScale,
            ShakeBaseFrequency,
            ShakeMaxFrequency);
        _shakeElapsed = 0f;
        ReseedShakeNoise();
    }

    private void StartAudienceShotShakeImpulse(float intensity)
    {
        var scaledIntensity = Math.Max(0f, intensity * _screenShakeIntensity);
        if (scaledIntensity <= 0f)
            return;

        var carry = _overlayShakeDuration <= 0.0001f
            ? 0f
            : 1f - Math.Clamp(_overlayShakeElapsed / Math.Max(_overlayShakeDuration, 0.0001f), 0f, 1f);

        var newPositionAmplitude = Math.Clamp(scaledIntensity * ShakePositionScale, 0f, ShakeMaxPositionAmplitude);
        var newRotationAmplitude = Math.Clamp(scaledIntensity * ShakeRotationScaleDegrees, 0f, ShakeMaxRotationDegrees);

        _overlayShakePositionAmplitude = Math.Clamp(Math.Max(_overlayShakePositionAmplitude * carry, newPositionAmplitude), 0f, ShakeMaxPositionAmplitude);
        _overlayShakeRotationAmplitude = Math.Clamp(Math.Max(_overlayShakeRotationAmplitude * carry, newRotationAmplitude), 0f, ShakeMaxRotationDegrees);
        _overlayShakeDuration = Math.Clamp(
            ShakeBaseDurationSeconds + scaledIntensity * ShakeDurationScaleSeconds,
            ShakeBaseDurationSeconds,
            ShakeMaxDurationSeconds);
        _overlayShakeFrequency = Math.Clamp(
            ShakeBaseFrequency + scaledIntensity * ShakeFrequencyScale,
            ShakeBaseFrequency,
            ShakeMaxFrequency);
        _overlayShakeElapsed = 0f;
        _audienceShotShakeActive = true;
    }

    private static float EvaluateShakeEnvelope(float progress)
    {
        if (progress <= 0f || progress >= 1f)
            return 0f;

        if (progress < ShakeAttackFraction)
        {
            var attackProgress = progress / ShakeAttackFraction;
            return attackProgress * attackProgress * (3f - 2f * attackProgress);
        }

        var releaseProgress = (progress - ShakeAttackFraction) / (1f - ShakeAttackFraction);
        return MathF.Pow(1f - releaseProgress, ShakeReleaseExponent);
    }

    private bool EvaluateShakeChannel(
        ref float elapsed,
        ref float duration,
        ref float positionAmplitude,
        ref float rotationAmplitude,
        float frequency,
        float frameTime,
        float sampleOffset,
        out Vector2 offset,
        out float rotationOffset)
    {
        offset = Vector2.Zero;
        rotationOffset = 0f;

        if (positionAmplitude <= 0.0001f && rotationAmplitude <= 0.0001f)
            return false;

        elapsed += frameTime;
        var safeDuration = Math.Max(0.0001f, duration);
        var progress = Math.Clamp(elapsed / safeDuration, 0f, 1f);
        var envelope = EvaluateShakeEnvelope(progress);
        if (envelope <= 0.0001f || progress >= 1f)
        {
            elapsed = 0f;
            duration = 0f;
            positionAmplitude = 0f;
            rotationAmplitude = 0f;
            return false;
        }

        var sampleTime = sampleOffset + elapsed * frequency;
        var localOffset = new Vector2(
            _shakePositionXNoise.GetNoise(sampleTime, 0f),
            _shakePositionYNoise.GetNoise(sampleTime, 0f)) * (positionAmplitude * envelope);
        var baseRotation = Angle.FromDegrees(_baseRotationDegrees);
        offset = baseRotation.RotateVec(localOffset);
        rotationOffset = _shakeRotationNoise.GetNoise(sampleTime, 0f) * rotationAmplitude * envelope;
        return true;
    }

    private void ApplyAudienceShotShake(WH40KCinematicNetState state)
    {
        if (state.ActiveShot == null || state.AudienceShakeIntensity <= 0f)
        {
            if (_audienceShotShakeActive)
                ResetAudienceShotShake();

            return;
        }

        StartAudienceShotShakeImpulse(state.AudienceShakeIntensity);
    }

    private void ResetAudienceShotShake()
    {
        _audienceShotShakeActive = false;
        _overlayShakeOffset = Vector2.Zero;
        _overlayShakeElapsed = 0f;
        _overlayShakeDuration = 0f;
        _overlayShakeFrequency = ShakeBaseFrequency;
        _overlayShakePositionAmplitude = 0f;
        _overlayShakeRotationAmplitude = 0f;
    }

    private void InitializeShakeNoise(FastNoiseLite noise)
    {
        noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(3);
        noise.SetFractalGain(0.55f);
        noise.SetFrequency(ShakeNoiseFrequency);
    }

    private void ReseedShakeNoise()
    {
        _shakePositionXNoise.SetSeed(_random.Next());
        _shakePositionYNoise.SetSeed(_random.Next());
        _shakeRotationNoise.SetSeed(_random.Next());
    }

    private void EnsureOverlay()
    {
        if (_overlay is { Disposed: true })
            _overlay = null;

        if (_overlay != null)
        {
            _overlay.Visible = true;
            return;
        }

        _overlay = new WH40KCinematicOverlay
        {
            Visible = true
        };

        _ui.RootControl.AddChild(_overlay);
        Robust.Client.UserInterface.Controls.LayoutContainer.SetAnchorPreset(
            _overlay,
            Robust.Client.UserInterface.Controls.LayoutContainer.LayoutPreset.Wide);
    }

    private void ApplyCinematicUIMode()
    {
        if (_overlay != null)
            _overlay.Visible = true;

        ApplyActiveScreenCinematicVisibility();

        _ui.PopupRoot.Visible = false;
        _ui.ModalRoot.Visible = false;

        if (FindRootChild("WindowRoot") is { } windowRoot)
            windowRoot.Visible = false;
    }

    private void RestoreUiState(ClientUiRestoreState state)
    {
        foreach (var screenChild in state.ScreenChildren)
        {
            if (screenChild.Control is { Disposed: false } control)
                control.Visible = screenChild.Visible;
        }

        _ui.PopupRoot.Visible = state.PopupRootVisible;
        _ui.ModalRoot.Visible = state.ModalRootVisible;

        if (FindRootChild("WindowRoot") is { } windowRoot)
            windowRoot.Visible = state.WindowRootVisible;
    }

    private Robust.Client.UserInterface.Control? FindRootChild(string name)
    {
        for (var i = 0; i < _ui.RootControl.ChildCount; i++)
        {
            var child = _ui.RootControl.GetChild(i);
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private ClientUiRestoreState CaptureUiRestoreState()
    {
        var screenChildren = new List<UiControlRestoreState>();
        if (_ui.ActiveScreen != null)
        {
            for (var i = 0; i < _ui.ActiveScreen.ChildCount; i++)
            {
                var child = _ui.ActiveScreen.GetChild(i);
                screenChildren.Add(new UiControlRestoreState(child, child.Visible));
            }
        }

        return new ClientUiRestoreState(
            _ui.PopupRoot.Visible,
            _ui.ModalRoot.Visible,
            FindRootChild("WindowRoot")?.Visible ?? true,
            screenChildren);
    }

    private void ApplyActiveScreenCinematicVisibility()
    {
        if (_ui.ActiveScreen == null)
            return;

        var viewportRoot = TryGetActiveScreenViewportRoot(_ui.ActiveScreen);
        if (viewportRoot == null)
            return;

        for (var i = 0; i < _ui.ActiveScreen.ChildCount; i++)
        {
            var child = _ui.ActiveScreen.GetChild(i);
            if (ReferenceEquals(child, viewportRoot))
                continue;

            child.Visible = false;
        }
    }

    private static Robust.Client.UserInterface.Control? TryGetActiveScreenViewportRoot(Robust.Client.UserInterface.Control activeScreen)
    {
        if (activeScreen is not UIScreen screen)
            return null;

        if (screen.GetWidget<MainViewport>() is not { } viewport)
            return null;

        Robust.Client.UserInterface.Control current = viewport;
        while (current.Parent != null && !ReferenceEquals(current.Parent, activeScreen))
        {
            current = current.Parent;
        }

        return current.Parent == activeScreen ? current : viewport;
    }

    private static float LerpDegrees(float from, float to, float amount)
    {
        var delta = ((to - from + 540f) % 360f) - 180f;
        return from + delta * amount;
    }

    private static float ApplyBlendEasing(float progress, WH40KCinematicCameraTransitionEasing easing)
    {
        progress = Math.Clamp(progress, 0f, 1f);

        return easing switch
        {
            WH40KCinematicCameraTransitionEasing.Linear => progress,
            WH40KCinematicCameraTransitionEasing.SineInOut => 0.5f - 0.5f * MathF.Cos(progress * MathF.PI),
            WH40KCinematicCameraTransitionEasing.QuadInOut => progress < 0.5f
                ? 2f * progress * progress
                : 1f - MathF.Pow(-2f * progress + 2f, 2f) / 2f,
            WH40KCinematicCameraTransitionEasing.CubicInOut => progress < 0.5f
                ? 4f * progress * progress * progress
                : 1f - MathF.Pow(-2f * progress + 2f, 3f) / 2f,
            WH40KCinematicCameraTransitionEasing.BackOut =>
                1f + 2.70158f * MathF.Pow(progress - 1f, 3f) + 1.70158f * MathF.Pow(progress - 1f, 2f),
            WH40KCinematicCameraTransitionEasing.BounceOut => EaseBounceOut(progress),
            WH40KCinematicCameraTransitionEasing.ExpoInOut => progress switch
            {
                <= 0f => 0f,
                >= 1f => 1f,
                < 0.5f => MathF.Pow(2f, 20f * progress - 10f) / 2f,
                _ => (2f - MathF.Pow(2f, -20f * progress + 10f)) / 2f
            },
            _ => progress
        };
    }

    private static float EaseBounceOut(float progress)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;

        if (progress < 1f / d1)
            return n1 * progress * progress;

        if (progress < 2f / d1)
        {
            progress -= 1.5f / d1;
            return n1 * progress * progress + 0.75f;
        }

        if (progress < 2.5f / d1)
        {
            progress -= 2.25f / d1;
            return n1 * progress * progress + 0.9375f;
        }

        progress -= 2.625f / d1;
        return n1 * progress * progress + 0.984375f;
    }

    private sealed class ClientUiRestoreState
    {
        public bool PopupRootVisible { get; }
        public bool ModalRootVisible { get; }
        public bool WindowRootVisible { get; }
        public IReadOnlyList<UiControlRestoreState> ScreenChildren { get; }

        public ClientUiRestoreState(
            bool popupRootVisible,
            bool modalRootVisible,
            bool windowRootVisible,
            IReadOnlyList<UiControlRestoreState> screenChildren)
        {
            PopupRootVisible = popupRootVisible;
            ModalRootVisible = modalRootVisible;
            WindowRootVisible = windowRootVisible;
            ScreenChildren = screenChildren;
        }
    }

    private readonly record struct UiControlRestoreState(Robust.Client.UserInterface.Control Control, bool Visible);

    private sealed class CinematicBlendState
    {
        public CinematicPose From { get; }
        public CinematicPose To { get; }
        public TimeSpan Duration { get; }
        public WH40KCinematicCameraTransitionEasing Easing { get; }
        public TimeSpan Elapsed;

        public CinematicBlendState(
            CinematicPose from,
            CinematicPose to,
            TimeSpan duration,
            WH40KCinematicCameraTransitionEasing easing)
        {
            From = from;
            To = to;
            Duration = duration;
            Easing = easing;
        }
    }

    private readonly record struct CinematicPose(MapCoordinates Coordinates, float Zoom, float RotationDegrees);
}
