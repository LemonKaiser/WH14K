using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Stores the player's WH40K HUD theme preference.
    ///     Auto follows faction theme assignment; any concrete theme stays fixed.
    /// </summary>
    public static readonly CVarDef<string> WH40KInterfaceThemePreference =
        CVarDef.Create("wh40k.interface_theme_preference", "WH40KAutoTheme", CVar.CLIENTONLY | CVar.ARCHIVE);
}
