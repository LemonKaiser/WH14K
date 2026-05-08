#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Content.Tests.Shared.Localization;

[TestFixture]
public sealed class RuPrototypeLocalizationSpecTests
{
    [Test]
    public void PrototypeSuffixInheritanceTargetsMustDefineSuffix()
    {
        var repoRoot = FindRepoRoot();
        var localeRoot = Path.Combine(repoRoot, "Resources", "Locale", "ru-RU", "ss14-ru", "prototypes");
        var entryRegex = new Regex(@"^ent-([A-Za-z0-9_-]+)\s*=", RegexOptions.Compiled);
        var attributeRegex = new Regex(@"^\s+\.([A-Za-z0-9_-]+)\s*=\s*(.*)$", RegexOptions.Compiled);
        var suffixReferenceRegex = new Regex(@"\{\s*(ent-[A-Za-z0-9_-]+)\.suffix\s*\}", RegexOptions.Compiled);
        var entries = new Dictionary<string, HashSet<string>>();

        foreach (var file in Directory.EnumerateFiles(localeRoot, "*.ftl", SearchOption.AllDirectories))
        {
            string? currentEntry = null;
            foreach (var line in File.ReadLines(file))
            {
                var entryMatch = entryRegex.Match(line);
                if (entryMatch.Success)
                {
                    currentEntry = $"ent-{entryMatch.Groups[1].Value}";
                    if (!entries.ContainsKey(currentEntry))
                        entries[currentEntry] = new HashSet<string>();

                    continue;
                }

                var attributeMatch = attributeRegex.Match(line);
                if (attributeMatch.Success && currentEntry != null)
                    entries[currentEntry].Add(attributeMatch.Groups[1].Value);
            }
        }

        var missingTargets = new List<string>();
        foreach (var file in Directory.EnumerateFiles(localeRoot, "*.ftl", SearchOption.AllDirectories))
        {
            string? currentEntry = null;
            var lineNumber = 0;

            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;

                var entryMatch = entryRegex.Match(line);
                if (entryMatch.Success)
                {
                    currentEntry = $"ent-{entryMatch.Groups[1].Value}";
                    continue;
                }

                var attributeMatch = attributeRegex.Match(line);
                if (!attributeMatch.Success || currentEntry == null || attributeMatch.Groups[1].Value != "suffix")
                    continue;

                var suffixReferenceMatch = suffixReferenceRegex.Match(attributeMatch.Groups[2].Value);
                if (!suffixReferenceMatch.Success)
                    continue;

                var target = suffixReferenceMatch.Groups[1].Value;
                if (!entries.TryGetValue(target, out var attributes) || !attributes.Contains("suffix"))
                {
                    missingTargets.Add(
                        $"{Path.GetRelativePath(repoRoot, file)}:{lineNumber}: {currentEntry} -> {target}");
                }
            }
        }

        Assert.That(missingTargets, Is.Empty,
            "Missing .suffix definitions for referenced ru-RU prototype localization entries:\n" +
            string.Join("\n", missingTargets.OrderBy(x => x)));
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
