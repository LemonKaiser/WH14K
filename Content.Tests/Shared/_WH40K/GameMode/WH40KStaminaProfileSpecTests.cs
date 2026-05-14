#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.GameMode;

[TestFixture]
public sealed class WH40KStaminaProfileSpecTests
{
    [Test]
    public void TeamBattleUsesModeratedSprintDrainAndReappliesDevelopmentModifiers()
    {
        var teamRuleSource = ReadRepoFile("Content.Server/_WH40K/GameTicking/Rules/WH40KTeamBattleRuleSystem.cs");
        var runtimeSource = ReadRepoFile("Content.Server/_WH40K/MetaProgress/WH40KCharacterDevelopmentRuntimeSystem.cs");

        Assert.Multiple(() =>
        {
            Assert.That(teamRuleSource, Does.Contain("private const float WH40KSprintDrain = 3f;"));
            Assert.That(teamRuleSource, Does.Contain("_characterDevelopment.RefreshStaminaProfileModifiers(ev.Mob);"));
            Assert.That(teamRuleSource, Does.Contain("_characterDevelopment.RefreshStaminaProfileModifiers(uid);"));
            Assert.That(runtimeSource, Does.Contain("after: new[] { typeof(WH40KTeamBattleRuleSystem) }"));
            Assert.That(runtimeSource, Does.Contain("public void RefreshStaminaProfileModifiers(EntityUid uid)"));
            Assert.That(runtimeSource, Does.Contain("baseline.StaminaSprintDrain = stamina.SprintDrain;"));
            Assert.That(runtimeSource, Does.Contain("baseline.StaminaWalkRecovery = stamina.WalkRecovery;"));
            Assert.That(runtimeSource, Does.Contain("ApplyStamina(uid, stamina, baseline, modifiers);"));
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
