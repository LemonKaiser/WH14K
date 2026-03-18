namespace Content.Server.Database;

/// <summary>
/// Persistent WH40K account progression payload.
/// </summary>
public sealed record WH40KMetaProgressDbData(
    int LifetimeXp,
    int SeasonXp,
    DateTimeOffset LastProgressAt,
    string? SelectedGhostSkinId,
    string? SelectedOocTitleId,
    string? SelectedOocNameColorId);

/// <summary>
/// Persistent WH40K achievement progression payload.
/// </summary>
public sealed record WH40KMetaAchievementDbData(
    string AchievementId,
    int ProgressValue,
    bool Unlocked,
    DateTimeOffset? UnlockedAt,
    bool Claimed,
    int Version,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Persistent WH40K decoration unlock payload.
/// </summary>
public sealed record WH40KMetaDecorationDbData(
    string UnlockId,
    bool Unlocked,
    DateTimeOffset? UnlockedAt,
    int SourceLevel,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Persistent WH40K development unlock payload.
/// </summary>
public sealed record WH40KMetaDevelopmentUnlockDbData(
    string NodeId,
    DateTimeOffset UnlockedAt,
    int SpentCost,
    DateTimeOffset UpdatedAt);
