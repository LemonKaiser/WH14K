using Content.Server._WH40K.Psyker;
using NUnit.Framework;
using Robust.UnitTesting;

namespace Content.Tests.Server._WH40K.Psyker;

[TestFixture]
public sealed class WH40KWarpBacklashSelectorTests : RobustUnitTest
{
    [Test]
    public void SelectUsesHighestTierForConfiguredShareOfPrimaryRolls()
    {
        var highestTierCount = 0;
        var lowerTierCount = 0;

        for (var i = 0; i < 100; i++)
        {
            var tier = WH40KWarpBacklashSelector.Select(800f, i / 100f, 0.5f);
            if (tier == WH40KWarpBacklashTier.Possession)
                highestTierCount++;
            else
                lowerTierCount++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(highestTierCount, Is.EqualTo(80));
            Assert.That(lowerTierCount, Is.EqualTo(20));
        });
    }

    [Test]
    public void SelectFallbackCanReachLowestAndHighestEligibleLowerTier()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KWarpBacklashSelector.Select(800f, 0.95f, 0f), Is.EqualTo(WH40KWarpBacklashTier.MildBurn));
            Assert.That(WH40KWarpBacklashSelector.Select(800f, 0.95f, 0.999f), Is.EqualTo(WH40KWarpBacklashTier.FleshRift));
            Assert.That(WH40KWarpBacklashSelector.Select(900f, 0.95f, 0.999f), Is.EqualTo(WH40KWarpBacklashTier.Possession));
        });
    }

    [Test]
    public void SelectHandlesBelowThresholdAndSingleTierBands()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KWarpBacklashSelector.Select(349f, 0f, 0f), Is.EqualTo(WH40KWarpBacklashTier.None));
            Assert.That(WH40KWarpBacklashSelector.Select(350f, 0.95f, 0.95f), Is.EqualTo(WH40KWarpBacklashTier.MildBurn));
            Assert.That(WH40KWarpBacklashSelector.Select(420f, 0.10f, 0f), Is.EqualTo(WH40KWarpBacklashTier.Stun));
            Assert.That(WH40KWarpBacklashSelector.Select(420f, 0.95f, 0.95f), Is.EqualTo(WH40KWarpBacklashTier.MildBurn));
        });
    }
}
