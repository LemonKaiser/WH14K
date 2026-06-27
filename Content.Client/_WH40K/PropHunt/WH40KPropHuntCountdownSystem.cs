using Content.Shared._WH40K.PropHunt;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;

namespace Content.Client._WH40K.PropHunt;

public sealed partial class WH40KPropHuntCountdownSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KPropHuntSeekerCountdownEvent>(OnCountdown);
    }

    private void OnCountdown(WH40KPropHuntSeekerCountdownEvent ev)
    {
        _ui.GetUIController<WH40KPropHuntSeekerCountdownUIController>().Apply(ev);
    }
}
