#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Content.Tests.Shared.Localizations;

[TestFixture]
public sealed class RuPrototypeEntityLocaleAuditTests
{
    private static readonly Regex EntryRegex = new(@"^(-?[A-Za-z0-9][A-Za-z0-9_-]*)\s*=", RegexOptions.Compiled);
    private static readonly Regex EntityTypeRegex = new(@"^\s*-\s*type:\s*entity\s*$", RegexOptions.Compiled);
    private static readonly Regex EntityIdRegex = new(@"^\s*id:\s*([A-Za-z0-9][A-Za-z0-9_-]*)\s*$", RegexOptions.Compiled);

    [Test]
    public void RuPrototypeEntityLocaleContainsNoDuplicateKeys()
    {
        var repoRoot = FindRepoRoot();
        var localeRoot = Path.Combine(repoRoot, "Resources", "Locale", "ru-RU");
        var seenKeys = new Dictionary<string, string>();
        var duplicateMessages = new List<string>();

        foreach (var file in Directory.EnumerateFiles(localeRoot, "*.ftl", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(localeRoot, file);

            foreach (var line in File.ReadLines(file))
            {
                var match = EntryRegex.Match(line);
                if (!match.Success)
                    continue;

                var key = match.Groups[1].Value;
                if (seenKeys.TryGetValue(key, out var existingPath))
                {
                    duplicateMessages.Add($"{key} is declared in both {existingPath} and {relativePath}.");
                    continue;
                }

                seenKeys[key] = relativePath;
            }
        }

        Assert.That(duplicateMessages, Is.Empty, string.Join("\n", duplicateMessages));
    }

    [Test]
    public void RuPrototypeEntityLocaleContainsNoDeadEntityKeys()
    {
        var repoRoot = FindRepoRoot();
        var localeRoot = Path.Combine(repoRoot, "Resources", "Locale", "ru-RU", "ss14-ru", "prototypes", "entities");
        var prototypeRoot = Path.Combine(repoRoot, "Resources", "Prototypes");
        var liveEntityIds = LoadLiveEntityIds(prototypeRoot);
        var staleKeys = new List<string>();

        foreach (var file in Directory.EnumerateFiles(localeRoot, "*.ftl", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(localeRoot, file);

            foreach (var line in File.ReadLines(file))
            {
                var match = EntryRegex.Match(line);
                if (!match.Success)
                    continue;

                var key = match.Groups[1].Value;
                if (!key.StartsWith("ent-"))
                    continue;

                var prototypeId = key["ent-".Length..];
                if (liveEntityIds.Contains(prototypeId))
                    continue;

                staleKeys.Add($"{key} in {relativePath} no longer matches any live entity prototype.");
            }
        }

        Assert.That(staleKeys, Is.Empty, string.Join("\n", staleKeys));
    }

    private static HashSet<string> LoadLiveEntityIds(string prototypeRoot)
    {
        var entityIds = new HashSet<string>();

        foreach (var file in Directory.EnumerateFiles(prototypeRoot, "*.yml", SearchOption.AllDirectories))
        {
            var inEntity = false;

            foreach (var line in File.ReadLines(file))
            {
                if (EntityTypeRegex.IsMatch(line))
                {
                    inEntity = true;
                    continue;
                }

                if (line.StartsWith("- type: "))
                {
                    inEntity = false;
                    continue;
                }

                if (!inEntity)
                    continue;

                var match = EntityIdRegex.Match(line);
                if (!match.Success)
                    continue;

                entityIds.Add(match.Groups[1].Value);
                inEntity = false;
            }
        }

        return entityIds;
    }

    private static string FindRepoRoot()
    {
        var cursor = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (cursor != null)
        {
            var candidate = Path.Combine(cursor.FullName, "Resources", "Prototypes");
            if (Directory.Exists(candidate))
                return cursor.FullName;

            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Resources/Prototypes.");
    }
}
