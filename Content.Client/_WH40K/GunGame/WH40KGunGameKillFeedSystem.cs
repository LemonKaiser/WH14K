using Content.Shared._WH40K.GunGame;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.GunGame;

public sealed partial class WH40KGunGameKillFeedSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KGunGameKillFeedEvent>(OnKillFeed);
    }

    private void OnKillFeed(WH40KGunGameKillFeedEvent ev)
    {
        _ui.GetUIController<WH40KGunGameKillFeedUIController>().Push(ev);
    }
}
