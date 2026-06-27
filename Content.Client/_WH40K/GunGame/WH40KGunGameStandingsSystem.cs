using Content.Shared._WH40K.GunGame;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.GunGame;

public sealed partial class WH40KGunGameStandingsSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KGunGameStandingsEvent>(OnStandings);
    }

    private void OnStandings(WH40KGunGameStandingsEvent ev)
    {
        _ui.GetUIController<WH40KGunGameStandingsUIController>().Apply(ev);
    }
}
