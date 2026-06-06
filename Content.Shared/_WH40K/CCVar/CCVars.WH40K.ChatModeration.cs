using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Automatic punishment applied when the standard chat rate limiter is breached.
    ///     Supported values: none, mute.
    /// </summary>
    public static readonly CVarDef<string> ChatRateLimitPunishment =
        CVarDef.Create("chat.rate_limit_punishment", "none", CVar.SERVERONLY);

    /// <summary>
    ///     Automatic chat mute duration, in minutes, for standard chat rate limit breaches.
    ///     Values below 1 are treated as 1 minute when mute punishment is enabled.
    /// </summary>
    public static readonly CVarDef<int> ChatRateLimitMuteMinutes =
        CVarDef.Create("chat.rate_limit_mute_minutes", 1, CVar.SERVERONLY);

    /// <summary>
    ///     If true, a player's visible chat lines are removed when the standard chat rate limiter triggers.
    /// </summary>
    public static readonly CVarDef<bool> ChatRateLimitDeleteMessages =
        CVarDef.Create("chat.rate_limit_delete_messages", false, CVar.SERVERONLY);

    /// <summary>
    ///     The period, in seconds, during which repeated identical chat messages are counted.
    ///     The repeated-message anti-spam triggers as soon as the configured count is reached.
    /// </summary>
    public static readonly CVarDef<float> ChatRepeatRateLimitPeriod =
        CVarDef.Create("chat.repeat_rate_limit_period", 5f, CVar.SERVERONLY);

    /// <summary>
    ///     How many identical normalized chat messages inside one repeat period trigger the repeated-message anti-spam.
    ///     A value of 3 means the third matching message is blocked.
    /// </summary>
    public static readonly CVarDef<int> ChatRepeatRateLimitCount =
        CVarDef.Create("chat.repeat_rate_limit_count", 3, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum delay, in seconds, between admin alerts about repeated-message chat spam.
    ///     Negative values disable admin announcements.
    /// </summary>
    public static readonly CVarDef<int> ChatRepeatRateLimitAnnounceAdminsDelay =
        CVarDef.Create("chat.repeat_rate_limit_announce_admins_delay", 30, CVar.SERVERONLY);

    /// <summary>
    ///     Automatic punishment applied when repeated-message chat spam is detected.
    ///     Supported values: none, mute.
    /// </summary>
    public static readonly CVarDef<string> ChatRepeatRateLimitPunishment =
        CVarDef.Create("chat.repeat_rate_limit_punishment", "mute", CVar.SERVERONLY);

    /// <summary>
    ///     Automatic chat mute duration, in minutes, for repeated-message chat spam.
    ///     Values below 1 are treated as 1 minute when mute punishment is enabled.
    /// </summary>
    public static readonly CVarDef<int> ChatRepeatRateLimitMuteMinutes =
        CVarDef.Create("chat.repeat_rate_limit_mute_minutes", 1, CVar.SERVERONLY);

    /// <summary>
    ///     If true, a player's visible chat lines are removed when repeated-message chat spam is detected.
    /// </summary>
    public static readonly CVarDef<bool> ChatRepeatRateLimitDeleteMessages =
        CVarDef.Create("chat.repeat_rate_limit_delete_messages", true, CVar.SERVERONLY);
}
