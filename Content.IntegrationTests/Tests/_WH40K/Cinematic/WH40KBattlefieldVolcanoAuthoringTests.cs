using System.IO;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared._WH40K.Cinematic;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KBattlefieldVolcanoAuthoringTests : WH40KCinematicServerOnlyGameTest
{
    private static readonly ResPath Battlefield40kMap = new("/Maps/_WH40K/battlefield40k.yml");

    [Test]
    public async Task Battlefield40kVolcanoMarkersAndPrototypeArePresent()
    {
        var cinematicSystem = Server.System<WH40KCinematicSystem>();
        var mapLoader = Server.System<MapLoaderSystem>();
        var resources = Server.ResolveDependency<IResourceManager>();
        var prototype = SProtoMan.Index(new ProtoId<WH40KCinematicPrototype>("WH40KCinematicBattlefield40kVolcanoEruption"));
        string mapText = string.Empty;

        await ServerPostStep(() =>
        {
            using var stream = resources.ContentFileRead(Battlefield40kMap);
            using var reader = new StreamReader(stream);
            mapText = reader.ReadToEnd();
        });

        Assert.That(mapText, Does.Contain("pointId: shot_01"));
        Assert.That(mapText, Does.Contain("pointId: shot_02"));
        Assert.That(mapText, Does.Contain("pointId: shot_03"));
        Assert.That(mapText, Does.Contain("anchorId: sound_01"));
        Assert.That(mapText, Does.Contain("anchorId: spawn_02"));
        Assert.That(mapText, Does.Contain("flowId: 1"));
        Assert.That(mapText, Does.Contain("flowId: 2"));
        Assert.That(mapText, Does.Contain("flowId: 3"));
        Assert.That(mapText, Does.Contain("nodeIndex: 16"));

        var lavaActionCount = 0;
        foreach (var step in prototype.Steps)
        {
            foreach (var action in step.Actions)
            {
                if (action.Type != WH40KCinematicActionType.RunLavaFlow)
                    continue;

                lavaActionCount++;
                Assert.That(
                    action.ObstacleMode,
                    Is.EqualTo(WH40KCinematicLavaObstacleMode.Ignore),
                    $"Battlefield volcano lava flow '{action.FlowId}' should ignore wall obstacles.");
            }
        }

        Assert.That(lavaActionCount, Is.EqualTo(3), "battlefield40k volcano cinematic should define three lava flow actions");

        await ServerStep(() =>
        {
            var errors = cinematicSystem.ValidatePrototype(prototype);
            Assert.That(errors, Is.Empty, string.Join("; ", errors));

            Assert.That(mapLoader.TryLoadMap(Battlefield40kMap, out _, out _), Is.True);
        });

        await Server.WaitIdleAsync();

        await ServerStep(() =>
        {
            Assert.That(cinematicSystem.ValidateLavaFlow("1"), Is.Empty, "flow 1 should be valid on battlefield40k");
            Assert.That(cinematicSystem.ValidateLavaFlow("2"), Is.Empty, "flow 2 should be valid on battlefield40k");
            Assert.That(cinematicSystem.ValidateLavaFlow("3"), Is.Empty, "flow 3 should be valid on battlefield40k");
        });
    }
}
