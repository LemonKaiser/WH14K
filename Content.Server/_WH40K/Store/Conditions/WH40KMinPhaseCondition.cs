using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.GameMode;
using Content.Shared.Store;

namespace Content.Server._WH40K.Store.Conditions;

/// <summary>
/// Restricts listing availability to a minimum global WH40K phase.
/// </summary>
public sealed partial class WH40KMinPhaseCondition : ListingCondition
{
    [DataField("phase", required: true)]
    public WH40KBattlePhase Phase = WH40KBattlePhase.Preparation;

    public override bool Condition(ListingConditionArgs args)
    {
        var rule = args.EntityManager.System<WH40KTeamRuleFacadeSystem>();
        return rule.GetCurrentPhase() >= Phase;
    }
}
