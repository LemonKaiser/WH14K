using Content.Server.Light.EntitySystems;
using Content.Shared.Light.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Light;

[TestFixture]
public sealed class RoofMappingPauseTests
{
    [Test]
    public async Task RoofMarkersWorkOnPreInitMappingMaps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitIdleAsync();

        MapId mapId = default;
        Entity<MapGridComponent> grid = default;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mapSys = entMan.System<SharedMapSystem>();
            var mapMan = server.ResolveDependency<IMapManager>();

            mapSys.CreateMap(out mapId, runMapInit: false);
            grid = mapMan.CreateGridEntity(mapId);

            mapSys.SetTile(grid, grid, Vector2i.Zero, new Tile(1));
            mapSys.SetTile(grid, grid, new Vector2i(1, 0), new Tile(1));

            Assert.That(mapSys.IsInitialized(mapId), Is.False);
            Assert.That(mapSys.IsPaused(mapId), Is.True);
            Assert.That(entMan.HasComponent<ImplicitRoofComponent>(grid.Owner), Is.True);
            Assert.That(entMan.HasComponent<RoofComponent>(grid.Owner), Is.False);

            entMan.SpawnEntity("NoRoofMarker", new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
        });

        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var roofSys = entMan.System<RoofSystem>();

            Assert.That(entMan.HasComponent<ImplicitRoofComponent>(grid.Owner), Is.False);
            Assert.That(entMan.TryGetComponent<RoofComponent>(grid.Owner, out var roofComp), Is.True);
            Assert.That(roofSys.IsRooved((grid.Owner, grid.Comp, roofComp!), Vector2i.Zero), Is.False);
            Assert.That(roofSys.IsRooved((grid.Owner, grid.Comp, roofComp!), new Vector2i(1, 0)), Is.True);

            entMan.SpawnEntity("RoofMarker", new EntityCoordinates(grid.Owner, 0.5f, 0.5f));
        });

        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var roofSys = entMan.System<RoofSystem>();
            var roofComp = entMan.GetComponent<RoofComponent>(grid.Owner);

            Assert.That(roofSys.IsRooved((grid.Owner, grid.Comp, roofComp), Vector2i.Zero), Is.True);
            Assert.That(roofSys.IsRooved((grid.Owner, grid.Comp, roofComp), new Vector2i(1, 0)), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
