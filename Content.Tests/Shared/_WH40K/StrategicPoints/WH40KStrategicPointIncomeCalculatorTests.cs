using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.StrategicPoints;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.StrategicPoints;

[TestFixture]
public sealed class WH40KStrategicPointIncomeCalculatorTests
{
    [Test]
    public void PreparationIncomeKeepsIntegerRemainderAcrossCycles()
    {
        var remainder = 0;

        var firstCycle = WH40KStrategicPointIncomeCalculator.ApplyPhaseMultiplier(
            1,
            WH40KBattlePhase.Preparation,
            ref remainder);
        var secondCycle = WH40KStrategicPointIncomeCalculator.ApplyPhaseMultiplier(
            1,
            WH40KBattlePhase.Preparation,
            ref remainder);

        Assert.Multiple(() =>
        {
            Assert.That(firstCycle, Is.EqualTo(0));
            Assert.That(secondCycle, Is.EqualTo(1));
            Assert.That(remainder, Is.EqualTo(0));
        });
    }

    [Test]
    public void BattlePhasesScaleIncomeAccordingToSpec()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GrantOnce(10, WH40KBattlePhase.Preparation), Is.EqualTo(5));
            Assert.That(GrantOnce(10, WH40KBattlePhase.Assault), Is.EqualTo(10));
            Assert.That(GrantOnce(10, WH40KBattlePhase.Apocalypse), Is.EqualTo(30));
        });
    }

    [Test]
    public void EffectiveIncomePreviewUsesSamePhaseMultiplierWithoutMutatingRemainder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KStrategicPointIncomeCalculator.GetEffectiveIncome(1, WH40KBattlePhase.Preparation), Is.EqualTo(0));
            Assert.That(WH40KStrategicPointIncomeCalculator.GetEffectiveIncome(30, WH40KBattlePhase.Preparation), Is.EqualTo(15));
            Assert.That(WH40KStrategicPointIncomeCalculator.GetEffectiveIncome(30, WH40KBattlePhase.Assault), Is.EqualTo(30));
            Assert.That(WH40KStrategicPointIncomeCalculator.GetEffectiveIncome(30, WH40KBattlePhase.Apocalypse), Is.EqualTo(90));
        });
    }

    [Test]
    public void NonPositiveIncomeGrantsNothing()
    {
        var remainder = 1;

        Assert.Multiple(() =>
        {
            Assert.That(WH40KStrategicPointIncomeCalculator.ApplyPhaseMultiplier(0, WH40KBattlePhase.Apocalypse, ref remainder), Is.Zero);
            Assert.That(WH40KStrategicPointIncomeCalculator.GetEffectiveIncome(-5, WH40KBattlePhase.Assault), Is.Zero);
            Assert.That(remainder, Is.EqualTo(1));
        });
    }

    private static int GrantOnce(int baseAmount, WH40KBattlePhase phase)
    {
        var remainder = 0;
        return WH40KStrategicPointIncomeCalculator.ApplyPhaseMultiplier(baseAmount, phase, ref remainder);
    }
}
