using System;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WH40K.Psyker;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Psyker.UI;

[UsedImplicitly]
public sealed partial class WH40KPsykerAstralUiController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private  IConfigurationManager _cfg = default!;
    [Dependency] private  IGameTiming _timing = default!;
    [Dependency] private  IPlayerManager _player = default!;

    private WH40KPsykerAstralOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;

        _cfg.OnValueChanged(CVars.LocCultureName, OnCultureChanged);
    }

    public void OnStateEntered(GameplayState state)
    {
        EnsureOverlay();
        HideOverlay();
    }

    public void OnStateExited(GameplayState state)
    {
        ShutdownOverlay();
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_player.LocalEntity is not { } uid ||
            !EntityManager.TryGetComponent<WH40KPsykerAstralProjectionComponent>(uid, out var astral))
        {
            HideOverlay();
            return;
        }

        EnsureOverlay();
        if (_overlay == null)
            return;

        if (_timing.CurTime < astral.RevealStartsAt)
        {
            HideOverlay();
            return;
        }

        var warp = EntityManager.GetComponentOrNull<WH40KWarpResourceComponent>(uid);
        var instability = EntityManager.GetComponentOrNull<WH40KWarpInstabilityComponent>(uid);
        var progression = EntityManager.GetComponentOrNull<WH40KPsykerProgressionComponent>(uid);
        var astralProgression = EntityManager.GetComponentOrNull<WH40KPsykerAstralProgressionComponent>(uid);
        var warpCurrent = warp?.CurrentCharge ?? 0f;
        var warpMax = Math.Max(1f, warp?.MaxCharge ?? 100f);
        var instabilityCurrent = instability?.CurrentInstability ?? 0f;
        var instabilityMax = Math.Max(1f, instability?.MaxInstability ?? 100f);
        var instabilityFraction = Math.Clamp(instabilityCurrent / instabilityMax, 0f, 1f);
        var fade = GetFade(astral);
        var canPurchase = _timing.CurTime >= astral.FadeEndsAt;

        _overlay.ApplyState(new WH40KPsykerAstralOverlayViewState(
            true,
            fade,
            progression?.Level ?? 1,
            $"{MathF.Round(warpCurrent, 1)}/{MathF.Round(warpMax, 1)}",
            $"{MathF.Round(instabilityCurrent, 1)}/{MathF.Round(instabilityMax, 1)}",
            _timing.CurTime >= astral.CanExitAt,
            canPurchase,
            astralProgression?.DisciplinePoints ?? 0,
            astralProgression?.TotalDisciplinePointsEarned ?? 0,
            astralProgression?.AstralDepth ?? 1,
            instabilityFraction,
            astralProgression?.AstralStrain ?? 0f,
            astralProgression?.ConstellationLayoutId ?? string.Empty,
            astralProgression != null ? astralProgression.UnlockedNodes : Array.Empty<string>(),
            astralProgression != null ? astralProgression.CollectibleStars : Array.Empty<WH40KPsykerAstralCollectibleStar>()));
    }

    private float GetFade(WH40KPsykerAstralProjectionComponent astral)
    {
        if (_timing.CurTime < astral.RevealStartsAt)
            return 0f;

        var duration = astral.FadeEndsAt - astral.RevealStartsAt;
        if (duration <= TimeSpan.Zero)
            return 1f;

        var progress = Math.Clamp(
            (float) ((_timing.CurTime - astral.RevealStartsAt).TotalSeconds / duration.TotalSeconds),
            0f,
            1f);

        return progress * progress * (3f - 2f * progress);
    }

    private void OnScreenLoad()
    {
        EnsureOverlay();
        HideOverlay();
    }

    private void OnScreenUnload()
    {
        ShutdownOverlay();
    }

    private void OnCultureChanged(string _)
    {
        _overlay?.Relocalize();
    }

    private void EnsureOverlay()
    {
        if (_overlay is { Disposed: true })
            _overlay = null;

        if (_overlay != null || UIManager.ActiveScreen == null)
            return;

        _overlay = new WH40KPsykerAstralOverlay
        {
            Visible = false
        };
        _overlay.ExitRequested += OnExitRequested;
        _overlay.PurchaseRequested += OnPurchaseRequested;
        _overlay.CollectibleStarRequested += OnCollectibleStarRequested;

        if (UIManager.ActiveScreen.GetWidget<MainViewport>()?.Parent is LayoutContainer layout)
        {
            layout.AddChild(_overlay);
            LayoutContainer.SetAnchorPreset(_overlay, LayoutContainer.LayoutPreset.Wide);
        }
        else
        {
            UIManager.RootControl.AddChild(_overlay);
            LayoutContainer.SetAnchorPreset(_overlay, LayoutContainer.LayoutPreset.Wide);
        }
    }

    private void HideOverlay()
    {
        if (_overlay is { Disposed: true })
            _overlay = null;

        if (_overlay == null)
            return;

        _overlay.Visible = false;
    }

    private void ShutdownOverlay()
    {
        if (_overlay == null)
            return;

        _overlay.ExitRequested -= OnExitRequested;
        _overlay.PurchaseRequested -= OnPurchaseRequested;
        _overlay.CollectibleStarRequested -= OnCollectibleStarRequested;

        if (!_overlay.Disposed)
            _overlay.Orphan();

        _overlay = null;
    }

    private void OnExitRequested()
    {
        EntityManager.System<WH40KPsykerAstralProjectionSystem>().RequestExit();
    }

    private void OnPurchaseRequested(string nodeId)
    {
        EntityManager.System<WH40KPsykerAstralProjectionSystem>().RequestNodePurchase(nodeId);
    }

    private void OnCollectibleStarRequested(int starId)
    {
        EntityManager.System<WH40KPsykerAstralProjectionSystem>().RequestCollectibleStar(starId);
    }
}
