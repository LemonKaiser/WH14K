using Content.Client.Eui;
using Content.Shared._WH40K.Administration.ScreenCheck;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._WH40K.Administration.ScreenCheck;

[UsedImplicitly]
public sealed class ScreenCheckEui : BaseEui
{
    private readonly ScreenCheckWindow _window;

    public ScreenCheckEui()
    {
        _window = new ScreenCheckWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
        _window.Cleanup();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not ScreenCheckEuiState cast)
            return;

        _window.UpdateState(cast);
    }
}
