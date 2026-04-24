using Content.Shared._WH40K.Command;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Command;

[UsedImplicitly]
public sealed class WH40KReinforcementConsoleBoundUserInterface : BoundUserInterface
{
    private WH40KCommandNodeReinforcementWindow? _window;

    public WH40KReinforcementConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WH40KCommandNodeReinforcementWindow>();
        _window.OnManualSubmitRequested += OnManualSubmitRequested;
        _window.OnAutoSaveRequested += OnAutoSaveRequested;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window != null && state is WH40KCommandReinforcementBoundUserInterfaceState cast)
            _window.UpdateState(cast);
    }

    private void OnManualSubmitRequested(WH40KCommandReinforcementDraftEntry[] roles)
    {
        SendMessage(new WH40KCommandNodeSubmitReinforcementRequestMessage(roles));
    }

    private void OnAutoSaveRequested(bool enabled, int thresholdPercent, WH40KCommandReinforcementDraftEntry[] roles)
    {
        SendMessage(new WH40KCommandNodeSaveAutoReinforcementMessage(enabled, thresholdPercent, roles));
    }
}
