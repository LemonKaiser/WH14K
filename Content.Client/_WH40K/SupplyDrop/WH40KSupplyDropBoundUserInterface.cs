using System;
using Content.Shared._WH40K.SupplyDrop;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.SupplyDrop;

[UsedImplicitly]
public sealed class WH40KSupplyDropBoundUserInterface : BoundUserInterface
{
    private WH40KSupplyDropWindow? _window;

    public WH40KSupplyDropBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WH40KSupplyDropWindow>();
        _window.LaunchButton.OnPressed += _ => SendMessage(new WH40KSupplyDropLaunchMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KSupplyDropBuiState cast)
            return;

        _window.ApplyState(cast);
    }
}
