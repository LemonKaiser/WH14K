using Content.Shared._WH40K.GameMode;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.GameMode;

[TestFixture]
public sealed class WH40KTeamProgressionMathTests
{
    private static readonly int[] Thresholds = { 120, 300, 600, 1000 };

    [Test]
    public void PositiveGainRaisesXpAndLevel()
    {
        var result = WH40KTeamProgressionMath.AdjustTeamXp(
            currentTeamXp: 110,
            currentLevel: 1,
            thresholds: Thresholds,
            delta: 15,
            allowDecrease: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.TeamXp, Is.EqualTo(125));
            Assert.That(result.Level, Is.EqualTo(2));
            Assert.That(result.AppliedDelta, Is.EqualTo(15));
        });
    }

    [Test]
    public void NegativeGainIsIgnoredForPersistentProgressionByDefault()
    {
        var result = WH40KTeamProgressionMath.AdjustTeamXp(
            currentTeamXp: 340,
            currentLevel: 3,
            thresholds: Thresholds,
            delta: -50,
            allowDecrease: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.TeamXp, Is.EqualTo(340));
            Assert.That(result.Level, Is.EqualTo(3));
            Assert.That(result.AppliedDelta, Is.Zero);
        });
    }

    [Test]
    public void NegativeGainCanBeAppliedWhenExplicitlyAllowed()
    {
        var result = WH40KTeamProgressionMath.AdjustTeamXp(
            currentTeamXp: 340,
            currentLevel: 3,
            thresholds: Thresholds,
            delta: -60,
            allowDecrease: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.TeamXp, Is.EqualTo(280));
            Assert.That(result.Level, Is.EqualTo(2));
            Assert.That(result.AppliedDelta, Is.EqualTo(-60));
        });
    }
}
