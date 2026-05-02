using System;
using Content.Shared._WH40K.Notifications;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._WH40K.Notifications;

public sealed class WH40KNotificationSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public WH40KNotificationEvent? LastNotification { get; private set; }
    public WH40KLocalizedNotificationEvent? LastLocalizedNotification { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KNotificationEvent>(OnNotification);
        SubscribeNetworkEvent<WH40KLocalizedNotificationEvent>(OnLocalizedNotification);
    }

    private void OnNotification(WH40KNotificationEvent ev)
    {
        LastLocalizedNotification = null;
        LastNotification = ev;
        _ui.GetUIController<WH40KNotificationUIController>().Push(ev);
    }

    private void OnLocalizedNotification(WH40KLocalizedNotificationEvent ev)
    {
        var category = ev.Category == WH40KNotificationCategory.Auto
            ? WH40KNotificationMetadata.InferCategoryFromLocKey(ev.LocKey)
            : ev.Category;
        var priority = ev.Priority == WH40KNotificationPriority.Auto
            ? WH40KNotificationMetadata.DefaultPriority(category)
            : ev.Priority;
        var icon = ev.Icon == WH40KNotificationIcon.Auto
            ? WH40KNotificationMetadata.DefaultIcon(category, ev.AccentColor)
            : ev.Icon;
        var title = ResolveTitle(ev.Title, category, ev.AccentColor);
        var text = ResolveLocMessage(ev);

        LastLocalizedNotification = ev;

        var resolved = new WH40KNotificationEvent(
            title,
            text,
            ev.AccentColor,
            ev.DurationSeconds,
            ev.Marquee,
            ev.Size,
            category,
            priority,
            icon,
            ev.StackKey,
            ev.IgnoreUserPreferences,
            ev.Sound);

        LastNotification = resolved;
        _ui.GetUIController<WH40KNotificationUIController>().Push(resolved);
    }

    private string ResolveTitle(string title, WH40KNotificationCategory category, Color accentColor)
    {
        if (IsDefaultVoxTitle(title))
            title = WH40KNotificationMetadata.DefaultTitle(category, accentColor);

        return Loc.HasString(title)
            ? Loc.GetString(title)
            : title;
    }

    private bool IsDefaultVoxTitle(string title)
    {
        if (title == "wh40k-notification-title-vox" ||
            string.Equals(title, "Vox report", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Loc.HasString("wh40k-notification-title-vox") &&
               string.Equals(title, Loc.GetString("wh40k-notification-title-vox"), StringComparison.OrdinalIgnoreCase);
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
