#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Equipment;

[TestFixture]
public sealed class WH40KSentryRetirementSpecTests
{
    [Test]
    public void SentryLaptopContentAndCodeAreRemovedEverywhere()
    {
        var commandUnlocks = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Objects/command_unlocks.yml");
        var latheUnlocks = ReadRepoFile("Resources/Prototypes/_WH40K/Recipes/Lathe/command_unlocks.yml");
        var cargoProducts = ReadRepoFile("Resources/Prototypes/_WH40K/Catalog/Cargo/products.yml");
        var nodeTree = ReadRepoFile("Resources/Prototypes/_WH40K/Command/node_tree.yml");
        var researchUnlocks = ReadRepoFile("Resources/Prototypes/_WH40K/Research/command_unlocks.yml");
        var entityNamesEn = ReadRepoFile("Resources/Locale/en-US/_wh40k/prototypes/entities.ftl");
        var entityNamesRu = ReadRepoFile("Resources/Locale/ru-RU/_wh40k/prototypes/entities.ftl");
        var researchNamesEn = ReadRepoFile("Resources/Locale/en-US/research/technologies.ftl");
        var researchNamesRu = ReadRepoFile("Resources/Locale/ru-RU/research/technologies.ftl");
        var repoRoot = FindRepoRoot();

        Assert.Multiple(() =>
        {
            Assert.That(commandUnlocks, Does.Not.Contain("WH40KSentryLaptop"));
            Assert.That(latheUnlocks, Does.Not.Contain("SentryLaptop"));
            Assert.That(cargoProducts, Does.Not.Contain("SentryLaptop"));
            Assert.That(nodeTree, Does.Not.Contain("SentryLaptop"));
            Assert.That(researchUnlocks, Does.Not.Contain("SentryLaptop"));
            Assert.That(researchUnlocks, Does.Not.Contain("WH40KEquipmentSentryLinks"));
            Assert.That(entityNamesEn, Does.Not.Contain("ent-WH40KSentryLaptop"));
            Assert.That(entityNamesRu, Does.Not.Contain("ent-WH40KSentryLaptop"));
            Assert.That(researchNamesEn, Does.Not.Contain("research-technology-wh40k-equipment-sentry-links"));
            Assert.That(researchNamesRu, Does.Not.Contain("research-technology-wh40k-equipment-sentry-links"));

            Assert.That(File.Exists(Path.Combine(repoRoot, "Resources", "Prototypes", "_WH40K", "Entities", "Objects", "Tools", "sentry_laptop.yml")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Resources", "Locale", "en-US", "_wh40k", "sentry_laptop.ftl")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Resources", "Locale", "ru-RU", "_wh40k", "sentry_laptop.ftl")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Content.Shared", "_WH40K", "Sentry", "Laptop", "WH40KSentryLaptopComponent.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Content.Shared", "_WH40K", "Sentry", "Laptop", "WH40KSentryLaptopUi.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Content.Client", "_WH40K", "Sentry", "Laptop", "WH40KSentryLaptopBoundUserInterface.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Content.Client", "_WH40K", "Sentry", "Laptop", "WH40KSentryLaptopWindow.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Content.Server", "_WH40K", "Sentry", "Laptop", "WH40KSentryLaptopSystem.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Content.Server", "_WH40K", "Sentry", "Laptop", "WH40KSentryLaptopWatcherComponent.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "Content.Server", "_WH40K", "Sentry", "Laptop", "WH40KSentryLinkedComponent.cs")), Is.False);
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
