using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables client-side fallback to alternate game-server addresses from the launcher connection screen.
    /// </summary>
    public static readonly CVarDef<bool> WH40KConnectionFallbackEnabled =
        CVarDef.Create("wh40k.connection_fallback.enabled", true, CVar.CLIENTONLY);

    /// <summary>
    ///     Comma-separated primary addresses that may use WH40K connection fallback. Empty allows any address.
    /// </summary>
    public static readonly CVarDef<string> WH40KConnectionFallbackPrimaryAddresses =
        CVarDef.Create("wh40k.connection_fallback.primary_addresses", "ss14://ebengrad.node-oheir.simplestation.org:25910", CVar.CLIENTONLY);

    /// <summary>
    ///     Comma-separated alternate addresses tried when the primary address fails at the transport layer.
    /// </summary>
    public static readonly CVarDef<string> WH40KConnectionFallbackAlternateAddresses =
        CVarDef.Create("wh40k.connection_fallback.alternate_addresses", "ss14://2612.koara.live:25910", CVar.CLIENTONLY);

    /// <summary>
    ///     Automatically switches to the first alternate address after a network-level connection failure.
    /// </summary>
    public static readonly CVarDef<bool> WH40KConnectionFallbackAutomatic =
        CVarDef.Create("wh40k.connection_fallback.automatic", true, CVar.CLIENTONLY);

    /// <summary>
    ///     Shows a manual alternate-address button on network-level connection failures.
    /// </summary>
    public static readonly CVarDef<bool> WH40KConnectionFallbackButtonEnabled =
        CVarDef.Create("wh40k.connection_fallback.button_enabled", true, CVar.CLIENTONLY);

    /// <summary>
    ///     Delay before automatic fallback reconnect starts. A small delay lets the connection state fully settle.
    /// </summary>
    public static readonly CVarDef<float> WH40KConnectionFallbackAutoDelaySeconds =
        CVarDef.Create("wh40k.connection_fallback.auto_delay_seconds", 1.0f, CVar.CLIENTONLY);

    /// <summary>
    ///     Extra delay after an established connection times out before trying an alternate address.
    ///     This gives the server time to release the old authenticated session before the same account reconnects.
    /// </summary>
    public static readonly CVarDef<float> WH40KConnectionFallbackDisconnectDelaySeconds =
        CVarDef.Create("wh40k.connection_fallback.disconnect_delay_seconds", 10.0f, CVar.CLIENTONLY);
}
