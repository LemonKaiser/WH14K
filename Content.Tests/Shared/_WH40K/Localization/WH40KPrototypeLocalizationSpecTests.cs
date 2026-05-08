#nullable enable
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Localization;

[TestFixture]
public sealed class WH40KPrototypeLocalizationSpecTests
{
    [Test]
    public void StrategicPointEntitiesUsePrototypeLocalizationInsteadOfRawLocKeys()
    {
        var points = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Structures/StrategicPoints/points.yml");
        var ruEntities = ReadRepoFile("Resources/Locale/ru-RU/_wh40k/prototypes/entities.ftl");
        var enEntities = ReadRepoFile("Resources/Locale/en-US/_wh40k/prototypes/entities.ftl");

        Assert.Multiple(() =>
        {
            Assert.That(points, Does.Not.Contain("name: wh40k-strategic-point-"));
            Assert.That(points, Does.Not.Contain("description: wh40k-strategic-point-"));

            Assert.That(ruEntities, Does.Contain("ent-WH40KStrategicPointAnchorResource = буровая площадка"));
            Assert.That(ruEntities, Does.Contain("ent-WH40KStrategicPointAnchorResearch = ноктолит"));
            Assert.That(ruEntities, Does.Contain("ent-WH40KStrategicPointResourceT1 = ресурсная точка"));

            Assert.That(enEntities, Does.Contain("ent-WH40KStrategicPointAnchorResource = drill site"));
            Assert.That(enEntities, Does.Contain("ent-WH40KStrategicPointAnchorResearch = noktolit"));
            Assert.That(enEntities, Does.Contain("ent-WH40KStrategicPointResourceT1 = resource point"));
        });
    }

    [Test]
    public void JobSpawnerSuffixesDoNotContainMojibake()
    {
        var jobs = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Markers/Spawners/jobs.yml");
        var ruEntities = ReadRepoFile("Resources/Locale/ru-RU/_wh40k/prototypes/entities.ftl");

        Assert.Multiple(() =>
        {
            Assert.That(jobs, Does.Not.Contain("РРјРїРµСЂРёСѓРј"));
            Assert.That(jobs, Does.Not.Contain("Р•СЂРµС‚РёРє"));
            Assert.That(jobs, Does.Contain("suffix: WH40K, Imperium"));
            Assert.That(jobs, Does.Contain("suffix: WH40K, Heretics"));
            Assert.That(jobs, Does.Not.Contain("name: novice"));
            Assert.That(jobs, Does.Not.Contain("name: magos"));
            Assert.That(ruEntities, Does.Contain("ent-SpawnPointNovice = новиций"));
            Assert.That(ruEntities, Does.Contain("ent-SpawnPointMagos = магос"));
            Assert.That(ruEntities, Does.Contain("ent-SpawnPointHNovice = новиций"));
            Assert.That(ruEntities, Does.Contain("ent-SpawnPointHMagos = магос"));
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
