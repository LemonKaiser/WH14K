using Content.Shared._WH40K.MurderMystery;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.MurderMystery;

[TestFixture]
public sealed class WH40KMurderMysteryMathTests
{
    [TestCase(0, 0, 0, 0)]
    [TestCase(1, 1, 0, 0)]
    [TestCase(2, 1, 1, 0)]
    [TestCase(10, 1, 1, 8)]
    [TestCase(11, 2, 2, 7)]
    [TestCase(20, 2, 2, 16)]
    public void RoleSplitScalesPerTenPlayers(int players, int murders, int sheriffs, int civilians)
    {
        var split = WH40KMurderMysteryMath.GetRoleSplit(players);

        Assert.Multiple(() =>
        {
            Assert.That(split.Murders, Is.EqualTo(murders));
            Assert.That(split.Sheriffs, Is.EqualTo(sheriffs));
            Assert.That(split.Civilians, Is.EqualTo(civilians));
        });
    }
}
