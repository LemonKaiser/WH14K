using System;

namespace Content.Shared._WH40K.Command;

public static class WH40KCommandEconomyCalculator
{
    public const int CommandTreeFundsPerCost = 35;
    public const int CommandTreeResearchPerCost = 10;
    public const int CommandNodeUpgradeFundsPerCost = 35;
    public const int CommandNodeUpgradeResearchPerCost = 10;
    public const int ReinforcementFundsPerInfluence = 20;
    public const int PassiveFallbackFundsPerPoint = 20;
    public const int MissionFundsPerDevelopmentPoint = 35;
    public const int MissionResearchPerDevelopmentPoint = 10;

    public static int GetCommandTreeFundsCost(int baseCost)
    {
        return ScaleCost(baseCost, CommandTreeFundsPerCost);
    }

    public static int GetCommandTreeResearchCost(int baseCost)
    {
        return ScaleCost(baseCost, CommandTreeResearchPerCost);
    }

    public static int GetCommandNodeUpgradeFundsCost(int baseCost)
    {
        return ScaleCost(baseCost, CommandNodeUpgradeFundsPerCost);
    }

    public static int GetCommandNodeUpgradeResearchCost(int baseCost)
    {
        return ScaleCost(baseCost, CommandNodeUpgradeResearchPerCost);
    }

    public static int GetReinforcementFundsCost(int influenceCost)
    {
        return ScaleCost(influenceCost, ReinforcementFundsPerInfluence);
    }

    public static int GetPassiveFallbackFundsReward(int pointEquivalent)
    {
        return ScaleCost(pointEquivalent, PassiveFallbackFundsPerPoint);
    }

    public static int GetMissionFundsReward(int developmentPoints)
    {
        return ScaleCost(developmentPoints, MissionFundsPerDevelopmentPoint);
    }

    public static int GetMissionResearchReward(int developmentPoints)
    {
        return ScaleCost(developmentPoints, MissionResearchPerDevelopmentPoint);
    }

    private static int ScaleCost(int value, int multiplier)
    {
        if (value <= 0 || multiplier <= 0)
            return 0;

        return Math.Max(1, value * multiplier);
    }
}
