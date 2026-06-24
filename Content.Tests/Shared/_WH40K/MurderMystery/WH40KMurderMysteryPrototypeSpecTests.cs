#nullable enable
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.MurderMystery;

[TestFixture]
public sealed class WH40KMurderMysteryPrototypeSpecTests
{
    [Test]
    public void MiniGamesHaveDedicatedMapPoolsAndModeExists()
    {
        var presets = ReadRepoFile("Resources/Prototypes/_WH40K/game_presets_wh40k.yml");
        var pools = ReadRepoFile("Resources/Prototypes/_WH40K/Maps/Pools/wh40k_minigames.yml");
        var rule = ReadRepoFile("Resources/Prototypes/_WH40K/GameRules/murder_mystery.yml");

        Assert.Multiple(() =>
        {
            Assert.That(presets, Does.Contain("id: WH40KGunGame"));
            Assert.That(presets, Does.Contain("supportedMaps: WH40KGunGameMapPool"));
            Assert.That(presets, Does.Contain("id: WH40KPropHunt"));
            Assert.That(presets, Does.Contain("supportedMaps: WH40KPropHuntMapPool"));
            Assert.That(presets, Does.Contain("id: WH40KMurderMystery"));
            Assert.That(presets, Does.Contain("supportedMaps: WH40KMurderMysteryMapPool"));
            Assert.That(pools, Does.Contain("id: WH40KPropHuntMapPool"));
            Assert.That(pools, Does.Contain("id: WH40KMurderMysteryMapPool"));
            Assert.That(pools, Does.Contain("WH40KGunGameMeteorArena"));
            Assert.That(pools, Does.Contain("WH40KGunGameDm01Entryway"));
            Assert.That(pools, Does.Contain("WH40KGunGameLiman"));
            Assert.That(rule, Does.Contain("id: WH40KMurderMystery"));
            Assert.That(rule, Does.Contain("roleAssignmentDelay: 30"));
            Assert.That(rule, Does.Contain("roundDuration: 900"));
            Assert.That(rule, Does.Contain("minimumPlayersToRun: 2"));
            Assert.That(rule, Does.Contain("winnerRewardXp: 500"));
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
