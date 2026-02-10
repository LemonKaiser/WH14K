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
    ///     If true, friendly fire is blocked between teammates unless attacker has WH40KFriendlyFireAllowed.
    /// </summary>
    public static readonly CVarDef<bool> WH40KFriendlyFireDisabled =
        CVarDef.Create("wh40k.friendly_fire_disabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Cooldown in seconds between friendly-fire ahelp warnings per player.
    /// </summary>
    public static readonly CVarDef<float> WH40KFriendlyFireAhelpCooldownSeconds =
        CVarDef.Create("wh40k.friendly_fire_ahelp_cooldown_seconds", 300f, CVar.SERVERONLY);

    /// <summary>
    ///     Minimum damage required to send a friendly-fire ahelp warning. 0 disables the threshold.
    /// </summary>
    public static readonly CVarDef<float> WH40KFriendlyFireAhelpMinDamage =
        CVarDef.Create("wh40k.friendly_fire_ahelp_min_damage", 5f, CVar.SERVERONLY);
}
