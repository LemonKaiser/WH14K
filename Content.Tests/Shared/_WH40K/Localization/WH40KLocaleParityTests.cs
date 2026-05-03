using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Localization;

[TestFixture]
public sealed class WH40KLocaleParityTests
{
    private static readonly string[] CriticalKeysMissingFromLocale =
    {
        "ent-ActionWH40KCharacterDevelopmentKidneyPurge",
        "ent-ActionWH40KCharacterDevelopmentWarFurnace",
        "ent-ClothingUniformJumpsuitPsykerSanctioned",
        "ent-ClothingBackpackPsykerSatchel",
        "ent-ClothingOuterPsykerWardCoat",
        "ent-ClothingShoesPsykerBoots",
    };

    private static readonly Dictionary<string, string> ExpectedEnglishTileNames = new()
    {
        ["ent-NecronFloorWH40k"] = "necron tile",
        ["ent-NecronFloorWH40k1"] = "necron tile",
        ["ent-BrickFloorWH40k"] = "brick floor",
        ["ent-BrickFloorWH40k1"] = "brick floor",
        ["ent-BrickFloorWH40k2"] = "brick floor",
        ["ent-BrickFloorWH40k3"] = "brick floor",
        ["ent-ChaosBrickAshFloorWH40k"] = "chaos brick floor",
        ["ent-ConcreteFloorWH40k"] = "concrete tile",
        ["ent-ConcreteFloorWH40k1"] = "concrete tile",
        ["ent-ConcreteFloorWH40k2"] = "concrete tile",
        ["ent-ConcreteFloorWH40k3"] = "concrete tile",
        ["ent-ConcreteFloorWH40k4"] = "concrete tile",
    };

    [Test]
    public void Wh40KLocaleKeySetsStayInSyncBetweenEnglishAndRussian()
    {
        var repoRoot = FindRepoRoot();
        var enDir = Path.Combine(repoRoot, "Resources", "Locale", "en-US", "_wh40k");
        var ruDir = Path.Combine(repoRoot, "Resources", "Locale", "ru-RU", "_wh40k");

        var enKeys = ReadFluentEntries(enDir);
        var ruKeys = ReadFluentEntries(ruDir);

        var enOnly = enKeys.Keys.Where(key => !ruKeys.ContainsKey(key)).Order().ToArray();
        var ruOnly = ruKeys.Keys.Where(key => !enKeys.ContainsKey(key)).Order().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(enOnly, Is.Empty,
                $"Keys present only in en-US/_wh40k:{System.Environment.NewLine}{string.Join(System.Environment.NewLine, enOnly)}");
            Assert.That(ruOnly, Is.Empty,
                $"Keys present only in ru-RU/_wh40k:{System.Environment.NewLine}{string.Join(System.Environment.NewLine, ruOnly)}");
        });
    }

    [Test]
    public void CriticalWh40KPlayerFacingKeysExistInBothLocales()
    {
        var repoRoot = FindRepoRoot();
        var enDir = Path.Combine(repoRoot, "Resources", "Locale", "en-US", "_wh40k");
        var ruDir = Path.Combine(repoRoot, "Resources", "Locale", "ru-RU", "_wh40k");

        var enKeys = ReadFluentEntries(enDir);
        var ruKeys = ReadFluentEntries(ruDir);

        Assert.Multiple(() =>
        {
            foreach (var key in CriticalKeysMissingFromLocale)
            {
                Assert.That(enKeys.ContainsKey(key), Is.True, $"Missing en-US key: {key}");
                Assert.That(ruKeys.ContainsKey(key), Is.True, $"Missing ru-RU key: {key}");
            }
        });
    }

    [Test]
    public void Wh40KEnglishTileNamesMatchExpectedValues()
    {
        var repoRoot = FindRepoRoot();
        var enDir = Path.Combine(repoRoot, "Resources", "Locale", "en-US", "_wh40k");
        var enEntries = ReadFluentEntries(enDir);

        Assert.Multiple(() =>
        {
            foreach (var pair in ExpectedEnglishTileNames)
            {
                Assert.That(enEntries.TryGetValue(pair.Key, out var entry), Is.True,
                    $"Missing en-US tile key: {pair.Key}");

                Assert.That(entry!.Value, Is.EqualTo(pair.Value),
                    $"Unexpected en-US tile name for {pair.Key}");
            }
        });
    }

    [Test]
    public void Wh40KFluentFilesDoNotContainTopLevelAttributes()
    {
        var repoRoot = FindRepoRoot();
        var localeRoots = new[]
        {
            Path.Combine(repoRoot, "Resources", "Locale", "en-US", "_wh40k"),
            Path.Combine(repoRoot, "Resources", "Locale", "ru-RU", "_wh40k"),
        };

        foreach (var localeRoot in localeRoots)
        {
            foreach (var file in Directory.EnumerateFiles(localeRoot, "*.ftl", SearchOption.AllDirectories))
            {
                var lineNumber = 0;
                foreach (var rawLine in File.ReadLines(file))
                {
                    lineNumber++;

                    if (string.IsNullOrWhiteSpace(rawLine) || char.IsWhiteSpace(rawLine[0]) || rawLine[0] == '#')
                        continue;

                    Assert.That(rawLine[0], Is.Not.EqualTo('.'),
                        $"{Path.GetRelativePath(repoRoot, file)}:{lineNumber} contains a top-level Fluent attribute.");
                }
            }
        }
    }

    private static Dictionary<string, FluentEntry> ReadFluentEntries(string root)
    {
        var result = new Dictionary<string, FluentEntry>();

        foreach (var file in Directory.EnumerateFiles(root, "*.ftl", SearchOption.AllDirectories))
        {
            foreach (var rawLine in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                if (char.IsWhiteSpace(rawLine[0]) || rawLine[0] == '#')
                    continue;

                var equalsIndex = rawLine.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                var key = rawLine[..equalsIndex].Trim();
                if (key.Length == 0)
                    continue;

                var value = rawLine[(equalsIndex + 1)..].Trim();
                result.TryAdd(key, new FluentEntry(file, value));
            }
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        var cursor = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (cursor != null)
        {
            var candidate = Path.Combine(cursor.FullName, "Resources", "Locale");
            if (Directory.Exists(candidate))
                return cursor.FullName;

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Resources/Locale.");
    }

    private sealed class FluentEntry
    {
        public FluentEntry(string file, string value)
        {
            File = file;
            Value = value;
        }

        public string File { get; }

        public string Value { get; }
    }
}
