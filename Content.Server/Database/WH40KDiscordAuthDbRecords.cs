namespace Content.Server.Database;

/// <summary>
/// Persistent WH40K Discord authorization payload.
/// </summary>
public sealed record WH40KDiscordAuthDbData(
    string DiscordUserId,
    string Username,
    string? GlobalName,
    string? AvatarHash,
    string AccessToken,
    string? RefreshToken,
    string TokenType,
    string Scope,
    DateTimeOffset LinkedAt,
    DateTimeOffset TokenExpiresAt,
    DateTimeOffset LastRefreshAt,
    string? GuildIdCached,
    DateTimeOffset? LastGuildRefreshAt,
    bool GuildMemberCached,
    string? GuildNickname,
    string RoleCacheJson);
