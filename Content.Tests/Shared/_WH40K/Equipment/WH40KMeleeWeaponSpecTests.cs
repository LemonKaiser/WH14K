#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Equipment;

[TestFixture]
public sealed class WH40KMeleeWeaponSpecTests
{
    [Test]
    public void EvisceratorUsesChainswordProfileWithTwentyFivePercentMoreDamage()
    {
        var chainsword = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Objects/Weapons/Melee/chainsword.yml");
        var eviscerator = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Objects/Weapons/Melee/chainsword_evestiator.yml");

        Assert.Multiple(() =>
        {
            Assert.That(chainsword, Does.Contain("attackRate: 4"));
            Assert.That(chainsword, Does.Contain("range: 1.6"));
            Assert.That(chainsword, Does.Contain("Blunt: 1"));
            Assert.That(chainsword, Does.Contain("Slash: 10"));
            Assert.That(chainsword, Does.Contain("Slash: 18"));

            Assert.That(eviscerator, Does.Contain("attackRate: 4"));
            Assert.That(eviscerator, Does.Contain("range: 1.6"));
            Assert.That(eviscerator, Does.Contain("Blunt: 1.25"));
            Assert.That(eviscerator, Does.Contain("Slash: 12.5"));
            Assert.That(eviscerator, Does.Contain("Slash: 22.5"));
            Assert.That(eviscerator, Does.Not.Contain("Shock:"));
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
