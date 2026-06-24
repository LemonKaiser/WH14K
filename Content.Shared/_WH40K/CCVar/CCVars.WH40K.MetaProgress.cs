using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Account-level cap for WH40K player meta progression. 0 means no cap.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaLevelCap =
        CVarDef.Create("wh40k.meta.level_cap", 40, CVar.SERVER | CVar.REPLICATED);

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
        CVarDef.Create("wh40k.meta.xp_round_win", 200, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for valid WH40K kill.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpKill =
        CVarDef.Create("wh40k.meta.xp_kill", 15, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum amount of kill XP per player in one round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpKillCapPerRound =
        CVarDef.Create("wh40k.meta.xp_kill_cap_per_round", 150, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective major outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveMajor =
        CVarDef.Create("wh40k.meta.xp_objective_major", 50, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for mission objective minor outcome.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveMinor =
        CVarDef.Create("wh40k.meta.xp_objective_minor", 25, CVar.SERVERONLY);

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
    ///     Base XP grant for personally constructing a strategic point.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpStrategicPointBuild =
        CVarDef.Create("wh40k.meta.xp_strategic_point_build", 15, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for personally upgrading a strategic point tier.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpStrategicPointUpgrade =
        CVarDef.Create("wh40k.meta.xp_strategic_point_upgrade", 20, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for personally destroying an enemy strategic point.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpStrategicPointDestroy =
        CVarDef.Create("wh40k.meta.xp_strategic_point_destroy", 30, CVar.SERVERONLY);

    /// <summary>
    ///     Base XP grant for being on the team that holds three strategic points for ten continuous minutes.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpStrategicPointTripleHold =
        CVarDef.Create("wh40k.meta.xp_strategic_point_triple_hold", 30, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum mission and strategic-point objective XP per player per round. 0 means unlimited.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpObjectiveCapPerRound =
        CVarDef.Create("wh40k.meta.xp_objective_cap_per_round", 500, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum repeatable WH40K round XP per player per round.
    ///     Achievement reward XP is not counted towards this cap.
    /// </summary>
    public static readonly CVarDef<int> WH40KMetaXpRepeatableCapPerRound =
        CVarDef.Create("wh40k.meta.xp_repeatable_cap_per_round", 1000, CVar.SERVERONLY);
}
