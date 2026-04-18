using System;
using Content.Client._WH40K.Vehicle.Fuel.UI;
using Content.Shared._WH40K.Vehicle.Fuel;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Vehicle.Fuel;

[UsedImplicitly]
public sealed class WH40KVehicleFuelTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WH40KVehicleFuelTerminalWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WH40KVehicleFuelTerminalWindow>();
        _window.OnAutoIntakePressed += enabled => SendMessage(new WH40KVehicleFuelTerminalToggleAutoIntakeMessage(enabled));
        _window.OnAutoRefuelPressed += enabled => SendMessage(new WH40KVehicleFuelTerminalToggleAutoRefuelMessage(enabled));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KVehicleFuelTerminalBuiState cast)
            return;

        _window.ApplyState(cast);
    }
}
