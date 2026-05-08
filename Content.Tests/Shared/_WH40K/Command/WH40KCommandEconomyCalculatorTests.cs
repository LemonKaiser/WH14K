using Content.Shared._WH40K.Command;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Command;

[TestFixture]
public sealed class WH40KCommandEconomyCalculatorTests
{
    [Test]
    public void CommandTreeCostUsesFundsAndResearch()
    {
        const int baseCost = 7;

        Assert.Multiple(() =>
        {
            Assert.That(
                WH40KCommandEconomyCalculator.GetCommandTreeFundsCost(baseCost),
                Is.EqualTo(baseCost * WH40KCommandEconomyCalculator.CommandTreeFundsPerCost));

            Assert.That(
                WH40KCommandEconomyCalculator.GetCommandTreeResearchCost(baseCost),
                Is.EqualTo(baseCost * WH40KCommandEconomyCalculator.CommandTreeResearchPerCost));
        });
    }

    [Test]
    public void CommandNodeUpgradeCostUsesFundsAndResearch()
    {
        const int baseCost = 3;

        Assert.Multiple(() =>
        {
            Assert.That(
                WH40KCommandEconomyCalculator.GetCommandNodeUpgradeFundsCost(baseCost),
                Is.EqualTo(baseCost * WH40KCommandEconomyCalculator.CommandNodeUpgradeFundsPerCost));

            Assert.That(
                WH40KCommandEconomyCalculator.GetCommandNodeUpgradeResearchCost(baseCost),
                Is.EqualTo(baseCost * WH40KCommandEconomyCalculator.CommandNodeUpgradeResearchPerCost));
        });
    }

    [Test]
    public void ReinforcementInfluenceCostAlsoRequiresFunds()
    {
        const int influenceCost = 5;

        Assert.Multiple(() =>
        {
            Assert.That(
                WH40KCommandEconomyCalculator.GetReinforcementFundsCost(influenceCost),
                Is.EqualTo(influenceCost * WH40KCommandEconomyCalculator.ReinforcementFundsPerInfluence));

            Assert.That(
                WH40KCommandEconomyCalculator.GetPassiveFallbackFundsReward(influenceCost),
                Is.EqualTo(influenceCost * WH40KCommandEconomyCalculator.PassiveFallbackFundsPerPoint));
        });
    }

    [Test]
    public void MissionDevelopmentPointsGrantFundsAndResearch()
    {
        const int developmentPoints = 4;

        Assert.Multiple(() =>
        {
            Assert.That(
                WH40KCommandEconomyCalculator.GetMissionFundsReward(developmentPoints),
                Is.EqualTo(developmentPoints * WH40KCommandEconomyCalculator.MissionFundsPerDevelopmentPoint));

            Assert.That(
                WH40KCommandEconomyCalculator.GetMissionResearchReward(developmentPoints),
                Is.EqualTo(developmentPoints * WH40KCommandEconomyCalculator.MissionResearchPerDevelopmentPoint));
        });
    }

    [Test]
    public void NonPositiveInputsClampToZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KCommandEconomyCalculator.GetCommandTreeFundsCost(0), Is.EqualTo(0));
            Assert.That(WH40KCommandEconomyCalculator.GetCommandTreeResearchCost(-1), Is.EqualTo(0));
            Assert.That(WH40KCommandEconomyCalculator.GetCommandNodeUpgradeFundsCost(0), Is.EqualTo(0));
            Assert.That(WH40KCommandEconomyCalculator.GetCommandNodeUpgradeResearchCost(-1), Is.EqualTo(0));
            Assert.That(WH40KCommandEconomyCalculator.GetReinforcementFundsCost(0), Is.EqualTo(0));
            Assert.That(WH40KCommandEconomyCalculator.GetPassiveFallbackFundsReward(-2), Is.EqualTo(0));
            Assert.That(WH40KCommandEconomyCalculator.GetMissionFundsReward(-1), Is.EqualTo(0));
            Assert.That(WH40KCommandEconomyCalculator.GetMissionResearchReward(0), Is.EqualTo(0));
        });
    }
}
