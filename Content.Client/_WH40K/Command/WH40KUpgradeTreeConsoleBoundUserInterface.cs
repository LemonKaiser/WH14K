using Content.Shared._WH40K.Command;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Command;

[UsedImplicitly]
public sealed class WH40KUpgradeTreeConsoleBoundUserInterface : BoundUserInterface
{
    private WH40KCommandNodeUpgradeSketchWindow? _window;

    public WH40KUpgradeTreeConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WH40KCommandNodeUpgradeSketchWindow>();
        _window.OnTreeNodePurchaseRequested += OnPurchaseRequested;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window != null && state is WH40KCommandNodeBoundUserInterfaceState cast)
            _window.UpdateState(cast, cast.ActiveDoctrineId);
    }

    private void OnPurchaseRequested(string nodeId)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
            SendMessage(new WH40KCommandNodePurchaseTreeNodeMessage(nodeId));
    }
}
