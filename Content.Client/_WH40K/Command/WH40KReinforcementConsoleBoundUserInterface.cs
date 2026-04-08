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
        _window.OnCallRequested += OnCallRequested;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window != null && state is WH40KCommandNodeBoundUserInterfaceState cast)
            _window.UpdateState(cast);
    }

    private void OnCallRequested(string optionId, int count)
    {
        if (!string.IsNullOrWhiteSpace(optionId))
            SendMessage(new WH40KCommandNodeCallReinforcementMessage(optionId, count));
    }
}
