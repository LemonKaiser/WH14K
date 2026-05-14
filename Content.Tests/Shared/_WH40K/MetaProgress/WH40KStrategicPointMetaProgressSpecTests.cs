#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.MetaProgress;

[TestFixture]
public sealed class WH40KStrategicPointMetaProgressSpecTests
{
    private static readonly string[] RetiredAchievementIds =
    [
        "wh40k-ach-frontline-anchor",
        "wh40k-ach-point-breaker",
        "wh40k-ach-flag-keeper",
        "wh40k-ach-sector-dominator",
        "wh40k-ach-wall-of-steel",
        "wh40k-ach-objective-ace",
    ];

    private static readonly string[] NewStrategicAchievementIds =
    [
        "wh40k-ach-triad-stand",
        "wh40k-ach-field-engineer",
        "wh40k-ach-escalation-order",
        "wh40k-ach-master-builder",
        "wh40k-ach-bastion-smith",
        "wh40k-ach-strongpoint-saboteur",
    ];

    [Test]
    public void ActiveAchievementCatalogUsesStrategicPointAchievements()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/MetaProgress/achievements.yml");

        Assert.Multiple(() =>
        {
            foreach (var retiredId in RetiredAchievementIds)
            {
                Assert.That(source, Does.Not.Contain($"id: {retiredId}"));
            }

            foreach (var achievementId in NewStrategicAchievementIds)
            {
                Assert.That(source, Does.Contain($"id: {achievementId}"));
            }

            Assert.That(source, Does.Contain("progressStatKey: strategic.point.hold.triple10m.validated"));
            Assert.That(source, Does.Contain("progressStatKey: strategic.point.build.validated"));
            Assert.That(source, Does.Contain("progressStatKey: strategic.point.upgrade.validated"));
            Assert.That(source, Does.Contain("progressStatKey: strategic.point.destroy.validated"));
        });
    }

    [Test]
    public void ObjectiveDecorationsPointToNewStrategicAchievements()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/MetaProgress/unlockables.yml");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("id: decor-title-trench-lord"));
            Assert.That(source, Does.Contain("- wh40k-ach-triad-stand"));
            Assert.That(source, Does.Contain("id: decor-title-relic-keeper"));
            Assert.That(source, Does.Contain("- wh40k-ach-field-engineer"));
            Assert.That(source, Does.Contain("id: decor-title-gatekeeper"));
            Assert.That(source, Does.Contain("- wh40k-ach-bastion-smith"));
            Assert.That(source, Does.Contain("id: decor-title-astral-sentinel"));
            Assert.That(source, Does.Contain("- wh40k-ach-master-builder"));
            Assert.That(source, Does.Contain("id: decor-title-blood-banner"));
            Assert.That(source, Does.Contain("- wh40k-ach-strongpoint-saboteur"));
            Assert.That(source, Does.Contain("id: decor-title-pyre-marshal"));
            Assert.That(source, Does.Contain("- wh40k-ach-escalation-order"));

            foreach (var retiredId in RetiredAchievementIds)
            {
                Assert.That(source, Does.Not.Contain(retiredId));
            }
        });
    }

    [Test]
    public void StrategicPointRuntimeRaisesProgressEvents()
    {
        var source = ReadRepoFile("Content.Server/_WH40K/StrategicPoints/WH40KStrategicPointSystem.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("RaiseLocalEvent(new WH40KStrategicPointBuiltEvent("));
            Assert.That(source, Does.Contain("RaiseLocalEvent(new WH40KStrategicPointUpgradedEvent("));
            Assert.That(source, Does.Contain("RaiseLocalEvent(new WH40KStrategicPointDestroyedEvent("));
            Assert.That(source, Does.Contain("RaiseLocalEvent(new WH40KStrategicPointTripleHoldCompletedEvent("));
            Assert.That(source, Does.Contain("UpdateTripleHoldMilestones(now);"));
        });
    }

    [Test]
    public void MetaProgressConsumesStrategicPointEventsUnderRaisedObjectiveCap()
    {
        var metaSource = ReadRepoFile("Content.Server/_WH40K/MetaProgress/WH40KMetaProgressSystem.cs");
        var cvarSource = ReadRepoFile("Content.Shared/_WH40K/CCVar/CCVars.WH40K.cs");

        Assert.Multiple(() =>
        {
            Assert.That(metaSource, Does.Contain("SubscribeLocalEvent<WH40KStrategicPointBuiltEvent>(OnStrategicPointBuilt);"));
            Assert.That(metaSource, Does.Contain("SubscribeLocalEvent<WH40KStrategicPointUpgradedEvent>(OnStrategicPointUpgraded);"));
            Assert.That(metaSource, Does.Contain("SubscribeLocalEvent<WH40KStrategicPointDestroyedEvent>(OnStrategicPointDestroyed);"));
            Assert.That(metaSource, Does.Contain("SubscribeLocalEvent<WH40KStrategicPointTripleHoldCompletedEvent>(OnStrategicPointTripleHoldCompleted);"));
            Assert.That(metaSource, Does.Contain("GrantStrategicPointXp("));
            Assert.That(metaSource, Does.Contain("ClampObjectiveXpByRoundCap(userId, scaledXp)"));
            Assert.That(metaSource, Does.Contain("PruneRetiredAchievementState"));

            Assert.That(cvarSource, Does.Contain("wh40k.meta.xp_strategic_point_build"));
            Assert.That(cvarSource, Does.Contain("wh40k.meta.xp_strategic_point_upgrade"));
            Assert.That(cvarSource, Does.Contain("wh40k.meta.xp_strategic_point_destroy"));
            Assert.That(cvarSource, Does.Contain("wh40k.meta.xp_strategic_point_triple_hold"));
            Assert.That(cvarSource, Does.Contain("wh40k.meta.xp_objective_cap_per_round\", 500"));
        });
    }

    private static string ReadRepoFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Resources")) &&
                File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate WH14K repository root.");
        return string.Empty;
    }
}
