#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.StrategicPoints;

[TestFixture]
public sealed class WH40KStrategicPointSpecTests
{
    private static readonly Regex StrategicPointLocKeyRegex = new(
        @"wh40k-strategic-point-[a-z0-9-]+",
        RegexOptions.Compiled);

    [Test]
    public void ProfilesMatchThreePointEconomySpec()
    {
        var profiles = ReadRepoFile("Resources/Prototypes/_WH40K/StrategicPoints/profiles.yml");

        AssertProfile(
            profiles,
            "WH40KStrategicPointResource",
            "Resource",
            "fundsIncome",
            new[] { 30, 70, 130 },
            new[] { "researchIncome", "influenceIncome" },
            "Uranium");

        AssertProfile(
            profiles,
            "WH40KStrategicPointResearch",
            "Research",
            "researchIncome",
            new[] { 15, 40, 80 },
            new[] { "fundsIncome", "influenceIncome" },
            "Plasma");

        AssertProfile(
            profiles,
            "WH40KStrategicPointInfluence",
            "Influence",
            "influenceIncome",
            new[] { 1, 2, 4 },
            new[] { "fundsIncome", "researchIncome" },
            "Plasteel");
    }

    [Test]
    public void ConstructionRecipesBindOnlyToMatchingFreeAnchors()
    {
        var recipes = ReadRepoFile("Resources/Prototypes/_WH40K/Recipes/Construction/strategic_points.yml");
        var graphs = ReadRepoFile("Resources/Prototypes/_WH40K/Recipes/Construction/strategic_points_graphs.yml");

        AssertRecipe(recipes, "WH40KStrategicResourcePointT1", "Resource", "0.9", canBuildInImpassable: true, requiresClearTile: false);
        AssertRecipe(recipes, "WH40KStrategicResearchPointT1", "Research", "0.9", canBuildInImpassable: true, requiresClearTile: false);
        AssertRecipe(recipes, "WH40KStrategicInfluencePointT1", "Influence", "0.9", canBuildInImpassable: true, requiresClearTile: false);

        AssertGraph(graphs, "WH40KStrategicResourcePoint", "Resource", "WH40KStrategicPointResource", "WH40KStrategicPointResourceT1", "0.9");
        AssertGraph(graphs, "WH40KStrategicResearchPoint", "Research", "WH40KStrategicPointResearch", "WH40KStrategicPointResearchT1", "0.9");
        AssertGraph(graphs, "WH40KStrategicInfluencePoint", "Influence", "WH40KStrategicPointInfluence", "WH40KStrategicPointInfluenceT1", "0.9");
    }

    [Test]
    public void PointPrototypesExposeUiRepairHealthBarsAndDataDrivenLayout()
    {
        var points = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Structures/StrategicPoints/points.yml");

        var resourceAnchor = EntityBlock(points, "WH40KStrategicPointAnchorResource");
        var researchAnchor = EntityBlock(points, "WH40KStrategicPointAnchorResearch");
        var influenceAnchor = EntityBlock(points, "WH40KStrategicPointAnchorInfluence");

        Assert.Multiple(() =>
        {
            Assert.That(resourceAnchor, Does.Contain("pointType: Resource"));
            Assert.That(resourceAnchor, Does.Contain("buildRadius: 0.9"));
            Assert.That(resourceAnchor, Does.Not.Contain("hideSpriteWhenBuilt: true"));

            Assert.That(researchAnchor, Does.Contain("pointType: Research"));
            Assert.That(researchAnchor, Does.Contain("sprite: _WH40K/StrategicPoints/research/noktolit_chaos.rsi"));
            Assert.That(researchAnchor, Does.Contain("offset: 0,0.5"));
            Assert.That(researchAnchor, Does.Contain("buildRadius: 1.9"));
            Assert.That(researchAnchor, Does.Contain("builtOffset: 1,1"));

            Assert.That(influenceAnchor, Does.Contain("pointType: Influence"));
            Assert.That(influenceAnchor, Does.Contain("buildRadius: 0.9"));
            Assert.That(influenceAnchor, Does.Contain("hideSpriteWhenBuilt: true"));
        });

        AssertPointPrototype(points, "WH40KStrategicPointResourceT1", "Resource", "WH40KStrategicPointResource", "WH40KStrategicResourcePoint");
        AssertPointPrototype(points, "WH40KStrategicPointResearchT1", "Research", "WH40KStrategicPointResearch", "WH40KStrategicResearchPoint");
        AssertPointPrototype(points, "WH40KStrategicPointInfluenceT1", "Influence", "WH40KStrategicPointInfluence", "WH40KStrategicInfluencePoint");
    }

    [Test]
    public void StrategicPointLocalizationKeysUsedByCodeAndPrototypesExistInEnglishAndRussian()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var relativePath in new[]
                 {
                     "Content.Server/_WH40K/StrategicPoints/WH40KStrategicPointSystem.cs",
                     "Content.Client/_WH40K/StrategicPoints/UI/WH40KStrategicPointWindow.xaml.cs",
                     "Resources/Prototypes/_WH40K/Entities/Structures/StrategicPoints/points.yml",
                     "Resources/Prototypes/_WH40K/Recipes/Construction/strategic_points.yml"
                 })
        {
            foreach (Match match in StrategicPointLocKeyRegex.Matches(ReadRepoFile(relativePath)))
                keys.Add(match.Value);
        }

        var enKeys = FtlKeys(ReadRepoFile("Resources/Locale/en-US/_wh40k/strategic-points.ftl"));
        var ruKeys = FtlKeys(ReadRepoFile("Resources/Locale/ru-RU/_wh40k/strategic-points.ftl"));

        Assert.Multiple(() =>
        {
            Assert.That(keys.Except(enKeys).ToArray(), Is.Empty, "Missing English strategic point localization keys.");
            Assert.That(keys.Except(ruKeys).ToArray(), Is.Empty, "Missing Russian strategic point localization keys.");
        });
    }

    [Test]
    public void AppearanceEnumsAreNetSerializable()
    {
        var typesSource = ReadRepoFile("Content.Shared/_WH40K/StrategicPoints/WH40KStrategicPointTypes.cs");

        Assert.Multiple(() =>
        {
            Assert.That(typesSource, Does.Contain("[Serializable, NetSerializable]\npublic enum WH40KStrategicPointType"));
            Assert.That(typesSource, Does.Contain("[Serializable, NetSerializable]\npublic enum WH40KStrategicPointTier"));
            Assert.That(typesSource, Does.Contain("[Serializable, NetSerializable]\npublic enum WH40KStrategicPointVisuals"));
            Assert.That(typesSource, Does.Contain("[Serializable, NetSerializable]\npublic enum WH40KStrategicPointVisualLayers"));
        });
    }

    [Test]
    public void StrategicPointPlacementAndProtectionUseWh40KSpecificRules()
    {
        var sharedCondition = ReadRepoFile("Content.Shared/_WH40K/StrategicPoints/Construction/WH40KStrategicPointAnchorCondition.cs");
        var clientPlacement = ReadRepoFile("Content.Client/_WH40K/StrategicPoints/WH40KStrategicPointPlacement.cs");
        var serverSystem = ReadRepoFile("Content.Server/_WH40K/StrategicPoints/WH40KStrategicPointSystem.cs");
        var serverConstruction = ReadRepoFile("Content.Server/Construction/ConstructionSystem.Initial.cs");
        var sharedEvents = ReadRepoFile("Content.Shared/Construction/Events.cs");
        var points = ReadRepoFile("Resources/Prototypes/_WH40K/Entities/Structures/StrategicPoints/points.yml");
        var ruCategories = ReadRepoFile("Resources/Locale/ru-RU/construction/construction-categories.ftl");
        var enCategories = ReadRepoFile("Resources/Locale/en-US/construction/construction-categories.ftl");

        Assert.Multiple(() =>
        {
            Assert.That(sharedCondition, Does.Contain("anchor.BuiltOffset"));
            Assert.That(clientPlacement, Does.Contain("anchor.BuiltOffset"));
            Assert.That(clientPlacement, Does.Contain("public EntityUid PreviewAnchorUid => _previewAnchorUid;"));
            Assert.That(clientPlacement, Does.Contain("candidate.BuildRadius"));
            Assert.That(clientPlacement, Does.Contain("return !IsAnchorOccupied(_previewAnchorUid, anchor);"));
            Assert.That(serverConstruction, Does.Contain("WH40KPendingStrategicAnchorComponent"));
            Assert.That(serverConstruction, Does.Contain("ValidatePlacement(constructionPrototype, user, location, ev.Angle.GetCardinalDir(), placementTarget)"));
            Assert.That(sharedEvents, Does.Contain("public readonly NetEntity? PlacementTarget;"));
            Assert.That(serverSystem, Does.Contain("BeforeDamageChangedEvent"));
            Assert.That(serverSystem, Does.Contain("!args.Damage.AnyPositive()"));
            Assert.That(serverSystem, Does.Contain("_attackerResolver.TryResolveAttacker"));
            Assert.That(serverSystem, Does.Contain("SnapBuiltPointToAnchor(pointUid, anchorUid, anchor);"));
            Assert.That(serverSystem, Does.Contain("string.Equals(teamId, ent.Comp.OwnerTeamId"));
            Assert.That(points, Does.Contain("type: Anchorable"));
            Assert.That(points, Does.Contain("flags:\n    - Anchorable"));
            Assert.That(ruCategories, Does.Contain("construction-category-points ="));
            Assert.That(enCategories, Does.Contain("construction-category-points = Points"));
        });
    }

    [Test]
    public void StrategicPointLocalizationTextIsReadableAndMatchesIntendedLabels()
    {
        var ru = ReadRepoFile("Resources/Locale/ru-RU/_wh40k/strategic-points.ftl");
        var en = ReadRepoFile("Resources/Locale/en-US/_wh40k/strategic-points.ftl");
        var ruCategories = ReadRepoFile("Resources/Locale/ru-RU/construction/construction-categories.ftl");
        var enCategories = ReadRepoFile("Resources/Locale/en-US/construction/construction-categories.ftl");

        Assert.Multiple(() =>
        {
            Assert.That(ru, Does.Contain("wh40k-strategic-point-resource-anchor-name = буровая площадка"));
            Assert.That(ru, Does.Contain("wh40k-strategic-point-research-anchor-name = ноктолит"));
            Assert.That(ru, Does.Contain("wh40k-strategic-point-research-anchor-desc = Декоративный ноктолит отмечает место для исследовательской точки. Исследовательский узел строится на соседней плитке."));
            Assert.That(ru, Does.Contain("wh40k-strategic-point-resource-t1-name = ресурсная точка"));
            Assert.That(ru, Does.Contain("wh40k-strategic-point-research-t1-name = исследовательская точка"));
            Assert.That(ru, Does.Contain("wh40k-strategic-point-influence-t1-name = точка влияния"));
            Assert.That(ruCategories, Does.Contain("construction-category-points = Точки"));

            Assert.That(en, Does.Contain("wh40k-strategic-point-resource-anchor-name = drill site"));
            Assert.That(en, Does.Contain("wh40k-strategic-point-research-anchor-name = noktolit"));
            Assert.That(en, Does.Contain("wh40k-strategic-point-resource-t1-name = resource point"));
            Assert.That(en, Does.Contain("wh40k-strategic-point-research-t1-name = research point"));
            Assert.That(en, Does.Contain("wh40k-strategic-point-influence-t1-name = influence point"));
            Assert.That(enCategories, Does.Contain("construction-category-points = Points"));
        });
    }

    private static void AssertProfile(
        string profiles,
        string id,
        string pointType,
        string incomeField,
        int[] incomeByTier,
        string[] forbiddenIncomeFields,
        string t3SpecialMaterial)
    {
        var profile = ProfileBlock(profiles, id);
        var upgrades = SectionBlock(profile, "upgrades", "tiers");
        var tiers = SectionBlock(profile, "tiers", null);

        Assert.That(profile, Does.Contain($"pointType: {pointType}"));

        for (var tier = 1; tier <= 3; tier++)
        {
            var tierBlock = NumericEntryBlock(tiers, tier, 4);
            Assert.Multiple(() =>
            {
                Assert.That(tierBlock, Does.Contain($"maxHp: {tier * 250}"));
                Assert.That(tierBlock, Does.Contain($"teamXpIncome: {tier}"));
                Assert.That(tierBlock, Does.Contain($"{incomeField}: {incomeByTier[tier - 1]}"));
                Assert.That(tierBlock, Does.Contain("destroyTeamXpReward:"));
                Assert.That(tierBlock, Does.Contain("destroyInfluenceReward:"));
            });

            foreach (var forbidden in forbiddenIncomeFields)
                Assert.That(tierBlock, Does.Not.Contain($"{forbidden}:"));
        }

        var t2Upgrade = NumericEntryBlock(upgrades, 2, 4);
        var t3Upgrade = NumericEntryBlock(upgrades, 3, 4);
        Assert.Multiple(() =>
        {
            Assert.That(t2Upgrade, Does.Contain("seconds: 30"));
            Assert.That(t2Upgrade, Does.Contain("Steel: 20"));
            Assert.That(t2Upgrade, Does.Contain("Glass: 20"));
            Assert.That(t2Upgrade, Does.Not.Contain("Uranium:"));
            Assert.That(t2Upgrade, Does.Not.Contain("Plasma:"));
            Assert.That(t2Upgrade, Does.Not.Contain("Plasteel:"));

            Assert.That(t3Upgrade, Does.Contain("seconds: 60"));
            Assert.That(t3Upgrade, Does.Contain("Steel: 40"));
            Assert.That(t3Upgrade, Does.Contain("Glass: 40"));
            Assert.That(t3Upgrade, Does.Contain($"{t3SpecialMaterial}: 10"));
        });
    }

    private static void AssertRecipe(
        string recipes,
        string id,
        string pointType,
        string maxDistance,
        bool canBuildInImpassable,
        bool requiresClearTile)
    {
        var recipe = PrototypeBlock(recipes, "construction", id);
        Assert.Multiple(() =>
        {
            Assert.That(recipe, Does.Contain("category: construction-category-points"));
            Assert.That(recipe, Does.Contain("placementMode: WH40KStrategicPointPlacement"));
            Assert.That(recipe, Does.Contain("canRotate: false"));
            Assert.That(recipe, Does.Contain($"canBuildInImpassable: {canBuildInImpassable.ToString().ToLowerInvariant()}"));
            Assert.That(recipe, Does.Contain("!type:WH40KStrategicPointAnchorCondition"));
            Assert.That(recipe, Does.Contain($"pointType: {pointType}"));
            Assert.That(recipe, Does.Contain($"maxDistance: {maxDistance}"));
        });

        if (requiresClearTile)
            Assert.That(recipe, Does.Contain("!type:TileNotBlocked"));
        else
            Assert.That(recipe, Does.Not.Contain("!type:TileNotBlocked"));
    }

    private static void AssertGraph(
        string graphs,
        string id,
        string pointType,
        string profile,
        string entity,
        string maxDistance)
    {
        var graph = PrototypeBlock(graphs, "constructionGraph", id);
        Assert.Multiple(() =>
        {
            Assert.That(graph, Does.Contain("!type:WH40KBindStrategicPoint"));
            Assert.That(graph, Does.Contain($"pointType: {pointType}"));
            Assert.That(graph, Does.Contain($"profile: {profile}"));
            Assert.That(graph, Does.Contain($"maxDistance: {maxDistance}"));
            Assert.That(graph, Does.Contain("material: Steel"));
            Assert.That(graph, Does.Contain("amount: 5"));
            Assert.That(graph, Does.Contain("material: MetalRod"));
            Assert.That(graph, Does.Contain($"entity: {entity}"));
        });
    }

    private static void AssertPointPrototype(
        string points,
        string id,
        string pointType,
        string profile,
        string graph)
    {
        var point = EntityBlock(points, id);
        Assert.Multiple(() =>
        {
            Assert.That(point, Does.Contain("type: WH40KStrategicPoint"));
            Assert.That(point, Does.Contain($"pointType: {pointType}"));
            Assert.That(point, Does.Contain($"profile: {profile}"));
            Assert.That(point, Does.Contain("type: ActivatableUI"));
            Assert.That(point, Does.Contain("type: UserInterface"));
            Assert.That(point, Does.Contain("type: Damageable"));
            Assert.That(point, Does.Contain("type: Repairable"));
            Assert.That(point, Does.Contain("damageValue: -10"));
            Assert.That(point, Does.Contain("type: Anchorable"));
            Assert.That(point, Does.Contain("type: WH40KAlwaysShowHealthBar"));
            Assert.That(point, Does.Contain("maxHealth: 250"));
            Assert.That(point, Does.Contain("useMobThresholds: false"));
            Assert.That(point, Does.Contain($"graph: {graph}"));
        });
    }

    private static HashSet<string> FtlKeys(string ftl)
    {
        return ftl
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .Select(line => line.Split('=', 2)[0].Trim())
            .Where(key => key.StartsWith("wh40k-strategic-point-", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string EntityBlock(string source, string id)
    {
        return PrototypeBlock(source, "entity", id);
    }

    private static string ProfileBlock(string source, string id)
    {
        return PrototypeBlock(source, "wh40kStrategicPointProfile", id);
    }

    private static string PrototypeBlock(string source, string type, string id)
    {
        var normalized = "\n" + source;
        var idMarker = $"\n  id: {id}";
        var idIndex = normalized.IndexOf(idMarker, StringComparison.Ordinal);
        if (idIndex < 0)
            Assert.Fail($"Could not find prototype id '{id}'.");

        var typeMarker = $"\n- type: {type}";
        var start = normalized.LastIndexOf(typeMarker, idIndex, StringComparison.Ordinal);
        if (start < 0)
            Assert.Fail($"Could not find prototype type '{type}' for id '{id}'.");

        var next = normalized.IndexOf("\n- type:", idIndex + idMarker.Length, StringComparison.Ordinal);
        return normalized[start..(next < 0 ? normalized.Length : next)];
    }

    private static string SectionBlock(string source, string section, string? nextSection)
    {
        var marker = $"\n  {section}:";
        var normalized = "\n" + source;
        var start = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            Assert.Fail($"Could not find YAML section '{section}'.");

        var end = normalized.Length;
        if (nextSection != null)
        {
            var nextMarker = $"\n  {nextSection}:";
            var next = normalized.IndexOf(nextMarker, start + marker.Length, StringComparison.Ordinal);
            if (next >= 0)
                end = next;
        }

        return normalized[start..end];
    }

    private static string NumericEntryBlock(string source, int key, int indent)
    {
        var normalized = "\n" + source;
        var marker = $"\n{new string(' ', indent)}{key}:";
        var start = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            Assert.Fail($"Could not find YAML numeric entry '{key}'.");

        var tail = normalized[(start + marker.Length)..];
        var next = new Regex($@"\n {{{indent}}}\d+:", RegexOptions.Compiled).Match(tail);
        return normalized[start..(next.Success ? start + marker.Length + next.Index : normalized.Length)];
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
