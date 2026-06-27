using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WH40K.GunGame;
using JetBrains.Annotations;
using Robust.Client.State;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.IoC;

namespace Content.Client._WH40K.GunGame;

[UsedImplicitly]
public sealed partial class WH40KGunGameKillFeedUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IStateManager _state = default!;

    private WH40KGunGameKillFeedHudControl? _hud;

    public override void Initialize()
    {
        base.Initialize();

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;
    }

    public void OnStateEntered(GameplayState state)
    {
        EnsureHud();
    }

    public void OnStateExited(GameplayState state)
    {
        ShutdownHud();
    }

    public void Push(WH40KGunGameKillFeedEvent ev)
    {
        if (_state.CurrentState is not GameplayState)
            return;

        EnsureHud();
        _hud?.Push(ev);
    }

    private void OnScreenLoad()
    {
        EnsureHud();
    }

    private void OnScreenUnload()
    {
        ShutdownHud();
    }

    private void EnsureHud()
    {
        if (_hud is { Disposed: true })
            _hud = null;

        if (_hud != null || UIManager.ActiveScreen == null || _state.CurrentState is not GameplayState)
            return;

        if (UIManager.ActiveScreen.GetWidget<MainViewport>()?.Parent is not LayoutContainer viewportLayout)
            return;

        _hud = new WH40KGunGameKillFeedHudControl();
        viewportLayout.AddChild(_hud);
        LayoutContainer.SetAnchorAndMarginPreset(_hud, LayoutContainer.LayoutPreset.BottomRight, margin: 18);
        _hud.SetPositionLast();
    }

    private void ShutdownHud()
    {
        if (_hud == null)
            return;

        if (!_hud.Disposed)
            _hud.Orphan();

        _hud = null;
    }
}
