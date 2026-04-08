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
    private WH40KCommandNodeDoctrineWindow? _doctrineWindow;
    private WH40KCommandNodeBattleTacticWindow? _battleTacticWindow;
    private WH40KCommandNodeBoundUserInterfaceState? _latestState;
    private string _activeBattleTacticId = WH40KCommandNodeTactics.DefaultTacticId;

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
        _window.OnDoctrinePressed += OnDoctrinePressed;
        _window.OnBattleTacticPressed += OnBattleTacticPressed;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KCommandNodeBoundUserInterfaceState cast)
            return;

        _latestState = cast;
        _activeBattleTacticId = WH40KCommandNodeTactics.FindOrDefault(cast.ActiveBattleTacticId).Id;
        _window.UpdateState(cast);
        ApplyBattleTacticPreview();
        if (_teamCompositionWindow is { Disposed: false } compositionWindow)
            compositionWindow.UpdateState(cast);

        var activeDoctrineId = cast.ActiveDoctrineId;
        var doctrineLocked = cast.DoctrineLocked;

        if (_tacticalBonusesWindow is { Disposed: false } tacticalWindow)
            tacticalWindow.UpdateState(cast, activeDoctrineId);

        if (_doctrineWindow is { Disposed: false } doctrineWindow)
            doctrineWindow.UpdateState(cast, activeDoctrineId, doctrineLocked);

        if (_battleTacticWindow is { Disposed: false } battleTacticWindow)
            battleTacticWindow.UpdateState(cast);
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

        var activeDoctrineId = _latestState.ActiveDoctrineId;

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

        _tacticalBonusesWindow.UpdateState(_latestState, activeDoctrineId);
    }

    private void OnDoctrinePressed()
    {
        if (_latestState == null)
            return;

        var activeDoctrineId = _latestState.ActiveDoctrineId;
        var doctrineLocked = _latestState.DoctrineLocked;

        if (_doctrineWindow is not { Disposed: false })
        {
            _doctrineWindow = this.CreateDisposableControl<WH40KCommandNodeDoctrineWindow>();
            _doctrineWindow.OnDoctrineAssigned += OnDoctrineAssigned;
            _doctrineWindow.OpenCentered();
        }
        else if (_doctrineWindow.IsOpen)
        {
            _doctrineWindow.MoveToFront();
        }
        else
        {
            _doctrineWindow.OpenCentered();
        }

        _doctrineWindow.UpdateState(_latestState, activeDoctrineId, doctrineLocked);
    }

    private void OnBattleTacticPressed()
    {
        if (_latestState == null)
            return;

        if (_battleTacticWindow is not { Disposed: false })
        {
            _battleTacticWindow = this.CreateDisposableControl<WH40KCommandNodeBattleTacticWindow>();
            _battleTacticWindow.OnBattleTacticAssignRequested += OnBattleTacticAssignRequested;
            _battleTacticWindow.OpenCentered();
        }
        else if (_battleTacticWindow.IsOpen)
        {
            _battleTacticWindow.MoveToFront();
        }
        else
        {
            _battleTacticWindow.OpenCentered();
        }

        _battleTacticWindow.UpdateState(_latestState);
    }

    private void OnBattleTacticAssignRequested(string tacticId)
    {
        SendMessage(new WH40KCommandNodeAssignBattleTacticMessage(tacticId));
    }

    private void OnDoctrineAssigned(string doctrineId)
    {
        if (string.IsNullOrWhiteSpace(doctrineId))
            return;

        SendMessage(new WH40KCommandNodeAssignDoctrineMessage(doctrineId));
    }

    private void ApplyBattleTacticPreview()
    {
        if (_window == null)
            return;

        var display = WH40KCommandNodeBattleTacticWindow.ResolveBattleTacticDisplay(_activeBattleTacticId);
        _window.SetBattleTacticPreview(display.Name, display.Description);
    }
}
