using Content.Shared._WH40K.PropHunt;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.PropHunt;

public sealed partial class WH40KPropHuntRoleCountSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KPropHuntRoleCountEvent>(OnRoleCount);
    }

    private void OnRoleCount(WH40KPropHuntRoleCountEvent ev)
    {
        _ui.GetUIController<WH40KPropHuntRoleCountUIController>().Apply(ev);
    }
}
