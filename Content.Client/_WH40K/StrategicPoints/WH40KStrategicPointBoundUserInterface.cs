using System;
using Content.Client._WH40K.StrategicPoints.UI;
using Content.Shared._WH40K.StrategicPoints;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.StrategicPoints;

[UsedImplicitly]
public sealed class WH40KStrategicPointBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WH40KStrategicPointWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WH40KStrategicPointWindow>();
        _window.OnUpgradePressed += () => SendMessage(new WH40KStrategicPointStartUpgradeMessage());
        _window.OnRefreshRequested += () => SendMessage(new WH40KStrategicPointRefreshMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KStrategicPointBuiState cast)
            return;

        _window.ApplyState(cast);
    }
}
