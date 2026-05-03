#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Content.Tests.Shared.Localizations;

[TestFixture]
public sealed class LocaleDuplicateKeyAuditTests
{
    private static readonly Regex EntryRegex = new(@"^(-?[A-Za-z0-9][A-Za-z0-9_-]*)\s*=", RegexOptions.Compiled);

    [TestCase("en-US")]
    [TestCase("ru-RU")]
    public void LocaleContainsNoDuplicateKeys(string localeName)
    {
        var repoRoot = FindRepoRoot();
        var localeRoot = Path.Combine(repoRoot, "Resources", "Locale", localeName);
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
}
