#nullable enable
using System.IO;
using Content.Shared._WH40K.TacticalMap;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests.Shared._WH40K.TacticalMap;

[TestFixture]
public sealed class WH40KTacticalMapSnapshotCatalogTests
{
    private static readonly ResPath DefaultSnapshotPath = new("/Textures/_WH40K/Interface/TacticalMap/battlefield40k_snapshot.png");

    [TestCase("Battlefield40k", "/Maps/_WH40K/battlefield40k.yml", "/Textures/_WH40K/Interface/TacticalMap/battlefield40k_snapshot.png")]
    [TestCase("WinterAssault", "/Maps/_WH40K/WinterAssault.yml", "/Textures/_WH40K/Interface/TacticalMap/winterassault_snapshot.png")]
    [TestCase("TinyBattle", "/Maps/_WH40K/TinyBattle.yml", "/Textures/_WH40K/Interface/TacticalMap/tinybattle_snapshot.png")]
    public void KnownWh40KBattleMapsResolveDedicatedSnapshots(string mapId, string mapPath, string expectedSnapshotPath)
    {
        var resolved = WH40KTacticalMapSnapshotCatalog.ResolveSnapshotTexture(
            mapId,
            new ResPath(mapPath),
            DefaultSnapshotPath);

        Assert.That(resolved, Is.EqualTo(new ResPath(expectedSnapshotPath)));
    }

    [Test]
    public void KnownWh40KBattleSnapshotsExistOnDisk()
    {
        var repoRoot = FindRepoRoot();

        Assert.Multiple(() =>
        {
            foreach (var snapshot in new[]
                     {
                         "/Textures/_WH40K/Interface/TacticalMap/battlefield40k_snapshot.png",
                         "/Textures/_WH40K/Interface/TacticalMap/winterassault_snapshot.png",
                         "/Textures/_WH40K/Interface/TacticalMap/tinybattle_snapshot.png",
                     })
            {
                var relativePath = snapshot.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
                var absolutePath = Path.Combine(repoRoot, "Resources", relativePath);
                Assert.That(File.Exists(absolutePath), Is.True, $"Missing tactical map snapshot: {snapshot}");
            }
        });
    }

    private static string FindRepoRoot()
    {
        var cursor = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (cursor != null)
        {
            var candidate = Path.Combine(cursor.FullName, "Resources", "Prototypes");
            if (Directory.Exists(candidate))
                return cursor.FullName;

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Resources/Prototypes.");
    }
}
