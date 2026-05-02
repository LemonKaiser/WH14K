#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared.GameTicking;
using Content.Shared._WH40K.Cinematic;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using ClientCinematicSystem = Content.Client._WH40K.Cinematic.WH40KCinematicSystem;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KCinematicSceneControlTests : WH40KCinematicGameTest
{
    private const string CameraAPrototype = "WH40KCinematicPhase7CameraA";
    private const string CameraBPrototype = "WH40KCinematicPhase7CameraB";

    private const string CameraAId = "phase7_cam_a";
    private const string CameraBId = "phase7_cam_b";

    private const string SignalFlow = "WH40KCinematicPhase7Signal";
    private const string ControlFlow = "WH40KCinematicPhase7Control";
    private const string InvalidFlow = "WH40KCinematicPhase7Invalid";

    [TestPrototypes]
    private static readonly string TestPrototypes = $@"
- type: entity
  id: {CameraAPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicCameraPoint
    pointId: {CameraAId}
    zoom: 1.05
    rotation: -6

- type: entity
  id: {CameraBPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicCameraPoint
    pointId: {CameraBId}
    zoom: 0.95
    rotation: 10

- type: wh40kCinematic
  id: {SignalFlow}
  worldFreezeMode: LockPlayersOnly
  lockAudienceOnStart: false
  steps:
  - id: intro
    type: Shot
    waitMode: Duration
    duration: 0.20
    cameraPoint: {CameraAId}
  - id: wait_gate
    waitMode: AwaitSignal
    waitSignals:
    - resume_signal
  - id: outro
    type: Shot
    waitMode: Duration
    duration: 0.20
    cameraPoint: {CameraBId}
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {ControlFlow}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: hold
    type: Shot
    waitMode: Duration
    duration: 5.00
    cameraPoint: {CameraAId}
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {InvalidFlow}
  defaultWaitTimeout: -1
  steps:
  - id: invalid_wait
    waitMode: AwaitSignal
  - id: invalid_scene
    waitMode: Instant
    actions:
    - type: LoadSceneMap
      sceneMapPath: /Maps/Test/empty.yml
      sceneTransferMode: TeleportParticipants
  - id: invalid_scene_return
    waitMode: Instant
    actions:
    - type: LoadSceneMap
      contextId: invalid_scene
      sceneMapPath: /Maps/Test/empty.yml
      sceneTransferMode: TeleportParticipants
      entryAnchorId: scene_entry
      sceneCleanupPolicy: DestroyOnFinish
      sceneReturnPolicy: None
  - id: end
    type: EndCinematic
    waitMode: Terminal
";

    [Test]
    public async Task ValidationRejectsInvalidSceneControlAuthoring()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            var errors = serverSys.ValidatePrototype(SProtoMan.Index<WH40KCinematicPrototype>(InvalidFlow));
            Assert.That(errors.Any(error => error.Contains("defaultWaitTimeout must be >= 0")), Is.True);
            Assert.That(errors.Any(error => error.Contains("AwaitSignal requires at least one waitSignal")), Is.True);
            Assert.That(errors.Any(error => error.Contains("loadSceneMap requires contextId")), Is.True);
            Assert.That(errors.Any(error => error.Contains("TeleportParticipants scene load requires entryAnchorId")), Is.True);
            Assert.That(errors.Any(error => error.Contains("TeleportParticipants scene load cannot use DestroyOnFinish together with sceneReturnPolicy=None")), Is.True);
        });
    }

    [Test]
    public async Task AwaitSignalStepContinuesAfterRuntimeSignal()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var serverPlayerMgr = Server.ResolveDependency<IPlayerManager>();
        var clientSys = Client.System<ClientCinematicSystem>();

        await ServerStep(() =>
        {
            var attached = serverPlayerMgr.Sessions.Single().AttachedEntity!.Value;
            SpawnAuthoringMarkersNear(attached);
            Assert.That(serverSys.TryQueue(SignalFlow, out _), Is.True);
        });

        await WaitForPairConditionStep(() =>
            clientSys.ActiveState?.CinematicId == SignalFlow &&
            clientSys.ActiveState.ActiveStepId == "wait_gate",
            label: "wait for SignalFlow wait_gate");

        int runSerial = 0;
        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("wait_gate"));
            runSerial = clientSys.ActiveState.RunSerial;
        });

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryEmitSignal(runSerial, "resume_signal", out _), Is.True);
        });

        await WaitForPairConditionStep(() =>
            clientSys.ActiveState?.CinematicId == SignalFlow &&
            clientSys.ActiveState.ActiveStepId == "outro",
            label: "wait for SignalFlow outro");

        await WaitForPairConditionStep(
            () => clientSys.ActiveState == null && clientSys.LastStoppedEvent != null,
            label: "wait for SignalFlow completion");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Null);
            Assert.That(clientSys.LastStoppedEvent, Is.Not.Null);
            Assert.That(clientSys.LastStoppedEvent!.CinematicId, Is.EqualTo(SignalFlow));
        });
    }

    [Test]
    public async Task PauseResumeAndAdvanceControlActiveRun()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var serverPlayerMgr = Server.ResolveDependency<IPlayerManager>();
        var clientSys = Client.System<ClientCinematicSystem>();

        await ServerStep(() =>
        {
            var attached = serverPlayerMgr.Sessions.Single().AttachedEntity!.Value;
            SpawnAuthoringMarkersNear(attached);
            Assert.That(serverSys.TryQueue(ControlFlow, out _), Is.True);
        });

        await WaitForPairConditionStep(() =>
            clientSys.ActiveState?.CinematicId == ControlFlow &&
            clientSys.ActiveState.ActiveStepId == "hold",
            label: "wait for ControlFlow hold");

        int runSerial = 0;
        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("hold"));
            runSerial = clientSys.ActiveState.RunSerial;
        });

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryPauseRun(runSerial, out _), Is.True);
        });

        await RunTicksStep(10);

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("hold"));
        });

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryResumeRun(runSerial, out _), Is.True);
            Assert.That(serverSys.TryAdvanceRun(runSerial, out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState == null && clientSys.LastStoppedEvent != null,
            label: "wait for ControlFlow completion");
    }

    private void SpawnAuthoringMarkersNear(EntityUid target)
    {
        var coords = SComp<TransformComponent>(target).Coordinates;
        SEntMan.SpawnEntity(CameraAPrototype, coords);
        SEntMan.SpawnEntity(CameraBPrototype, coords);
    }
}
