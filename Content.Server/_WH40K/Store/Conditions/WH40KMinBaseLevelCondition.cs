using System;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Store.Components;
using Content.Shared.Store;

namespace Content.Server._WH40K.Store.Conditions;

/// <summary>
/// Restricts listing availability to teams with at least the configured base level.
/// </summary>
public sealed partial class WH40KMinBaseLevelCondition : ListingCondition
{
    [DataField("level", required: true)]
    public int Level = 1;

    public override bool Condition(ListingConditionArgs args)
    {
        var rule = args.EntityManager.System<WH40KTeamRuleFacadeSystem>();

        var teamId = ResolveTeamId(args, rule);
        if (string.IsNullOrEmpty(teamId))
            return false;

        if (!rule.TryGetTeamProgress(teamId, out var currentLevel, out _, out _))
            return false;

        return currentLevel >= Math.Max(1, Level);
    }

    private static string ResolveTeamId(ListingConditionArgs args, WH40KTeamRuleFacadeSystem rule)
    {
        if (args.StoreEntity is { } storeUid &&
            args.EntityManager.TryGetComponent(storeUid, out WH40KStoreTeamComponent? storeTeam) &&
            !string.IsNullOrWhiteSpace(storeTeam.TeamId))
        {
            return storeTeam.TeamId;
        }

        if (rule.TryGetTeamIdFromEntity(args.Buyer, out var teamId))
            return teamId;

        return string.Empty;
    }
}
