using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared._WH40K.Cinematic;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using ClientCinematicSystem = Content.Client._WH40K.Cinematic.WH40KCinematicSystem;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KCinematicShotCameraTests : WH40KCinematicGameTest
{
    private const string CameraPointPrototype = "WH40KCinematicPhase2CameraPoint";
    private const string BasicShot = "WH40KCinematicPhase2BasicShot";
    private const string PauseMapShot = "WH40KCinematicPhase2PauseMap";
    private const string MissingShotFallback = "WH40KCinematicPhase2MissingShotFallback";
    private const string InvalidBlend = "WH40KCinematicPhase2InvalidBlend";
    private const string DelayedLockShot = "WH40KCinematicPhase2DelayedLockShot";
    private const string MarkerRetainsShot = "WH40KCinematicPhase2MarkerRetainsShot";
    private const string CameraPointId = "phase2_cam";

    [TestPrototypes]
    private static readonly string TestPrototypes = $@"
- type: entity
  id: {CameraPointPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicCameraPoint
    pointId: {CameraPointId}
    zoom: 1.10
    rotation: 8

- type: wh40kCinematic
  id: {BasicShot}
  restoreInputDelay: 0.5
  steps:
  - id: intro
    type: Shot
    waitMode: Duration
    duration: 0.60
    cameraPoint: {CameraPointId}
    cameraTransition: Blend
    blendDuration: 0.20
    cameraZoom: 1.40
    cameraRotation: 22
    shake: 0.50
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {PauseMapShot}
  worldFreezeMode: PauseMap
  steps:
  - id: intro
    type: Shot
    waitMode: Duration
    duration: 3.00
    cameraPoint: {CameraPointId}
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {MissingShotFallback}
  steps:
  - id: missing
    type: Shot
    waitMode: Duration
    duration: 0.20
    cameraPoint: phase2_missing_cam
    optionalCameraPoint: true
  - id: fallback
    type: Shot
    waitMode: Duration
    duration: 0.35
    cameraPoint: {CameraPointId}
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {InvalidBlend}
  steps:
  - id: invalid
    type: Shot
    waitMode: Duration
    duration: 0.50
    cameraPoint: {CameraPointId}
    cameraTransition: Blend
    blendDuration: 0

- type: wh40kCinematic
  id: {DelayedLockShot}
  lockAudienceOnStart: false
  steps:
  - id: warning
    waitMode: Duration
    duration: 0.25
  - id: intro
    type: Shot
    waitMode: Duration
    duration: 0.40
    audienceLock: Lock
    cameraPoint: {CameraPointId}
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {MarkerRetainsShot}
  steps:
  - id: intro
    type: Shot
    waitMode: Duration
    duration: 0.20
    cameraPoint: {CameraPointId}
  - id: eruption_burst
    waitMode: Duration
    duration: 0.25
  - id: end
    type: EndCinematic
    waitMode: Terminal
";

    // TODO: add a dedicated latejoin / reconnect harness pass once multi-session cinematic tests are scheduled.

    [Test]
    public async Task ShotStateLocksPlayerRestoresEyeAndPublishesClientShot()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();
        var eyeManager = Client.ResolveDependency<IEyeManager>();
        var originalEye = new FixedEye();
        EntityUid player = default;

        await SpawnCameraPointAtPlayer();

        await ClientStep(() =>
        {
            var currentEye = eyeManager.CurrentEye;
            originalEye.Position = currentEye.Position;
            originalEye.Zoom = currentEye.Zoom;
            originalEye.Rotation = currentEye.Rotation;
            originalEye.Offset = currentEye.Offset;
            eyeManager.CurrentEye = originalEye;
        });

        await ServerStep(() =>
        {
            player = Server.PlayerMan.Sessions.Single().AttachedEntity!.Value;

            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(BasicShot), out _), Is.True);
            Assert.That(SEntMan.HasComponent<WH40KCinematicLockedComponent>(player), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientSys.IsCinematicModeActive && clientSys.ActiveState?.ActiveShot != null,
            maxTicks: 12,
            label: "wait for basic shot activation");

        await ClientStep(() =>
        {
            Assert.That(clientSys.IsCinematicModeActive, Is.True);
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveShot, Is.Not.Null);

            var shot = clientSys.ActiveState.ActiveShot!;
            Assert.Multiple(() =>
            {
                Assert.That(shot.CameraPointId, Is.EqualTo(CameraPointId));
                Assert.That(shot.Zoom, Is.EqualTo(1.40f).Within(0.001f));
                Assert.That(shot.RotationDegrees, Is.EqualTo(22f).Within(0.001f));
                Assert.That(shot.TransitionMode, Is.EqualTo(WH40KCinematicCameraTransitionMode.Blend));
                Assert.That(shot.BlendDurationSeconds, Is.EqualTo(0.20f).Within(0.001f));
                Assert.That(shot.ShakeIntensity, Is.EqualTo(0.50f).Within(0.001f));
            });
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState == null &&
                  clientSys.LastStoppedEvent != null &&
                  !serverSys.GetSnapshot().IsActive,
            maxTicks: 70,
            label: "wait for BasicShot completion");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Null);
            Assert.That(clientSys.LastStoppedEvent, Is.Not.Null);
            Assert.That(clientSys.LastStoppedEvent!.UnlockDelaySeconds, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(clientSys.IsCinematicModeActive, Is.False);
            Assert.That(ReferenceEquals(eyeManager.CurrentEye, originalEye), Is.True);
        });

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            Assert.That(SEntMan.HasComponent<WH40KCinematicLockedComponent>(player), Is.False);
        });
    }

    [Test]
    public async Task PauseMapModePausesAndRestoresPlayerMap()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var mapSystem = Server.System<SharedMapSystem>();
        MapId mapId = default;

        await SpawnCameraPointAtPlayer();

        await ServerStep(() =>
        {
            var player = Server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
            mapId = SComp<TransformComponent>(player).MapID;

            Assert.That(mapSystem.IsPaused(mapId), Is.False);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(PauseMapShot), out _), Is.True);
            Assert.That(mapSystem.IsPaused(mapId), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive && !mapSystem.IsPaused(mapId),
            maxTicks: 200,
            label: "wait for PauseMapShot completion and map unpause");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            Assert.That(mapSystem.IsPaused(mapId), Is.False);
        });
    }

    [Test]
    public async Task MissingCameraPointStepIsSkippedAndFallbackShotStillPlays()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();

        await SpawnCameraPointAtPlayer();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(MissingShotFallback), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState?.ActiveStepId == "fallback" && clientSys.ActiveState.ActiveShot != null,
            maxTicks: 12,
            label: "wait for fallback shot after missing camera point skip");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("fallback"));
            Assert.That(clientSys.ActiveState.ActiveShot, Is.Not.Null);
            Assert.That(clientSys.ActiveState.ActiveShot!.CameraPointId, Is.EqualTo(CameraPointId));
        });
    }

    [Test]
    public async Task MarkerStepRetainsPreviousFixedCameraShot()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();

        await SpawnCameraPointAtPlayer();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(MarkerRetainsShot), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState?.ActiveStepId == "eruption_burst" &&
                  clientSys.ActiveState.ActiveShot != null &&
                  clientSys.IsCinematicModeActive,
            maxTicks: 20,
            label: "wait for marker step retaining fixed shot");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("eruption_burst"));
            Assert.That(clientSys.ActiveState.ActiveShot, Is.Not.Null);
            Assert.That(clientSys.ActiveState.ActiveShot!.CameraPointId, Is.EqualTo(CameraPointId));
            Assert.That(clientSys.IsCinematicModeActive, Is.True);
        });
    }

    [Test]
    public async Task ShotShakeTemporarilyOffsetsOrRotatesClientEye()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();
        var eyeManager = Client.ResolveDependency<IEyeManager>();
        var maxOffsetMagnitude = 0f;
        var maxRotationDelta = 0f;

        await SpawnCameraPointAtPlayer();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(BasicShot), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () =>
            {
                if (!clientSys.IsCinematicModeActive || clientSys.ActiveState?.ActiveShot == null)
                    return false;

                maxOffsetMagnitude = Math.Max(maxOffsetMagnitude, eyeManager.CurrentEye.Offset.Length());
                var rotationDelta = Math.Abs((float) eyeManager.CurrentEye.Rotation.Degrees - clientSys.ActiveState.ActiveShot.RotationDegrees);
                maxRotationDelta = Math.Max(maxRotationDelta, rotationDelta);
                return maxOffsetMagnitude > 0.001f || maxRotationDelta > 0.01f;
            },
            maxTicks: 15,
            label: "observe camera shake");
    }

    [Test]
    public async Task ShotAddsAndRemovesCameraPointViewSubscription()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        EntityUid cameraPoint = default;
        ICommonSession session = null!;

        await EnsurePlayerOnTestMap();

        await ServerStep(() =>
        {
            if (!SEntMan.EntityQuery<WH40KCinematicCameraPointComponent>().Any())
            {
                var player = Server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
                var coords = SComp<TransformComponent>(player).Coordinates.Offset(new Vector2(25f, 25f));
                cameraPoint = SEntMan.SpawnEntity(CameraPointPrototype, coords);
            }
            else
            {
                var pointQuery = SEntMan.EntityQueryEnumerator<WH40KCinematicCameraPointComponent>();
                Assert.That(pointQuery.MoveNext(out cameraPoint, out _), Is.True);
            }

            session = Server.PlayerMan.Sessions.Single();
            Assert.That(session.ViewSubscriptions.Contains(cameraPoint), Is.False);
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(BasicShot), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => session != null && session.ViewSubscriptions.Contains(cameraPoint),
            maxTicks: 12,
            label: "wait for camera point subscription");

        await ServerStep(() =>
        {
            Assert.That(session.ViewSubscriptions.Contains(cameraPoint), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive && !session.ViewSubscriptions.Contains(cameraPoint),
            maxTicks: 70,
            label: "wait for camera point unsubscription");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            Assert.That(session.ViewSubscriptions.Contains(cameraPoint), Is.False);
        });
    }

    [Test]
    public async Task ValidatorRejectsInvalidBlendShotAuthoring()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            var errors = serverSys.ValidatePrototype(SProtoMan.Index<WH40KCinematicPrototype>(InvalidBlend));
            Assert.That(errors, Is.Not.Empty);
            Assert.That(errors.Any(error => error.Contains("blend transition requires blendDuration > 0.")), Is.True);
        });
    }

    [Test]
    public async Task DelayedLockShotStartsUnlockedThenLocksOnStep()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();
        EntityUid player = default;

        await SpawnCameraPointAtPlayer();

        await ServerStep(() =>
        {
            player = Server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(DelayedLockShot), out _), Is.True);
            Assert.That(SEntMan.HasComponent<WH40KCinematicLockedComponent>(player), Is.False);
            Assert.That(serverSys.GetSnapshot().ActiveStepId, Is.EqualTo("warning"));
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState?.ActiveStepId == "warning",
            maxTicks: 12,
            label: "wait for DelayedLockShot warning step");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("warning"));
            Assert.That(clientSys.ActiveState.AudienceLocked, Is.False);
            Assert.That(clientSys.IsCinematicModeActive, Is.False);
        });

        await WaitForPairConditionStep(
            () => serverSys.GetSnapshot().ActiveStepId == "intro" &&
                  SEntMan.HasComponent<WH40KCinematicLockedComponent>(player),
            maxTicks: 20,
            label: "wait for audience lock on intro");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().ActiveStepId, Is.EqualTo("intro"));
            Assert.That(SEntMan.HasComponent<WH40KCinematicLockedComponent>(player), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState?.ActiveStepId == "intro" &&
                  clientSys.ActiveState.AudienceLocked &&
                  clientSys.ActiveState.ActiveShot != null &&
                  clientSys.IsCinematicModeActive,
            maxTicks: 8,
            label: "wait for client intro shot with lock");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("intro"));
            Assert.That(clientSys.ActiveState.AudienceLocked, Is.True);
            Assert.That(clientSys.IsCinematicModeActive, Is.True);
            Assert.That(clientSys.ActiveState.ActiveShot, Is.Not.Null);
        });
    }

    private async Task SpawnCameraPointAtPlayer()
    {
        await EnsurePlayerOnTestMap();

        await ServerStep(() =>
        {
            if (SEntMan.EntityQuery<WH40KCinematicCameraPointComponent>().Any())
                return;

            var player = Server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
            SEntMan.SpawnEntity(CameraPointPrototype, SComp<TransformComponent>(player).Coordinates);
        });
    }

    private async Task EnsurePlayerOnTestMap()
    {
        var testMap = await Pair.CreateTestMap();

        await ServerStep(() =>
        {
            var player = Server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
            var transformSystem = Server.System<SharedTransformSystem>();

            transformSystem.SetCoordinates(player, testMap.GridCoords);

            Assert.That(SComp<TransformComponent>(player).MapID, Is.Not.EqualTo(MapId.Nullspace));
            Assert.That(SComp<TransformComponent>(player).MapID, Is.EqualTo(testMap.MapId));
        });
    }
}
