using Content.Shared._WH40K.AccountLoad;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Robust.Client.State;
using Robust.Shared.GameObjects;

namespace Content.Client._WH40K.AccountLoad;

public sealed partial class WH40KAccountLoadSystem : EntitySystem
{
    [Dependency] private IStateManager _state = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KAccountLoadStatusEvent>(OnAccountLoadStatus);
    }

    private void OnAccountLoadStatus(WH40KAccountLoadStatusEvent message)
    {
        if (_state.CurrentState is LobbyState or GameplayState)
            return;

        var state = _state.CurrentState as WH40KAccountLoadState
            ?? (WH40KAccountLoadState) _state.RequestStateChange<WH40KAccountLoadState>();
        state.UpdateStatus(message);
    }
}
