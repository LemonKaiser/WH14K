#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WH40K.Cinematic;
using Content.Shared._WH40K.Cinematic;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K.Cinematic;

[TestFixture]
[NonParallelizable]
public sealed class WH40KCinematicNpcTrackTests : WH40KCinematicGameTest
{
    private const string NpcAnchorPrototype = "WH40KCinematicPhase8NpcAnchor";
    private const string NpcAnchorId = "phase8_npc_spawn";
    private const string ValidTrack = "WH40KCinematicPhase8Track";
    private const string InvalidTrack = "WH40KCinematicPhase8BrokenTrack";
    private const string ValidFlow = "WH40KCinematicPhase8Valid";
    private const string InvalidFlow = "WH40KCinematicPhase8Invalid";
    private const string InvalidTrackFlow = "WH40KCinematicPhase8InvalidTrackFlow";

    [TestPrototypes]
    private static readonly string TestPrototypes = $@"
- type: entity
  id: {NpcAnchorPrototype}
  parent: MarkerBase
  components:
  - type: WH40KCinematicNpcAnchor
    anchorId: {NpcAnchorId}
    rotation: 45

- type: wh40kCinematicActorTrack
  id: {ValidTrack}
  segments:
  - id: opener
    entries:
    - at: 0
      action:
        type: NpcSpeak
        npcId: actor_01
        message: ""Hold position.""

- type: wh40kCinematicActorTrack
  id: {InvalidTrack}
  segments:
  - id: broken
    entries:
    - at: 0.5
      action:
        type: NpcSpeak
        id: should_not_exist
        npcId: actor_01
        message: ""Broken.""
    - at: 0.25
      action:
        type: PlayActorTrack
        npcId: actor_01
        trackId: {ValidTrack}

- type: wh40kCinematic
  id: {ValidFlow}
  steps:
  - id: intro
    waitMode: Instant
    actions:
    - type: SpawnNpc
      npcId: actor_01
      anchorId: {NpcAnchorId}
      prototype: MobHuman
    - type: PlayActorTrack
      npcId: actor_01
      trackId: {ValidTrack}
      trackSegmentId: opener
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {InvalidTrackFlow}
  steps:
  - id: intro
    waitMode: Instant
    actions:
    - type: SpawnNpc
      npcId: actor_01
      anchorId: {NpcAnchorId}
      prototype: MobHuman
    - type: PlayActorTrack
      npcId: actor_01
      trackId: {InvalidTrack}
      trackSegmentId: broken
  - id: end
    type: EndCinematic
    waitMode: Terminal

- type: wh40kCinematic
  id: {InvalidFlow}
  steps:
  - id: broken
    waitMode: Instant
    actions:
    - type: SpawnNpc
      anchorId: {NpcAnchorId}
    - type: NpcMoveByOffset
      npcId: actor_01
    - type: NpcUseEntity
      npcId: actor_01
    - type: PlayActorTrack
      npcId: actor_01
  - id: end
    type: EndCinematic
    waitMode: Terminal
";

    [Test]
    public async Task ValidationRejectsInvalidNpcTrackAuthoring()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            var errors = serverSys.ValidatePrototype(SProtoMan.Index<WH40KCinematicPrototype>(InvalidFlow));
            Assert.That(errors.Any(error => error.Contains("spawnNpc requires npcId")), Is.True);
            Assert.That(errors.Any(error => error.Contains("npcMoveByOffset requires offset")), Is.True);
            Assert.That(errors.Any(error => error.Contains("npcUseEntity requires targetNpcId, anchorId, or prototype")), Is.True);
            Assert.That(errors.Any(error => error.Contains("playActorTrack requires trackId")), Is.True);
        });
    }

    [Test]
    public async Task LoadedValidationAcceptsBasicNpcAnchorAndTrackAuthoring()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();
        var playerManager = Server.ResolveDependency<IPlayerManager>();

        await ServerStep(() =>
        {
            var attached = playerManager.Sessions.Single().AttachedEntity!.Value;
            var coords = Server.EntMan.GetComponent<TransformComponent>(attached).Coordinates;
            SSpawnAtPosition(NpcAnchorPrototype, coords);
            Assert.That(serverSys.TryValidateLoadedPrototype(ValidFlow, out _), Is.True);
        });
    }

    [Test]
    public async Task ValidationRejectsBrokenActorTrackAuthoring()
    {
        var serverSys = Server.System<WH40KCinematicSystem>();

        await ServerStep(() =>
        {
            var errors = serverSys.ValidatePrototype(SProtoMan.Index<WH40KCinematicPrototype>(InvalidTrackFlow));
            Assert.That(errors.Any(error => error.Contains("entries must be sorted by ascending at")), Is.True);
            Assert.That(errors.Any(error => error.Contains("explicit action id is not supported inside actor tracks")), Is.True);
            Assert.That(errors.Any(error => error.Contains("nested playActorTrack is not supported inside actor tracks")), Is.True);
        });
    }
}
