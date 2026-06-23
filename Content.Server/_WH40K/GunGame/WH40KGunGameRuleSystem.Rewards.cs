using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking.Rules.Components;
using Content.Server._WH40K.Stats;
using Robust.Shared.Network;

namespace Content.Server._WH40K.GunGame;

public sealed partial class WH40KGunGameRuleSystem
{
    private static readonly int[] PlacementRewardXp =
    {
        1000,
        700,
        500
    };

    private void GrantPlacementMetaProgressRewards(WH40KGunGameRuleComponent rule)
    {
        if (rule.PlacementRewardsGranted || rule.PlayerLevel.Count == 0)
            return;

        rule.PlacementRewardsGranted = true;

        var placements = rule.PlayerLevel
            .OrderByDescending(entry => entry.Value)
            .ThenByDescending(entry => rule.PlayerKills.GetValueOrDefault(entry.Key))
            .ThenBy(entry => entry.Key.ToString(), System.StringComparer.Ordinal)
            .Take(PlacementRewardXp.Length)
            .ToArray();

        for (var i = 0; i < placements.Length; i++)
        {
            var rewardXp = PlacementRewardXp[i];
            if (rewardXp <= 0)
                continue;

            var metadata = new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                ["mode"] = "WH40KGunGame",
                ["place"] = (i + 1).ToString(),
                ["roundId"] = GameTicker.RoundId.ToString()
            };

            _metaProgress.GrantLifetimeXp(placements[i].Key, rewardXp, WH40KPlayerStatKeys.MetaXpGunGamePlace, metadata);
            _sawmill.Info($"Granted Gun Game placement meta XP: user={placements[i].Key}, place={i + 1}, xp={rewardXp}, round={GameTicker.RoundId}.");
        }
    }
}
