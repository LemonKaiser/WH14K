#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Jobs;

[TestFixture]
public sealed class WH40KJobRoleSpecTests
{
    [Test]
    public void ChaplainRoleDoesNotCarryClownGunFailureComponent()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/Roles/Jobs/Imperium/Chaplain.yml");
        var chaplainJob = ExtractBlock(source, "id: WH40KChaplain", "- type: startingGear");

        Assert.Multiple(() =>
        {
            Assert.That(chaplainJob, Does.Contain("- type: Vocal"));
            Assert.That(chaplainJob, Does.Not.Contain("- type: Clumsy"));
            Assert.That(chaplainJob, Does.Not.Contain("gunShootFailDamage:"));
        });
    }

    [Test]
    public void ChaosLineRolesDoNotUseImperialHudEyeGroup()
    {
        var roleLoadouts = ReadRepoFile("Resources/Prototypes/_WH40K/Loadouts/role_loadouts.yml");
        var loadoutGroups = ReadRepoFile("Resources/Prototypes/_WH40K/Loadouts/loadout_groups.yml");
        var hGuardsman = ExtractBlock(roleLoadouts, "id: JobHGuardsman", "- type: roleLoadout");
        var hVoxScout = ExtractBlock(roleLoadouts, "id: JobHVoxScout", "- type: roleLoadout");
        var hereticEyes = ExtractBlock(loadoutGroups, "id: WH40KHereticLineEyes", "- type: loadoutGroup");

        Assert.Multiple(() =>
        {
            Assert.That(hGuardsman, Does.Contain("- WH40KHereticLineEyes"));
            Assert.That(hGuardsman, Does.Not.Contain("- WH40KImperialEyes"));
            Assert.That(hVoxScout, Does.Contain("- WH40KHereticLineEyes"));
            Assert.That(hVoxScout, Does.Not.Contain("- WH40KImperialEyes"));
            Assert.That(hereticEyes, Does.Contain("name: loadout-group-wh40k-heretic-line-eyes"));
            Assert.That(hereticEyes, Does.Contain("- WH40KGuardsmanGlasses"));
            Assert.That(hereticEyes, Does.Not.Contain("- WH40KEyesGuard"));
        });
    }

    private static string ExtractBlock(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start == -1)
        {
            Assert.Fail($"Could not find start marker '{startMarker}'.");
            return string.Empty;
        }

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end == -1)
            end = source.Length;

        return source.Substring(start, end - start);
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
