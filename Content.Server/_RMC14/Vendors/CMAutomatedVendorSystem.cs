using Content.Shared._RMC14.Vendors;
using Robust.Server.GameObjects;

namespace Content.Server._RMC14.Vendors;

public sealed partial class CMAutomatedVendorSystem : SharedCMAutomatedVendorSystem
{
    [Dependency] private  UserInterfaceSystem _ui = default!;

    protected override void OnVendBui(Entity<CMAutomatedVendorComponent> vendor, ref CMVendorVendBuiMsg args)
    {
        base.OnVendBui(vendor, ref args);
        _ui.ServerSendUiMessage(vendor.Owner, args.UiKey, new CMVendorRefreshBuiMsg(), args.Actor);
    }
}

