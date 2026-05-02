using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared.Maps;
using Content.Shared._WH40K.Cinematic;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KCinematicLavaFlowTests : WH40KCinematicServerOnlyGameTest
{
    private const string StraightFlowId = "phase4_straight";
    private const string GuidedFlowId = "phase4_guided";
    private const string Width3FlowId = "phase4_width3";
    private const string Width3ProgressFlowId = "phase4_width3_progress";
    private const string Width5FlowId = "phase4_width5";
    private const string BlockedFlowId = "phase4_blocked";
    private const string IgnoreWallFlowId = "phase4_ignore_wall";
    private const string BrokenFlowId = "phase4_broken";
    private const string PersistentFlowId = "phase4_persistent";

    private const string StraightCinematic = "WH40KCinematicPhase4Straight";
    private const string GuidedCinematic = "WH40KCinematicPhase4Guided";
    private const string Width3Cinematic = "WH40KCinematicPhase4Width3";
    private const string Width3ProgressCinematic = "WH40KCinematicPhase4Width3Progress";
    private const string Width5Cinematic = "WH40KCinematicPhase4Width5";
    private const string BlockedCinematic = "WH40KCinematicPhase4Blocked";
    private const string IgnoreWallCinematic = "WH40KCinematicPhase4IgnoreWall";
    private const string PauseMapCinematic = "WH40KCinematicPhase4PauseMap";
    private const string PersistentCinematic = "WH40KCinematicPhase4Persistent";

    private const string TestWallPrototype = "WH40KCinematicPhase4Wall";
    private static readonly ProtoId<ContentTileDefinition> SnowTileId = "FloorSnow";
    private static readonly ProtoId<ContentTileDefinition> BasaltTileId = "FloorBasalt";

    [TestPrototypes]
    private static readonly string TestPrototypes = $@"
- type: entity
  id: {TestWallPrototype}
  components:
  - type: Transform
    anchored: true
  - type: Tag
    tags:
    - Wall

- type: wh40kCinematic
  id: {StraightCinematic}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {StraightFlowId}
      width: 1
      advanceInterval: 0
      tilesPerAdvance: 64
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {GuidedCinematic}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {GuidedFlowId}
      width: 1
      advanceInterval: 0
      tilesPerAdvance: 64
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {Width3Cinematic}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {Width3FlowId}
      width: 3
      advanceInterval: 0
      tilesPerAdvance: 64
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {Width5Cinematic}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {Width5FlowId}
      width: 5
      advanceInterval: 0
      tilesPerAdvance: 128
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {Width3ProgressCinematic}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {Width3ProgressFlowId}
      width: 3
      widthShape: Square
      advanceInterval: 0.20
      tilesPerAdvance: 1
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {BlockedCinematic}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {BlockedFlowId}
      width: 1
      obstacleMode: StopOnWallOrEmpty
      advanceInterval: 0
      tilesPerAdvance: 64
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {IgnoreWallCinematic}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {IgnoreWallFlowId}
      width: 1
      obstacleMode: Ignore
      preserveExistingFloor: false
      advanceInterval: 0
      tilesPerAdvance: 64
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {PauseMapCinematic}
  worldFreezeMode: PauseMap
  steps:
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {StraightFlowId}
      width: 1
      advanceInterval: 0
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {PersistentCinematic}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: end
    type: EndCinematic
    waitMode: Terminal
    actions:
    - type: RunLavaFlow
      flowId: {PersistentFlowId}
      width: 1
      advanceInterval: 0.20
      tilesPerAdvance: 1
      persistAfterCinematic: true
";

    [Test]
    public async Task RunLavaFlowMutatesStraightRoute()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await ServerStep(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", StraightFlowId, WH40KCinematicLavaMarkerRole.Start, 0, gridUid, gridComp, (0, 0));
            SpawnLavaMarker("WH40KCinematicLavaEndMarker", StraightFlowId, WH40KCinematicLavaMarkerRole.End, 999, gridUid, gridComp, (4, 0));
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(StraightCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 12,
            label: "wait for straight lava flow completion");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            for (var x = 0; x <= 4; x++)
            {
                AssertSnowTile(gridUid, gridComp, new Vector2i(x, 0));
                Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(x, 0)), Is.True);
            }
        });
    }

    [Test]
    public async Task RunLavaFlowFollowsGuideMarkerSequence()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await ServerStep(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", GuidedFlowId, WH40KCinematicLavaMarkerRole.Start, 0, gridUid, gridComp, (0, 0));
            SpawnLavaMarker("WH40KCinematicLavaGuideMarker", GuidedFlowId, WH40KCinematicLavaMarkerRole.Guide, 1, gridUid, gridComp, (0, 3));
            SpawnLavaMarker("WH40KCinematicLavaEndMarker", GuidedFlowId, WH40KCinematicLavaMarkerRole.End, 999, gridUid, gridComp, (4, 3));
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(GuidedCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 12,
            label: "wait for guided lava flow completion");

        await ServerStep(() =>
        {
            foreach (var tile in new[]
                     {
                         new Vector2i(0, 0),
                         new Vector2i(0, 1),
                         new Vector2i(0, 2),
                         new Vector2i(0, 3),
                         new Vector2i(1, 3),
                         new Vector2i(2, 3),
                         new Vector2i(3, 3),
                         new Vector2i(4, 3)
                     })
            {
                AssertSnowTile(gridUid, gridComp, tile);
                Assert.That(HasLavaOverlay(gridUid, gridComp, tile), Is.True);
            }
        });
    }

    [Test]
    public async Task RunLavaFlowSupportsWidthThreeAndFive()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        EntityUid grid3Uid = default;
        MapGridComponent grid3Comp = default!;
        EntityUid grid5Uid = default;
        MapGridComponent grid5Comp = default!;
        var overlays3 = 0;
        var overlays5 = 0;

        await ServerStep(() =>
        {
            (grid3Uid, grid3Comp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", Width3FlowId, WH40KCinematicLavaMarkerRole.Start, 0, grid3Uid, grid3Comp, (0, 0));
            SpawnLavaMarker("WH40KCinematicLavaEndMarker", Width3FlowId, WH40KCinematicLavaMarkerRole.End, 999, grid3Uid, grid3Comp, (4, 0));

            (grid5Uid, grid5Comp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", Width5FlowId, WH40KCinematicLavaMarkerRole.Start, 0, grid5Uid, grid5Comp, (0, 0));
            SpawnLavaMarker("WH40KCinematicLavaEndMarker", Width5FlowId, WH40KCinematicLavaMarkerRole.End, 999, grid5Uid, grid5Comp, (4, 0));

            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(Width3Cinematic), out _), Is.True);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(Width5Cinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive && serverSys.GetSnapshot().QueueLength == 0,
            maxTicks: 40,
            label: "wait for width3 and width5 lava flow completion");

        await ServerStep(() =>
        {
            overlays3 = CountLavaOverlayTiles(grid3Uid, grid3Comp);
            overlays5 = CountLavaOverlayTiles(grid5Uid, grid5Comp);

            Assert.That(overlays3, Is.GreaterThan(5));
            Assert.That(overlays5, Is.GreaterThan(overlays3));
            AssertSnowTile(grid3Uid, grid3Comp, new Vector2i(2, 1));
            AssertSnowTile(grid5Uid, grid5Comp, new Vector2i(2, 2));
            Assert.That(HasLavaOverlay(grid3Uid, grid3Comp, new Vector2i(2, 1)), Is.True);
            Assert.That(HasLavaOverlay(grid5Uid, grid5Comp, new Vector2i(2, 2)), Is.True);
        });
    }

    [Test]
    public async Task RunLavaFlowWidthThreeAdvancesAsContinuousFront()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await ServerStep(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", Width3ProgressFlowId, WH40KCinematicLavaMarkerRole.Start, 0, gridUid, gridComp, (0, 0));
            SpawnLavaMarker("WH40KCinematicLavaEndMarker", Width3ProgressFlowId, WH40KCinematicLavaMarkerRole.End, 999, gridUid, gridComp, (3, 0));
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(Width3ProgressCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => HasLavaOverlay(gridUid, gridComp, new Vector2i(0, 0)) &&
                  HasLavaOverlay(gridUid, gridComp, new Vector2i(-1, 0)),
            maxTicks: 20,
            label: "wait for continuous width3 lava front");

        await ServerStep(() =>
        {
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(0, 0)), Is.True);
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(-1, 0)), Is.True);
        });
    }

    [Test]
    public async Task RunLavaFlowStopsOnWallObstacle()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await ServerStep(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", BlockedFlowId, WH40KCinematicLavaMarkerRole.Start, 0, gridUid, gridComp, (0, 0));
            SpawnLavaMarker("WH40KCinematicLavaEndMarker", BlockedFlowId, WH40KCinematicLavaMarkerRole.End, 999, gridUid, gridComp, (4, 0));
            SEntMan.SpawnEntity(TestWallPrototype, Server.System<SharedMapSystem>().ToCenterCoordinates(gridUid, new Vector2i(2, 0), gridComp));
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(BlockedCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 12,
            label: "wait for blocked lava flow");

        await ServerStep(() =>
        {
            AssertSnowTile(gridUid, gridComp, new Vector2i(0, 0));
            AssertSnowTile(gridUid, gridComp, new Vector2i(1, 0));
            AssertSnowTile(gridUid, gridComp, new Vector2i(2, 0));
            AssertSnowTile(gridUid, gridComp, new Vector2i(3, 0));
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(0, 0)), Is.True);
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(1, 0)), Is.True);
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(2, 0)), Is.False);
        });
    }

    [Test]
    public async Task RunLavaFlowIgnoresWallObstacleWhenConfigured()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await ServerStep(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", IgnoreWallFlowId, WH40KCinematicLavaMarkerRole.Start, 0, gridUid, gridComp, (0, 0));
            SpawnLavaMarker("WH40KCinematicLavaEndMarker", IgnoreWallFlowId, WH40KCinematicLavaMarkerRole.End, 999, gridUid, gridComp, (4, 0));
            SEntMan.SpawnEntity(TestWallPrototype, Server.System<SharedMapSystem>().ToCenterCoordinates(gridUid, new Vector2i(2, 0), gridComp));
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(IgnoreWallCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 12,
            label: "wait for ignore-wall lava flow");

        await ServerStep(() =>
        {
            for (var x = 0; x <= 4; x++)
            {
                AssertBasaltTile(gridUid, gridComp, new Vector2i(x, 0));
            }

            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(0, 0)), Is.True);
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(4, 0)), Is.True);
        });
    }

    [Test]
    public async Task PauseMapRunLavaFlowPrototypeIsRejected()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            var errors = serverSys.ValidatePrototype(SProtoMan.Index<WH40KCinematicPrototype>(PauseMapCinematic));
            Assert.That(errors.Any(error => error.Contains("runLavaFlow is not compatible with PauseMap")), Is.True);
        });
    }

    [Test]
    public async Task ValidateLavaFlowReportsBrokenMarkerTopology()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            var (gridUid, gridComp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", BrokenFlowId, WH40KCinematicLavaMarkerRole.Start, 0, gridUid, gridComp, (0, 0));

            var errors = serverSys.ValidateLavaFlow(BrokenFlowId);
            Assert.That(errors.Any(error => error.Contains("requires exactly one lava end marker")), Is.True);
        });
    }

    [Test]
    public async Task PersistentRunLavaFlowContinuesAfterCinematicEnd()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await ServerStep(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            SpawnLavaMarker("WH40KCinematicLavaStartMarker", PersistentFlowId, WH40KCinematicLavaMarkerRole.Start, 0, gridUid, gridComp, (0, 0));
            SpawnLavaMarker("WH40KCinematicLavaEndMarker", PersistentFlowId, WH40KCinematicLavaMarkerRole.End, 999, gridUid, gridComp, (3, 0));
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(PersistentCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive &&
                  HasLavaOverlay(gridUid, gridComp, new Vector2i(0, 0)) &&
                  !HasLavaOverlay(gridUid, gridComp, new Vector2i(3, 0)),
            maxTicks: 12,
            label: "wait for persistent lava cinematic completion");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            AssertSnowTile(gridUid, gridComp, new Vector2i(0, 0));
            AssertSnowTile(gridUid, gridComp, new Vector2i(3, 0));
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(0, 0)), Is.True);
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(3, 0)), Is.False);
        });

        await WaitForPairConditionStep(
            () => HasLavaOverlay(gridUid, gridComp, new Vector2i(3, 0)),
            maxTicks: 60,
            label: "wait for persistent lava continuation after cinematic end");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            for (var x = 0; x <= 3; x++)
            {
                AssertSnowTile(gridUid, gridComp, new Vector2i(x, 0));
                Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(x, 0)), Is.True);
            }
        });
    }

    private (EntityUid GridUid, MapGridComponent GridComp) CreateFilledGrid()
    {
        var mapManager = Server.ResolveDependency<IMapManager>();
        var mapSystem = Server.System<SharedMapSystem>();
        var fillTile = SProtoMan.Index(SnowTileId);
        mapSystem.CreateMap(out var mapId);
        var gridUid = mapManager.CreateGridEntity(mapId);
        var gridComp = SComp<MapGridComponent>(gridUid);

        for (var x = -6; x <= 12; x++)
        {
            for (var y = -6; y <= 12; y++)
            {
                mapSystem.SetTile(gridUid, gridComp, new Vector2i(x, y), new Tile(fillTile.TileId));
            }
        }

        return (gridUid, gridComp);
    }

    private EntityUid SpawnLavaMarker(
        string prototypeId,
        string flowId,
        WH40KCinematicLavaMarkerRole role,
        int nodeIndex,
        EntityUid gridUid,
        MapGridComponent gridComp,
        Vector2i tile)
    {
        var mapSystem = Server.System<SharedMapSystem>();
        var marker = SEntMan.SpawnEntity(prototypeId, mapSystem.ToCenterCoordinates(gridUid, tile, gridComp));
        var lavaMarker = SComp<WH40KCinematicLavaMarkerComponent>(marker);
        lavaMarker.FlowId = flowId;
        lavaMarker.Role = role;
        lavaMarker.NodeIndex = nodeIndex;
        return marker;
    }

    private void AssertBasaltTile(EntityUid gridUid, MapGridComponent gridComp, Vector2i tile)
    {
        var basalt = SProtoMan.Index(BasaltTileId);
        var mapSystem = Server.System<SharedMapSystem>();
        Assert.That(mapSystem.GetTileRef(gridUid, gridComp, tile).Tile.TypeId, Is.EqualTo(basalt.TileId));
    }

    private void AssertSnowTile(EntityUid gridUid, MapGridComponent gridComp, Vector2i tile)
    {
        var snow = SProtoMan.Index(SnowTileId);
        var mapSystem = Server.System<SharedMapSystem>();
        Assert.That(mapSystem.GetTileRef(gridUid, gridComp, tile).Tile.TypeId, Is.EqualTo(snow.TileId));
    }

    private int CountBasaltTiles(EntityUid gridUid, MapGridComponent gridComp)
    {
        var basalt = SProtoMan.Index(BasaltTileId);
        var mapSystem = Server.System<SharedMapSystem>();
        var count = 0;

        for (var x = -6; x <= 12; x++)
        {
            for (var y = -6; y <= 12; y++)
            {
                if (mapSystem.GetTileRef(gridUid, gridComp, new Vector2i(x, y)).Tile.TypeId == basalt.TileId)
                    count++;
            }
        }

        return count;
    }

    private int CountLavaOverlayTiles(EntityUid gridUid, MapGridComponent gridComp)
    {
        var count = 0;

        for (var x = -6; x <= 12; x++)
        {
            for (var y = -6; y <= 12; y++)
            {
                if (HasLavaOverlay(gridUid, gridComp, new Vector2i(x, y)))
                    count++;
            }
        }

        return count;
    }

    private bool HasLavaOverlay(EntityUid gridUid, MapGridComponent gridComp, Vector2i tile)
    {
        var mapSystem = Server.System<SharedMapSystem>();
        var anchored = mapSystem.GetAnchoredEntitiesEnumerator(gridUid, gridComp, tile);
        while (anchored.MoveNext(out var entity))
        {
            if (SComp<MetaDataComponent>(entity.Value).EntityPrototype?.ID == "FloorLavaEntity")
                return true;
        }

        return false;
    }
}
