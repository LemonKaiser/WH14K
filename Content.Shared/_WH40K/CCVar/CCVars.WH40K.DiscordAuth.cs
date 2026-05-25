using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables the WH40K Discord authorization / account-linking feature set.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDiscordAuthEnabled =
        CVarDef.Create("wh40k.discord_auth_enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Discord OAuth2 application client id used for account linking.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthClientId =
        CVarDef.Create("wh40k.discord_auth_client_id", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Discord OAuth2 application client secret used for token exchange.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthClientSecret =
        CVarDef.Create("wh40k.discord_auth_client_secret", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Exact redirect URI configured in the Discord application for the callback endpoint.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthRedirectUri =
        CVarDef.Create("wh40k.discord_auth_redirect_uri", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Discord guild id used to validate server membership and role access.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthGuildId =
        CVarDef.Create("wh40k.discord_auth_guild_id", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     If true, players must link a Discord account to satisfy WH40K Discord access policy.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDiscordAuthRequireLink =
        CVarDef.Create("wh40k.discord_auth_require_link", false, CVar.SERVERONLY);

    /// <summary>
    ///     If true, players must be a member of the configured Discord guild to satisfy WH40K Discord access policy.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDiscordAuthRequireGuildMember =
        CVarDef.Create("wh40k.discord_auth_require_guild_member", false, CVar.SERVERONLY);

    /// <summary>
    ///     Comma-separated Discord role ids. If set, players must have at least one of these roles to satisfy WH40K Discord access policy.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthRequiredRoleIds =
        CVarDef.Create("wh40k.discord_auth_required_role_ids", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     If true, enforce the configured WH40K Discord access policy during server connection approval instead of lobby/round flow.
    /// </summary>
    public static readonly CVarDef<bool> WH40KDiscordAuthGateOnConnect =
        CVarDef.Create("wh40k.discord_auth_gate_on_connect", false, CVar.SERVERONLY);

    /// <summary>
    ///     Lifetime in seconds for a pending Discord link request state token.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthLinkRequestTtlSeconds =
        CVarDef.Create("wh40k.discord_auth_link_request_ttl_seconds", 600, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum seconds between manual profile refresh requests for one player.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthRefreshCooldownSeconds =
        CVarDef.Create("wh40k.discord_auth_refresh_cooldown_seconds", 30, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum seconds between automatic/manual Discord refresh attempts triggered from server connect-gate flow.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthConnectRefreshCooldownSeconds =
        CVarDef.Create("wh40k.discord_auth_connect_refresh_cooldown_seconds", 30, CVar.SERVERONLY);

    /// <summary>
    ///     How long cached guild membership / role data should be considered fresh for display purposes.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthCacheTtlMinutes =
        CVarDef.Create("wh40k.discord_auth_cache_ttl_minutes", 720, CVar.SERVERONLY);

    /// <summary>
    ///     Shared secret for authenticating relay callback requests from an external auth proxy.
    ///     When set, the game server accepts POST /wh40k/discord-auth/relay with code+state
    ///     forwarded by the external server. Must match the secret configured on the proxy.
    /// </summary>
    public static readonly CVarDef<string> WH40KDiscordAuthRelaySecret =
        CVarDef.Create("wh40k.discord_auth_relay_secret", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);
}
