using Content.Shared._WH40K.Command;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Command;

[UsedImplicitly]
public sealed class WH40KMissionBoardConsoleBoundUserInterface : BoundUserInterface
{
    private WH40KCommandNodeMissionBoardWindow? _window;

    public WH40KMissionBoardConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WH40KCommandNodeMissionBoardWindow>();
        _window.OnTaskSelected += OnTaskSelected;
        _window.OnPinpointerSyncRequested += OnPinpointerSync;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window != null && state is WH40KCommandNodeBoundUserInterfaceState cast)
            _window.UpdateState(cast);
    }

    private void OnTaskSelected(string taskId)
    {
        if (!string.IsNullOrWhiteSpace(taskId))
            SendMessage(new WH40KCommandNodeAssignMissionTaskMessage(taskId));
    }

    private void OnPinpointerSync()
    {
        SendMessage(new WH40KCommandNodeSyncMissionPinpointerMessage());
    }
}
