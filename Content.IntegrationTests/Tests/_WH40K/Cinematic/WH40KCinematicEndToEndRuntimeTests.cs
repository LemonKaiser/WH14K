using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared.Maps;
using Content.Shared._WH40K.Cinematic;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using ClientCinematicSystem = Content.Client._WH40K.Cinematic.WH40KCinematicSystem;
using ClientNotificationSystem = Content.Client._WH40K.Notifications.WH40KNotificationSystem;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KCinematicEndToEndRuntimeTests : WH40KCinematicGameTest
{
    private const string IntroCameraPrototype = "WH40KCinematicPhase5IntroCamera";
    private const string OutroCameraPrototype = "WH40KCinematicPhase5OutroCamera";
    private const string SoundAnchorPrototype = "WH40KCinematicPhase5SoundAnchor";
    private const string SpawnAnchorPrototype = "WH40KCinematicPhase5SpawnAnchor";
    private const string TimedSpawnPrototype = "WH40KCinematicPhase5TimedSpawn";

    private const string IntroCameraId = "phase5_cam_intro";
    private const string OutroCameraId = "phase5_cam_outro";
    private const string SoundAnchorId = "phase5_sound";
    private const string SpawnAnchorId = "phase5_spawn";
    private const string LavaFlowId = "phase5_flow";

    private const string HappyPathCinematic = "WH40KCinematicPhase5HappyPath";
    private const string ReconnectLongCinematic = "WH40KCinematicPhase5ReconnectLong";
    private const string FollowUpCinematic = "WH40KCinematicPhase5ReconnectFollowUp";

    private static readonly ProtoId<ContentTileDefinition> SnowTileId = "FloorSnow";
    private static readonly ProtoId<ContentTileDefinition> BasaltTileId = "FloorBasalt";

    [TestPrototypes]
    private static readonly string TestPrototypes = $@"
- type: entity
  id: {IntroCameraPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicCameraPoint
    pointId: {IntroCameraId}
    zoom: 1.20
    rotation: -6

- type: entity
  id: {OutroCameraPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicCameraPoint
    pointId: {OutroCameraId}
    zoom: 1.05
    rotation: 12

- type: entity
  id: {SoundAnchorPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicSoundAnchor
    anchorId: {SoundAnchorId}

- type: entity
  id: {SpawnAnchorPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicSpawnAnchor
    anchorId: {SpawnAnchorId}

- type: entity
  id: {TimedSpawnPrototype}
  components:
  - type: TimedDespawn
    lifetime: 3.0

- type: wh40kCinematic
  id: {HappyPathCinematic}
  worldFreezeMode: LockPlayersOnly
  restoreInputDelay: 0.20
  steps:
  - id: notify
    waitMode: Duration
    duration: 0.15
    actions:
    - type: Notify
      title: Apocalypse Phase
      text: Volcano activity detected
      category: Event
      priority: Critical
      icon: Event
  - id: intro
    type: Shot
    waitMode: Duration
    duration: 0.25
    cameraPoint: {IntroCameraId}
    actions:
    - type: PlayAnchorSound
      anchorId: {SoundAnchorId}
      sound:
        path: /Audio/Misc/notice1.ogg
    - type: SpawnAtAnchor
      anchorId: {SpawnAnchorId}
      prototype: {TimedSpawnPrototype}
  - id: lava
    waitMode: AwaitCompletion
    actions:
    - type: RunLavaFlow
      flowId: {LavaFlowId}
      width: 3
      advanceInterval: 0
      tilesPerAdvance: 128
      blocking: true
  - id: outro
    type: Shot
    waitMode: Duration
    duration: 0.20
    cameraPoint: {OutroCameraId}
    cameraTransition: Blend
    blendDuration: 0.10
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {ReconnectLongCinematic}
  worldFreezeMode: LockPlayersOnly
  restoreInputDelay: 0.10
  steps:
  - id: intro
    type: Shot
    waitMode: Duration
    duration: 5.00
    cameraPoint: {IntroCameraId}
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {FollowUpCinematic}
  worldFreezeMode: LockPlayersOnly
  restoreInputDelay: 0.10
  steps:
  - id: outro
    type: Shot
    waitMode: Duration
    duration: 0.25
    cameraPoint: {OutroCameraId}
  - id: end
    type: EndCinematic
    waitMode: Terminal
";

    [Test]
    public async Task FullHappyPathRunsNotifyShotSpawnAndLavaFlow()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();
        var clientNotifications = Client.System<ClientNotificationSystem>();
        EntityUid gridUid = default;
        MapGridComponent gridComp = default!;

        await ServerStep(() =>
        {
            (gridUid, gridComp) = CreateFilledGrid();
            SpawnAuthoringMarkers(gridUid, gridComp);

            Assert.That(serverSys.TryValidateLoadedPrototype(HappyPathCinematic, out var validationMessage), Is.True, validationMessage);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(HappyPathCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientNotifications.LastNotification != null && clientSys.ActiveState != null,
            maxTicks: 12,
            label: "wait for HappyPath notify and start");

        await ClientStep(() =>
        {
            Assert.That(clientNotifications.LastNotification, Is.Not.Null);
            Assert.That(clientNotifications.LastNotification!.Title, Is.EqualTo("Apocalypse Phase"));
            Assert.That(clientNotifications.LastNotification.Text, Does.Contain("Volcano"));
            Assert.That(clientSys.ActiveState, Is.Not.Null);
        });

        await WaitForPairConditionStep(
            () => serverSys.GetSnapshot().ActiveStepId == "intro" && SEntMan.EntityQuery<TimedDespawnComponent>().Any(),
            maxTicks: 12,
            label: "wait for HappyPath intro and spawn");

        await ServerStep(() =>
        {
            var snapshot = serverSys.GetSnapshot();
            Assert.That(snapshot.ActiveStepId, Is.EqualTo("intro"));
            Assert.That(SEntMan.EntityQuery<TimedDespawnComponent>().Any(), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 50,
            label: "wait for HappyPath completion");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            AssertSnowTile(gridUid, gridComp, new Vector2i(0, 0));
            AssertSnowTile(gridUid, gridComp, new Vector2i(2, 0));
            AssertSnowTile(gridUid, gridComp, new Vector2i(4, 1));
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(0, 0)), Is.True);
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(2, 0)), Is.True);
            Assert.That(HasLavaOverlay(gridUid, gridComp, new Vector2i(4, 1)), Is.True);
        });

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Null);
            Assert.That(clientSys.LastStoppedEvent, Is.Not.Null);
            Assert.That(clientSys.LastStoppedEvent!.CinematicId, Is.EqualTo(HappyPathCinematic));
            Assert.That(clientSys.LastStoppedEvent.Completed, Is.True);
            Assert.That(clientSys.LastStoppedEvent.UnlockDelaySeconds, Is.EqualTo(0.20f).Within(0.001f));
        });
    }

    [Test]
    public async Task QueueReconnectAndCleanupKeepsRuntimeConsistent()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();
        var serverPlayerMgr = Server.ResolveDependency<IPlayerManager>();
        var clientNetManager = Client.ResolveDependency<IClientNetManager>();
        string username = null!;

        await ServerStep(() =>
        {
            var (gridUid, gridComp) = CreateFilledGrid();
            SpawnAuthoringMarkers(gridUid, gridComp);

            username = serverPlayerMgr.Sessions.Single().Name;

            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(ReconnectLongCinematic), out _), Is.True);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(FollowUpCinematic), out _), Is.True);
            Assert.That(serverSys.GetSnapshot().ActiveCinematicId, Is.EqualTo(ReconnectLongCinematic));
            Assert.That(serverSys.GetSnapshot().QueueLength, Is.EqualTo(1));
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState?.CinematicId == ReconnectLongCinematic,
            maxTicks: 12,
            label: "wait for ReconnectLongCinematic start");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.CinematicId, Is.EqualTo(ReconnectLongCinematic));
        });

        await ClientStep(() =>
        {
            clientNetManager.ClientDisconnect("phase5 reconnect test");
        });

        await WaitForPairConditionStep(
            () => serverPlayerMgr.PlayerCount == 0,
            maxTicks: 30,
            label: "wait for client disconnect");

        await ServerStep(() =>
        {
            Assert.That(serverPlayerMgr.PlayerCount, Is.EqualTo(0));
        });

        Client.SetConnectTarget(Server);
        await ClientPostStep(() =>
        {
            clientNetManager.ClientConnect(null!, 0, username);
        });

        await WaitForPairConditionStep(
            () => serverPlayerMgr.PlayerCount == 1 &&
                  serverSys.GetSnapshot().IsActive &&
                  serverSys.GetSnapshot().ActiveCinematicId == ReconnectLongCinematic,
            maxTicks: 80,
            label: "wait for reconnect and long cinematic restore");

        await ServerStep(() =>
        {
            Assert.That(serverPlayerMgr.PlayerCount, Is.EqualTo(1));
            Assert.That(serverSys.GetSnapshot().IsActive, Is.True);
            Assert.That(serverSys.GetSnapshot().ActiveCinematicId, Is.EqualTo(ReconnectLongCinematic));
        });

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryStopActive("Phase 5 cleanup test.", markCompleted: false), Is.True);
            Assert.That(serverSys.GetSnapshot().IsActive, Is.True);
            Assert.That(serverSys.GetSnapshot().ActiveCinematicId, Is.EqualTo(FollowUpCinematic));
        });

        await WaitForPairConditionStep(
            () =>
            {
                var attached = serverPlayerMgr.Sessions.Single().AttachedEntity;
                return !serverSys.GetSnapshot().IsActive &&
                       attached != null &&
                       !SEntMan.HasComponent<WH40KCinematicLockedComponent>(attached.Value);
            },
            maxTicks: 50,
            label: "wait for follow-up completion after cleanup");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            var attached = serverPlayerMgr.Sessions.Single().AttachedEntity;
            Assert.That(attached, Is.Not.Null);
            Assert.That(SEntMan.HasComponent<WH40KCinematicLockedComponent>(attached!.Value), Is.False);
        });

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Null);
        });
    }

    [Test]
    public async Task ToolingPreviewAndLoadedValidationSmoke()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var console = Server.ResolveDependency<IConsoleHost>();

        await ServerStep(() =>
        {
            var (gridUid, gridComp) = CreateFilledGrid();
            SpawnAuthoringMarkers(gridUid, gridComp);

            Assert.That(serverSys.TryDescribePrototype(HappyPathCinematic, out var describeMessage), Is.True);
            Assert.That(describeMessage, Does.Contain("shots=2"));

            Assert.That(serverSys.TryValidateLoadedPrototype(HappyPathCinematic, out var loadedMessage), Is.True, loadedMessage);

            console.ExecuteCommand($"wh40kcinematic preview-shot {HappyPathCinematic} intro 5");
            console.ExecuteCommand($"wh40kcinematic preview-anchor {SoundAnchorId} sound 5");
            console.ExecuteCommand($"wh40kcinematic preview-cinematic {HappyPathCinematic} 5");
            console.ExecuteCommand($"wh40kcinematic validate-loaded {HappyPathCinematic}");
        });

        await WaitForPairConditionStep(
            () => CountEntitiesByPrototype("WH40KCinematicShotPreviewMarker") >= 3 &&
                  CountEntitiesByPrototype("WH40KCinematicAnchorPreviewMarker") >= 2 &&
                  CountEntitiesByPrototype("WH40KCinematicLavaPreviewMarker") > 0,
            maxTicks: 12,
            label: "wait for tooling preview markers");

        await ServerStep(() =>
        {
            Assert.That(CountEntitiesByPrototype("WH40KCinematicShotPreviewMarker"), Is.GreaterThanOrEqualTo(3));
            Assert.That(CountEntitiesByPrototype("WH40KCinematicAnchorPreviewMarker"), Is.GreaterThanOrEqualTo(2));
            Assert.That(CountEntitiesByPrototype("WH40KCinematicLavaPreviewMarker"), Is.GreaterThan(0));
        });
    }

    private void SpawnAuthoringMarkers(EntityUid gridUid, MapGridComponent gridComp)
    {
        var mapSystem = Server.System<SharedMapSystem>();
        SEntMan.SpawnEntity(IntroCameraPrototype, mapSystem.ToCenterCoordinates(gridUid, new Vector2i(0, 0), gridComp));
        SEntMan.SpawnEntity(OutroCameraPrototype, mapSystem.ToCenterCoordinates(gridUid, new Vector2i(4, 1), gridComp));
        SEntMan.SpawnEntity(SoundAnchorPrototype, mapSystem.ToCenterCoordinates(gridUid, new Vector2i(1, 0), gridComp));
        SEntMan.SpawnEntity(SpawnAnchorPrototype, mapSystem.ToCenterCoordinates(gridUid, new Vector2i(1, 1), gridComp));

        SpawnLavaMarker("WH40KCinematicLavaStartMarker", LavaFlowId, WH40KCinematicLavaMarkerRole.Start, 0, gridUid, gridComp, new Vector2i(0, 0));
        SpawnLavaMarker("WH40KCinematicLavaGuideMarker", LavaFlowId, WH40KCinematicLavaMarkerRole.Guide, 1, gridUid, gridComp, new Vector2i(2, 0));
        SpawnLavaMarker("WH40KCinematicLavaEndMarker", LavaFlowId, WH40KCinematicLavaMarkerRole.End, 999, gridUid, gridComp, new Vector2i(4, 1));
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

    private int CountEntitiesByPrototype(string prototypeId)
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == prototypeId)
                count++;
        }

        return count;
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
