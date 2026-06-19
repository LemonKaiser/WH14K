using Content.Client.Eui;
using Content.Shared.Eui;
using Content.Shared._WH40K.Administration.Mute;
using JetBrains.Annotations;

namespace Content.Client._WH40K.Administration.Mute;

[UsedImplicitly]
public sealed class WH40KMutePanelEui : BaseEui
{
    private readonly WH40KMutePanel _window;

    public WH40KMutePanelEui()
    {
        _window = new WH40KMutePanel();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.MuteSubmitted += request => SendMessage(new WH40KMutePanelEuiStateMsg.CreateMuteRequest(request));
        _window.PlayerChanged += player => SendMessage(new WH40KMutePanelEuiStateMsg.GetPlayerInfoRequest(player));
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
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not WH40KMutePanelEuiState cast)
            return;

        _window.UpdateMuteFlag(cast.CanMute);
        _window.UpdatePlayerData(cast.PlayerName);
    }
}
