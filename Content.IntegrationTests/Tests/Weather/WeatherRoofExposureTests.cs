using System.Numerics;
using Content.Server.Light.EntitySystems;
using Content.Server.Weather;
using Content.Shared.Light.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Weather;

[TestFixture]
[TestOf(typeof(WeatherSystem))]
public sealed class WeatherRoofExposureTests
{
    private const string WeatherPrototype = "WHAcidRain";

    [Test]
    public async Task OverlappingRoofedGridBlocksExposure()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitIdleAsync();
        await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        var exposed = true;
        EntityUid entity = default;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mapMan = server.ResolveDependency<IMapManager>();
            var mapSys = entMan.System<SharedMapSystem>();
            var xforms = entMan.System<SharedTransformSystem>();
            var roof = entMan.System<RoofSystem>();
            var weather = entMan.System<WeatherSystem>();

            var floorGrid = pair.TestMap.Grid;
            entMan.RemoveComponent<ImplicitRoofComponent>(floorGrid.Owner);
            mapSys.SetTile(floorGrid, floorGrid, Vector2i.Zero, new Tile(1));

            var roofGrid = mapMan.CreateGridEntity(pair.TestMap.MapId);
            mapSys.SetTile(roofGrid, roofGrid, Vector2i.Zero, new Tile(1));
            xforms.SetLocalPosition(roofGrid.Owner, Vector2.Zero);

            entMan.EnsureComponent<RoofComponent>(roofGrid.Owner);
            roof.SetRoof((roofGrid.Owner, roofGrid.Comp, null), Vector2i.Zero, true);

            entity = entMan.SpawnEntity(null, new EntityCoordinates(floorGrid.Owner, 0.5f, 0.5f));
        });

        await pair.RunTicksSync(2);
        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var weather = entMan.System<WeatherSystem>();
            Assert.That(weather.TryGetWeatherPrototype(WeatherPrototype, out var weatherProto), Is.True);
            exposed = weather.CanWeatherAffectEntity(entity, weatherProto, entMan.GetComponent<TransformComponent>(entity));
        });

        Assert.That(exposed, Is.False);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NearbyRoofedGridDoesNotBlockExposure()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitIdleAsync();
        await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        var exposed = false;
        EntityUid entity = default;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mapMan = server.ResolveDependency<IMapManager>();
            var mapSys = entMan.System<SharedMapSystem>();
            var xforms = entMan.System<SharedTransformSystem>();
            var roof = entMan.System<RoofSystem>();
            var weather = entMan.System<WeatherSystem>();

            var floorGrid = pair.TestMap.Grid;
            entMan.RemoveComponent<ImplicitRoofComponent>(floorGrid.Owner);
            mapSys.SetTile(floorGrid, floorGrid, Vector2i.Zero, new Tile(1));

            var roofGrid = mapMan.CreateGridEntity(pair.TestMap.MapId);
            mapSys.SetTile(roofGrid, roofGrid, Vector2i.Zero, new Tile(1));
            xforms.SetLocalPosition(roofGrid.Owner, new Vector2(4f, 0f));

            entMan.EnsureComponent<RoofComponent>(roofGrid.Owner);
            roof.SetRoof((roofGrid.Owner, roofGrid.Comp, null), Vector2i.Zero, true);

            entity = entMan.SpawnEntity(null, new EntityCoordinates(floorGrid.Owner, 0.5f, 0.5f));
        });

        await pair.RunTicksSync(2);
        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var weather = entMan.System<WeatherSystem>();
            Assert.That(weather.TryGetWeatherPrototype(WeatherPrototype, out var weatherProto), Is.True);
            exposed = weather.CanWeatherAffectEntity(entity, weatherProto, entMan.GetComponent<TransformComponent>(entity));
        });

        Assert.That(exposed, Is.True);
        await pair.CleanReturnAsync();
    }
}
