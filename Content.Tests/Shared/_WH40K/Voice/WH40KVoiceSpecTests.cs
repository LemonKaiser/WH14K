#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Voice;

[TestFixture]
public sealed class WH40KVoiceSpecTests
{
    [Test]
    public void DwarfBattlecryEmoteSoundsAreConfigured()
    {
        var source = ReadRepoFile("Resources/Prototypes/Voice/speech_emote_sounds.yml");
        var unisexDwarf = ExtractBlock(source, "id: UnisexDwarf");
        var femaleDwarf = ExtractBlock(source, "id: FemaleDwarf");

        Assert.Multiple(() =>
        {
            Assert.That(unisexDwarf, Does.Contain("Battlecry:"));
            Assert.That(unisexDwarf, Does.Contain("collection: MaleBattlecry"));
            Assert.That(femaleDwarf, Does.Contain("Battlecry:"));
            Assert.That(femaleDwarf, Does.Contain("collection: FemaleBattlecry"));
        });
    }

    private static string ExtractBlock(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start == -1)
        {
            Assert.Fail($"Could not find block marker '{marker}'.");
            return string.Empty;
        }

        var blockStart = source.LastIndexOf("- type: emoteSounds", start, StringComparison.Ordinal);
        if (blockStart == -1)
            blockStart = start;

        var nextBlock = source.IndexOf("\n- type: emoteSounds", start, StringComparison.Ordinal);
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
