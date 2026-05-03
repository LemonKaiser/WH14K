#nullable enable
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Jobs;

[TestFixture]
public sealed class WH40KJobDogTagTests
{
    private static readonly Dictionary<string, string> DistinctJobsThatMustKeepTheirOwnDogTags = new()
    {
        ["VoxScout"] = "VoxScout",
        ["HVoxScout"] = "HVoxScout",
        ["WH40KChaplain"] = "WH40KChaplain",
        ["Major"] = "Major",
        ["HEnginseer"] = "HEnginseer",
        ["HExplorator"] = "HExplorator",
        ["HGenetor"] = "HGenetor",
        ["HMagos"] = "HMagos",
        ["HNovice"] = "HNovice",
    };

    [Test]
    public void DistinctWh40KJobsUseMatchingDogTags()
    {
        var repoRoot = FindRepoRoot();
        var jobsRoot = Path.Combine(repoRoot, "Resources", "Prototypes", "_WH40K", "Roles", "Jobs");
        var dogTagsPath = Path.Combine(repoRoot, "Resources", "Prototypes", "_WH40K", "Entities", "Objects", "Misc", "dog_tags.yml");

        var jobs = ReadJobs(jobsRoot);
        var startingGear = ReadStartingGear(jobsRoot);
        var dogTags = ReadDogTags(dogTagsPath);

        Assert.Multiple(() =>
        {
            foreach (var pair in DistinctJobsThatMustKeepTheirOwnDogTags)
            {
                Assert.That(jobs.TryGetValue(pair.Key, out var gearId), Is.True, $"Missing job prototype: {pair.Key}");
                Assert.That(startingGear.TryGetValue(gearId!, out var dogTagId), Is.True,
                    $"Missing starting gear or dog tag slot for {pair.Key}: {gearId}");
                Assert.That(dogTags.TryGetValue(dogTagId!, out var dogTagJob), Is.True,
                    $"Missing dog tag preset mapping for {pair.Key}: {dogTagId}");
                Assert.That(dogTagJob, Is.EqualTo(pair.Value),
                    $"Dog tag job mismatch for {pair.Key}. Gear {gearId} uses {dogTagId}, which is preset as {dogTagJob}.");
            }
        });
    }

    private static Dictionary<string, string> ReadJobs(string root)
    {
        var result = new Dictionary<string, string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories))
        {
            string? currentType = null;
            string? currentJobId = null;
            string? currentStartingGear = null;

            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith("- type: "))
                {
                    if (currentType == "job" && currentJobId != null && currentStartingGear != null)
                        result[currentJobId] = currentStartingGear;

                    currentType = line["- type: ".Length..].Trim();
                    currentJobId = null;
                    currentStartingGear = null;
                    continue;
                }

                if (currentType != "job")
                    continue;

                if (currentJobId == null && line.StartsWith("  id: "))
                {
                    currentJobId = line["  id: ".Length..].Trim();
                    continue;
                }

                if (currentStartingGear == null && line.StartsWith("  startingGear: "))
                    currentStartingGear = line["  startingGear: ".Length..].Trim();
            }

            if (currentType == "job" && currentJobId != null && currentStartingGear != null)
                result[currentJobId] = currentStartingGear;
        }

        return result;
    }

    private static Dictionary<string, string> ReadStartingGear(string root)
    {
        var result = new Dictionary<string, string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories))
        {
            string? currentType = null;
            string? currentGearId = null;
            string? currentDogTagId = null;
            var inEquipment = false;

            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith("- type: "))
                {
                    if (currentType == "startingGear" && currentGearId != null && currentDogTagId != null)
                        result[currentGearId] = currentDogTagId;

                    currentType = line["- type: ".Length..].Trim();
                    currentGearId = null;
                    currentDogTagId = null;
                    inEquipment = false;
                    continue;
                }

                if (currentType != "startingGear")
                    continue;

                if (currentGearId == null && line.StartsWith("  id: "))
                {
                    currentGearId = line["  id: ".Length..].Trim();
                    continue;
                }

                if (line == "  equipment:")
                {
                    inEquipment = true;
                    continue;
                }

                if (inEquipment && line.StartsWith("  ") && !line.StartsWith("    "))
                {
                    inEquipment = false;
                }

                if (inEquipment && currentDogTagId == null && line.StartsWith("    id: "))
                    currentDogTagId = line["    id: ".Length..].Trim();
            }

            if (currentType == "startingGear" && currentGearId != null && currentDogTagId != null)
                result[currentGearId] = currentDogTagId;
        }

        return result;
    }

    private static Dictionary<string, string> ReadDogTags(string path)
    {
        var result = new Dictionary<string, string>();

        string? currentType = null;
        string? currentEntityId = null;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("- type: "))
            {
                currentType = line["- type: ".Length..].Trim();
                currentEntityId = null;
                continue;
            }

            if (currentType != "entity")
                continue;

            if (currentEntityId == null && line.StartsWith("  id: "))
            {
                currentEntityId = line["  id: ".Length..].Trim();
                continue;
            }

            if (currentEntityId != null && line.StartsWith("    job: "))
                result[currentEntityId] = line["    job: ".Length..].Trim();
        }

        return result;
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
