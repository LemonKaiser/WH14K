using System;
using Content.Shared._WH40K.Mortar;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Mortar;

[UsedImplicitly]
public sealed class WH40KMortarBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WH40KMortarWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WH40KMortarWindow>();
        _window.SetTargetButton.OnPressed += _ => SendMessage(new WH40KMortarSetTargetMessage((Parse(_window.TargetXInput), Parse(_window.TargetYInput))));
        _window.SetDialButton.OnPressed += _ => SendMessage(new WH40KMortarSetDialMessage((Parse(_window.DialXInput), Parse(_window.DialYInput))));
        _window.SetLaserDesignatorButton.OnPressed += _ => SendMessage(new WH40KMortarSetLinkedDesignatorMessage(Parse(_window.LaserDesignatorIdInput)));
        _window.ToggleLaserModeButton.OnPressed += _ => SendMessage(new WH40KMortarToggleLaserModeMessage());
        _window.FireButton.OnPressed += _ => SendMessage(new WH40KMortarFireMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not WH40KMortarBuiState cast)
            return;

        _window.ApplyState(cast);
    }

    private static int Parse(LineEdit lineEdit)
    {
        return int.TryParse(lineEdit.Text, out var value) ? value : 0;
    }
}
