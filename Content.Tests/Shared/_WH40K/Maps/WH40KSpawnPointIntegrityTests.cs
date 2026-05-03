#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Maps;

[TestFixture]
public sealed class WH40KSpawnPointIntegrityTests
{
    private static readonly Dictionary<string, double> ImperiumHereticsMapDividers = new()
    {
        ["TinyBattle"] = -20,
        ["Battlefield40k"] = 0,
        ["WinterAssault"] = 120,
    };

    [Test]
    public void Wh40KJobSpawnPointPrototypesMatchTheirNames()
    {
        var repoRoot = FindRepoRoot();
        var spawnPointPath = Path.Combine(repoRoot, "Resources", "Prototypes", "_WH40K", "Entities", "Markers", "Spawners", "jobs.yml");

        var spawnPointJobs = ReadSpawnPointJobs(spawnPointPath);

        Assert.Multiple(() =>
        {
            foreach (var (spawnPointId, actualJobId) in spawnPointJobs)
            {
                var expectedJobId = ExpectedJobIdForSpawnPoint(spawnPointId);
                Assert.That(actualJobId, Is.EqualTo(expectedJobId),
                    $"{spawnPointId} should point to {expectedJobId}, but currently points to {actualJobId}.");
            }
        });
    }

    [Test]
    public void Wh40KBattleMapsProvideNamedSpawnMarkersForEveryAvailableJob()
    {
        var repoRoot = FindRepoRoot();
        var mapPrototypesRoot = Path.Combine(repoRoot, "Resources", "Prototypes", "_WH40K", "Maps");
        var battleMaps = ReadBattleMaps(mapPrototypesRoot);

        Assert.Multiple(() =>
        {
            foreach (var battleMap in battleMaps.Values)
            {
                var placedSpawnPoints = ReadPlacedSpawnPoints(Path.Combine(repoRoot, NormalizeResourcePath(battleMap.MapPath)));
                var placedIds = placedSpawnPoints.Select(point => point.ProtoId).ToHashSet(StringComparer.Ordinal);

                foreach (var jobId in battleMap.AvailableJobs)
                {
                    var expectedSpawnPointId = ExpectedSpawnPointIdForJob(jobId);
                    Assert.That(placedIds.Contains(expectedSpawnPointId), Is.True,
                        $"{battleMap.MapId} is missing {expectedSpawnPointId} for available job {jobId}.");
                }
            }
        });
    }

    [Test]
    public void Wh40KBattleMapsKeepFactionSpawnPointsOnTheirOwnSide()
    {
        var repoRoot = FindRepoRoot();
        var mapPrototypesRoot = Path.Combine(repoRoot, "Resources", "Prototypes", "_WH40K", "Maps");
        var spawnPointPath = Path.Combine(repoRoot, "Resources", "Prototypes", "_WH40K", "Entities", "Markers", "Spawners", "jobs.yml");
        var battleMaps = ReadBattleMaps(mapPrototypesRoot);
        var jobSpawnPoints = ReadSpawnPointJobs(spawnPointPath);

        Assert.Multiple(() =>
        {
            foreach (var battleMap in battleMaps.Values)
            {
                Assert.That(ImperiumHereticsMapDividers.TryGetValue(battleMap.MapId, out var dividerX), Is.True,
                    $"Missing side divider for {battleMap.MapId}.");

                var placedSpawnPoints = ReadPlacedSpawnPoints(Path.Combine(repoRoot, NormalizeResourcePath(battleMap.MapPath)));

                foreach (var placedSpawnPoint in placedSpawnPoints)
                {
                    if (!jobSpawnPoints.TryGetValue(placedSpawnPoint.ProtoId, out var jobId))
                        continue;

                    var isHeretic = jobId.StartsWith('H');
                    var isOnHereticSide = placedSpawnPoint.X > dividerX;
                    var expectedSide = isHeretic ? "heretic" : "imperium";
                    var actualSide = isOnHereticSide ? "heretic" : "imperium";

                    Assert.That(isOnHereticSide, Is.EqualTo(isHeretic),
                        $"{battleMap.MapId} places {placedSpawnPoint.ProtoId} at ({placedSpawnPoint.X}, {placedSpawnPoint.Y}) on the {actualSide} side, but it belongs to the {expectedSide} side.");
                }
            }
        });
    }

    private static Dictionary<string, BattleMapInfo> ReadBattleMaps(string root)
    {
        var result = new Dictionary<string, BattleMapInfo>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(root, "*.yml", SearchOption.TopDirectoryOnly))
        {
            string? currentType = null;
            string? currentMapId = null;
            string? currentMapPath = null;
            HashSet<string>? currentAvailableJobs = null;
            var inAvailableJobs = false;

            foreach (var rawLine in File.ReadLines(file))
            {
                var line = rawLine;

                if (line.StartsWith("- type: "))
                {
                    if (currentType == "gameMap" && currentMapId != null && currentMapPath != null && currentAvailableJobs != null)
                        result[currentMapId] = new BattleMapInfo(currentMapId, currentMapPath, currentAvailableJobs);

                    currentType = line["- type: ".Length..].Trim();
                    currentMapId = null;
                    currentMapPath = null;
                    currentAvailableJobs = null;
                    inAvailableJobs = false;
                    continue;
                }

                if (currentType != "gameMap")
                    continue;

                if (currentMapId == null && line.StartsWith("  id: "))
                {
                    currentMapId = line["  id: ".Length..].Trim();
                    continue;
                }

                if (currentMapPath == null && line.StartsWith("  mapPath: "))
                {
                    currentMapPath = line["  mapPath: ".Length..].Trim();
                    continue;
                }

                if (line.Trim() == "availableJobs:")
                {
                    currentAvailableJobs = new HashSet<string>(StringComparer.Ordinal);
                    inAvailableJobs = true;
                    continue;
                }

                if (!inAvailableJobs || currentAvailableJobs == null)
                    continue;

                if (!line.StartsWith("            "))
                {
                    inAvailableJobs = false;
                    continue;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                var separator = trimmed.IndexOf(':');
                if (separator <= 0)
                    continue;

                currentAvailableJobs.Add(trimmed[..separator].Trim());
            }

            if (currentType == "gameMap" && currentMapId != null && currentMapPath != null && currentAvailableJobs != null)
                result[currentMapId] = new BattleMapInfo(currentMapId, currentMapPath, currentAvailableJobs);
        }

        return result;
    }

    private static Dictionary<string, string> ReadSpawnPointJobs(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentEntityId = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine;

            if (line.StartsWith("- type: "))
            {
                currentEntityId = null;
                continue;
            }

            if (currentEntityId == null && line.StartsWith("  id: "))
            {
                currentEntityId = line["  id: ".Length..].Trim();
                continue;
            }

            if (currentEntityId != null && line.TrimStart().StartsWith("job_id: ", StringComparison.Ordinal))
            {
                result[currentEntityId] = line.Trim()["job_id: ".Length..].Trim();
                currentEntityId = null;
            }
        }

        return result;
    }

    private static List<PlacedSpawnPoint> ReadPlacedSpawnPoints(string path)
    {
        var result = new List<PlacedSpawnPoint>();
        string? currentProtoId = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine;

            if (line.StartsWith("- proto: "))
            {
                currentProtoId = line["- proto: ".Length..].Trim();
                continue;
            }

            if (currentProtoId == null || !currentProtoId.StartsWith("SpawnPoint", StringComparison.Ordinal))
                continue;

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("pos: ", StringComparison.Ordinal))
                continue;

            var coordinates = trimmed["pos: ".Length..].Split(',');
            if (coordinates.Length != 2)
                continue;

            result.Add(new PlacedSpawnPoint(
                currentProtoId,
                double.Parse(coordinates[0], CultureInfo.InvariantCulture),
                double.Parse(coordinates[1], CultureInfo.InvariantCulture)));
        }

        return result;
    }

    private static string ExpectedJobIdForSpawnPoint(string spawnPointId)
    {
        var suffix = spawnPointId["SpawnPoint".Length..];

        if (suffix.StartsWith("HStation", StringComparison.Ordinal))
            return "H" + suffix["HStation".Length..];

        if (suffix.StartsWith("Station", StringComparison.Ordinal))
            return suffix["Station".Length..];

        return suffix;
    }

    private static string ExpectedSpawnPointIdForJob(string jobId)
    {
        return jobId switch
        {
            "Enginseer" => "SpawnPointStationEnginseer",
            "HEnginseer" => "SpawnPointHStationEnginseer",
            _ => $"SpawnPoint{jobId}",
        };
    }

    private static string NormalizeResourcePath(string resourcePath)
    {
        return Path.Combine("Resources", resourcePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));
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

    private sealed record BattleMapInfo(string MapId, string MapPath, HashSet<string> AvailableJobs);

    private sealed record PlacedSpawnPoint(string ProtoId, double X, double Y);
}
