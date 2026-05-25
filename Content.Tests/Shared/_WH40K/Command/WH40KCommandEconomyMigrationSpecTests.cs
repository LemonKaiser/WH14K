#nullable enable
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Command;

[TestFixture]
public sealed class WH40KCommandEconomyMigrationSpecTests
{
    [Test]
    public void TeamBattleConfigUsesRecommendedTeamXpThresholds()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/GameRules/wh40k_team_battle_configs.yml");
        var prototype = ReadRepoFile("Content.Server/_WH40K/GameTicking/Rules/Prototypes/WH40KTeamBattleConfigPrototype.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("baseLevelThresholds: [120, 300, 600, 1000, 1500, 2200, 3100, 4200]"));
            Assert.That(prototype, Does.Contain("BaseLevelThresholds = new() { 120, 300, 600, 1000, 1500, 2200, 3100, 4200 }"));
        });
    }

    [Test]
    public void CommandTreeTimeRestrictionsAreDisabledInData()
    {
        var source = ReadRepoFile("Resources/Prototypes/_WH40K/Command/node_tree.yml");
        var lines = source
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("minRoundTimeSeconds:", System.StringComparison.Ordinal))
            .ToArray();

        Assert.That(lines, Is.Not.Empty);
        Assert.That(lines.All(line => line == "minRoundTimeSeconds: 0"), Is.True);
    }

    [Test]
    public void CommandUiTalksAboutTeamXpInsteadOfFrontPoints()
    {
        var en = ReadRepoFile("Resources/Locale/en-US/_wh40k/ui.ftl");
        var ru = ReadRepoFile("Resources/Locale/ru-RU/_wh40k/ui.ftl");
        var enOptions = ReadRepoFile("Resources/Locale/en-US/escape-menu/ui/options-menu.ftl");
        var ruOptions = ReadRepoFile("Resources/Locale/ru-RU/escape-menu/ui/options-menu.ftl");

        Assert.Multiple(() =>
        {
            Assert.That(en, Does.Contain("w40k-cmd-points = Team XP: { $points }"));
            Assert.That(ru, Does.Contain("w40k-cmd-points = Team XP: { $points }"));
            Assert.That(en, Does.Not.Contain("Front points: { $points }"));
            Assert.That(ru, Does.Not.Contain("Очки фронта: { $points }"));
            Assert.That(enOptions, Does.Contain("ui-options-wh40k-notifications-category-point = Team economy"));
            Assert.That(ruOptions, Does.Contain("ui-options-wh40k-notifications-category-point = Экономика команды"));
        });
    }

    [Test]
    public void BattleAdminCommandExposesPointDebugSubcommandsWithoutTelemetryLegacyAlias()
    {
        var source = ReadRepoFile("Content.Server/_WH40K/GameTicking/Commands/WH40KBattleAdminCommand.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("point-list"));
            Assert.That(source, Does.Contain("point-reset"));
            Assert.That(source, Does.Contain("point-set-owner"));
            Assert.That(source, Does.Contain("point-set-tier"));
            Assert.That(source, Does.Not.Contain("eco-telemetry"));
            Assert.That(source, Does.Not.Contain("telemetry <on|off>"));
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
