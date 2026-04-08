using System;
using Robust.Shared.Random;

namespace Content.Server._WH40K.Psyker;

public enum WH40KWarpBacklashTier
{
    None,
    MildBurn,
    Stun,
    Collapse,
    Drop,
    Bleed,
    Doppelganger,
    FleshRift,
    Possession,
    Mutation,
}

internal static class WH40KWarpBacklashSelector
{
    public const float HighestTierChance = 0.8f;
    public const float MildBacklashThreshold = 350f;
    public const float StunBacklashThreshold = 400f;
    public const float CollapseBacklashThreshold = 500f;
    public const float DropBacklashThreshold = 550f;
    public const float BleedBacklashThreshold = 600f;
    public const float DoppelgangerBacklashThreshold = 650f;
    public const float FleshRiftBacklashThreshold = 700f;
    public const float PossessionBacklashThreshold = 800f;
    public const float MutationBacklashThreshold = 900f;

    private const float MaxExclusiveRoll = 0.99999994f;

    private static readonly WH40KWarpBacklashTier[] OrderedTiers =
    {
        WH40KWarpBacklashTier.MildBurn,
        WH40KWarpBacklashTier.Stun,
        WH40KWarpBacklashTier.Collapse,
        WH40KWarpBacklashTier.Drop,
        WH40KWarpBacklashTier.Bleed,
        WH40KWarpBacklashTier.Doppelganger,
        WH40KWarpBacklashTier.FleshRift,
        WH40KWarpBacklashTier.Possession,
        WH40KWarpBacklashTier.Mutation,
    };

    public static WH40KWarpBacklashTier Select(float instability, IRobustRandom random)
    {
        return Select(instability, random.NextFloat(), random.NextFloat());
    }

    public static WH40KWarpBacklashTier Select(float instability, float highestTierRoll, float lowerTierRoll)
    {
        var highestTierIndex = GetHighestTierIndex(instability);
        if (highestTierIndex < 0)
            return WH40KWarpBacklashTier.None;

        if (highestTierIndex == 0 || highestTierRoll < HighestTierChance)
            return OrderedTiers[highestTierIndex];

        var clampedLowerTierRoll = Math.Clamp(lowerTierRoll, 0f, MaxExclusiveRoll);
        var lowerTierIndex = (int) (clampedLowerTierRoll * highestTierIndex);
        return OrderedTiers[lowerTierIndex];
    }

    private static int GetHighestTierIndex(float instability)
    {
        if (instability >= MutationBacklashThreshold)
            return 8;

        if (instability >= PossessionBacklashThreshold)
            return 7;

        if (instability >= FleshRiftBacklashThreshold)
            return 6;

        if (instability >= DoppelgangerBacklashThreshold)
            return 5;

        if (instability >= BleedBacklashThreshold)
            return 4;

        if (instability >= DropBacklashThreshold)
            return 3;

        if (instability >= CollapseBacklashThreshold)
            return 2;

        if (instability >= StunBacklashThreshold)
            return 1;

        if (instability >= MildBacklashThreshold)
            return 0;

        return -1;
    }
}
