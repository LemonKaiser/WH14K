using System;
using Content.Shared._WH40K.MetaProgress;
using NUnit.Framework;
using Robust.UnitTesting;

namespace Content.Tests.Shared._WH40K.MetaProgress;

[TestFixture]
public sealed class WH40KMetaProgressMathTests : RobustUnitTest
{
    [Test]
    public void RequiredXpIsMonotonic()
    {
        var previous = WH40KMetaProgressMath.GetRequiredXpForLevel(1);

        for (var level = 2; level <= 100; level++)
        {
            var current = WH40KMetaProgressMath.GetRequiredXpForLevel(level);
            Assert.That(current, Is.GreaterThanOrEqualTo(previous));
            previous = current;
        }
    }

    [Test]
    public void RequiredXpNormalizesInvalidLevel()
    {
        var levelOne = WH40KMetaProgressMath.GetRequiredXpForLevel(1);

        Assert.Multiple(() =>
        {
            Assert.That(WH40KMetaProgressMath.GetRequiredXpForLevel(0), Is.EqualTo(levelOne));
            Assert.That(WH40KMetaProgressMath.GetRequiredXpForLevel(-5), Is.EqualTo(levelOne));
        });
    }

    [Test]
    public void PlaytimeConversionRoundsDownAndClamps()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KMetaProgressMath.LifetimeXpFromOverallPlaytime(TimeSpan.FromSeconds(59)), Is.EqualTo(0));
            Assert.That(WH40KMetaProgressMath.LifetimeXpFromOverallPlaytime(TimeSpan.FromSeconds(60)), Is.EqualTo(2));
            Assert.That(WH40KMetaProgressMath.LifetimeXpFromOverallPlaytime(TimeSpan.FromMinutes(12.9)), Is.EqualTo(24));
            Assert.That(WH40KMetaProgressMath.LifetimeXpFromOverallPlaytime(TimeSpan.FromMinutes(-10)), Is.EqualTo(0));
        });
    }

    [Test]
    public void CalculateFromLifetimeXpHandlesLevelBoundary()
    {
        var requiredForLevelOne = WH40KMetaProgressMath.GetRequiredXpForLevel(1);
        var requiredForLevelTwo = WH40KMetaProgressMath.GetRequiredXpForLevel(2);

        var beforeLevelUp = WH40KMetaProgressMath.CalculateFromLifetimeXp(requiredForLevelOne - 1);
        var exactLevelUp = WH40KMetaProgressMath.CalculateFromLifetimeXp(requiredForLevelOne);

        Assert.Multiple(() =>
        {
            Assert.That(beforeLevelUp.Level, Is.EqualTo(1));
            Assert.That(beforeLevelUp.CurrentXp, Is.EqualTo(requiredForLevelOne - 1));
            Assert.That(beforeLevelUp.RequiredXp, Is.EqualTo(requiredForLevelOne));

            Assert.That(exactLevelUp.Level, Is.EqualTo(2));
            Assert.That(exactLevelUp.CurrentXp, Is.EqualTo(0));
            Assert.That(exactLevelUp.RequiredXp, Is.EqualTo(requiredForLevelTwo));
        });
    }

    [Test]
    public void CalculateFromLifetimeXpRespectsLevelCap()
    {
        const int lifetimeXp = 500_000;
        const int levelCap = 20;

        var capped = WH40KMetaProgressMath.CalculateFromLifetimeXp(lifetimeXp, levelCap);

        Assert.Multiple(() =>
        {
            Assert.That(capped.Level, Is.EqualTo(levelCap));
            Assert.That(capped.CurrentXp, Is.GreaterThanOrEqualTo(0));
            Assert.That(capped.CurrentXp, Is.LessThanOrEqualTo(capped.RequiredXp));
            Assert.That(capped.LifetimeXp, Is.EqualTo(lifetimeXp));
        });
    }

    [Test]
    public void CalculateFromLifetimeXpNormalizesNegativeInput()
    {
        var result = WH40KMetaProgressMath.CalculateFromLifetimeXp(-1000);

        Assert.Multiple(() =>
        {
            Assert.That(result.Level, Is.EqualTo(1));
            Assert.That(result.CurrentXp, Is.EqualTo(0));
            Assert.That(result.RequiredXp, Is.EqualTo(WH40KMetaProgressMath.GetRequiredXpForLevel(1)));
            Assert.That(result.LifetimeXp, Is.EqualTo(0));
        });
    }

    [Test]
    public void TotalSkillPointsUsesPerLevelEntriesAndDefaults()
    {
#pragma warning disable RA0039
        var table = new WH40KMetaLevelRewardTablePrototype
        {
            DefaultSkillPoints = 1,
            Entries = new()
            {
                new WH40KMetaLevelRewardEntry { Level = 1, SkillPoints = 0 },
                new WH40KMetaLevelRewardEntry { Level = 4, SkillPoints = 2 },
            },
        };
#pragma warning restore RA0039

        Assert.Multiple(() =>
        {
            Assert.That(WH40KMetaProgressMath.CalculateTotalSkillPointsForLevel(1, table), Is.EqualTo(0));
            Assert.That(WH40KMetaProgressMath.CalculateTotalSkillPointsForLevel(3, table), Is.EqualTo(2));
            Assert.That(WH40KMetaProgressMath.CalculateTotalSkillPointsForLevel(4, table), Is.EqualTo(4));
            Assert.That(WH40KMetaProgressMath.CalculateTotalSkillPointsForLevel(5, table), Is.EqualTo(5));
        });
    }

    [Test]
    public void AchievementProgressIsClampedToTarget()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KMetaProgressMath.ClampAchievementProgress(-10, 100), Is.EqualTo(0));
            Assert.That(WH40KMetaProgressMath.ClampAchievementProgress(40, 100), Is.EqualTo(40));
            Assert.That(WH40KMetaProgressMath.ClampAchievementProgress(160, 100), Is.EqualTo(100));
        });
    }

    [Test]
    public void AchievementCompletionUsesClampedProgress()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KMetaProgressMath.IsAchievementCompleted(99, 100), Is.False);
            Assert.That(WH40KMetaProgressMath.IsAchievementCompleted(100, 100), Is.True);
            Assert.That(WH40KMetaProgressMath.IsAchievementCompleted(150, 100), Is.True);
            Assert.That(WH40KMetaProgressMath.IsAchievementCompleted(-5, 100), Is.False);
        });
    }
}
