using System;
using Content.Client._WH40K.Tank.UI;
using Content.Shared._WH40K.Tank;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Tank;

[UsedImplicitly]
public sealed class WH40KTankBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WH40KTankWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WH40KTankWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KTankBuiState cast)
            return;

        _window.ApplyState(cast);
    }
}
