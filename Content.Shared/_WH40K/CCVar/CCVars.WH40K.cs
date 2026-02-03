using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Seconds between WH40K team rule victory checks.
    /// </summary>
    public static readonly CVarDef<float> WH40KTeamCheckInterval =
        CVarDef.Create("wh40k.team_check_interval", 3f, CVar.SERVERONLY);

    /// <summary>
    ///     If true, victory checks are skipped until every team has at least one assigned member.
    /// </summary>
    public static readonly CVarDef<bool> WH40KRequireAllTeamsPresent =
        CVarDef.Create("wh40k.require_all_teams_present", true, CVar.SERVERONLY);

    /// <summary>
    ///     Round time limit in seconds. 0 disables the limit.
    /// </summary>
    public static readonly CVarDef<float> WH40KRoundTimeLimitSeconds =
        CVarDef.Create("wh40k.round_time_limit_seconds", 3600f, CVar.SERVERONLY);

    /// <summary>
    ///     Enables or disables friendly-fire ahelp warnings for WH40K team battle.
    /// </summary>
    public static readonly CVarDef<bool> WH40KFriendlyFireAhelpEnabled =
        CVarDef.Create("wh40k.friendly_fire_ahelp_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     If true, players cannot damage teammates unless their role allows friendly fire.
    /// </summary>
    public static readonly CVarDef<bool> WH40KFriendlyFireEnabled =
        CVarDef.Create("wh40k.friendly_fire_enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Cooldown in seconds between friendly-fire ahelp warnings per player.
    /// </summary>
    public static readonly CVarDef<float> WH40KFriendlyFireAhelpCooldownSeconds =
        CVarDef.Create("wh40k.friendly_fire_ahelp_cooldown_seconds", 300f, CVar.SERVERONLY);
}
