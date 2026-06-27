using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WH40K.PropHunt;
using JetBrains.Annotations;
using Robust.Client.State;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.IoC;

namespace Content.Client._WH40K.PropHunt;

[UsedImplicitly]
public sealed partial class WH40KPropHuntRoleCountUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    private const int HudTopMargin = 104;

    [Dependency] private IStateManager _state = default!;

    private WH40KPropHuntRoleCountHudControl? _hud;
    private WH40KPropHuntRoleCountEvent? _lastEvent;

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
        ReapplyState();
    }

    public void OnStateExited(GameplayState state)
    {
        ShutdownHud();
    }

    public void Apply(WH40KPropHuntRoleCountEvent ev)
    {
        _lastEvent = ev;

        if (_state.CurrentState is not GameplayState)
            return;

        EnsureHud();
        _hud?.Apply(ev);
    }

    private void OnScreenLoad()
    {
        EnsureHud();
        ReapplyState();
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

        var screen = UIManager.ActiveScreen;
        if (screen == null)
            return;

        _hud = new WH40KPropHuntRoleCountHudControl();
        screen.AddChild(_hud);
        LayoutContainer.SetAnchorAndMarginPreset(_hud, LayoutContainer.LayoutPreset.CenterTop, margin: HudTopMargin);
        LayoutContainer.SetPosition(_hud, new Vector2(-WH40KPropHuntRoleCountHudControl.CanvasWidth * 0.5f, 0f));
        _hud.SetPositionLast();
    }

    private void ReapplyState()
    {
        if (_hud == null || _lastEvent == null)
            return;

        _hud.Apply(_lastEvent);
    }

    private void ShutdownHud()
    {
        _lastEvent = null;

        if (_hud == null)
            return;

        _hud.Clear();

        if (!_hud.Disposed)
            _hud.Orphan();

        _hud = null;
    }
}
