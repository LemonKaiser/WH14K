using System;
using Content.Shared._WH40K.Command;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Command;

[UsedImplicitly]
public sealed class WH40KCommandNodeBoundUserInterface : BoundUserInterface
{
    private WH40KCommandNodeWindow? _window;
    private WH40KCommandNodeTeamCompositionWindow? _teamCompositionWindow;
    private WH40KCommandNodeTacticalBonusesWindow? _tacticalBonusesWindow;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;

    public WH40KCommandNodeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WH40KCommandNodeWindow>();
        _window.OnUpgradePressed += () => SendMessage(new WH40KCommandNodeUpgradePressedMessage());
        _window.OnTeamCompositionPressed += OnTeamCompositionPressed;
        _window.OnTacticalBonusesPressed += OnTacticalBonusesPressed;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KCommandNodeBoundUserInterfaceState cast)
            return;

        _latestState = cast;
        _window.UpdateState(cast);
        if (_teamCompositionWindow is { Disposed: false } compositionWindow)
            compositionWindow.UpdateState(cast);

        if (_tacticalBonusesWindow is { Disposed: false } tacticalWindow)
            tacticalWindow.UpdateState(cast);
    }

    private void OnTeamCompositionPressed()
    {
        if (_latestState == null)
            return;

        if (_teamCompositionWindow is not { Disposed: false })
        {
            _teamCompositionWindow = this.CreateDisposableControl<WH40KCommandNodeTeamCompositionWindow>();
            _teamCompositionWindow.OpenCentered();
        }
        else if (_teamCompositionWindow.IsOpen)
        {
            _teamCompositionWindow.MoveToFront();
        }
        else
        {
            _teamCompositionWindow.OpenCentered();
        }

        _teamCompositionWindow.UpdateState(_latestState);
        SendMessage(new WH40KCommandNodeTeamCompositionPressedMessage());
    }

    private void OnTacticalBonusesPressed()
    {
        if (_latestState == null)
            return;

        if (_tacticalBonusesWindow is not { Disposed: false })
        {
            _tacticalBonusesWindow = this.CreateDisposableControl<WH40KCommandNodeTacticalBonusesWindow>();
            _tacticalBonusesWindow.OpenCentered();
        }
        else if (_tacticalBonusesWindow.IsOpen)
        {
            _tacticalBonusesWindow.MoveToFront();
        }
        else
        {
            _tacticalBonusesWindow.OpenCentered();
        }

        _tacticalBonusesWindow.UpdateState(_latestState);
    }
}
