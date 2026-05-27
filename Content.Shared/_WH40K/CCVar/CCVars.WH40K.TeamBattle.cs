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
    ///     Optional global round time limit override in seconds.
    ///     Values less than or equal to 0 keep the rule prototype's own limit.
    /// </summary>
    public static readonly CVarDef<float> WH40KRoundTimeLimitSeconds =
        CVarDef.Create("wh40k.round_time_limit_seconds", 0f, CVar.SERVERONLY);

    /// <summary>
    ///     Automatically starts WH40K lobby votes after the round returns to a clean pre-round lobby state.
    /// </summary>
    public static readonly CVarDef<bool> WH40KLobbyAutoVoteEnabled =
        CVarDef.Create("wh40k.lobby_auto_vote.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Automatically starts a preset vote when WH40K lobby auto-vote is active.
    /// </summary>
    public static readonly CVarDef<bool> WH40KLobbyAutoVotePresetEnabled =
        CVarDef.Create("wh40k.lobby_auto_vote.preset_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Automatically starts a map vote after preset voting when WH40K lobby auto-vote is active.
    /// </summary>
    public static readonly CVarDef<bool> WH40KLobbyAutoVoteMapEnabled =
        CVarDef.Create("wh40k.lobby_auto_vote.map_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Delay in seconds before the automatic WH40K lobby vote sequence begins.
    /// </summary>
    public static readonly CVarDef<float> WH40KLobbyAutoVoteDelaySeconds =
        CVarDef.Create("wh40k.lobby_auto_vote.delay_seconds", 2f, CVar.SERVERONLY);

    /// <summary>
    ///     If true, extends the lobby countdown when needed so automatic WH40K lobby votes can finish before preload.
    /// </summary>
    public static readonly CVarDef<bool> WH40KLobbyAutoVoteEnsureLobbyTime =
        CVarDef.Create("wh40k.lobby_auto_vote.ensure_lobby_time", true, CVar.SERVERONLY);

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
