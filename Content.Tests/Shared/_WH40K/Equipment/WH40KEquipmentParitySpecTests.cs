#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Equipment;

[TestFixture]
public sealed class WH40KEquipmentParitySpecTests
{
    [Test]
    public void BloodPactGlovesKeepOfficerGradeShockProtection()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Clothing/Hands/gloves.yml");
        var colonelGloves = ExtractEntityBlock(source, "ClothingHandsGlovesCombatColonel");
        var bloodPactGloves = ExtractEntityBlock(source, "ClothingHandsGlovesBloodPact");

        Assert.Multiple(() =>
        {
            Assert.That(colonelGloves, Does.Contain("- type: Insulated"));
            Assert.That(bloodPactGloves, Does.Contain("- type: Insulated"));
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
