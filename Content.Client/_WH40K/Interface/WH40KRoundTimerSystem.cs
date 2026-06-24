using Content.Shared._WH40K.Interface;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Interface;

public sealed partial class WH40KRoundTimerSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KRoundTimerEvent>(OnRoundTimer);
    }

    private void OnRoundTimer(WH40KRoundTimerEvent ev)
    {
        _ui.GetUIController<WH40KRoundTimerUIController>().Apply(ev);
    }
}
