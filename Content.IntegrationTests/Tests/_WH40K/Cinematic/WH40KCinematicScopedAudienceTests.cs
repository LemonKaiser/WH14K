#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared._WH40K.Cinematic;
using Content.Shared.Trigger;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using ClientCinematicSystem = Content.Client._WH40K.Cinematic.WH40KCinematicSystem;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KCinematicScopedAudienceTests : WH40KCinematicGameTest
{
    private const string CameraAPrototype = "WH40KCinematicPhase6CameraA";
    private const string CameraBPrototype = "WH40KCinematicPhase6CameraB";
    private const string TriggerPrototype = "WH40KCinematicPhase6Trigger";

    private const string CameraAId = "phase6_cam_a";
    private const string CameraBId = "phase6_cam_b";

    private const string TriggeredFlow = "WH40KCinematicPhase6Triggered";
    private const string DamageFlow = "WH40KCinematicPhase6Damage";
    private const string LowPriorityFlow = "WH40KCinematicPhase6LowPriority";
    private const string HighPriorityFlow = "WH40KCinematicPhase6HighPriority";
    private const string InvalidFlow = "WH40KCinematicPhase6Invalid";

    [TestPrototypes]
    private static readonly string TestPrototypes = $@"
- type: entity
  id: {CameraAPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicCameraPoint
    pointId: {CameraAId}
    zoom: 1.10
    rotation: -4

- type: entity
  id: {CameraBPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicCameraPoint
    pointId: {CameraBId}
    zoom: 1.00
    rotation: 8

- type: entity
  id: {TriggerPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicTrigger
    cinematic: {TriggeredFlow}
    audienceMode: TriggerUser
    oncePerUser: true

- type: wh40kCinematic
  id: {TriggeredFlow}
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: intro
    type: Shot
    waitMode: Duration
    duration: 0.30
    cameraPoint: {CameraAId}
  - id: player_view
    type: Shot
    waitMode: Duration
    duration: 0.30
    cameraSource: PlayerEntity
    audienceLock: Unlock
  - id: outro
    type: Shot
    waitMode: Duration
    duration: 0.30
    cameraPoint: {CameraBId}
    cameraTransition: Blend
    cameraEasing: BounceOut
    blendDuration: 0.10
    drawFov: false
    drawLight: false
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {DamageFlow}
  worldFreezeMode: LockPlayersOnly
  lockAudienceOnStart: false
  steps:
  - id: damage
    waitMode: Duration
    duration: 0.10
    actions:
    - type: ApplyLocalDamageToAudience
      damage:
        types:
          Blunt: 12
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {LowPriorityFlow}
  priority: 5
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: long_intro
    type: Shot
    waitMode: Duration
    duration: 5.00
    cameraPoint: {CameraAId}
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {HighPriorityFlow}
  priority: 50
  worldFreezeMode: LockPlayersOnly
  steps:
  - id: fast_intro
    type: Shot
    waitMode: Duration
    duration: 0.20
    cameraPoint: {CameraBId}
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {InvalidFlow}
  steps:
  - id: illegal_player_camera
    type: Shot
    waitMode: Duration
    duration: 0.20
    cameraSource: PlayerEntity
  - id: illegal_sound_scope
    waitMode: Duration
    duration: 0.20
    actions:
    - type: PlayGlobalSound
      deliveryScope: Pvs
      sound:
        path: /Audio/Misc/notice1.ogg
  - id: end
    type: EndCinematic
    waitMode: Terminal
";

    [Test]
    public async Task ValidationRejectsInvalidScopedAudienceAuthoring()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            var errors = serverSys.ValidatePrototype(SProtoMan.Index<WH40KCinematicPrototype>(InvalidFlow));
            Assert.That(errors.Any(error => error.Contains("non-fixed shot camera sources require audienceLock: Unlock")), Is.True);
            Assert.That(errors.Any(error => error.Contains("playGlobalSound only supports Audience or Broadcast")), Is.True);
        });
    }

    [Test]
    public async Task TriggeredScopedRunCanReturnToPlayerViewMidCinematic()
    {
        var serverPlayerMgr = Server.ResolveDependency<IPlayerManager>();
        var clientSys = Client.System<ClientCinematicSystem>();
        EntityUid trigger = default;

        await ServerStep(() =>
        {
            var player = serverPlayerMgr.Sessions.Single().AttachedEntity!.Value;
            SpawnAuthoringMarkersNear(player);
            trigger = SEntMan.SpawnEntity(TriggerPrototype, SComp<TransformComponent>(player).Coordinates);

            var ev = new TriggerEvent(player);
            SEntMan.EventBus.RaiseLocalEvent(trigger, ref ev);
            Assert.That(ev.Handled, Is.True);
        });

        await WaitForPairConditionStep(() =>
            clientSys.ActiveState?.CinematicId == TriggeredFlow &&
            clientSys.ActiveState.ActiveStepId == "intro" &&
            clientSys.IsCinematicModeActive,
            label: "wait for TriggeredFlow intro step");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.CinematicId, Is.EqualTo(TriggeredFlow));
            Assert.That(clientSys.ActiveState.ActiveStepId, Is.EqualTo("intro"));
            Assert.That(clientSys.IsCinematicModeActive, Is.True);
        });

        await WaitForPairConditionStep(() =>
            clientSys.ActiveState?.ActiveStepId == "player_view" &&
            clientSys.ActiveState.ActiveShot == null &&
            !clientSys.IsCinematicModeActive,
            label: "wait for return to player camera");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("player_view"));
            Assert.That(clientSys.ActiveState.ActiveShot, Is.Null);
            Assert.That(clientSys.IsCinematicModeActive, Is.False);
        });

        await WaitForPairConditionStep(() =>
            clientSys.ActiveState?.ActiveStepId == "outro" &&
            clientSys.ActiveState.ActiveShot != null &&
            clientSys.IsCinematicModeActive,
            label: "wait for TriggeredFlow outro step");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.ActiveStepId, Is.EqualTo("outro"));
            Assert.That(clientSys.ActiveState.ActiveShot, Is.Not.Null);
            Assert.That(clientSys.IsCinematicModeActive, Is.True);
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState == null && clientSys.LastStoppedEvent != null,
            label: "wait for TriggeredFlow completion");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Null);
            Assert.That(clientSys.LastStoppedEvent, Is.Not.Null);
            Assert.That(clientSys.LastStoppedEvent!.CinematicId, Is.EqualTo(TriggeredFlow));
        });
    }

    [Test]
    public async Task ScopedAudienceDamageAppliesToAttachedEntity()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var damageableSystem = Server.System<DamageableSystem>();
        var serverPlayerMgr = Server.ResolveDependency<IPlayerManager>();
        EntityUid attached = default;

        await ServerStep(() =>
        {
            attached = serverPlayerMgr.Sessions.Single().AttachedEntity!.Value;
            SpawnAuthoringMarkersNear(attached);
            Assert.That(serverSys.TryQueueForUsers(
                SProtoMan.Index<WH40KCinematicPrototype>(DamageFlow),
                new[] { serverPlayerMgr.Sessions.Single().UserId },
                out _), Is.True);
        });

        await WaitForPairConditionStep(() =>
        {
            if (!SEntMan.TryGetComponent(attached, out DamageableComponent? damageable))
                return false;

            return damageableSystem.GetTotalDamage((attached, damageable)).Float() > 0f;
        },
        maxTicks: 30,
        label: "wait for local audience damage");

        await ServerStep(() =>
        {
            Assert.That(SEntMan.TryGetComponent(attached, out DamageableComponent? damageable), Is.True);
            Assert.That(damageableSystem.GetTotalDamage((attached, damageable)).Float(), Is.GreaterThan(0f));
        });
    }

    [Test]
    public async Task HigherPriorityScopedRunInterruptsLowerPriorityAudienceConflict()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var serverPlayerMgr = Server.ResolveDependency<IPlayerManager>();
        var clientSys = Client.System<ClientCinematicSystem>();
        var userId = default(NetUserId);

        await ServerStep(() =>
        {
            var attached = serverPlayerMgr.Sessions.Single().AttachedEntity!.Value;
            SpawnAuthoringMarkersNear(attached);
            userId = serverPlayerMgr.Sessions.Single().UserId;

            Assert.That(serverSys.TryQueueForUsers(
                SProtoMan.Index<WH40KCinematicPrototype>(LowPriorityFlow),
                new[] { userId },
                out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState?.CinematicId == LowPriorityFlow,
            label: "wait for low priority local cinematic start");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.CinematicId, Is.EqualTo(LowPriorityFlow));
        });

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueueForUsers(
                SProtoMan.Index<WH40KCinematicPrototype>(HighPriorityFlow),
                new[] { userId },
                out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientSys.ActiveState?.CinematicId == HighPriorityFlow,
            label: "wait for high priority local cinematic interrupt");

        await ClientStep(() =>
        {
            Assert.That(clientSys.ActiveState, Is.Not.Null);
            Assert.That(clientSys.ActiveState!.CinematicId, Is.EqualTo(HighPriorityFlow));
        });
    }

    private void SpawnAuthoringMarkersNear(EntityUid target)
    {
        var coords = SComp<TransformComponent>(target).Coordinates;
        SEntMan.SpawnEntity(CameraAPrototype, coords);
        SEntMan.SpawnEntity(CameraBPrototype, coords);
    }
}
