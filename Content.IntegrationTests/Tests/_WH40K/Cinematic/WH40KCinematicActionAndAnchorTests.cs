using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared._WH40K.Cinematic;
using Robust.Client.Graphics;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using ClientNotificationSystem = Content.Client._WH40K.Notifications.WH40KNotificationSystem;
using ClientCinematicSystem = Content.Client._WH40K.Cinematic.WH40KCinematicSystem;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KCinematicActionAndAnchorTests : WH40KCinematicGameTest
{
    private const string SoundAnchorPrototype = "WH40KCinematicPhase3SoundAnchor";
    private const string SpawnAnchorPrototype = "WH40KCinematicPhase3SpawnAnchor";
    private const string TimedSpawnedPrototype = "WH40KCinematicPhase3TimedSpawned";
    private const string NotifyCinematic = "WH40KCinematicPhase3Notify";
    private const string TimeoutCinematic = "WH40KCinematicPhase3AwaitTimeout";
    private const string PersistentSoundCinematic = "WH40KCinematicPhase3PersistentSound";
    private const string StoppablePersistentSoundCinematic = "WH40KCinematicPhase3StoppablePersistentSound";
    private const string AudienceShakeCinematic = "WH40KCinematicPhase3AudienceShake";
    private const string SpawnBlockingCinematic = "WH40KCinematicPhase3SpawnBlocking";
    private const string OptionalAnchorCinematic = "WH40KCinematicPhase3OptionalAnchor";
    private const string SoundAnchorId = "phase3_sound";
    private const string SpawnAnchorId = "phase3_spawn";

    [TestPrototypes]
    private static readonly string TestPrototypes = $@"
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
  id: {TimedSpawnedPrototype}
  components:
  - type: TimedDespawn
    lifetime: 0.15

- type: wh40kCinematic
  id: {NotifyCinematic}
  steps:
  - id: notify
    waitMode: Duration
    duration: 0.20
    actions:
    - type: Notify
      title: Apocalypse
      text: Volcano activity detected
      category: Event
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {TimeoutCinematic}
  steps:
  - id: wait-loop
    waitMode: AwaitCompletionOrTimeout
    timeout: 0.25
    actions:
    - type: PlayGlobalSound
      sound:
        path: /Audio/Misc/notice1.ogg
      audio:
        loop: true
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {PersistentSoundCinematic}
  steps:
  - id: rumble
    waitMode: Duration
    duration: 0.10
    actions:
    - type: PlayAnchorSound
      anchorId: {SoundAnchorId}
      sound:
        path: /Audio/Misc/notice1.ogg
      audio:
        loop: true
      persistAfterCinematic: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {StoppablePersistentSoundCinematic}
  steps:
  - id: start-rumble
    waitMode: Duration
    duration: 0.20
    actions:
    - type: PlayAnchorSound
      id: rumble-a
      anchorId: {SoundAnchorId}
      sound:
        path: /Audio/Misc/notice1.ogg
      audio:
        loop: true
      persistAfterCinematic: true
    - type: PlayAnchorSound
      id: rumble-b
      anchorId: {SoundAnchorId}
      sound:
        path: /Audio/Misc/notice1.ogg
      audio:
        loop: true
      persistAfterCinematic: true
  - id: stop-rumble
    waitMode: Duration
    duration: 0.05
    actions:
    - type: StopActions
      targetActionIds:
      - rumble-a
      - rumble-b
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {AudienceShakeCinematic}
  lockAudienceOnStart: false
  steps:
  - id: tremor
    waitMode: Duration
    duration: 0.30
    actions:
    - type: StartAudienceShake
      id: phase3-quake
      shakeIntensity: 0.90
      shakeRampDuration: 0.10
      shakePulseInterval: 0.04
  - id: calm
    waitMode: Duration
    duration: 0.05
    actions:
    - type: StopActions
      targetActionId: phase3-quake
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {SpawnBlockingCinematic}
  steps:
  - id: spawn
    waitMode: AwaitCompletion
    actions:
    - type: SpawnAtAnchor
      anchorId: {SpawnAnchorId}
      prototype: {TimedSpawnedPrototype}
      blocking: true
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {OptionalAnchorCinematic}
  steps:
  - id: optional-anchor
    waitMode: Instant
    actions:
    - type: PlayAnchorSound
      anchorId: phase3_missing_anchor
      sound:
        path: /Audio/Misc/notice1.ogg
      optionalAnchor: true
  - id: fallback
    waitMode: Duration
    duration: 0.15
    actions:
    - type: Notify
      title: Fallback
      text: Optional anchor skipped
  - id: end
    type: EndCinematic
    waitMode: Terminal
";

    [Test]
    public async Task NotifyActionPushesClientNotification()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientNotifications = Client.System<ClientNotificationSystem>();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(NotifyCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientNotifications.LastNotification != null,
            maxTicks: 12,
            label: "wait for Notify client notification");

        await ClientStep(() =>
        {
            Assert.That(clientNotifications.LastNotification, Is.Not.Null);
            Assert.That(clientNotifications.LastNotification!.Title, Is.EqualTo("Apocalypse"));
            Assert.That(clientNotifications.LastNotification.Text, Is.EqualTo("Volcano activity detected"));
        });
    }

    [Test]
    public async Task AwaitCompletionOrTimeoutAdvancesPastLoopingSound()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(TimeoutCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => serverSys.GetSnapshot().ActiveStepId == "wait-loop",
            maxTicks: 12,
            label: "wait for TimeoutCinematic wait-loop");

        await ServerStep(() =>
        {
            var snapshot = serverSys.GetSnapshot();
            Assert.That(snapshot.IsActive, Is.True);
            Assert.That(snapshot.ActiveStepId, Is.EqualTo("wait-loop"));
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 40,
            label: "wait for looping sound timeout");

        await ServerStep(() => Assert.That(serverSys.GetSnapshot().IsActive, Is.False));
    }

    [Test]
    public async Task PersistentAnchorSoundSurvivesCinematicEnd()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var audioSys = Server.System<SharedAudioSystem>();
        var initialAudioCount = 0;

        await SpawnSoundAnchorAtPlayer();

        await ServerStep(() =>
        {
            initialAudioCount = SEntMan.EntityQuery<AudioComponent>().Count();
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(PersistentSoundCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive &&
                  SEntMan.EntityQuery<AudioComponent>().Count() > initialAudioCount,
            maxTicks: 24,
            label: "wait for PersistentSoundCinematic completion");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            Assert.That(SEntMan.EntityQuery<AudioComponent>().Count(), Is.GreaterThan(initialAudioCount));
        });

        await ServerStep(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                audioSys.Stop(uid);
            }
        });
    }

    [Test]
    public async Task StopActionsCanStopPersistentLoopingSoundsByActionId()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var initialAudioCount = 0;

        await SpawnSoundAnchorAtPlayer();

        await ServerStep(() =>
        {
            initialAudioCount = SEntMan.EntityQuery<AudioComponent>().Count();
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(StoppablePersistentSoundCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => SEntMan.EntityQuery<AudioComponent>().Count() > initialAudioCount,
            maxTicks: 12,
            label: "wait for persistent audio start");

        await ServerStep(() =>
        {
            Assert.That(SEntMan.EntityQuery<AudioComponent>().Count(), Is.GreaterThan(initialAudioCount));
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive &&
                  SEntMan.EntityQuery<AudioComponent>().Count() == initialAudioCount,
            maxTicks: 40,
            label: "wait for stopActions to stop persistent audio");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
            Assert.That(SEntMan.EntityQuery<AudioComponent>().Count(), Is.EqualTo(initialAudioCount));
        });
    }

    [Test]
    public async Task AudienceShakeRunsBeforeLockWithoutEnteringCinematicModeAndCanBeStopped()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientSys = Client.System<ClientCinematicSystem>();
        var eyeManager = Client.ResolveDependency<IEyeManager>();
        var maxOffsetMagnitude = 0f;

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(AudienceShakeCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () =>
            {
                if (clientSys.ActiveState?.ActiveStepId != "tremor")
                    return false;

                Assert.That(clientSys.IsCinematicModeActive, Is.False);
                maxOffsetMagnitude = Math.Max(maxOffsetMagnitude, eyeManager.CurrentEye.Offset.Length());
                return maxOffsetMagnitude > 0.001f;
            },
            maxTicks: 14,
            label: "observe audience shake without cinematic mode");

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 40,
            label: "wait for AudienceShakeCinematic completion");

        await ServerStep(() =>
        {
            Assert.That(serverSys.GetSnapshot().IsActive, Is.False);
        });

        await ClientStep(() =>
        {
            Assert.That(clientSys.IsCinematicModeActive, Is.False);
        });
    }

    [Test]
    public async Task SpawnAtAnchorBlockingWaitCompletesWhenSpawnedEntityDespawns()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await SpawnSpawnAnchorAtPlayer();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(SpawnBlockingCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => serverSys.GetSnapshot().ActiveStepId == "spawn",
            maxTicks: 12,
            label: "wait for blocking spawn step");

        await ServerStep(() =>
        {
            var snapshot = serverSys.GetSnapshot();
            Assert.That(snapshot.IsActive, Is.True);
            Assert.That(snapshot.ActiveStepId, Is.EqualTo("spawn"));
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 40,
            label: "wait for blocking spawn completion");

        await ServerStep(() => Assert.That(serverSys.GetSnapshot().IsActive, Is.False));
    }

    [Test]
    public async Task MissingOptionalAnchorActionDoesNotAbortTimeline()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var clientNotifications = Client.System<ClientNotificationSystem>();

        await ServerStep(() =>
        {
            Assert.That(serverSys.TryQueue(SProtoMan.Index<WH40KCinematicPrototype>(OptionalAnchorCinematic), out _), Is.True);
        });

        await WaitForPairConditionStep(
            () => clientNotifications.LastNotification?.Text == "Optional anchor skipped",
            maxTicks: 12,
            label: "wait for fallback notification after optional anchor");

        await ClientStep(() =>
        {
            Assert.That(clientNotifications.LastNotification, Is.Not.Null);
            Assert.That(clientNotifications.LastNotification!.Text, Is.EqualTo("Optional anchor skipped"));
        });

        await WaitForPairConditionStep(
            () => !serverSys.GetSnapshot().IsActive,
            maxTicks: 24,
            label: "wait for OptionalAnchorCinematic completion");

        await ServerStep(() => Assert.That(serverSys.GetSnapshot().IsActive, Is.False));
    }

    private async Task SpawnSoundAnchorAtPlayer()
    {
        await ServerStep(() =>
        {
            if (SEntMan.EntityQuery<WH40KCinematicSoundAnchorComponent>().Any())
                return;

            var player = Server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
            SEntMan.SpawnEntity(SoundAnchorPrototype, SComp<TransformComponent>(player).Coordinates);
        });
    }

    private async Task SpawnSpawnAnchorAtPlayer()
    {
        await ServerStep(() =>
        {
            if (SEntMan.EntityQuery<WH40KCinematicSpawnAnchorComponent>().Any())
                return;

            var player = Server.PlayerMan.Sessions.Single().AttachedEntity!.Value;
            SEntMan.SpawnEntity(SpawnAnchorPrototype, SComp<TransformComponent>(player).Coordinates);
        });
    }
}
