using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
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
}
