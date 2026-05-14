#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.StrategicPoints;

[TestFixture]
public sealed class WH40KStrategicPointDurabilitySpecTests
{
    [Test]
    public void StrategicPointsAreLockedBackToTheGridAfterConstruction()
    {
        var source = ReadRepoFile("Content.Server/_WH40K/StrategicPoints/WH40KStrategicPointSystem.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("EnsureStrategicEntityLocked(pointUid, anchorCoordinates.Offset(anchor.BuiltOffset));"));
            Assert.That(source, Does.Contain("EnsureStrategicEntityLocked(ent.Owner);"));
            Assert.That(source, Does.Contain("EnsureStrategicPointLocked(ent.Owner, ent.Comp);"));
            Assert.That(source, Does.Contain("_transform.AnchorEntity(uid, xform);"));
            Assert.That(source, Does.Contain("_physics.SetBodyType(uid, BodyType.Static, body: physics);"));
        });
    }

    [Test]
    public void TierOneStrategicPointsKeepSeventyPercentExplosionResistance()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Structures/StrategicPoints/points.yml");
        var resource = ExtractEntityBlock(source, "WH40KStrategicPointResourceT1");
        var research = ExtractEntityBlock(source, "WH40KStrategicPointResearchT1");
        var influence = ExtractEntityBlock(source, "WH40KStrategicPointInfluenceT1");

        Assert.Multiple(() =>
        {
            Assert.That(resource, Does.Contain("- type: ExplosionResistance"));
            Assert.That(resource, Does.Contain("damageCoefficient: 0.3"));
            Assert.That(research, Does.Contain("- type: ExplosionResistance"));
            Assert.That(research, Does.Contain("damageCoefficient: 0.3"));
            Assert.That(influence, Does.Contain("- type: ExplosionResistance"));
            Assert.That(influence, Does.Contain("damageCoefficient: 0.3"));
        });
    }

    private static string ExtractEntityBlock(string source, string prototypeId)
    {
        var marker = $"id: {prototypeId}";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start == -1)
        {
            Assert.Fail($"Could not find prototype '{prototypeId}'.");
            return string.Empty;
        }

        var blockStart = source.LastIndexOf("- type: entity", start, StringComparison.Ordinal);
        if (blockStart == -1)
            blockStart = start;

        var nextBlock = source.IndexOf("\n- type: entity", start, StringComparison.Ordinal);
        if (nextBlock == -1)
            nextBlock = source.Length;

        return source.Substring(blockStart, nextBlock - blockStart);
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
