using Content.Client.UserInterface.Controls;
using Content.Shared._RMC14.Waypointer;
using Content.Shared._RMC14.Waypointer.Components;
using Content.Shared._RMC14.Waypointer.Events;
using Content.Shared.Actions.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._RMC14.Waypointer;

[UsedImplicitly]
public sealed partial class WaypointerMenuBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private  IPrototypeManager _prototype = default!;

    private SimpleRadialMenu? _menu;

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent<ActionComponent>(Owner, out var actionComp) ||
            !EntMan.TryGetComponent<ActiveWaypointerComponent>(actionComp.Container, out var waypointer))
        {
            return;
        }

        var options = CreateButtons(waypointer);
        if (options == null)
            return;

        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.SetButtons(options);
        _menu.OpenCentered();
    }

    private IEnumerable<RadialMenuOptionBase>? CreateButtons(ActiveWaypointerComponent waypointer)
    {
        if (waypointer.WaypointerProtoIds == null)
            return null;

        var options = new List<RadialMenuOptionBase>();

        var state = waypointer.Active ? "action_icon_off" : "action_icon_on";
        var sprite = new SpriteSpecifier.Rsi(waypointer.RadialMenuIconPath, state);
        options.Add(new RadialMenuActionOption<bool>(HandleRadialMenuClick, !waypointer.Active)
        {
            IconSpecifier = RadialMenuIconSpecifier.With(sprite),
            ToolTip = Loc.GetString(waypointer.Active ? "rmc-waypointer-disable-all" : "rmc-waypointer-enable-all"),
        });

        foreach (var pair in waypointer.WaypointerProtoIds)
        {
            if (!_prototype.Resolve(pair.Key, out var proto))
                continue;

            var waypointerState = pair.Value ? "disable" : "enable";
            var waypointerSprite = new SpriteSpecifier.Rsi(proto.RadialMenuIconPath, waypointerState);
            options.Add(new RadialMenuActionOption<ProtoId<WaypointerPrototype>>(HandleRadialMenuClick, pair.Key)
            {
                IconSpecifier = RadialMenuIconSpecifier.With(waypointerSprite),
                ToolTip = Loc.GetString(
                    pair.Value ? "rmc-waypointer-disable" : "rmc-waypointer-enable",
                    ("waypointer", Loc.GetString(proto.Name))),
            });
        }

        return options;
    }

    private void HandleRadialMenuClick(bool toggleAll)
    {
        SendPredictedMessage(new WaypointersToggledMessage(toggleAll));
    }

    private void HandleRadialMenuClick(ProtoId<WaypointerPrototype> waypointer)
    {
        SendPredictedMessage(new WaypointerStatusChangedMessage(waypointer));
    }
}
