#nullable enable
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.GameMode;

[TestFixture]
public sealed class WH40KOrbitalBombardmentTargetingSpecTests
{
    [Test]
    public void OrbitalBombardmentTargetsBattleGridInsteadOfStrategicPoints()
    {
        var source = ReadRepoFile("Content.Server/_WH40K/GameTicking/Rules/WH40KTeamBattleRuleSystem.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("TryResolveOrbitalTargetGrid"));
            Assert.That(source, Does.Contain("_station.GetStationInMap(mapId)"));
            Assert.That(source, Does.Contain("_station.GetLargestGrid(stationUid)"));
            Assert.That(source, Does.Contain("grid.LocalAABB"));
            Assert.That(source, Does.Contain("_map.GridTileToLocal(gridUid, grid, tile)"));
            Assert.That(source, Does.Not.Contain("EntityQueryEnumerator<WH40KStrategicPointAnchorComponent, TransformComponent>()"));
            Assert.That(source, Does.Not.Contain("EntityQueryEnumerator<WH40KStrategicPointComponent, TransformComponent>()"));
            Assert.That(source, Does.Not.Contain("EntityQueryEnumerator<WH40KInfluencePointComponent, TransformComponent>()"));
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
