using System;

namespace Content.Shared._WH40K.Tiers;

public static class WH40KTierMath
{
    public static (int Tier1, int Tier2, int Tier3) NormalizeThresholds(
        int tier1MinBaseLevel,
        int tier2MinBaseLevel,
        int tier3MinBaseLevel)
    {
        var tier1 = Math.Max(1, tier1MinBaseLevel);
        var tier2 = Math.Max(tier1, tier2MinBaseLevel);
        var tier3 = Math.Max(tier2, tier3MinBaseLevel);
        return (tier1, tier2, tier3);
    }

    public static int SelectTier(
        int level,
        int tier1MinBaseLevel,
        int tier2MinBaseLevel,
        int tier3MinBaseLevel)
    {
        var (tier1, tier2, tier3) = NormalizeThresholds(
            tier1MinBaseLevel,
            tier2MinBaseLevel,
            tier3MinBaseLevel);

        if (level >= tier3)
            return 3;

        if (level >= tier2)
            return 2;

        if (level >= tier1)
            return 1;

        return 0;
    }
}
