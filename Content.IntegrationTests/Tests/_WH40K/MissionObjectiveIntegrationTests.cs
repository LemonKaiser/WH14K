#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server._WH40K.Command;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.Influence;
using Content.Server._WH40K.Stats;
using Content.Shared._WH40K.Command;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class MissionObjectiveIntegrationTests
{
    private const string Imperium = "Imperium";
    private const string Heretics = "Heretics";

    private const string CargoMissionId = "imp_omnissiah_convoy";
    private const string BannerMissionId = "imp_raise_the_standard";
    private const string HereticsBannerMissionId = "her_raise_the_warp_standard";
    private const string IntelRelayMissionId = "imp_vox_litany_relay";
    private const string ZoneMissionId = "imp_ruin_purge";

    private const string CargoPrototype = "WH40KMissionCargoCrate";
    private const string CargoDeliveryPrototype = "WH40KMissionCargoCrateDelivery";
    private const string MissionBeaconPrototype = "WH40KMissionZoneBeacon";

    private const string ImperiumBannerPrototype = "WHGvardiaBanner";
    private const string HereticsBannerPrototype = "WHChaosBanner";
    private static readonly string[] ImperiumBannerPrototypes =
    [
        "WHGvardiaBanner",
        "WHGvardiaBanner2",
        "MechanicusBanner"
    ];

    private const string ImperiumReinforcementPrototype = "MobHumanWH40KImperiumReinforcement";
    private const string HereticsReinforcementPrototype = "MobHumanWH40KHereticReinforcement";

    private const float DeliveryRadius = 5f;
    private const float BannerDetectionRadius = 1.45f;

    private sealed class MissionOutcomeProbeSystem : EntitySystem
    {
        public readonly List<WH40KMissionOutcomeAppliedEvent> Outcomes = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<WH40KMissionOutcomeAppliedEvent>(OnOutcome);
        }

        private void OnOutcome(WH40KMissionOutcomeAppliedEvent ev)
        {
            Outcomes.Add(ev);
        }
    }

    [Test]
    public async Task CargoMissionRequiresDeliveryRadiusAndResolvesMajorOnOwnNode()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        await EnsureSinglePlayerTeamAsync(pair, Imperium);
        await ClearMissionOutcomeProbeAsync(pair);

        var existingCargo = await CaptureMissionCargoSetAsync(pair);
        _ = await StartFactionMissionAsync(pair, Imperium, CargoMissionId);
        var cargo = await WaitForNewCargoSpawnAsync(pair, existingCargo);

        EntityUid ownNode = default;
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            ownNode = FindCommandNodeByTeam(entMan, Imperium);
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var query = entMan.GetEntityQuery<TransformComponent>();

            var ownXform = entMan.GetComponent<TransformComponent>(ownNode);
            var ownWorld = xform.GetWorldPosition(ownXform, query);
            xform.SetWorldPosition(cargo, ownWorld + new Vector2(DeliveryRadius + 1.5f, 0.5f));
        });

        await pair.RunTicksSync(140);

        await server.WaitAssertion(() =>
        {
            var mission = server.System<WH40KCommandEventMissionRuntimeSystem>();
            var probe = server.System<MissionOutcomeProbeSystem>();

            var state = mission.BuildTeamMissionRuntimeState(Imperium);
            Assert.Multiple(() =>
            {
                Assert.That(state.IsActive, Is.True, "Mission should still be active when cargo is outside delivery radius.");
                Assert.That(
                    probe.Outcomes.Any(o => o.MissionId == CargoMissionId && string.Equals(o.TeamId, Imperium, StringComparison.OrdinalIgnoreCase)),
                    Is.False,
                    "No outcome should be applied before cargo enters delivery radius.");
            });
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var nodeCoords = entMan.GetComponent<TransformComponent>(ownNode).Coordinates;
            xform.SetCoordinates(cargo, nodeCoords);
        });

        var outcome = await WaitForMissionOutcomeAsync(pair, CargoMissionId, Imperium);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.ObjectiveType, Is.EqualTo(WH40KMissionObjectiveType.CargoDelivery));
            Assert.That(outcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Major));
        });

        await pair.RunTicksSync(40);
        await server.WaitAssertion(() =>
        {
            var mission = server.System<WH40KCommandEventMissionRuntimeSystem>();
            Assert.That(
                mission.BuildTeamMissionRuntimeState(Imperium).IsActive,
                Is.False,
                "Mission should be inactive after successful delivery.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CargoMissionDeliveryToEnemyNodeResolvesFailure()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        await EnsureSinglePlayerTeamAsync(pair, Imperium);
        await ClearMissionOutcomeProbeAsync(pair);

        var existingCargo = await CaptureMissionCargoSetAsync(pair);
        _ = await StartFactionMissionAsync(pair, Imperium, CargoMissionId);
        var cargo = await WaitForNewCargoSpawnAsync(pair, existingCargo);

        EntityUid enemyNode = default;
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            enemyNode = FindCommandNodeByTeam(entMan, Heretics);
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var nodeCoords = entMan.GetComponent<TransformComponent>(enemyNode).Coordinates;
            xform.SetCoordinates(cargo, nodeCoords);
        });

        var outcome = await WaitForMissionOutcomeAsync(pair, CargoMissionId, Imperium);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.ObjectiveType, Is.EqualTo(WH40KMissionObjectiveType.CargoDelivery));
            Assert.That(outcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Failure));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BannerMissionsRequireBannerEntityAndCompleteWhenPlaced()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        // ── Part 1: Imperium banner mission ─────────────────────────────────────
        await EnsureSinglePlayerTeamAsync(pair, Imperium);
        await ClearMissionOutcomeProbeAsync(pair);

        var impMission = await StartFactionMissionAsync(pair, Imperium, BannerMissionId);
        var impAnchor = await WaitForObjectiveBeaconAnchorAsync(pair, impMission.MissionTitle);
        await AssertAnchorTileValidAsync(pair, impAnchor);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            DeleteEntitiesByPrototypeInRadius(entMan, impAnchor, BannerDetectionRadius + 0.2f, ImperiumBannerPrototypes);
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
            xform.SetWorldPosition(player, impAnchor.Position);

            for (var i = 0; i < 3; i++)
            {
                var mob = entMan.SpawnEntity(ImperiumReinforcementPrototype, impAnchor);
                var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(mob);
                member.TeamId = Imperium;
            }
        });

        await pair.RunTicksSync(220);

        await server.WaitAssertion(() =>
        {
            var runtime = server.System<WH40KCommandEventMissionRuntimeSystem>();
            var probe = server.System<MissionOutcomeProbeSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(runtime.BuildTeamMissionRuntimeState(Imperium).IsActive, Is.True);
                Assert.That(
                    probe.Outcomes.Any(o => o.MissionId == BannerMissionId && string.Equals(o.TeamId, Imperium, StringComparison.OrdinalIgnoreCase)),
                    Is.False,
                    "Banner mission must not resolve before required banner appears in objective zone.");
            });
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            _ = entMan.SpawnEntity(ImperiumBannerPrototype, impAnchor);
        });

        var impOutcome = await WaitForMissionOutcomeAsync(pair, BannerMissionId, Imperium, maxTicks: 900);
        Assert.Multiple(() =>
        {
            Assert.That(impOutcome.ObjectiveType, Is.EqualTo(WH40KMissionObjectiveType.BannerHold));
            Assert.That(impOutcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Major));
        });

        // ── Part 2: Heretics banner mission ─────────────────────────────────────
        await EnsureSinglePlayerTeamAsync(pair, Heretics);
        await ClearMissionOutcomeProbeAsync(pair);

        var herMission = await StartFactionMissionAsync(pair, Heretics, HereticsBannerMissionId);
        var herAnchor = await WaitForObjectiveBeaconAnchorAsync(pair, herMission.MissionTitle);
        await AssertAnchorTileValidAsync(pair, herAnchor);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            DeleteEntitiesByPrototypeInRadius(entMan, herAnchor, BannerDetectionRadius + 0.2f, [HereticsBannerPrototype]);
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
            xform.SetWorldPosition(player, herAnchor.Position);

            for (var i = 0; i < 3; i++)
            {
                var mob = entMan.SpawnEntity(HereticsReinforcementPrototype, herAnchor);
                var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(mob);
                member.TeamId = Heretics;
            }
        });

        await pair.RunTicksSync(220);

        await server.WaitAssertion(() =>
        {
            var runtime = server.System<WH40KCommandEventMissionRuntimeSystem>();
            var probe = server.System<MissionOutcomeProbeSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(runtime.BuildTeamMissionRuntimeState(Heretics).IsActive, Is.True);
                Assert.That(
                    probe.Outcomes.Any(o => o.MissionId == HereticsBannerMissionId && string.Equals(o.TeamId, Heretics, StringComparison.OrdinalIgnoreCase)),
                    Is.False,
                    "Heretics banner mission must not resolve before Chaos banner appears in objective zone.");
            });
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            _ = entMan.SpawnEntity(HereticsBannerPrototype, herAnchor);
        });

        var herOutcome = await WaitForMissionOutcomeAsync(pair, HereticsBannerMissionId, Heretics, maxTicks: 900);
        Assert.Multiple(() =>
        {
            Assert.That(herOutcome.ObjectiveType, Is.EqualTo(WH40KMissionObjectiveType.BannerHold));
            Assert.That(herOutcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Major));
        });

        await pair.CleanReturnAsync();
    }


    [Test]
    public async Task CompletedFactionMissionIsTemporarilyExcludedFromOfferRoll()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        await EnsureSinglePlayerTeamAsync(pair, Imperium);
        await ClearMissionOutcomeProbeAsync(pair);

        var existingCargo = await CaptureMissionCargoSetAsync(pair);
        _ = await StartFactionMissionAsync(pair, Imperium, CargoMissionId);
        var cargo = await WaitForNewCargoSpawnAsync(pair, existingCargo);

        EntityUid ownNode = default;
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            ownNode = FindCommandNodeByTeam(entMan, Imperium);
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var nodeCoords = entMan.GetComponent<TransformComponent>(ownNode).Coordinates;
            xform.SetCoordinates(cargo, nodeCoords);
        });

        var outcome = await WaitForMissionOutcomeAsync(pair, CargoMissionId, Imperium);
        Assert.That(outcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Major));

        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            var runtime = server.System<WH40KCommandEventMissionRuntimeSystem>();
            var offers = runtime.RollFactionMissionOffers(Imperium, 10);
            Assert.That(
                offers.Any(o => string.Equals(o.MissionId, CargoMissionId, StringComparison.OrdinalIgnoreCase)),
                Is.False,
                "Recently completed mission should be excluded from immediate faction offer rolls.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IntelRelayMissionAcceleratesOwnAndDelaysEnemyEventRolls()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        await EnsureSinglePlayerTeamAsync(pair, Imperium);
        await ClearMissionOutcomeProbeAsync(pair);

        var mission = await StartFactionMissionAsync(pair, Imperium, IntelRelayMissionId);

        var ownNextRollBefore = 0;
        var enemyNextRollBefore = 0;
        await server.WaitAssertion(() =>
        {
            var runtime = server.System<WH40KCommandEventMissionRuntimeSystem>();

            var ownState = runtime.BuildTeamEventRuntimeState(Imperium);
            Assert.Multiple(() =>
            {
                Assert.That(ownState.HasProfile, Is.True);
                Assert.That(ownState.NextRollSeconds, Is.GreaterThan(180));
            });
            ownNextRollBefore = ownState.NextRollSeconds;

            var enemyState = runtime.BuildTeamEventRuntimeState(Heretics);
            Assert.Multiple(() =>
            {
                Assert.That(enemyState.HasProfile, Is.True);
                Assert.That(enemyState.NextRollSeconds, Is.GreaterThan(120));
            });
            enemyNextRollBefore = enemyState.NextRollSeconds;
        });

        var anchor = await WaitForObjectiveBeaconAnchorAsync(pair, mission.MissionTitle);
        await AssertAnchorTileValidAsync(pair, anchor);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
            xform.SetWorldPosition(player, anchor.Position);

            for (var i = 0; i < 3; i++)
            {
                var mob = entMan.SpawnEntity(ImperiumReinforcementPrototype, anchor);
                var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(mob);
                member.TeamId = Imperium;
            }
        });

        var outcome = await WaitForMissionOutcomeAsync(pair, IntelRelayMissionId, Imperium, maxTicks: 900);
        Assert.That(outcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Major));

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var runtime = server.System<WH40KCommandEventMissionRuntimeSystem>();

            // Own team timer should be accelerated.
            var ownAfter = runtime.BuildTeamEventRuntimeState(Imperium);
            var ownDelta = ownNextRollBefore - ownAfter.NextRollSeconds;
            Assert.Multiple(() =>
            {
                Assert.That(ownAfter.HasProfile, Is.True);
                Assert.That(
                    ownDelta,
                    Is.GreaterThanOrEqualTo(70),
                    $"Expected mission token to accelerate next roll, but delta was too small (before={ownNextRollBefore}, after={ownAfter.NextRollSeconds}, delta={ownDelta}).");
            });

            // Enemy team timer should be delayed.
            var enemyAfter = runtime.BuildTeamEventRuntimeState(Heretics);
            var enemyDelta = enemyAfter.NextRollSeconds - enemyNextRollBefore;
            Assert.Multiple(() =>
            {
                Assert.That(enemyAfter.HasProfile, Is.True);
                Assert.That(
                    enemyDelta,
                    Is.GreaterThanOrEqualTo(40),
                    $"Expected enemy event-roll timer to be delayed by relay effect, but delta was too small (before={enemyNextRollBefore}, after={enemyAfter.NextRollSeconds}, delta={enemyDelta}).");
            });
        });

        await pair.CleanReturnAsync();
    }


    [Test]
    public async Task ZoneMissionAnchorIsValidAndPresenceCompletesObjective()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        await EnsureSinglePlayerTeamAsync(pair, Imperium);
        await ClearMissionOutcomeProbeAsync(pair);

        var mission = await StartFactionMissionAsync(pair, Imperium, ZoneMissionId);
        var anchor = await WaitForObjectiveBeaconAnchorAsync(pair, mission.MissionTitle);
        await AssertAnchorTileValidAsync(pair, anchor);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
            xform.SetWorldPosition(player, anchor.Position);

            for (var i = 0; i < 3; i++)
            {
                var mob = entMan.SpawnEntity(ImperiumReinforcementPrototype, anchor);
                var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(mob);
                member.TeamId = Imperium;
            }
        });

        var outcome = await WaitForMissionOutcomeAsync(pair, ZoneMissionId, Imperium, maxTicks: 900);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.ObjectiveType, Is.EqualTo(WH40KMissionObjectiveType.ZoneControl));
            Assert.That(outcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Major));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MissionMarkersAppearForCargoMissionAndCleanupOnResolveAndRestart()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        await EnsureSinglePlayerTeamAsync(pair, Imperium);
        await ClearMissionOutcomeProbeAsync(pair);

        var baselineVisuals = await CaptureMissionVisualSetAsync(pair);
        var existingCargo = await CaptureMissionCargoSetAsync(pair);
        _ = await StartFactionMissionAsync(pair, Imperium, CargoMissionId);
        var cargo = await WaitForNewCargoSpawnAsync(pair, existingCargo);

        var missionVisuals = await WaitForNewMissionVisualsAsync(pair, baselineVisuals);
        Assert.That(missionVisuals.Count, Is.GreaterThanOrEqualTo(2), "Expected objective beacon and delivery marker visuals.");

        EntityUid ownNode = default;
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            ownNode = FindCommandNodeByTeam(entMan, Imperium);
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var nodeCoords = entMan.GetComponent<TransformComponent>(ownNode).Coordinates;
            xform.SetCoordinates(cargo, nodeCoords);
        });

        var outcome = await WaitForMissionOutcomeAsync(pair, CargoMissionId, Imperium);
        Assert.That(outcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Major));

        await pair.RunTicksSync(40);
        await server.WaitAssertion(() =>
        {
            var mission = server.System<WH40KCommandEventMissionRuntimeSystem>();
            Assert.That(mission.BuildTeamMissionRuntimeState(Imperium).IsActive, Is.False);
        });
        await AssertAllEntitiesDeletedAsync(pair, missionVisuals);

        var restartBaseline = await CaptureMissionVisualSetAsync(pair);
        _ = await StartFactionMissionAsync(pair, Imperium, ZoneMissionId);
        var restartVisuals = await WaitForNewMissionVisualsAsync(pair, restartBaseline);
        Assert.That(restartVisuals.Count, Is.GreaterThan(0), "Expected mission visuals before round restart cleanup.");

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(30);
        await AssertAllEntitiesDeletedAsync(pair, restartVisuals);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrossSystemMissionAndInfluenceSmokeRemainsConsistent()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        await EnsureSinglePlayerTeamAsync(pair, Imperium);
        await ClearMissionOutcomeProbeAsync(pair);

        NetUserId userId = default;
        await server.WaitAssertion(() =>
        {
            userId = server.ResolveDependency<IPlayerManager>().Sessions.Single().UserId;
        });

        var existingCargo = await CaptureMissionCargoSetAsync(pair);
        _ = await StartFactionMissionAsync(pair, Imperium, CargoMissionId);
        var cargo = await WaitForNewCargoSpawnAsync(pair, existingCargo);

        EntityUid ownNode = default;
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            ownNode = FindCommandNodeByTeam(entMan, Imperium);
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var nodeCoords = entMan.GetComponent<TransformComponent>(ownNode).Coordinates;
            xform.SetCoordinates(cargo, nodeCoords);
        });

        var outcome = await WaitForMissionOutcomeAsync(pair, CargoMissionId, Imperium);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.ObjectiveType, Is.EqualTo(WH40KMissionObjectiveType.CargoDelivery));
            Assert.That(outcome.Tier, Is.EqualTo(WH40KMissionOutcomeTier.Major));
        });

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single().AttachedEntity!.Value;
            entMan.EventBus.RaiseEvent(EventSource.Local, new WH40KInfluencePointCapturedEvent(Imperium, player));
            entMan.EventBus.RaiseEvent(EventSource.Local, new WH40KInfluencePointRewardTickEvent(Imperium, player, 2));
        });

        await pair.RunTicksSync(25);

        await server.WaitAssertion(() =>
        {
            var mission = server.System<WH40KCommandEventMissionRuntimeSystem>();
            var probe = server.System<MissionOutcomeProbeSystem>();
            var stats = server.System<WH40KPlayerStatsSystem>();

            var missionOutcomeCount = probe.Outcomes.Count(o =>
                string.Equals(o.MissionId, CargoMissionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.TeamId, Imperium, StringComparison.OrdinalIgnoreCase));

            Assert.Multiple(() =>
            {
                Assert.That(mission.BuildTeamMissionRuntimeState(Imperium).IsActive, Is.False);
                Assert.That(missionOutcomeCount, Is.EqualTo(1), "Cargo mission should resolve exactly once.");
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.LogisticsDeliverySuccess), Is.GreaterThanOrEqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.LogisticsDeliveryValue), Is.GreaterThan(0));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ObjectiveCaptureSuccess), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccess), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ObjectiveCaptureSuccessValidated), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccessValidated), Is.EqualTo(1));
            });
        });

        await pair.CleanReturnAsync();
    }

    private static async Task<TestPair> StartWh40KRoundAsync()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
            InLobby = true,
            DummyTicker = false,
            Fresh = true
        });

        await pair.WaitCommand("forcemap Battlefield40k");
        await pair.WaitCommand("setgamepreset WH40KTeamBattle 9999");
        await pair.WaitCommand("startround");
        await pair.RunTicksSync(60);

        // WH40K requires faction selection before late-join.
        await pair.Client.WaitPost(() =>
        {
            var factionSys = pair.Client.System<Content.Client._WH40K.LateJoin.WH40KFactionSystem>();
            factionSys.SelectFaction("Imperium", Content.Shared._WH40K.LateJoin.WH40KFactionSelectionPurpose.LateJoin);
        });
        await pair.RunTicksSync(10);

        await pair.Server.WaitPost(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            ticker.MakeJoinGame(playerMan.Sessions.Single(), EntityUid.Invalid, "Guardsman");
        });
        await pair.RunTicksSync(20);

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();

            Assert.Multiple(() =>
            {
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
                Assert.That(playerMan.Sessions.Single().AttachedEntity, Is.Not.Null);
            });
        });

        return pair;
    }

    private static async Task EnsureSinglePlayerTeamAsync(TestPair pair, string teamId)
    {
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var player = playerMan.Sessions.Single().AttachedEntity!.Value;

            var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(player);
            member.TeamId = teamId;
        });
    }

    private static async Task ClearMissionOutcomeProbeAsync(TestPair pair)
    {
        await pair.Server.WaitAssertion(() =>
        {
            pair.Server.System<MissionOutcomeProbeSystem>().Outcomes.Clear();
        });
    }

    private static async Task<WH40KCommandMissionRuntimeState> StartFactionMissionAsync(
        TestPair pair,
        string teamId,
        string missionId)
    {
        var server = pair.Server;
        WH40KCommandMissionRuntimeState started = null!;

        await server.WaitAssertion(() =>
        {
            var runtime = server.System<WH40KCommandEventMissionRuntimeSystem>();
            Assert.That(
                runtime.TryStartFactionMission(teamId, missionId, out started),
                Is.True,
                $"Expected faction mission '{missionId}' for team '{teamId}' to start.");
            Assert.That(started.IsActive, Is.True);
            Assert.That(started.MissionId, Is.EqualTo(missionId));
        });

        return started;
    }

    private static async Task<HashSet<EntityUid>> CaptureMissionCargoSetAsync(TestPair pair)
    {
        var server = pair.Server;
        var result = new HashSet<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            result = FindMissionCargoEntities(entMan);
        });

        return result;
    }

    private static async Task<EntityUid> WaitForNewCargoSpawnAsync(
        TestPair pair,
        IReadOnlySet<EntityUid> knownCargo,
        int maxTicks = 900)
    {
        var server = pair.Server;

        for (var i = 0; i < maxTicks; i++)
        {
            EntityUid found = default;
            var hasFound = false;
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                foreach (var cargo in FindMissionCargoEntities(entMan))
                {
                    if (knownCargo.Contains(cargo))
                        continue;

                    found = cargo;
                    hasFound = true;
                    break;
                }
            });

            if (hasFound)
                return found;

            await pair.RunTicksSync(1);
        }

        Assert.Fail("Timed out waiting for mission cargo spawn.");
        return default;
    }

    private static HashSet<EntityUid> FindMissionCargoEntities(IEntityManager entMan)
    {
        var result = new HashSet<EntityUid>();
        var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.MapID == MapId.Nullspace)
                continue;

            var proto = meta.EntityPrototype?.ID;
            if (!string.Equals(proto, CargoPrototype, StringComparison.Ordinal) &&
                !string.Equals(proto, CargoDeliveryPrototype, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(uid);
        }

        return result;
    }

    private static async Task<MapCoordinates> WaitForObjectiveBeaconAnchorAsync(
        TestPair pair,
        string objectiveLabel,
        int maxTicks = 700)
    {
        var server = pair.Server;

        for (var i = 0; i < maxTicks; i++)
        {
            MapCoordinates coords = default;
            var found = false;
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                found = TryFindMissionBeaconByLabel(entMan, objectiveLabel, out coords);
            });

            if (found)
                return coords;

            await pair.RunTicksSync(1);
        }

        Assert.Fail($"Timed out waiting for mission beacon with label '{objectiveLabel}'.");
        return default;
    }

    private static bool TryFindMissionBeaconByLabel(
        IEntityManager entMan,
        string objectiveLabel,
        out MapCoordinates coords)
    {
        coords = default;
        var xformSystem = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
        var xformQuery = entMan.GetEntityQuery<TransformComponent>();
        var query = entMan.EntityQueryEnumerator<WH40KMissionObjectiveVisualComponent, TransformComponent, MetaDataComponent>();

        while (query.MoveNext(out _, out var visual, out var xform, out var meta))
        {
            if (xform.MapID == MapId.Nullspace)
                continue;

            if (!string.Equals(meta.EntityPrototype?.ID, MissionBeaconPrototype, StringComparison.Ordinal))
                continue;

            if (!string.Equals(visual.Label, objectiveLabel, StringComparison.OrdinalIgnoreCase))
                continue;

            var world = xformSystem.GetWorldPosition(xform, xformQuery);
            coords = new MapCoordinates(world, xform.MapID);
            return true;
        }

        return false;
    }

    private static async Task AssertAnchorTileValidAsync(TestPair pair, MapCoordinates anchor)
    {
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mapManager = server.ResolveDependency<IMapManager>();
            var mapSystem = entMan.EntitySysManager.GetEntitySystem<SharedMapSystem>();
            var turf = entMan.EntitySysManager.GetEntitySystem<TurfSystem>();

            Assert.That(anchor.MapId, Is.Not.EqualTo(MapId.Nullspace));
            Assert.That(mapManager.TryFindGridAt(anchor, out var gridUid, out var grid), Is.True);
            Assert.That(grid, Is.Not.Null);

            var tileIndices = mapSystem.WorldToTile(gridUid, grid!, anchor.Position);
            Assert.That(mapSystem.TryGetTileRef(gridUid, grid!, tileIndices, out var tileRef), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(tileRef.Tile.IsEmpty, Is.False);
                Assert.That(turf.IsSpace(tileRef), Is.False);
            });
        });
    }

    private static void DeleteEntitiesByPrototypeInRadius(
        IEntityManager entMan,
        MapCoordinates center,
        float radius,
        IReadOnlyCollection<string> prototypes)
    {
        if (center.MapId == MapId.Nullspace || prototypes.Count == 0)
            return;

        var wanted = new HashSet<string>(prototypes, StringComparer.OrdinalIgnoreCase);
        var radiusSquared = radius * radius;
        var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
        var xformQuery = entMan.GetEntityQuery<TransformComponent>();
        var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var meta, out var entityXform))
        {
            if (entityXform.MapID != center.MapId)
                continue;

            var proto = meta.EntityPrototype?.ID;
            if (proto is null || !wanted.Contains(proto))
                continue;

            var world = xform.GetWorldPosition(entityXform, xformQuery);
            if ((world - center.Position).LengthSquared() > radiusSquared)
                continue;

            entMan.QueueDeleteEntity(uid);
        }
    }

    private static async Task<WH40KMissionOutcomeAppliedEvent> WaitForMissionOutcomeAsync(
        TestPair pair,
        string missionId,
        string teamId,
        int maxTicks = 700)
    {
        var server = pair.Server;

        for (var i = 0; i < maxTicks; i++)
        {
            WH40KMissionOutcomeAppliedEvent? outcome = null;
            await server.WaitPost(() =>
            {
                var probe = server.System<MissionOutcomeProbeSystem>();
                outcome = probe.Outcomes.LastOrDefault(o =>
                    string.Equals(o.MissionId, missionId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(o.TeamId, teamId, StringComparison.OrdinalIgnoreCase));
            });

            if (outcome is not null)
                return outcome;

            await pair.RunTicksSync(1);
        }

        Assert.Fail($"Timed out waiting for mission outcome (mission='{missionId}', team='{teamId}').");
        return null!;
    }

    private static async Task<HashSet<EntityUid>> CaptureMissionVisualSetAsync(TestPair pair)
    {
        var result = new HashSet<EntityUid>();
        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var query = entMan.EntityQueryEnumerator<WH40KMissionObjectiveVisualComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == MapId.Nullspace)
                    continue;

                result.Add(uid);
            }
        });

        return result;
    }

    private static async Task<HashSet<EntityUid>> WaitForNewMissionVisualsAsync(
        TestPair pair,
        IReadOnlySet<EntityUid> baseline,
        int maxTicks = 700)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var current = await CaptureMissionVisualSetAsync(pair);
            var diff = current.Where(uid => !baseline.Contains(uid)).ToHashSet();
            if (diff.Count > 0)
                return diff;

            await pair.RunTicksSync(1);
        }

        Assert.Fail("Timed out waiting for mission visual marker spawn.");
        return new HashSet<EntityUid>();
    }

    private static async Task AssertAllEntitiesDeletedAsync(TestPair pair, IReadOnlyCollection<EntityUid> entities)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            foreach (var uid in entities)
            {
                Assert.That(entMan.Deleted(uid), Is.True, $"Expected entity {uid} to be deleted during cleanup.");
            }
        });
    }

    private static EntityUid FindCommandNodeByTeam(IEntityManager entMan, string teamId)
    {
        var query = entMan.EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out var uid, out var node))
        {
            if (string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                return uid;
        }

        Assert.Fail($"Could not find command node for team '{teamId}'.");
        return default;
    }
}
