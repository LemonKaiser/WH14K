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
public sealed partial class WH40KPropHuntSeekerCountdownUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IStateManager _state = default!;

    private WH40KPropHuntSeekerCountdownHudControl? _hud;
    private WH40KPropHuntSeekerCountdownEvent? _lastEvent;

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

    public void Apply(WH40KPropHuntSeekerCountdownEvent ev)
    {
        if (_state.CurrentState is not GameplayState)
            return;

        _lastEvent = ev;
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

        _hud = new WH40KPropHuntSeekerCountdownHudControl();
        UIManager.ActiveScreen.AddChild(_hud);
        LayoutContainer.SetAnchorPreset(_hud, LayoutContainer.LayoutPreset.Wide);
        LayoutContainer.SetPosition(_hud, Vector2.Zero);
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

        if (!_hud.Disposed)
            _hud.Orphan();

        _hud = null;
    }
}
