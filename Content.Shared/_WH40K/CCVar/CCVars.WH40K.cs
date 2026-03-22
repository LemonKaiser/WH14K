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

    /// <summary>
    ///     Enables the WH40K vignette fullscreen post-process shader.
    /// </summary>
    public static readonly CVarDef<bool> WH40KGrimdarkShaderEnabled =
        CVarDef.Create("wh40k.grimdark_shader_enabled", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Account-level cap for WH40K player meta progression. 0 means no cap.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaLevelCap =
        CVarDef.Create("wh40k.meta.level_cap", 40, CVar.SERVERONLY);

    /// <summary>
    ///     XP multiplier for WH40K player meta progression.
    /// </summary>
    public static readonly CVarDef<float> WH40KMetaXpMultiplier =
        CVarDef.Create("wh40k.meta.xp_multiplier", 1.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Legacy compatibility flag for meta unlock checks.
    ///     If true, level/achievement requirements are bypassed for decoration selection and meta-gated loadouts.
    /// </summary>
    public static readonly CVarDef<bool> WH40KMetaUnlocksEnforced =
        CVarDef.Create("wh40k.meta.unlocks_enforced", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     If true, admin visual overrides keep priority over WH40K OOC/ghost decorations.
    ///     If false, WH40K decorations are allowed to style admin chat/ghost visuals as well.
    /// </summary>
    public static readonly CVarDef<bool> WH40KMetaAdminPriorityOverDecorations =
        CVarDef.Create("wh40k.meta.admin_priority_over_decorations", true, CVar.SERVERONLY);

    /// <summary>
    ///     Controls whether WH40K decoration styling is applied to the whole OOC line (`OOC:`, name, and message).
    ///     Modes: 0 = off, 1 = admins only, 2 = all players.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaOocDecorationLineMode =
        CVarDef.Create("wh40k.meta.ooc_decoration_line_mode", 0, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for winning WH40K round.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpRoundWin =
        CVarDef.Create("wh40k.meta.xp_round_win", 100, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for valid WH40K kill.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpKill =
        CVarDef.Create("wh40k.meta.xp_kill", 10, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum number of kill-XP grants per player in one round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpKillCapPerRound =
        CVarDef.Create("wh40k.meta.xp_kill_cap_per_round", 30, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective major outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveMajor =
        CVarDef.Create("wh40k.meta.xp_objective_major", 35, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective minor outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveMinor =
        CVarDef.Create("wh40k.meta.xp_objective_minor", 20, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective timeout outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveTimeout =
        CVarDef.Create("wh40k.meta.xp_objective_timeout", 10, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective failure outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveFailure =
        CVarDef.Create("wh40k.meta.xp_objective_failure", 0, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum objective-XP per player per round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveCapPerRound =
        CVarDef.Create("wh40k.meta.xp_objective_cap_per_round", 120, CVar.SERVERONLY);

    /// <summary>
    ///     Enables verbose server trace logs for WH40K meta/stat progression pipeline.
    /// </summary>
    public static readonly CVarDef<bool> WH40KMetaStatsTrace =
        CVarDef.Create("wh40k.meta.stats_trace", false, CVar.SERVERONLY);

    /// <summary>
    ///     Enables verbose in-round WH40K economy telemetry (team CP/FP deltas, spending and periodic snapshots).
    /// </summary>
    public static readonly CVarDef<bool> WH40KEconomyTelemetryTrace =
        CVarDef.Create("wh40k.economy.telemetry_trace", false, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between periodic WH40K economy telemetry snapshots.
    /// </summary>
    public static readonly CVarDef<float> WH40KEconomyTelemetrySnapshotIntervalSeconds =
        CVarDef.Create("wh40k.economy.telemetry_snapshot_interval_seconds", 180f, CVar.SERVERONLY);

    /// <summary>
    ///     Absolute CP delta threshold that marks a telemetry line as a burst.
    /// </summary>
    public static readonly CVarDef<int> WH40KEconomyTelemetryBurstCommandDelta =
        CVarDef.Create("wh40k.economy.telemetry_burst_cp_delta", 24, CVar.SERVERONLY);

    /// <summary>
    ///     Enables WH40K projectile prediction reconciliation (client hit report + server validation).
    /// </summary>
    public static readonly CVarDef<bool> WH40KGunPrediction =
        CVarDef.Create("wh40k.gun_prediction", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum allowed deviation for client-reported coordinates around lag-compensated target position.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionCoordinateDeviation =
        CVarDef.Create("wh40k.gun_prediction_coordinate_deviation", 1.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Secondary wider deviation window for older lag-comp snapshot fallback.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionLowestCoordinateDeviation =
        CVarDef.Create("wh40k.gun_prediction_lowest_coordinate_deviation", 1.5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Expands server-side fixture bounds during prediction validation to reduce false negatives.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionAabbEnlargement =
        CVarDef.Create("wh40k.gun_prediction_aabb_enlargement", 0.1f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum age (seconds) for a predicted hit report since projectile spawn. 0 disables the age gate.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionMaxReportAgeSeconds =
        CVarDef.Create("wh40k.gun_prediction_max_report_age_seconds", 2.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum number of targets accepted in one predicted hit report payload.
    /// </summary>
    public static readonly CVarDef<int> WH40KGunPredictionMaxHitsPerReport =
        CVarDef.Create("wh40k.gun_prediction_max_hits_per_report", 8, CVar.SERVERONLY);

    /// <summary>
    ///     Enables debug logs for rejected WH40K predicted projectile hit reports.
    /// </summary>
    public static readonly CVarDef<bool> WH40KGunPredictionLogRejectedHits =
        CVarDef.Create("wh40k.gun_prediction_log_rejected_hits", false, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum front-point reward budget per team from tactical fulton extraction in one round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KFultonFrontRewardCapPerRound =
        CVarDef.Create("wh40k.fulton_front_reward_cap_per_round", 80, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum command-point reward budget per team from tactical fulton extraction in one round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KFultonCommandRewardCapPerRound =
        CVarDef.Create("wh40k.fulton_command_reward_cap_per_round", 80, CVar.SERVERONLY);

    /// <summary>
    ///     Enables mission-runtime cargo completion hook from successful tactical fulton extraction.
    /// </summary>
    public static readonly CVarDef<bool> WH40KFultonMissionHookEnabled =
        CVarDef.Create("wh40k.fulton_mission_hook_enabled", true, CVar.SERVERONLY);

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
        CVarDef.Create("wh40k.discord_auth_connect_refresh_cooldown_seconds", 15, CVar.SERVERONLY);

    /// <summary>
    ///     How long cached guild membership / role data should be considered fresh for display purposes.
    /// </summary>
    public static readonly CVarDef<int> WH40KDiscordAuthCacheTtlMinutes =
        CVarDef.Create("wh40k.discord_auth_cache_ttl_minutes", 720, CVar.SERVERONLY);

}
