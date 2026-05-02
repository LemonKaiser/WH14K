#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K.Construction;

[TestFixture]
public sealed class WH40KConstructionRecipeTests : GameTest
{
    private static readonly string[] GeneralRecipes =
    [
        "WH40KWallSolid",
        "WH40KChaosBrickWall",
        "WH40KCatwalk",
        "WH40KTable",
        "WH40KTableReinforced",
        "WH40KTableGlass",
        "WH40KTableWood",
        "WH40KAirlock",
        "WH40KAirlockGlass",
        "WH40KPoweredSmallLight",
        "WH40KPoweredLight",
        "WH40KFalloutBarricades",
        "WH40KFalloutBarricades4",
        "WH40KFalloutBarricades6",
        "WH40KFalloutBarricades7",
        "WH40KWindow",
        "WH40KDirtReinforcedWindow",
        "WH40KReinforcedWindow",
        "WH40KWindowDirectional",
        "WH40KReinforcedWindowDirectional",
        "WH40KWindowDiagonal",
        "WH40KReinforcedWindowDiagonal",
        "WH40KConveyorBelt",
        "WH40KConveyorManipulator",
        "WH40KFloodlight",
        "WH40KSignalButton",
        "WH40KTwoWayLever",
        "WH40KChair",
        "WH40KStool",
        "WH40KStoolBar",
        "WH40KChairBrass",
        "WH40KChairOfficeLight",
        "WH40KChairOfficeDark",
        "WH40KChairComfy",
        "WH40KChairPilotSeat",
        "WH40KChairWood",
        "WH40KChairMeat",
        "WH40KChairRitual",
        "WH40KChairFolding",
        "WH40KChairSteelBench",
        "WH40KStoolCard",
        "WH40KChairWoodBench",
        "WH40KRedComfBench",
        "WH40KBlueComfBench",
        "WH40KCrateMaterial",
        "WH40KRack",
        "WH40KWoodDoor",
        "WH40KMetalDoor",
        "WH40KClosetSteel",
        "WH40KRailing",
        "WH40KRailingCornerSmall",
        "WH40KWindoorSecure",
        "WH40KWindoor",
        "WH40KGrille",
        "WH40KBed",
        "WH40KPlasticFlapsAirtightClear",
        "WH40KTargetHuman",
        "WH40KTargetSyndicate",
        "WH40KTargetClown",
        "WH40KTargetStrange",
        "WH40KAPC",
        "WH40KSMES",
        "WH40KSubstation",
        "WH40KFenceMetalStraight",
        "WH40KFenceMetalGate",
        "WH40KFenceMetalCorner",
        "WH40KStairsSteel",
        "WH40KStairsSteelStage",
        "WH40KStairsWhite",
        "WH40KStairsWhiteStage",
        "WH40KStairsDark",
        "WH40KStairsDarkStage",
        "WH40KStairsWood",
        "WH40KStairsWoodStage",
        "WH40KBookshelf",
        "WH40KCurtains",
        "WH40KWoodenSupportWall",
        "WH40KWoodenSupport",
        "WH40KWoodenSupportBeam",
        "WH40KGenericTank",
        "WH40KPortableGeneratorJrPacman",
        "WH40KFloorDrain",
    ];

    private static readonly string[] ImperiumRecipes =
    [
        "WH40KGuardFlag",
        "WH40KGvardiaBanner",
        "WH40KGvardiaBanner2",
        "WH40KMechanicusBanner",
        "WH40KMedicalBanner",
        "WH40KAirlockImperium",
    ];

    private static readonly string[] HereticRecipes =
    [
        "WH40KChaosFlag",
        "WH40KChaosBanner",
        "WH40KAirlockChaos",
    ];

    [Test]
    public async Task RequestedRecipesExistAndFactionLocksAreCorrect()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in GeneralRecipes)
                {
                    Assert.That(SProtoMan.TryIndex<ConstructionPrototype>(id, out var recipe), Is.True,
                        $"Missing expected WH40K construction recipe: {id}");

                    Assert.That(recipe, Is.Not.Null);
                    Assert.That(recipe!.WH40KAllowedTeams, Is.Empty,
                        $"Recipe {id} should be available to both WH40K factions.");
                    Assert.That(recipe.IsWh40KTeamAllowed("Imperium"), Is.True);
                    Assert.That(recipe.IsWh40KTeamAllowed("Heretics"), Is.True);
                }

                foreach (var id in ImperiumRecipes)
                {
                    Assert.That(SProtoMan.TryIndex<ConstructionPrototype>(id, out var recipe), Is.True,
                        $"Missing expected Imperium construction recipe: {id}");

                    Assert.That(recipe, Is.Not.Null);
                    Assert.That(recipe!.WH40KAllowedTeams, Is.EquivalentTo(new[] { "Imperium" }));
                    Assert.That(recipe.IsWh40KTeamAllowed("Imperium"), Is.True);
                    Assert.That(recipe.IsWh40KTeamAllowed("Heretics"), Is.False);
                    Assert.That(recipe.IsWh40KTeamAllowed(null), Is.False);
                }

                foreach (var id in HereticRecipes)
                {
                    Assert.That(SProtoMan.TryIndex<ConstructionPrototype>(id, out var recipe), Is.True,
                        $"Missing expected Heretic construction recipe: {id}");

                    Assert.That(recipe, Is.Not.Null);
                    Assert.That(recipe!.WH40KAllowedTeams, Is.EquivalentTo(new[] { "Heretics" }));
                    Assert.That(recipe.IsWh40KTeamAllowed("Heretics"), Is.True);
                    Assert.That(recipe.IsWh40KTeamAllowed("Imperium"), Is.False);
                    Assert.That(recipe.IsWh40KTeamAllowed(null), Is.False);
                }
            });
        });
    }

    [Test]
    public async Task FloodlightWrapperStartsWithoutBuiltInCell()
    {
        await Server.WaitAssertion(() =>
        {
            var floodlightId = "WH40KFloodlight";
            Assert.That(SProtoMan.TryGetMapping(typeof(EntityPrototype), floodlightId, out MappingDataNode? floodlight), Is.True);
            Assert.That(floodlight, Is.Not.Null);
            Assert.That(floodlight!.TryGet("components", out SequenceDataNode? components), Is.True);
            Assert.That(components, Is.Not.Null);

            var itemSlots = components!
                .OfType<MappingDataNode>()
                .Single(node => node.Get<ValueDataNode>("type").Value == "ItemSlots");

            var slots = itemSlots.Get<MappingDataNode>("slots");
            var cellSlot = slots.Get<MappingDataNode>("cell_slot");

            Assert.That(cellSlot.TryGet("startingItem", out ValueDataNode? startingItem), Is.True);
            Assert.That(startingItem, Is.Not.Null);
            Assert.That(startingItem!.IsNull, Is.True);
        });
    }
}
