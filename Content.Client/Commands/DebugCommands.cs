using Content.Client.Markers;
using Content.Client.Popups;
using Content.Client.SubFloor;
using Content.Client.Light;
using Content.Client._WH40K.TacticalMap;
using Robust.Client.Graphics;
using Robust.Shared.Console;

namespace Content.Client.Commands;

internal sealed partial class ShowMarkersCommand : LocalizedEntityCommands
{
    [Dependency] private MarkerSystem _markerSystem = default!;

    public override string Command => "showmarkers";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _markerSystem.MarkersVisible ^= true;
    }
}

internal sealed partial class ShowSubFloor : LocalizedEntityCommands
{
    [Dependency] private SubFloorHideSystem _subfloorSystem = default!;

    public override string Command => "showsubfloor";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _subfloorSystem.ShowAll ^= true;
    }
}

internal sealed partial class NotifyCommand : LocalizedEntityCommands
{
    [Dependency] private PopupSystem _popupSystem = default!;

    public override string Command => "notify";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _popupSystem.PopupCursor(args[0]);
    }
}

internal sealed partial class ShowRoofCommand : LocalizedEntityCommands
{
    [Dependency] private  IOverlayManager _overlay = default!;

    public override string Command => "showroof";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_overlay.HasOverlay<RoofDebugOverlay>())
        {
            _overlay.RemoveOverlay<RoofDebugOverlay>();
            return;
        }

        _overlay.AddOverlay(new RoofDebugOverlay());
    }
}

internal sealed partial class ShowTacticalBlackoutCommand : LocalizedEntityCommands
{
    [Dependency] private  IOverlayManager _overlay = default!;

    public override string Command => "showtacticalblackout";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_overlay.HasOverlay<WH40KTacticalMapBlackoutDebugOverlay>())
        {
            _overlay.RemoveOverlay<WH40KTacticalMapBlackoutDebugOverlay>();
            return;
        }

        _overlay.AddOverlay(new WH40KTacticalMapBlackoutDebugOverlay());
    }
}
