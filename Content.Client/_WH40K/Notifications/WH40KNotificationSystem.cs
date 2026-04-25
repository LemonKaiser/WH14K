using Content.Shared._WH40K.Notifications;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.Notifications;

public sealed class WH40KNotificationSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KNotificationEvent>(OnNotification);
        SubscribeNetworkEvent<WH40KLocalizedNotificationEvent>(OnLocalizedNotification);
    }

    private void OnNotification(WH40KNotificationEvent ev)
    {
        _ui.GetUIController<WH40KNotificationUIController>().Push(ev);
    }

    private void OnLocalizedNotification(WH40KLocalizedNotificationEvent ev)
    {
        var title = ResolveTitle(ev.Title);
        var text = ResolveLocMessage(ev);

        _ui.GetUIController<WH40KNotificationUIController>().Push(new WH40KNotificationEvent(
            title,
            text,
            ev.AccentColor,
            ev.DurationSeconds,
            ev.Marquee,
            ev.Size));
    }

    private string ResolveTitle(string title)
    {
        return Loc.HasString(title)
            ? Loc.GetString(title)
            : title;
    }

    private string ResolveLocMessage(WH40KLocalizedNotificationEvent ev)
    {
        if (ev.LocArgs == null || ev.LocArgs.Count == 0)
            return Loc.GetString(ev.LocKey);

        var args = new (string, object)[ev.LocArgs.Count];
        var i = 0;
        foreach (var kv in ev.LocArgs)
        {
            object value;
            if (ev.ResolveArgValues && Loc.HasString(kv.Value))
                value = Loc.GetString(kv.Value);
            else
                value = kv.Value;

            args[i++] = (kv.Key, value);
        }

        return Loc.GetString(ev.LocKey, args);
    }
}
