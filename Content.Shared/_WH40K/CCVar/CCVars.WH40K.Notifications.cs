using Content.Shared._WH40K.Notifications;
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Controls how WH40K HUD notifications are displayed on this client.
    /// </summary>
    public static readonly CVarDef<string> WH40KNotificationDisplayMode =
        CVarDef.Create("wh40k.notification.display_mode", WH40KNotificationMetadata.DisplayModeFull, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     If true, WH40K notifications are also mirrored into the local chat feed.
    /// </summary>
    public static readonly CVarDef<bool> WH40KNotificationChatEnabled =
        CVarDef.Create("wh40k.notification.chat_enabled", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Comma-separated WH40K notification category ids enabled for this client. Admin notifications ignore this.
    /// </summary>
    public static readonly CVarDef<string> WH40KNotificationEnabledCategories =
        CVarDef.Create(
            "wh40k.notification.enabled_categories",
            "critical,point,weather,event,objective,mission,economy,reinforcement,info",
            CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Volume scale for WH40K notification sounds. Zero mutes non-admin notification sounds.
    /// </summary>
    public static readonly CVarDef<float> WH40KNotificationSoundVolume =
        CVarDef.Create("wh40k.notification.sound_volume", 1.0f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
