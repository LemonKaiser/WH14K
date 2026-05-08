#nullable enable
using System.Numerics;
using Content.Client.Construction;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Construction.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Placement;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server.GameTicking;
using Content.Shared._WH40K.StrategicPoints;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K.Construction;

[TestFixture]
public sealed class WH40KStrategicPointConstructionTests : InteractionTest
{
    private const string TeamId = "Imperium";
    private const string RuleId = "WH40KTeamBattle";

    protected override PoolSettings Settings => new()
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false
    };

    [TestCase("WH40KStrategicPointAnchorInfluence", "WH40KStrategicInfluencePointT1", "WH40KStrategicPointInfluenceT1", WH40KStrategicPointType.Influence, 2.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResource", "WH40KStrategicResourcePointT1", "WH40KStrategicPointResourceT1", WH40KStrategicPointType.Resource, 5.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResearch", "WH40KStrategicResearchPointT1", "WH40KStrategicPointResearchT1", WH40KStrategicPointType.Research, 2.5f, 5.5f)]
    public async Task StrategicGhostAutomaticallyResolvesPlacementTarget(
        string anchorPrototype,
        string recipePrototype,
        string pointPrototype,
        WH40KStrategicPointType pointType,
        float anchorX,
        float anchorY)
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(anchorX, anchorY);
        await EnsureTileArea(anchorX, anchorY);
        var anchor = await Spawn(anchorPrototype, anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);

        TargetCoords = buildCoords;
        await StartConstruction(recipePrototype);
        await AssertGhostPlacementTarget(anchor);

        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await InteractUsing(Steel, 5);

        AssertPrototype(pointPrototype);
        await AssertBoundPoint(anchor, pointType, WH40KStrategicPointTier.T1);
    }

    [TestCase("WH40KStrategicPointAnchorInfluence", "WH40KStrategicInfluencePointT1", "WH40KStrategicPointInfluenceT1", WH40KStrategicPointType.Influence, 2.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResource", "WH40KStrategicResourcePointT1", "WH40KStrategicPointResourceT1", WH40KStrategicPointType.Resource, 5.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResearch", "WH40KStrategicResearchPointT1", "WH40KStrategicPointResearchT1", WH40KStrategicPointType.Research, 2.5f, 5.5f)]
    public async Task StrategicPointHoverPreviewMatchesPlacedGhost(
        string anchorPrototype,
        string recipePrototype,
        string pointPrototype,
        WH40KStrategicPointType pointType,
        float anchorX,
        float anchorY)
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(anchorX, anchorY);
        await EnsureTileArea(anchorX, anchorY);
        var anchor = await Spawn(anchorPrototype, anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);

        var hoverPlacement = await CaptureClientHoverPlacement(recipePrototype, buildCoords);
        var ghostPlacement = await CaptureClientGhostPlacementFromHover(recipePrototype, buildCoords);

        Assert.That(ghostPlacement.MapCoordinates.MapId, Is.EqualTo(hoverPlacement.MapCoordinates.MapId));
        Assert.That(
            (ghostPlacement.MapCoordinates.Position - hoverPlacement.MapCoordinates.Position).Length(),
            Is.LessThanOrEqualTo(0.01f),
            $"Expected hover preview position {hoverPlacement.MapCoordinates.Position}, but placed ghost ended up at {ghostPlacement.MapCoordinates.Position}.");
        Assert.That(
            (ghostPlacement.SpriteOffset - hoverPlacement.SpriteOffset).Length(),
            Is.LessThanOrEqualTo(0.01f),
            $"Expected hover preview sprite offset {hoverPlacement.SpriteOffset}, but placed ghost used {ghostPlacement.SpriteOffset}.");

        await StartStrategicConstructionGhost(recipePrototype, anchor);
        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await InteractUsing(Steel, 5);

        AssertPrototype(pointPrototype);
        await AssertBoundPoint(anchor, pointType, WH40KStrategicPointTier.T1);
    }

    [TestCase("WH40KStrategicPointAnchorInfluence", "WH40KStrategicInfluencePointT1", "WH40KStrategicPointInfluenceT1", WH40KStrategicPointType.Influence, 2.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResource", "WH40KStrategicResourcePointT1", "WH40KStrategicPointResourceT1", WH40KStrategicPointType.Resource, 5.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResearch", "WH40KStrategicResearchPointT1", "WH40KStrategicPointResearchT1", WH40KStrategicPointType.Research, 2.5f, 5.5f)]
    public async Task StrategicPointConstructionBuildsAndBindsToAnchor(
        string anchorPrototype,
        string recipePrototype,
        string pointPrototype,
        WH40KStrategicPointType pointType,
        float anchorX,
        float anchorY)
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(anchorX, anchorY);
        await EnsureTileArea(anchorX, anchorY);
        var anchor = await Spawn(anchorPrototype, anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(buildCoords, new Vector2(0f, -0.75f));

        await SetTile(Plating, anchorCoords, MapData.Grid);
        await SetTile(Plating, buildCoords, MapData.Grid);
        await SetTile(Plating, playerCoords, MapData.Grid);
        await SetPlayerCoords(playerCoords);

        TargetCoords = buildCoords;
        await StartConstruction(recipePrototype);
        await SetStrategicGhostPlacementTarget(anchor);
        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await RunTicks(1);
        await InteractUsing(Steel, 5);

        AssertPrototype(pointPrototype);
        await AssertBoundPoint(anchor, pointType, WH40KStrategicPointTier.T1);
    }

    [Test]
    public async Task StrategicPointUpgradeConsumesMaterialsAndAdvancesTier()
    {
        await SetupBattleBuilder(withUpgradeSkill: true);

        var anchorCoords = GridCoords(2.5f, 2.5f);
        await EnsureTileArea(2.5f, 2.5f);
        var anchor = await Spawn("WH40KStrategicPointAnchorInfluence", anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(buildCoords, new Vector2(0f, -0.75f));

        await SetTile(Plating, anchorCoords, MapData.Grid);
        await SetTile(Plating, buildCoords, MapData.Grid);
        await SetTile(Plating, playerCoords, MapData.Grid);
        await SetPlayerCoords(playerCoords);

        TargetCoords = buildCoords;
        await StartConstruction("WH40KStrategicInfluencePointT1");
        await SetStrategicGhostPlacementTarget(anchor);
        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await RunTicks(1);
        await InteractUsing(Steel, 5);

        AssertPrototype("WH40KStrategicPointInfluenceT1");
        await AssertBoundPoint(anchor, WH40KStrategicPointType.Influence, WH40KStrategicPointTier.T1);

        await InteractUsing(Steel, 20);
        await InteractUsing(Glass, 20);

        await DeleteHeldEntity();
        await Interact(awaitDoAfters: false);
        Assert.That(IsUiOpen(WH40KStrategicPointUiKey.Key), Is.True);

        await SendBui(WH40KStrategicPointUiKey.Key, new WH40KStrategicPointStartUpgradeMessage());
        await AwaitDoAfters();

        await Server.WaitAssertion(() =>
        {
            Assert.That(STarget, Is.Not.Null);
            var point = SEntMan.GetComponent<WH40KStrategicPointComponent>(STarget!.Value);
            Assert.That(point.Tier, Is.EqualTo(WH40KStrategicPointTier.T2));
            Assert.That(point.UpgradeInProgress, Is.False);
            Assert.That(point.LoadedUpgradeMaterials, Is.Empty);
            Assert.That(point.OwnerTeamId, Is.EqualTo(TeamId));
        });

        await AssertBoundPoint(anchor, WH40KStrategicPointType.Influence, WH40KStrategicPointTier.T2);
    }

    [Test]
    public async Task StrategicResearchPointConstructionStartsFromAnchorRange()
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(2.5f, 5.5f);
        await EnsureTileArea(2.5f, 5.5f);
        var anchor = await Spawn("WH40KStrategicPointAnchorResearch", anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));

        await SetTile(Plating, anchorCoords, MapData.Grid);
        await SetTile(Plating, buildCoords, MapData.Grid);
        await SetTile(Plating, playerCoords, MapData.Grid);
        await SetPlayerCoords(playerCoords);

        TargetCoords = buildCoords;
        await StartConstruction("WH40KStrategicResearchPointT1");
        await SetStrategicGhostPlacementTarget(anchor);
        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await RunTicks(1);
        await InteractUsing(Steel, 5);

        AssertPrototype("WH40KStrategicPointResearchT1");
        await AssertBoundPoint(anchor, WH40KStrategicPointType.Research, WH40KStrategicPointTier.T1);
    }

    [Test]
    public async Task StrategicResearchPointConstructionStartsViaUseOnAnchorSprite()
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(2.5f, 5.5f);
        await EnsureTileArea(2.5f, 5.5f);
        var anchor = await Spawn("WH40KStrategicPointAnchorResearch", anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));
        var clickCoords = await OffsetCoords(anchorCoords, new Vector2(-0.9f, 0.4f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost("WH40KStrategicResearchPointT1", anchor);
        var ghostPlacement = await CaptureClientPlacement(Target!.Value);

        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: clickCoords, cursorEntity: anchor);
        await RunTicks(5);
        await AwaitDoAfters();
        await CheckTargetChange();

        AssertPrototype("WH40KStrategicPointResearchT1");
        await AssertBoundPoint(anchor, WH40KStrategicPointType.Research, WH40KStrategicPointTier.T1);
        await AssertClientPlacementMatches(ghostPlacement);
    }

    [Test]
    public async Task StrategicResearchPointConstructionStartsViaUseOnGhost()
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(2.5f, 5.5f);
        await EnsureTileArea(2.5f, 5.5f);
        var anchor = await Spawn("WH40KStrategicPointAnchorResearch", anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(buildCoords, new Vector2(0f, -0.75f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost("WH40KStrategicResearchPointT1", anchor);
        var ghost = Target!.Value;
        var ghostPlacement = await CaptureClientPlacement(ghost);

        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: buildCoords, cursorEntity: ghost);
        await RunTicks(5);
        await AwaitDoAfters();
        await CheckTargetChange();

        AssertPrototype("WH40KStrategicPointResearchT1");
        await AssertBoundPoint(anchor, WH40KStrategicPointType.Research, WH40KStrategicPointTier.T1);
        await AssertClientPlacementMatches(ghostPlacement);
    }

    [Test]
    public async Task StrategicResearchPointConstructionStartsViaUseOnGhostWithoutClientPlacementTarget()
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(2.5f, 5.5f);
        await EnsureTileArea(2.5f, 5.5f);
        var anchor = await Spawn("WH40KStrategicPointAnchorResearch", anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(buildCoords, new Vector2(0f, -0.75f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost("WH40KStrategicResearchPointT1", anchor);
        var ghost = Target!.Value;
        var ghostPlacement = await CaptureClientPlacement(ghost);

        await ClearStrategicGhostPlacementTarget();
        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: buildCoords, cursorEntity: ghost);
        await RunTicks(5);
        await AwaitDoAfters();
        await CheckTargetChange();

        AssertPrototype("WH40KStrategicPointResearchT1");
        await AssertBoundPoint(anchor, WH40KStrategicPointType.Research, WH40KStrategicPointTier.T1);
        await AssertClientPlacementMatches(ghostPlacement);
    }

    [Test]
    public async Task StrategicResearchPointConstructionStartsViaUseBetweenAnchorAndGhost()
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(2.5f, 5.5f);
        await EnsureTileArea(2.5f, 5.5f);
        var anchor = await Spawn("WH40KStrategicPointAnchorResearch", anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(1.1f, 0.1f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost("WH40KStrategicResearchPointT1", anchor);
        var ghostPlacement = await CaptureClientPlacement(Target!.Value);

        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: anchorCoords, cursorEntity: anchor);
        await RunTicks(5);
        await AwaitDoAfters();
        await CheckTargetChange();

        AssertPrototype("WH40KStrategicPointResearchT1");
        await AssertBoundPoint(anchor, WH40KStrategicPointType.Research, WH40KStrategicPointTier.T1);
        await AssertClientPlacementMatches(ghostPlacement);
    }

    [TestCase("WH40KStrategicPointAnchorInfluence", "WH40KStrategicInfluencePointT1", "WH40KStrategicPointInfluenceT1", WH40KStrategicPointType.Influence, 2.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResource", "WH40KStrategicResourcePointT1", "WH40KStrategicPointResourceT1", WH40KStrategicPointType.Resource, 5.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResearch", "WH40KStrategicResearchPointT1", "WH40KStrategicPointResearchT1", WH40KStrategicPointType.Research, 2.5f, 5.5f)]
    public async Task StrategicPointConstructionStartsViaUseOnAnchor(
        string anchorPrototype,
        string recipePrototype,
        string pointPrototype,
        WH40KStrategicPointType pointType,
        float anchorX,
        float anchorY)
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(anchorX, anchorY);
        await EnsureTileArea(anchorX, anchorY);
        var anchor = await Spawn(anchorPrototype, anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost(recipePrototype, anchor);
        var ghostPlacement = await CaptureClientPlacement(Target!.Value);

        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: anchorCoords, cursorEntity: anchor);
        await RunTicks(5);
        await AwaitDoAfters();
        await CheckTargetChange();

        AssertPrototype(pointPrototype);
        await AssertBoundPoint(anchor, pointType, WH40KStrategicPointTier.T1);
        await AssertClientPlacementMatches(ghostPlacement);
    }

    [Test]
    public async Task StrategicResearchPointConstructionStartsViaUseOnAnchorWithoutClientPlacementTarget()
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(2.5f, 5.5f);
        await EnsureTileArea(2.5f, 5.5f);
        var anchor = await Spawn("WH40KStrategicPointAnchorResearch", anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost("WH40KStrategicResearchPointT1", anchor);
        var ghostPlacement = await CaptureClientPlacement(Target!.Value);

        await ClearStrategicGhostPlacementTarget();
        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: anchorCoords, cursorEntity: anchor);
        await RunTicks(5);
        await AwaitDoAfters();
        await CheckTargetChange();

        AssertPrototype("WH40KStrategicPointResearchT1");
        await AssertBoundPoint(anchor, WH40KStrategicPointType.Research, WH40KStrategicPointTier.T1);
        await AssertClientPlacementMatches(ghostPlacement);
    }

    [TestCase("WH40KStrategicPointAnchorInfluence", "WH40KStrategicInfluencePointT1", "WH40KStrategicPointInfluenceT1", WH40KStrategicPointType.Influence, 2.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResource", "WH40KStrategicResourcePointT1", "WH40KStrategicPointResourceT1", WH40KStrategicPointType.Resource, 5.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResearch", "WH40KStrategicResearchPointT1", "WH40KStrategicPointResearchT1", WH40KStrategicPointType.Research, 2.5f, 5.5f)]
    public async Task StrategicPointConstructionStartsViaUseOnGhost(
        string anchorPrototype,
        string recipePrototype,
        string pointPrototype,
        WH40KStrategicPointType pointType,
        float anchorX,
        float anchorY)
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(anchorX, anchorY);
        await EnsureTileArea(anchorX, anchorY);
        var anchor = await Spawn(anchorPrototype, anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(buildCoords, new Vector2(0f, -0.75f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost(recipePrototype, anchor);
        var ghost = Target!.Value;
        var ghostPlacement = await CaptureClientPlacement(ghost);

        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: buildCoords, cursorEntity: ghost);
        await RunTicks(5);
        await AwaitDoAfters();
        await CheckTargetChange();

        AssertPrototype(pointPrototype);
        await AssertBoundPoint(anchor, pointType, WH40KStrategicPointTier.T1);
        await AssertClientPlacementMatches(ghostPlacement);
    }

    [TestCase("WH40KStrategicPointAnchorInfluence", "WH40KStrategicInfluencePointT1", "WH40KStrategicPointInfluenceT1", WH40KStrategicPointType.Influence, 2.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResource", "WH40KStrategicResourcePointT1", "WH40KStrategicPointResourceT1", WH40KStrategicPointType.Resource, 5.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResearch", "WH40KStrategicResearchPointT1", "WH40KStrategicPointResearchT1", WH40KStrategicPointType.Research, 2.5f, 5.5f)]
    public async Task StrategicPointConstructionStartsViaUseNearAnchorWithoutEntityHit(
        string anchorPrototype,
        string recipePrototype,
        string pointPrototype,
        WH40KStrategicPointType pointType,
        float anchorX,
        float anchorY)
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(anchorX, anchorY);
        await EnsureTileArea(anchorX, anchorY);
        var anchor = await Spawn(anchorPrototype, anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost(recipePrototype, anchor);
        var ghostPlacement = await CaptureClientPlacement(Target!.Value);

        await SpawnEntity((Rod, 5), SEntMan.GetCoordinates(playerCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: anchorCoords, cursorEntity: NetEntity.Invalid);
        await RunTicks(5);
        await AwaitDoAfters();
        await CheckTargetChange();

        AssertPrototype(pointPrototype);
        await AssertBoundPoint(anchor, pointType, WH40KStrategicPointTier.T1);
        await AssertClientPlacementMatches(ghostPlacement);
    }

    [TestCase("WH40KStrategicPointAnchorInfluence", "WH40KStrategicInfluencePointT1", 2.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResource", "WH40KStrategicResourcePointT1", 5.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResearch", "WH40KStrategicResearchPointT1", 2.5f, 5.5f)]
    public async Task StrategicPointUseOnNearbyItemDoesNotStartConstruction(
        string anchorPrototype,
        string recipePrototype,
        float anchorX,
        float anchorY)
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(anchorX, anchorY);
        await EnsureTileArea(anchorX, anchorY);
        var anchor = await Spawn(anchorPrototype, anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));
        var itemCoords = await OffsetCoords(anchorCoords, new Vector2(-0.35f, -0.35f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost(recipePrototype, anchor);

        var nearbyItem = await SpawnEntity(Wrench, SEntMan.GetCoordinates(itemCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: itemCoords, cursorEntity: FromServer(nearbyItem));
        await RunTicks(5);

        await AssertAnchorHasNoBuiltPoint(anchor);
        await AssertConstructionGhostStillPresent();
    }

    [TestCase("WH40KStrategicPointAnchorInfluence", "WH40KStrategicInfluencePointT1", 2.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResource", "WH40KStrategicResourcePointT1", 5.5f, 2.5f)]
    [TestCase("WH40KStrategicPointAnchorResearch", "WH40KStrategicResearchPointT1", 2.5f, 5.5f)]
    public async Task StrategicPointUseOnNearbyItemCoordinatesWithoutEntityHitDoesNotStartConstruction(
        string anchorPrototype,
        string recipePrototype,
        float anchorX,
        float anchorY)
    {
        await SetupBattleBuilder();

        var anchorCoords = GridCoords(anchorX, anchorY);
        await EnsureTileArea(anchorX, anchorY);
        var anchor = await Spawn(anchorPrototype, anchorCoords);
        var buildCoords = await GetAnchorBuildCoords(anchor);
        var playerCoords = await OffsetCoords(anchorCoords, new Vector2(0f, -0.75f));
        var itemCoords = await OffsetCoords(anchorCoords, new Vector2(-0.35f, -0.35f));

        await PrepareStrategicUseScenario(anchorCoords, buildCoords, playerCoords);
        await StartStrategicConstructionGhost(recipePrototype, anchor);

        _ = await SpawnEntity(Wrench, SEntMan.GetCoordinates(itemCoords));
        await PlaceInHands((Steel, 5));
        await PressKey(EngineKeyFunctions.Use, coordinates: itemCoords, cursorEntity: NetEntity.Invalid);
        await RunTicks(5);

        await AssertAnchorHasNoBuiltPoint(anchor);
        await AssertConstructionGhostStillPresent();
    }

    private async Task SetupBattleBuilder(bool withUpgradeSkill = false)
    {
        await Server.WaitPost(() =>
        {
            var ticker = SEntMan.System<GameTicker>();
            ticker.StartGameRule(RuleId);

            var member = SEntMan.EnsureComponent<WH40KTeamMemberComponent>(SPlayer);
            member.TeamId = TeamId;

            if (withUpgradeSkill)
                SEntMan.EnsureComponent<WH40KStrategicPointUpgradeSkillComponent>(SPlayer);
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var teamRule = SEntMan.System<WH40KTeamBattleRuleSystem>();
            Assert.That(teamRule.TryGetTeamIdFromEntity(SPlayer, out var resolvedTeam), Is.True);
            Assert.That(resolvedTeam, Is.EqualTo(TeamId));
        });
    }

    private NetCoordinates GridCoords(float x, float y)
    {
        var coords = Transform.WithEntityId(MapData.GridCoords.Offset(new Vector2(x, y)), MapData.MapUid);
        return SEntMan.GetNetCoordinates(coords);
    }

    private async Task<NetCoordinates> OffsetCoords(NetCoordinates coordinates, Vector2 offset)
    {
        var result = default(NetCoordinates);
        await Server.WaitPost(() =>
        {
            result = SEntMan.GetNetCoordinates(SEntMan.GetCoordinates(coordinates).Offset(offset));
        });

        return result;
    }

    private async Task<NetCoordinates> GetAnchorBuildCoords(NetEntity anchor)
    {
        var buildCoords = default(NetCoordinates);
        await Server.WaitPost(() =>
        {
            var anchorUid = ToServer(anchor);
            var anchorComp = SEntMan.GetComponent<WH40KStrategicPointAnchorComponent>(anchorUid);
            var anchorXform = SEntMan.GetComponent<TransformComponent>(anchorUid);
            buildCoords = SEntMan.GetNetCoordinates(anchorXform.Coordinates.Offset(anchorComp.BuiltOffset));
        });

        return buildCoords;
    }

    private async Task SetPlayerCoords(NetCoordinates coordinates)
    {
        PlayerCoords = coordinates;
        await Server.WaitPost(() => Transform.SetCoordinates(SPlayer, SEntMan.GetCoordinates(coordinates)));
        await RunTicks(5);
    }

    private async Task PrepareStrategicUseScenario(
        NetCoordinates anchorCoords,
        NetCoordinates buildCoords,
        NetCoordinates playerCoords)
    {
        await SetTile(Plating, anchorCoords, MapData.Grid);
        await SetTile(Plating, buildCoords, MapData.Grid);
        await SetTile(Plating, playerCoords, MapData.Grid);
        await SetPlayerCoords(playerCoords);
    }

    private async Task StartStrategicConstructionGhost(string recipePrototype, NetEntity anchor)
    {
        var buildCoords = await GetAnchorBuildCoords(anchor);
        TargetCoords = buildCoords;
        await StartConstruction(recipePrototype);
        await SetStrategicGhostPlacementTarget(anchor);
    }

    private async Task SetStrategicGhostPlacementTarget(NetEntity anchor)
    {
        await Client.WaitPost(() =>
        {
            var ghost = CTarget;
            Assert.That(ghost, Is.Not.Null);

            var ghostComp = CEntMan.GetComponent<ConstructionGhostComponent>(ghost.Value);
            ghostComp.PlacementTarget = ToClient(anchor);
        });
    }

    private async Task AssertGhostPlacementTarget(NetEntity anchor)
    {
        await Client.WaitAssertion(() =>
        {
            Assert.That(CTarget, Is.Not.Null);

            var ghostComp = CEntMan.GetComponent<ConstructionGhostComponent>(CTarget!.Value);
            Assert.That(ghostComp.PlacementTarget, Is.EqualTo(ToClient(anchor)));
        });
    }

    private async Task ClearStrategicGhostPlacementTarget()
    {
        await Client.WaitPost(() =>
        {
            Assert.That(CTarget, Is.Not.Null);

            var ghostComp = CEntMan.GetComponent<ConstructionGhostComponent>(CTarget!.Value);
            ghostComp.PlacementTarget = null;
        });
    }

    private readonly record struct ClientPlacementSnapshot(
        MapCoordinates MapCoordinates,
        Vector2 SpriteOffset);

    private async Task<ClientPlacementSnapshot> CaptureClientHoverPlacement(string recipePrototype, NetCoordinates buildCoords)
    {
        var snapshot = default(ClientPlacementSnapshot);

        await Client.WaitPost(() =>
        {
            var placement = (PlacementManager) Client.ResolveDependency<IPlacementManager>();
            var prototype = Client.ResolveDependency<IPrototypeManager>().Index<ConstructionPrototype>(recipePrototype);
            var transformSys = CEntMan.System<SharedTransformSystem>();
            var previewCoords = CEntMan.GetCoordinates(buildCoords);

            placement.BeginPlacing(new PlacementInformation
            {
                IsTile = false,
                PlacementOption = prototype.PlacementMode,
            }, new ConstructionPlacementHijack(CConSys, prototype));

            placement.CurrentMode!.MouseCoords = previewCoords;

            Assert.That(placement.CurrentPlacementOverlayEntity, Is.Not.Null);
            var overlayUid = placement.CurrentPlacementOverlayEntity!.Value;
            var sprite = CEntMan.GetComponent<SpriteComponent>(overlayUid);
            var mapCoordinates = transformSys.ToMapCoordinates(previewCoords);

            snapshot = new ClientPlacementSnapshot(mapCoordinates, sprite.Offset);
        });

        return snapshot;
    }

    private async Task<ClientPlacementSnapshot> CaptureClientGhostPlacementFromHover(string recipePrototype, NetCoordinates buildCoords)
    {
        var snapshot = default(ClientPlacementSnapshot);

        await Client.WaitPost(() =>
        {
            var placement = (PlacementManager) Client.ResolveDependency<IPlacementManager>();
            var prototype = Client.ResolveDependency<IPrototypeManager>().Index<ConstructionPrototype>(recipePrototype);
            var previewCoords = CEntMan.GetCoordinates(buildCoords);

            Assert.That(CConSys.TrySpawnGhost(prototype, previewCoords, Direction.South, out var ghostUid), Is.True);
            Assert.That(ghostUid, Is.Not.Null);

            var ghost = ghostUid!.Value;
            var transformSys = CEntMan.System<SharedTransformSystem>();
            var sprite = CEntMan.GetComponent<SpriteComponent>(ghost);
            var mapCoordinates = transformSys.GetMapCoordinates(ghost);

            snapshot = new ClientPlacementSnapshot(mapCoordinates, sprite.Offset);

            CConSys.ClearGhost(ghost.GetHashCode());
            placement.Clear();
        });

        await RunTicks(1);
        return snapshot;
    }

    private async Task<ClientPlacementSnapshot> CaptureClientPlacement(NetEntity entity)
    {
        var snapshot = default(ClientPlacementSnapshot);

        await Client.WaitPost(() =>
        {
            var uid = CEntMan.GetEntity(entity);
            var transformSys = CEntMan.System<SharedTransformSystem>();
            var sprite = CEntMan.GetComponent<SpriteComponent>(uid);
            var mapCoordinates = transformSys.GetMapCoordinates(uid);
            snapshot = new ClientPlacementSnapshot(
                mapCoordinates,
                sprite.Offset);
        });

        return snapshot;
    }

    private async Task AssertClientPlacementMatches(ClientPlacementSnapshot expected)
    {
        await Client.WaitAssertion(() =>
        {
            Assert.That(CTarget, Is.Not.Null);

            var uid = CTarget!.Value;
            var transformSys = CEntMan.System<SharedTransformSystem>();
            var sprite = CEntMan.GetComponent<SpriteComponent>(uid);
            var actualMap = transformSys.GetMapCoordinates(uid);

            Assert.That(actualMap.MapId, Is.EqualTo(expected.MapCoordinates.MapId));
            Assert.That(
                (actualMap.Position - expected.MapCoordinates.Position).Length(),
                Is.LessThanOrEqualTo(0.01f),
                $"Expected built point to remain at ghost position {expected.MapCoordinates.Position}, but got {actualMap.Position}.");
            Assert.That(
                (sprite.Offset - expected.SpriteOffset).Length(),
                Is.LessThanOrEqualTo(0.01f),
                $"Expected built point sprite offset {expected.SpriteOffset}, but got {sprite.Offset}.");
        });
    }

    private async Task EnsureTileArea(float centerX, float centerY, int radius = 3)
    {
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                await SetTile(Plating, GridCoords(centerX + x, centerY + y), MapData.Grid);
            }
        }
    }

    private async Task AssertBoundPoint(NetEntity anchor, WH40KStrategicPointType pointType, WH40KStrategicPointTier tier)
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(Target, Is.Not.Null);

            var anchorUid = ToServer(anchor);
            var pointUid = STarget;
            Assert.That(pointUid, Is.Not.Null);

            var anchorComp = SEntMan.GetComponent<WH40KStrategicPointAnchorComponent>(anchorUid);
            var pointComp = SEntMan.GetComponent<WH40KStrategicPointComponent>(pointUid.Value);
            var expectedCoords = SEntMan.GetComponent<TransformComponent>(anchorUid).Coordinates.Offset(anchorComp.BuiltOffset);
            var expectedMap = Transform.ToMapCoordinates(expectedCoords);
            var actualMap = Transform.GetMapCoordinates(pointUid.Value);

            Assert.That(anchorComp.BuiltPoint, Is.EqualTo(pointUid.Value));
            Assert.That(pointComp.Anchor, Is.EqualTo(anchorUid));
            Assert.That(pointComp.PointType, Is.EqualTo(pointType));
            Assert.That(pointComp.Tier, Is.EqualTo(tier));
            Assert.That(pointComp.OwnerTeamId, Is.EqualTo(TeamId));
            Assert.That(SEntMan.GetComponent<TransformComponent>(pointUid.Value).Anchored, Is.True);
            Assert.That(actualMap.MapId, Is.EqualTo(expectedMap.MapId));
            Assert.That(
                (actualMap.Position - expectedMap.Position).Length(),
                Is.LessThanOrEqualTo(0.01f),
                $"Expected {expectedMap.Position} from anchor {anchorUid}, but built point ended at {actualMap.Position}.");
        });
    }

    private async Task AssertAnchorHasNoBuiltPoint(NetEntity anchor)
    {
        await Server.WaitAssertion(() =>
        {
            var anchorComp = SEntMan.GetComponent<WH40KStrategicPointAnchorComponent>(ToServer(anchor));
            Assert.That(anchorComp.BuiltPoint is { Valid: true }, Is.False);
        });
    }

    private async Task AssertConstructionGhostStillPresent()
    {
        await Client.WaitAssertion(() =>
        {
            Assert.That(CTarget, Is.Not.Null);
            _ = CEntMan.GetComponent<ConstructionGhostComponent>(CTarget!.Value);
        });
    }
}
