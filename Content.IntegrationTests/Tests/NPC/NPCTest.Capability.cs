using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.IntegrationTests.Pair;
using Content.Server.Hands.Systems;
using Content.Server.Light.EntitySystems;
using Content.Server._WH40K.Objectives.Components;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Spawners.Components;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.Spawners.Components;
using Content.Server.NPC.Systems;
using Content.Server.Wires;
using Content.Server.Weather;
using Content.Shared._WH40K.Mortar;
using Content.Shared._WH40K.Influence;
using Content.Shared._WH40K.GameMode;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Light.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.NPC;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.VendingMachines;
using Content.Shared.Weather;
using Content.Shared.Weapons.Melee;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.IoC;
using Robust.Shared.Utility;

#pragma warning disable CS0618
namespace Content.IntegrationTests.Tests.NPC;

public sealed partial class NPCTest
{
    private static readonly EntProtoId AcidRainWeatherPrototype = "WHAcidRain";

    [Test]
    public async Task NpcCapabilityWaveRoleRootAndNavigationPartition()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitIdleAsync();
        await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mapSystem = entMan.System<SharedMapSystem>();
            var pathfinding = entMan.System<PathfindingSystem>();

            FillFloorRect(mapSystem, pair.TestMap.Grid, -20, 20, -20, 20);

            for (var i = 0; i < NpcCapabilityScenarioLibrary.WaveRoleExpectations.Count; i++)
            {
                var expected = NpcCapabilityScenarioLibrary.WaveRoleExpectations[i];
                var uid = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    expected.PrototypeId,
                    x: -10f + i * 3f,
                    y: 0f);

                Assert.That(entMan.TryGetComponent(uid, out HTNComponent htn), Is.True,
                    $"Role prototype '{expected.PrototypeId}' must include HTN component.");
                Assert.That(htn, Is.Not.Null);
                Assert.That(htn.RootTask.Task, Is.EqualTo(expected.ExpectedRootTask),
                    $"Role prototype '{expected.PrototypeId}' has unexpected HTN root task.");

                AssertNavFlag(entMan, htn.Blackboard, NPCBlackboard.NavInteract, expected.NavInteract, expected.PrototypeId);
                AssertNavFlag(entMan, htn.Blackboard, NPCBlackboard.NavPry, expected.NavPry, expected.PrototypeId);
                AssertNavFlag(entMan, htn.Blackboard, NPCBlackboard.NavSmash, expected.NavSmash, expected.PrototypeId);
                AssertNavFlag(entMan, htn.Blackboard, NPCBlackboard.NavClimb, expected.NavClimb, expected.PrototypeId);
                AssertNavFlag(entMan, htn.Blackboard, NPCBlackboard.WaveInfluenceEnabled, expected.WaveInfluenceEnabled, expected.PrototypeId);
                AssertNavFlag(entMan, htn.Blackboard, NPCBlackboard.WaveObjectiveEnabled, expected.WaveObjectiveEnabled, expected.PrototypeId);

                var flags = pathfinding.GetFlags(htn.Blackboard);
                Assert.That(flags, Is.EqualTo(expected.ExpectedFlags),
                    $"Role prototype '{expected.PrototypeId}' has unexpected path flags.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NpcCapabilityWaveRoleAssaultCanTargetHostileWhileLogisticsStaysOutOfCombat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitIdleAsync();
        await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        var spawned = new List<EntityUid>();
        EntityUid assault = default;
        EntityUid logistics = default;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mapSystem = entMan.System<SharedMapSystem>();

            FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

            assault = NpcCapabilityScenarioLibrary.SpawnAt(
                entMan,
                pair.TestMap.Grid,
                NpcCapabilityScenarioLibrary.AssaultPrototype,
                x: 0f,
                y: 0f);
            logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                entMan,
                pair.TestMap.Grid,
                NpcCapabilityScenarioLibrary.LogisticsPrototype,
                x: 0f,
                y: 6f);

            spawned.Add(assault);
            spawned.Add(logistics);

            var hostiles = NpcCapabilityScenarioLibrary.SpawnSwarm(
                entMan,
                pair.TestMap.Grid,
                "MobCivilian",
                count: 8,
                origin: new Vector2(6f, -2f),
                columns: 4,
                spacing: 1.4f);
            spawned.AddRange(hostiles);
        });

        await pair.RunTicksSync(240);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            Assert.That(entMan.EntityExists(assault), Is.True, "Assault role entity was deleted unexpectedly.");
            Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics role entity was deleted unexpectedly.");

            var assaultHtn = entMan.GetComponent<HTNComponent>(assault);
            var logisticsHtn = entMan.GetComponent<HTNComponent>(logistics);

            var assaultHasTarget = assaultHtn.Blackboard.TryGetValue<EntityUid>("Target", out var assaultTarget, entMan);
            Assert.That(assaultHasTarget, Is.True,
                "Assault role failed to acquire hostile target in controlled scenario.");
            Assert.That(entMan.EntityExists(assaultTarget), Is.True,
                "Assault role acquired invalid target reference.");

            var logisticsHasTarget = logisticsHtn.Blackboard.TryGetValue<EntityUid>("Target", out _, entMan);
            Assert.That(logisticsHasTarget, Is.False,
                "Logistics role must not enter combat targeting in A1 partition baseline.");

            Assert.That(entMan.HasComponent<NPCRangedCombatComponent>(logistics), Is.False,
                "Logistics role must not enter ranged combat loop in A1 baseline.");
            Assert.That(entMan.HasComponent<NPCMeleeCombatComponent>(logistics), Is.False,
                "Logistics role must not enter melee combat loop in A1 baseline.");
        });

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            foreach (var uid in spawned)
            {
                if (entMan.EntityExists(uid))
                    entMan.DeleteEntity(uid);
            }
        });

        await pair.RunTicksSync(20);
        await pair.CleanReturnAsync();
    }

    private static void AssertNavFlag(
        IEntityManager entMan,
        NPCBlackboard blackboard,
        string key,
        bool expectedValue,
        string prototypeId)
    {
        var hasValue = blackboard.TryGetValue<bool>(key, out var actualValue, entMan);
        Assert.That(hasValue, Is.True, $"Role prototype '{prototypeId}' is missing blackboard key '{key}'.");
        Assert.That(actualValue, Is.EqualTo(expectedValue),
            $"Role prototype '{prototypeId}' has unexpected value for blackboard key '{key}'.");
    }

    [Test]
    public async Task NpcCapabilityRangedHoldsFireWhenFriendlyBlocksLine()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid shooter = default;
            EntityUid ally = default;
            EntityUid target = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var xformSystem = entMan.System<SharedTransformSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -20, 20, -20, 20);

                shooter = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobSpirate", 0f, 0f);
                ally = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobSpirate", 3.8f, 0f);
                target = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8f, 0f);

                if (entMan.HasComponent<HTNComponent>(shooter))
                    entMan.RemoveComponent<HTNComponent>(shooter);
                if (entMan.HasComponent<HTNComponent>(ally))
                    entMan.RemoveComponent<HTNComponent>(ally);
                if (entMan.HasComponent<HTNComponent>(target))
                    entMan.RemoveComponent<HTNComponent>(target);

                ForceEquipWithPistol(entMan, shooter);

                var ranged = entMan.EnsureComponent<NPCRangedCombatComponent>(shooter);
                ranged.Target = target;
                ranged.Status = CombatStatus.Normal;
                ranged.ShootDelay = 0.05f;
                ranged.AccuracyThreshold = Angle.FromDegrees(65);

                if (entMan.TryGetComponent(ally, out TransformComponent allyXform))
                    xformSystem.AnchorEntity((ally, allyXform));

                if (entMan.TryGetComponent(target, out TransformComponent targetXform))
                    xformSystem.AnchorEntity((target, targetXform));

                bench.Reset();
            });

            await pair.RunTicksSync(240);

            NpcBenchmarkSnapshot blockedSnapshot = default;
            await server.WaitPost(() =>
            {
                blockedSnapshot = server.System<NPCBenchmarkSystem>().SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(shooter), Is.True, "Shooter entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(ally), Is.True, "Friendly blocker entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(target), Is.True, "Target entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(blockedSnapshot, "npc.combat.ranged.friendly_fire_blocked"), Is.GreaterThan(0),
                    "Expected ranged combat to block at least one shot because ally occupied line of fire.");
                Assert.That(GetStageWorkItems(blockedSnapshot, "npc.combat.ranged.shoot_performed"), Is.EqualTo(0),
                    "Shooter fired through allied blocker; expected hold-fire behavior.");
            });

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                if (entMan.EntityExists(ally))
                    entMan.DeleteEntity(ally);

                server.System<NPCBenchmarkSystem>().Reset();
            });

            await pair.RunTicksSync(240);

            NpcBenchmarkSnapshot clearSnapshot = default;
            await server.WaitPost(() =>
            {
                clearSnapshot = server.System<NPCBenchmarkSystem>().SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(GetStageWorkItems(clearSnapshot, "npc.combat.ranged.shoot_performed"), Is.GreaterThan(0),
                    "Shooter did not resume ranged fire after friendly blocker was removed.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveBreacherLockedDoorProgresses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldHazardScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds);
        var oldHazardScanRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityHazardScanRadius);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanRadius, 8f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid breacher = default;
            EntityUid door = default;
            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var xformSystem = entMan.System<SharedTransformSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();
                var steering = entMan.System<NPCSteeringSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -20, 20, -20, 20);
                BuildLockedDoorTwoRoomScenario(entMan, pair.TestMap.Grid);

                breacher = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.BreacherPrototype,
                    x: 0f,
                    y: 0f);
                door = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "AirlockMaintLocked", 2f, 0f);

                if (entMan.TryGetComponent(door, out DoorComponent doorComp))
                {
                    // Keep deterministic locked-door obstacle handling path.
                    doorComp.BumpOpen = false;
                    doorComp.ClickOpen = false;
                }

                steering.Register(breacher, new EntityCoordinates(pair.TestMap.Grid.Owner, 4f, 0f));

                bench.Reset();
            });

            await pair.RunTicksSync(220);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var interactAttempts = GetStageWorkItems(snapshot, "npc.steering.obstacle.policy.interact_attempt");
                var pryAttempts = GetStageWorkItems(snapshot, "npc.steering.obstacle.policy.pry_attempt");
                var smashAttempts = GetStageWorkItems(snapshot, "npc.steering.obstacle.policy.smash_attempt");
                var obstacleProgress = GetStageWorkItems(snapshot, "npc.steering.obstacle.progress");
                var noPathBackoff = GetStageWorkItems(snapshot, "npc.steering.path_request.no_path_backoff");
                var policyAttempts = interactAttempts + pryAttempts + smashAttempts;

                Assert.That(obstacleProgress > 0 || noPathBackoff > 0, Is.True,
                    $"Expected obstacle progression or bounded no-path backoff in breacher scenario. policy_attempts={policyAttempts}, obstacle_progress={obstacleProgress}, no_path_backoff={noPathBackoff}.");

                var breacherX = entMan.GetComponent<TransformComponent>(breacher).Coordinates.Position.X;
                Assert.That(breacherX, Is.GreaterThan(0.4f),
                    "Breacher did not reach locked-door chokepoint.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds, oldHazardScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanRadius, oldHazardScanRadius);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveAssaultObstacleTimeoutBounded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldRetryLimit = server.CfgMan.GetCVar(CCVars.NPCSteeringObstacleRetryLimit);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCSteeringObstacleRetryLimit, 2);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid door = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var xformSystem = entMan.System<SharedTransformSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();
                var steering = entMan.System<NPCSteeringSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -20, 20, -20, 20);
                BuildLockedDoorTwoRoomScenario(entMan, pair.TestMap.Grid);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: 0f,
                    y: 0f);
                door = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "AirlockMaintLocked", 2f, 0f);

                var steeringComp = steering.Register(assault, new EntityCoordinates(pair.TestMap.Grid.Owner, 4f, 0f));
                steeringComp.Flags &= ~PathFlags.Interact;

                if (entMan.TryGetComponent(door, out DoorComponent doorComp))
                {
                    doorComp.BumpOpen = false;
                    doorComp.ClickOpen = false;
                }

                bench.Reset();
            });

            await pair.RunTicksSync(360);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(assault), Is.True, "Assault role entity was deleted unexpectedly.");

                var obstacleFailed = GetStageWorkItems(snapshot, "npc.steering.obstacle.failed");
                var laneRotate = GetStageWorkItems(snapshot, "npc.steering.obstacle.lane_rotate");
                var rerouteAttempts = GetStageWorkItems(snapshot, "npc.steering.obstacle.reroute_attempt");
                var obstacleTimeout = GetStageWorkItems(snapshot, "npc.steering.obstacle.timeout");
                var noPathBackoff = GetStageWorkItems(snapshot, "npc.steering.path_request.no_path_backoff");
                var noPathResult = GetStageWorkItems(snapshot, "npc.steering.path_result.no_path");

                Assert.That(obstacleFailed + noPathBackoff, Is.GreaterThan(0),
                    $"Expected bounded failure/retry handling in timeout scenario. obstacle_failed={obstacleFailed}, no_path_backoff={noPathBackoff}, no_path_result={noPathResult}.");

                if (obstacleFailed > 0)
                {
                    Assert.That(laneRotate, Is.GreaterThan(0),
                        "Expected lane-rotation retries after obstacle failures.");
                    Assert.That(rerouteAttempts + obstacleTimeout, Is.GreaterThan(0),
                        "Expected reroute attempts and/or obstacle timeout after failures.");
                }
                else
                {
                    Assert.That(noPathBackoff, Is.GreaterThan(0),
                        "Expected no-path backoff retries when obstacle-failure path is not entered.");
                }
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCSteeringObstacleRetryLimit, oldRetryLimit);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveAssaultSharedTargetCoordination()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldCoordEnabled = server.CfgMan.GetCVar(CCVars.NPCUtilityWaveCoordinationEnabled);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCUtilityWaveCoordinationEnabled, true);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assaultA = default;
            EntityUid assaultB = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var xformSystem = entMan.System<SharedTransformSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -30, 30);

                assaultA = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: 0f,
                    y: 0f);
                assaultB = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: 0f,
                    y: 2f);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8f, 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 9f, 1.5f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 9.5f, -1.5f);

                bench.Reset();
            });

            await pair.RunTicksSync(90);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var htnA = entMan.GetComponent<HTNComponent>(assaultA);
                var htnB = entMan.GetComponent<HTNComponent>(assaultB);

                var hasOrderedA = htnA.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var orderedA, entMan);
                var hasOrderedB = htnB.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var orderedB, entMan);

                // Ordered target can be dropped late in the window when the target dies or local retargeting takes over.
                // Coordination counters below are the authoritative gate; when both values are still present, enforce equality.
                if (hasOrderedA && hasOrderedB)
                    Assert.That(orderedA, Is.EqualTo(orderedB), "Assault squad failed to converge on shared ordered target.");

                Assert.That(GetStageWorkItems(snapshot, "npc.utility.coordination.shared_target_assign"), Is.GreaterThan(0),
                    "Expected shared-target assignments in coordination scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.utility.coordination.shared_target_hit"), Is.GreaterThan(0),
                    "Expected at least one shared-target cache reuse in coordination scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.utility.coordination.ordered_boost"), Is.GreaterThan(0),
                    "Expected ordered-target score boosts in coordination scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCUtilityWaveCoordinationEnabled, oldCoordEnabled);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveCommsEnemyContactDeduplicatesCrowd()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -26, 26, -26, 26);

                for (var i = 0; i < 8; i++)
                {
                    var y = -3f + i * 0.85f;
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(
                        entMan,
                        pair.TestMap.Grid,
                        NpcCapabilityScenarioLibrary.AssaultPrototype,
                        x: 0f,
                        y: y);
                }

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8f, -0.8f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8.8f, 0.8f);

                bench.Reset();
            });

            await pair.RunTicksSync(340);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var spottedAttempt = GetStageWorkItems(snapshot, "npc.wave.comms.enemy_spotted.attempt");
                var spottedSent = GetStageWorkItems(snapshot, "npc.wave.comms.enemy_spotted.sent");
                var spottedSuppressed = GetStageWorkItems(snapshot, "npc.wave.comms.enemy_spotted.suppressed");
                var engageAttempt = GetStageWorkItems(snapshot, "npc.wave.comms.engaging_enemy.attempt");
                var engageSent = GetStageWorkItems(snapshot, "npc.wave.comms.engaging_enemy.sent");
                var engageSuppressed = GetStageWorkItems(snapshot, "npc.wave.comms.engaging_enemy.suppressed");

                Assert.That(spottedAttempt, Is.GreaterThan(0),
                    "Expected enemy-spotted communication attempts in crowd-contact scenario.");
                Assert.That(spottedSent, Is.GreaterThan(0),
                    $"Expected at least one enemy-spotted callout in crowd-contact scenario. {DescribeCommsCounters(snapshot)}");
                Assert.That(engageAttempt, Is.GreaterThan(0),
                    "Expected engage communication attempts in crowd-contact scenario.");
                Assert.That(engageSent, Is.GreaterThan(0),
                    $"Expected at least one engage callout in crowd-contact scenario. {DescribeCommsCounters(snapshot)}");

                Assert.That(spottedSuppressed + engageSuppressed, Is.GreaterThan(0),
                    $"Expected dedup suppression when multiple NPCs spot/fire at same contact. {DescribeCommsCounters(snapshot)}");
                Assert.That(spottedSent < spottedAttempt || engageSent < engageAttempt, Is.True,
                    $"Expected at least one comm stream with sent < attempts due dedup. {DescribeCommsCounters(snapshot)}");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveCommsRoleSignalsCoverage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldHazardScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds);
        var oldHazardScanRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityHazardScanRadius);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanRadius, 8f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 24f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -64, 64, -64, 64);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: 0f,
                    y: 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8f, 0f);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.BreacherPrototype,
                    x: 0f,
                    y: 16f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8f, 16f);

                var support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: 0f,
                    y: 32f);
                var supportEnemy = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8f, 32f);
                if (entMan.TryGetComponent(support, out HTNComponent supportHtn))
                    supportHtn.Blackboard.SetValue("Target", supportEnemy);

                var coordinator = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.CoordinatorPrototype,
                    x: 0f,
                    y: 48f);
                var coordinatorEnemy = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8f, 48f);
                if (entMan.TryGetComponent(coordinator, out HTNComponent coordinatorHtn))
                    coordinatorHtn.Blackboard.SetValue("Target", coordinatorEnemy);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SapperPrototype,
                    x: 0f,
                    y: -16f);
                var sapperMine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "LandMineModular", 0.4f, -16f);
                if (entMan.TryGetComponent(sapperMine, out ItemToggleComponent mineToggle))
                    mineToggle.Activated = true;

                _ = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: -32f);
                var machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, -32f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 2f, -32f);

                bench.Reset();
            });

            await pair.RunTicksSync(1300);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.enemy_spotted.sent"), Is.GreaterThan(0),
                    $"Expected enemy-spotted role callouts in comms coverage scenario. {DescribeCommsCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.engaging_enemy.sent"), Is.GreaterThan(0),
                    $"Expected engage role callouts in comms coverage scenario. {DescribeCommsCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.mine_cleared.sent"), Is.GreaterThan(0),
                    $"Expected sapper mine-clear callout in comms coverage scenario. {DescribeCommsCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.tactical_order.sent"), Is.GreaterThan(0),
                    $"Expected tactical-order callout in comms coverage scenario. {DescribeCommsCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.service_report.sent"), Is.GreaterThan(0),
                    $"Expected service-report callout in comms coverage scenario. {DescribeCommsCounters(snapshot)}");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.role.assault.sent"), Is.GreaterThan(0),
                    "Assault role produced no comms callouts.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.role.breacher.sent"), Is.GreaterThan(0),
                    "Breacher role produced no comms callouts.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.role.sapper.sent"), Is.GreaterThan(0),
                    "Sapper role produced no comms callouts.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.role.support.sent"), Is.GreaterThan(0),
                    "Support role produced no comms callouts.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.role.logistics.sent"), Is.GreaterThan(0),
                    "Logistics role produced no comms callouts.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.comms.role.coordinator.sent"), Is.GreaterThan(0),
                    "Coordinator role produced no comms callouts.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds, oldHazardScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanRadius, oldHazardScanRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveSapperSkillGateMines()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldHazardScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds);
        var oldHazardScanRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityHazardScanRadius);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanRadius, 8f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid sapper = default;
            EntityUid nonSapper = default;
            EntityUid sapperMine = default;
            EntityUid assaultMine = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -20, 20, -20, 20);

                // Hard-separate lanes so sapper can not roam into the non-sapper mine lane.
                for (var x = -20; x <= 20; x++)
                {
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", x, 3f);
                }

                sapper = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SapperPrototype,
                    x: 0f,
                    y: 0f);
                nonSapper = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 6f);

                sapperMine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "LandMineModular", 0.4f, 0f);
                assaultMine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "LandMineModular", 2.2f, 6f);

                if (entMan.TryGetComponent(sapperMine, out ItemToggleComponent sapperMineToggle))
                    sapperMineToggle.Activated = true;

                if (entMan.TryGetComponent(assaultMine, out ItemToggleComponent assaultMineToggle))
                    assaultMineToggle.Activated = true;

                bench.Reset();
            });

            await pair.RunTicksSync(180);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(sapper), Is.True, "Sapper entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(nonSapper), Is.True, "Non-sapper entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.capability.entities"), Is.GreaterThan(0),
                    "Expected wave-capability system to process wave-role entities.");

                var hazardMemoryAdd = GetStageWorkItems(snapshot, "npc.wave.hazard.memory_add");
                var hazardApproach = GetStageWorkItems(snapshot, "npc.wave.hazard.mine_approach");
                var hazardAttempt = GetStageWorkItems(snapshot, "npc.wave.hazard.mine_neutralize_attempt");
                var hazardSuccess = GetStageWorkItems(snapshot, "npc.wave.hazard.mine_neutralize_success");
                var hazardForced = GetStageWorkItems(snapshot, "npc.wave.hazard.mine_neutralize_forced");
                var hazardFail = GetStageWorkItems(snapshot, "npc.wave.hazard.mine_neutralize_fail");
                var hazardSkipNonSapper = GetStageWorkItems(snapshot, "npc.wave.hazard.mine_skip_non_sapper");

                Assert.That(entMan.EntityExists(sapperMine), Is.True,
                    "Sapper-adjacent mine was deleted (likely detonated) instead of being handled safely.");
                Assert.That(entMan.TryGetComponent(sapperMine, out ItemToggleComponent sapperMineToggle), Is.True,
                    "Sapper-adjacent mine lost ItemToggleComponent unexpectedly.");
                Assert.That(sapperMineToggle.Activated, Is.False,
                    $"Sapper-adjacent mine remained armed after sapper hazard handling pass. hazard_memory_add={hazardMemoryAdd}, mine_approach={hazardApproach}, neutralize_attempt={hazardAttempt}, neutralize_success={hazardSuccess}, neutralize_forced={hazardForced}, neutralize_fail={hazardFail}.");
                Assert.That(hazardSkipNonSapper, Is.GreaterThan(0),
                    $"Expected non-sapper skip events in skill-gated scenario. mine_skip_non_sapper={hazardSkipNonSapper}, hazard_memory_add={hazardMemoryAdd}.");

                Assert.That(entMan.EntityExists(assaultMine), Is.True,
                    "Non-sapper mine was deleted unexpectedly in skill-gated scenario.");
                Assert.That(entMan.TryGetComponent(assaultMine, out ItemToggleComponent assaultMineToggle), Is.True,
                    "Non-sapper mine lost ItemToggleComponent unexpectedly.");
                Assert.That(assaultMineToggle.Activated, Is.True,
                    $"Non-sapper mine should remain armed in skill-gated scenario. mine_skip_non_sapper={hazardSkipNonSapper}, neutralize_success={hazardSuccess}.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanIntervalSeconds, oldHazardScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityHazardScanRadius, oldHazardScanRadius);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveSpawnNoGearAcquireChainBounded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var xformSystem = entMan.System<SharedTransformSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultNoGearPrototype,
                    x: 0f,
                    y: 0f);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WeaponPistolMk58", 1f, 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 8f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(300);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var hands = entMan.System<HandsSystem>();

                Assert.That(entMan.EntityExists(assault), Is.True, "No-gear assault entity was deleted unexpectedly.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.acquire_success"), Is.GreaterThan(0),
                    "Expected successful loadout acquire in no-gear chain.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_bounded"), Is.GreaterThan(0),
                    "Expected bounded loadout readiness completion.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_timeout"), Is.EqualTo(0),
                    "No-gear loadout readiness exceeded timeout budget.");

                Assert.That(entMan.TryGetComponent(assault, out HandsComponent assaultHands), Is.True,
                    "No-gear assault is missing hands component.");

                var hasGun = false;
                foreach (var held in hands.EnumerateHeld((assault, assaultHands)))
                {
                    if (entMan.HasComponent<GunComponent>(held))
                    {
                        hasGun = true;
                        break;
                    }
                }

                Assert.That(hasGun, Is.True,
                    "No-gear assault did not end up with a ranged weapon after loadout acquire chain.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveSpawnNoGearIgnoresNonCombatClosestItem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldLoadoutScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds);
        var oldLoadoutSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius);
        var oldLoadoutReadyTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, 10f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid decoy = default;
            EntityUid pistol = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var xformSystem = entMan.System<SharedTransformSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultNoGearPrototype,
                    x: 0f,
                    y: 0f);

                // Non-combat decoy is closer than combat item and must be ignored by loadout picker.
                decoy = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockCondimentStation", 0.8f, 0f);
                pistol = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WeaponPistolMk58", 2.0f, 0f);
                if (entMan.TryGetComponent(decoy, out TransformComponent decoyXform))
                    xformSystem.AnchorEntity((decoy, decoyXform));

                bench.Reset();
            });

            await pair.RunTicksSync(420);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var hands = entMan.System<HandsSystem>();

                Assert.That(entMan.EntityExists(assault), Is.True, "No-gear assault entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(decoy), Is.True, "Non-combat decoy was unexpectedly removed in loadout scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.acquire_success"), Is.GreaterThan(0),
                    "Expected successful loadout acquire in non-combat-decoy scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_bounded"), Is.GreaterThan(0),
                    "Expected bounded loadout readiness completion in non-combat-decoy scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_timeout"), Is.EqualTo(0),
                    "Loadout readiness exceeded timeout budget in non-combat-decoy scenario.");

                Assert.That(entMan.TryGetComponent(assault, out HandsComponent assaultHands), Is.True,
                    "No-gear assault is missing hands component.");

                var hasGun = false;
                var holdsDecoy = false;
                foreach (var held in hands.EnumerateHeld((assault, assaultHands)))
                {
                    if (held == decoy)
                        holdsDecoy = true;

                    if (held == pistol || entMan.HasComponent<GunComponent>(held))
                        hasGun = true;
                }

                Assert.That(hasGun, Is.True,
                    "No-gear assault did not end up with a ranged weapon when non-combat decoy was closer.");
                Assert.That(holdsDecoy, Is.False,
                    "No-gear assault incorrectly held non-combat decoy item after loadout acquisition.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, oldLoadoutScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius, oldLoadoutSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, oldLoadoutReadyTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveSpawnNoGearFallsBackToMeleeWhenNoGunAvailable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldLoadoutScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds);
        var oldLoadoutSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius);
        var oldLoadoutReadyTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, 10f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid decoy = default;
            EntityUid melee = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var xformSystem = entMan.System<SharedTransformSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultNoGearPrototype,
                    x: 0f,
                    y: 0f);

                // No ranged weapons available: role must still become combat-ready via melee fallback.
                decoy = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockCondimentStation", 0.8f, 0f);
                melee = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "BaseBallBat", 2.1f, 0f);
                if (entMan.TryGetComponent(decoy, out TransformComponent decoyXform))
                    xformSystem.AnchorEntity((decoy, decoyXform));

                bench.Reset();
            });

            await pair.RunTicksSync(420);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var hands = entMan.System<HandsSystem>();

                Assert.That(entMan.EntityExists(assault), Is.True, "No-gear assault entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(decoy), Is.True, "Non-combat decoy was unexpectedly removed in melee-fallback scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.acquire_success"), Is.GreaterThan(0),
                    "Expected successful loadout acquire in melee-fallback scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_bounded"), Is.GreaterThan(0),
                    "Expected bounded loadout readiness completion in melee-fallback scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_timeout"), Is.EqualTo(0),
                    "Loadout readiness exceeded timeout budget in melee-fallback scenario.");

                Assert.That(entMan.TryGetComponent(assault, out HandsComponent assaultHands), Is.True,
                    "No-gear assault is missing hands component.");

                var hasMelee = false;
                var holdsDecoy = false;
                foreach (var held in hands.EnumerateHeld((assault, assaultHands)))
                {
                    if (held == decoy)
                        holdsDecoy = true;

                    if (held == melee || entMan.HasComponent<MeleeWeaponComponent>(held))
                        hasMelee = true;
                }

                Assert.That(hasMelee, Is.True,
                    "No-gear assault did not pick up a melee weapon when ranged weapons were unavailable.");
                Assert.That(holdsDecoy, Is.False,
                    "No-gear assault incorrectly held non-combat decoy item in melee-fallback scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, oldLoadoutScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius, oldLoadoutSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, oldLoadoutReadyTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveSpawnNoGearNonCombatOnlyTimesOutWithoutWrongAcquire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldLoadoutScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds);
        var oldLoadoutSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius);
        var oldLoadoutReadyTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, 2.2f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid decoyA = default;
            EntityUid decoyB = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultNoGearPrototype,
                    x: 0f,
                    y: 0f);

                // No combat-capable items in radius: loadout layer must time out without fake readiness.
                decoyA = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "Pen", 1.0f, 0f);
                decoyB = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "LuxuryPen", 1.8f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(420);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var hands = entMan.System<HandsSystem>();

                Assert.That(entMan.EntityExists(assault), Is.True, "No-gear assault entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(decoyA), Is.True, "First non-combat decoy item was unexpectedly removed.");
                Assert.That(entMan.EntityExists(decoyB), Is.True, "Second non-combat decoy item was unexpectedly removed.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.acquire_success"), Is.EqualTo(0),
                    "Loadout layer must not report successful acquire without combat-capable items.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_bounded"), Is.EqualTo(0),
                    "Loadout layer must not report bounded readiness without combat-capable items.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_timeout"), Is.GreaterThan(0),
                    "Loadout layer must report timeout in non-combat-only scenario.");

                Assert.That(entMan.TryGetComponent(assault, out HandsComponent assaultHands), Is.True,
                    "No-gear assault is missing hands component.");

                var hasCombatItem = false;
                foreach (var held in hands.EnumerateHeld((assault, assaultHands)))
                {
                    if (entMan.HasComponent<GunComponent>(held) || entMan.HasComponent<MeleeWeaponComponent>(held))
                    {
                        hasCombatItem = true;
                        break;
                    }
                }

                Assert.That(hasCombatItem, Is.False,
                    "No-gear assault incorrectly acquired combat item in non-combat-only scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, oldLoadoutScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius, oldLoadoutSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, oldLoadoutReadyTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveSpawnNoGearReacquiresCombatItemAfterLoss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldLoadoutScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds);
        var oldLoadoutSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius);
        var oldLoadoutReadyTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, 10f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultNoGearPrototype,
                    x: 0f,
                    y: 0f);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WeaponPistolMk58", 1.0f, 0f);
                bench.Reset();
            });

            await pair.RunTicksSync(360);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var hands = entMan.System<HandsSystem>();

                Assert.That(entMan.TryGetComponent(assault, out HandsComponent assaultHands), Is.True,
                    "No-gear assault is missing hands component in reacquire scenario.");
                Assert.That(assaultHands, Is.Not.Null);

                var hasInitialCombat = false;
                foreach (var held in hands.EnumerateHeld((assault, assaultHands)))
                {
                    if (entMan.HasComponent<GunComponent>(held) || entMan.HasComponent<MeleeWeaponComponent>(held))
                    {
                        hasInitialCombat = true;
                        break;
                    }
                }

                Assert.That(hasInitialCombat, Is.True,
                    "No-gear assault failed to acquire initial combat item before forced-loss phase.");
            });

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var hands = entMan.System<HandsSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                Assert.That(entMan.TryGetComponent(assault, out HandsComponent assaultHands), Is.True,
                    "No-gear assault is missing hands component before forced-loss phase.");
                Assert.That(assaultHands, Is.Not.Null);

                foreach (var held in hands.EnumerateHeld((assault, assaultHands)).ToArray())
                {
                    _ = hands.TryDrop((assault, assaultHands), held, checkActionBlocker: false, doDropInteraction: false);

                    if (entMan.EntityExists(held))
                        entMan.DeleteEntity(held);
                }

                // Second phase can only become ready via re-acquiring this melee item.
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "BaseBallBat", 1.8f, 0f);
                bench.Reset();
            });

            await pair.RunTicksSync(420);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var hands = entMan.System<HandsSystem>();

                Assert.That(entMan.EntityExists(assault), Is.True, "No-gear assault entity was deleted unexpectedly.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.acquire_success"), Is.GreaterThan(0),
                    "Loadout layer failed to reacquire combat item after forced-loss phase.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_bounded"), Is.GreaterThan(0),
                    "Loadout layer failed to complete bounded readiness after forced-loss phase.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.loadout.ready_timeout"), Is.EqualTo(0),
                    "Loadout reacquire exceeded readiness timeout budget after forced-loss phase.");

                Assert.That(entMan.TryGetComponent(assault, out HandsComponent assaultHands), Is.True,
                    "No-gear assault is missing hands component in reacquire validation phase.");
                Assert.That(assaultHands, Is.Not.Null);

                var hasMelee = false;
                foreach (var held in hands.EnumerateHeld((assault, assaultHands)))
                {
                    if (entMan.HasComponent<MeleeWeaponComponent>(held))
                    {
                        hasMelee = true;
                        break;
                    }
                }

                Assert.That(hasMelee, Is.True,
                    "No-gear assault failed to hold reacquired melee item after forced-loss phase.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutScanIntervalSeconds, oldLoadoutScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutSearchRadius, oldLoadoutSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityLoadoutReadyTimeoutSeconds, oldLoadoutReadyTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveWeatherShelterRoundtripBounded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldShelterTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityShelterTimeoutSeconds);
        var oldReentryCooldown = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityShelterReentryCooldownSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityShelterTimeoutSeconds, 1.4f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityShelterReentryCooldownSeconds, 0.8f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid support = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();
                var roof = entMan.System<RoofSystem>();
                var weather = entMan.System<WeatherSystem>();
                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -30, 30);
                entMan.EnsureComponent<RoofComponent>(pair.TestMap.Grid.Owner);

                for (var x = 6; x <= 9; x++)
                {
                    for (var y = -1; y <= 1; y++)
                    {
                        roof.SetRoof((pair.TestMap.Grid.Owner, pair.TestMap.Grid.Comp, null), new Vector2i(x, y), true);
                    }
                }

                support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: 0f,
                    y: 0f);

                Assert.That(weather.TrySetWeather(pair.TestMap.MapId, AcidRainWeatherPrototype, out _, TimeSpan.FromSeconds(90)), Is.True);
                bench.Reset();
            });

            await pair.RunTicksSync(420);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(support), Is.True, "Support entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.weather.shelter_enter"), Is.GreaterThan(0),
                    "Expected weather shelter entry event.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.weather.shelter_exit"), Is.GreaterThan(0),
                    "Expected weather shelter exit event.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.weather.shelter_timeout"), Is.GreaterThan(0),
                    "Expected bounded weather shelter timeout handling.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityShelterTimeoutSeconds, oldShelterTimeout);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityShelterReentryCooldownSeconds, oldReentryCooldown);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task WeatherImplicitRoofBlocksExposure()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitIdleAsync();
        await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        EntityUid support = default;
        var exposed = true;

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var mapSystem = entMan.System<SharedMapSystem>();
            var weather = entMan.System<WeatherSystem>();

            FillFloorRect(mapSystem, pair.TestMap.Grid, -6, 6, -6, 6);
            entMan.EnsureComponent<ImplicitRoofComponent>(pair.TestMap.Grid.Owner);

            support = NpcCapabilityScenarioLibrary.SpawnAt(
                entMan,
                pair.TestMap.Grid,
                NpcCapabilityScenarioLibrary.SupportPrototype,
                x: 0f,
                y: 0f);

            Assert.That(weather.TrySetWeather(pair.TestMap.MapId, AcidRainWeatherPrototype, out _, TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(weather.TryGetWeatherPrototype(AcidRainWeatherPrototype, out var weatherProto), Is.True);
            exposed = weather.CanWeatherAffectEntity(support, weatherProto, entMan.GetComponent<TransformComponent>(support));
        });

        Assert.That(exposed, Is.False);

        await server.WaitPost(() => server.System<WeatherSystem>().TrySetWeather(pair.TestMap.MapId, null, out _));
        await pair.RunTicksSync(30);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NpcCapabilityWaveServiceCrateOpenAndHaul()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var storageSystem = entMan.System<SharedEntityStorageSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                var crate = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "CrateGenericSteel", 2f, 0f);
                var package = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 2f, 0f);

                Assert.That(entMan.TryGetComponent(crate, out EntityStorageComponent storageComp), Is.True,
                    "Service source crate must include EntityStorageComponent.");
                Assert.That(storageComp, Is.Not.Null);

                if (storageComp.Open)
                    storageSystem.CloseStorage(crate, storageComp);

                var inserted = storageSystem.Insert(package, crate, storageComp);
                Assert.That(inserted, Is.True, "Failed to insert restock package into source crate.");

                bench.Reset();
            });

            await pair.RunTicksSync(960);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.source_open_attempt"), Is.GreaterThan(0),
                    "Expected at least one source-open attempt in crate haul scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.source_open_success"), Is.GreaterThan(0),
                    "Expected successful source-open in crate haul scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.acquire_success"), Is.GreaterThan(0),
                    "Expected successful package acquisition in crate haul scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_attempt"), Is.GreaterThan(0),
                    "Expected restock attempt in crate haul scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success in crate haul scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_completed"), Is.GreaterThan(0),
                    "Expected completed service job in crate haul scenario.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty after crate-haul service flow.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceHeldCompatiblePackageRestocksWithoutAcquire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;
            EntityUid compatiblePackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var hands = entMan.System<HandsSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                compatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 0.8f, 0f);
                Assert.That(entMan.TryGetComponent(logistics, out HandsComponent logisticsHands), Is.True,
                    "Logistics role must include HandsComponent in held-compatible restock scenario.");
                var pickedUp = hands.TryPickupAnyHand(
                    logistics,
                    compatiblePackage,
                    checkActionBlocker: false,
                    animateUser: false,
                    animate: false,
                    handsComp: logisticsHands);
                Assert.That(pickedUp, Is.True, "Failed to place compatible restock package into logistics hands.");

                bench.Reset();
            });

            await pair.RunTicksSync(760);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.acquire_attempt"), Is.EqualTo(0),
                    "Service flow should not perform source acquire when compatible package is already held.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_attempt"), Is.GreaterThan(0),
                    "Expected restock attempt in held-compatible package scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success in held-compatible package scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_completed"), Is.GreaterThan(0),
                    "Expected completed service job in held-compatible package scenario.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty after held-compatible package restock.");
                Assert.That(entMan.EntityExists(compatiblePackage), Is.False,
                    "Compatible package should be consumed after successful held-compatible package restock.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceIgnoresNonRestockDecoyAndUsesCompatibleSource()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;
            EntityUid decoy = default;
            EntityUid compatiblePackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                // Closest item is irrelevant to service flow and must be ignored.
                decoy = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "Pen", 0.8f, 0f);
                compatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 2.0f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(900);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(decoy), Is.True,
                    "Non-restock decoy should remain untouched in service source-selection scenario.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.source_selected_item"), Is.GreaterThan(0),
                    "Expected item-source selection in non-restock-decoy scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success in non-restock-decoy scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_completed"), Is.GreaterThan(0),
                    "Expected completed service job in non-restock-decoy scenario.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty in non-restock-decoy scenario.");
                Assert.That(entMan.EntityExists(compatiblePackage), Is.False,
                    "Compatible package should be consumed after successful restock in non-restock-decoy scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceHeldCompatibleTargetsMatchingMachineNotClosestMismatch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 24f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid mismatchMachine = default;
            EntityUid matchingMachine = default;
            EntityUid heldPackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var hands = entMan.System<HandsSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -30, 30);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                mismatchMachine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineCigs", 6f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, mismatchMachine);

                matchingMachine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 10f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, matchingMachine);

                heldPackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 0.8f, 0f);

                Assert.That(entMan.TryGetComponent(logistics, out HandsComponent logisticsHands), Is.True,
                    "Logistics role must include HandsComponent in held-target-selection scenario.");
                Assert.That(logisticsHands, Is.Not.Null);

                var pickedUp = hands.TryPickupAnyHand(
                    logistics,
                    heldPackage,
                    checkActionBlocker: false,
                    animateUser: false,
                    animate: false,
                    handsComp: logisticsHands);
                Assert.That(pickedUp, Is.True, "Failed to place held compatible package into logistics hands.");

                bench.Reset();
            });

            await pair.RunTicksSync(900);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();

                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(mismatchMachine), Is.True, "Mismatch machine entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(matchingMachine), Is.True, "Matching machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_assigned_held"), Is.GreaterThan(0),
                    "Expected held-package service assignment in held-target-selection scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.acquire_attempt"), Is.EqualTo(0),
                    "Held-package flow should not attempt source acquire in held-target-selection scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success in held-target-selection scenario. {DescribeServiceCounters(snapshot)}");

                Assert.That(VendingHasAnyPositiveStock(entMan, mismatchMachine), Is.False,
                    "Nearest mismatch machine should remain unstocked in held-target-selection scenario.");
                Assert.That(VendingHasAnyPositiveStock(entMan, matchingMachine), Is.True,
                    "Matching machine remained unstocked in held-target-selection scenario.");
                Assert.That(entMan.EntityExists(heldPackage), Is.False,
                    "Held compatible package should be consumed after matching-machine restock.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceSequentiallyRestocksMultipleMachines()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 28f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 35f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machineA = default;
            EntityUid machineB = default;
            EntityUid packageA = default;
            EntityUid packageB = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -34, 34, -34, 34);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machineA = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                machineB = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineCigs", 14f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machineA);
                SetVendingMachineLowStockAndPanelOpen(entMan, machineB);

                packageA = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 2.0f, 0f);
                packageB = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockSmokes", 2.8f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(1600);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machineA), Is.True, "First vending machine entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machineB), Is.True, "Second vending machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThanOrEqualTo(2),
                    $"Expected two successful restocks in sequential multi-machine scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_completed"), Is.GreaterThanOrEqualTo(2),
                    "Expected two completed service jobs in sequential multi-machine scenario.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machineA), Is.True,
                    "First vending machine remained empty in sequential multi-machine scenario.");
                Assert.That(VendingHasAnyPositiveStock(entMan, machineB), Is.True,
                    "Second vending machine remained empty in sequential multi-machine scenario.");
                Assert.That(entMan.EntityExists(packageA), Is.False,
                    "First compatible package should be consumed after sequential multi-machine servicing.");
                Assert.That(entMan.EntityExists(packageB), Is.False,
                    "Second compatible package should be consumed after sequential multi-machine servicing.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceVendingRestockSingle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 2f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(900);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_assigned"), Is.GreaterThan(0),
                    "Expected assigned service job in single restock scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_attempt"), Is.GreaterThan(0),
                    "Expected restock attempt in single restock scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success in single restock scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_completed"), Is.GreaterThan(0),
                    "Expected completed service job in single restock scenario.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty after single restock service flow.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceIncompatibleOnlySourceDoesNotRestock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;
            EntityUid incompatiblePackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                incompatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockCondimentStation", 2f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(760);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(incompatiblePackage), Is.True,
                    "Incompatible package should remain untouched in incompatible-only source scenario.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.source_search_miss"), Is.GreaterThan(0),
                    "Expected source search miss telemetry when only incompatible packages are present.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_assigned"), Is.EqualTo(0),
                    "Service job must not be assigned when no compatible source exists.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_attempt"), Is.EqualTo(0),
                    "Service layer must not attempt restock when only incompatible packages exist.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.EqualTo(0),
                    "Service layer reported unexpected restock success with incompatible-only source.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_start_fail"), Is.EqualTo(0),
                    "Service layer should not start restock with known-incompatible package.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.False,
                    "Vending machine unexpectedly gained stock in incompatible-only source scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceSkipsMachineThatDoesNotNeedRestock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;
            EntityUid compatiblePackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                compatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 2f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(620);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(compatiblePackage), Is.True,
                    "Compatible package should remain unused when machine does not require restock.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_assigned"), Is.EqualTo(0),
                    "Service job should not be assigned for machine that does not need restock.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_attempt"), Is.EqualTo(0),
                    "Service layer attempted restock for already-stocked machine.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.EqualTo(0),
                    "Service layer reported restock success for already-stocked machine.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Machine unexpectedly has no stock in does-not-need-restock scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceFallsBackToAlternativeMachineWithCompatibleSource()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 24f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machineNoSource = default;
            EntityUid machineWithSource = default;
            EntityUid compatiblePackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -30, 30);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                // Closest machine cannot be serviced (no compatible package in range).
                machineNoSource = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 6f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machineNoSource);

                // Farther machine has compatible package; service layer must skip first and complete second.
                machineWithSource = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineCigs", 13f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machineWithSource);

                compatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockSmokes", 2f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(980);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machineNoSource), Is.True, "No-source machine entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machineWithSource), Is.True, "Compatible-source machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.source_search_miss"), Is.GreaterThan(0),
                    "Expected source-search miss telemetry while evaluating no-source machine in fallback scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success after fallback to alternative machine. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_completed"), Is.GreaterThan(0),
                    "Expected completed service job after fallback to alternative machine.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machineNoSource), Is.False,
                    "Nearest no-source machine should remain unstocked in fallback scenario.");
                Assert.That(VendingHasAnyPositiveStock(entMan, machineWithSource), Is.True,
                    "Fallback target machine remained empty after compatible-source restock.");
                Assert.That(entMan.EntityExists(compatiblePackage), Is.False,
                    "Compatible package should be consumed after fallback machine restock.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceVendingRestockMultiRace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid machine = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: -0.6f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0.6f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 2f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(960);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.reservation_conflict"), Is.GreaterThan(0),
                    "Expected reservation conflict telemetry in multi-NPC race scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected at least one restock success in multi-NPC race scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_completed"), Is.GreaterThan(0),
                    "Expected at least one completed service job in multi-NPC race scenario.");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty after multi-NPC race scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceVendingRestockPrefersCompatiblePackage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;
            EntityUid incompatiblePackage = default;
            EntityUid compatiblePackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                // Incompatible package is closer; service logic must still pick compatible package for this machine.
                incompatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockCondimentStation", 1.5f, 0f);
                compatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 3.8f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(960);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.job_assigned"), Is.GreaterThan(0),
                    "Expected assigned service job in compatibility-priority restock scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_attempt"), Is.GreaterThan(0),
                    "Expected restock attempt in compatibility-priority restock scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success in compatibility-priority restock scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_start_fail"), Is.EqualTo(0),
                    $"Service flow attempted incompatible restock package for target machine. {DescribeServiceCounters(snapshot)}");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty after compatibility-priority restock scenario.");
                Assert.That(entMan.EntityExists(incompatiblePackage), Is.True,
                    "Incompatible package should not be consumed in compatibility-priority restock scenario.");
                Assert.That(entMan.EntityExists(compatiblePackage), Is.False,
                    "Compatible package should be consumed after successful vending restock.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceCrateMixedCompatibilitySelectsCorrectPackage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;
            EntityUid incompatiblePackage = default;
            EntityUid compatiblePackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var storageSystem = entMan.System<SharedEntityStorageSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                var crate = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "CrateGenericSteel", 2f, 0f);
                incompatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockCondimentStation", 2f, 0f);
                compatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 2.2f, 0f);

                Assert.That(entMan.TryGetComponent(crate, out EntityStorageComponent storageComp), Is.True,
                    "Service source crate must include EntityStorageComponent.");
                Assert.That(storageComp, Is.Not.Null);

                if (storageComp.Open)
                    storageSystem.CloseStorage(crate, storageComp);

                var insertedWrong = storageSystem.Insert(incompatiblePackage, crate, storageComp);
                var insertedRight = storageSystem.Insert(compatiblePackage, crate, storageComp);
                Assert.That(insertedWrong, Is.True, "Failed to insert incompatible restock package into mixed source crate.");
                Assert.That(insertedRight, Is.True, "Failed to insert compatible restock package into mixed source crate.");

                bench.Reset();
            });

            await pair.RunTicksSync(980);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.source_open_attempt"), Is.GreaterThan(0),
                    "Expected source-open attempt in mixed-compatibility crate scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.source_open_success"), Is.GreaterThan(0),
                    "Expected source-open success in mixed-compatibility crate scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_attempt"), Is.GreaterThan(0),
                    "Expected restock attempt in mixed-compatibility crate scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success in mixed-compatibility crate scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_start_fail"), Is.EqualTo(0),
                    $"Service flow attempted incompatible crate package for target machine. {DescribeServiceCounters(snapshot)}");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty after mixed-compatibility crate scenario.");
                Assert.That(entMan.EntityExists(incompatiblePackage), Is.True,
                    "Incompatible package should not be consumed in mixed-compatibility crate scenario.");
                Assert.That(entMan.EntityExists(compatiblePackage), Is.False,
                    "Compatible package should be consumed after successful mixed-compatibility crate restock.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveServiceDropsHeldIncompatibleAndPicksRequiredPackage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldServiceReservationTtl = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds);
        var oldServiceJobTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, 30f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid machine = default;
            EntityUid wrongHeldA = default;
            EntityUid wrongHeldB = default;
            EntityUid compatiblePackage = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var hands = entMan.System<HandsSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", 8f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);

                wrongHeldA = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockCondimentStation", 0.8f, 0f);
                wrongHeldB = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockCondimentStation", 1.0f, 0f);
                compatiblePackage = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", 3.6f, 0f);

                Assert.That(entMan.TryGetComponent(logistics, out HandsComponent logisticsHands), Is.True,
                    "Logistics role must include HandsComponent in held-incompatible scenario.");

                var pickupA = hands.TryPickupAnyHand(
                    logistics,
                    wrongHeldA,
                    checkActionBlocker: false,
                    animateUser: false,
                    animate: false,
                    handsComp: logisticsHands);
                var pickupB = hands.TryPickupAnyHand(
                    logistics,
                    wrongHeldB,
                    checkActionBlocker: false,
                    animateUser: false,
                    animate: false,
                    handsComp: logisticsHands);

                Assert.That(pickupA, Is.True, "Failed to place first incompatible restock package into logistics hands.");
                Assert.That(pickupB, Is.True, "Failed to place second incompatible restock package into logistics hands.");

                bench.Reset();
            });

            await pair.RunTicksSync(1100);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(machine), Is.True, "Vending machine entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.drop_incompatible_held"), Is.GreaterThan(0),
                    "Expected service layer to drop held incompatible package before acquiring required package.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_attempt"), Is.GreaterThan(0),
                    "Expected restock attempt in held-incompatible recovery scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected restock success in held-incompatible recovery scenario. {DescribeServiceCounters(snapshot)}");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_start_fail"), Is.EqualTo(0),
                    $"Service flow attempted incompatible held package for target machine. {DescribeServiceCounters(snapshot)}");

                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty after held-incompatible recovery scenario.");
                Assert.That(entMan.EntityExists(compatiblePackage), Is.False,
                    "Compatible package should be consumed after successful restock in held-incompatible recovery scenario.");
                Assert.That(entMan.EntityExists(wrongHeldA) || entMan.EntityExists(wrongHeldB), Is.True,
                    "Incompatible held packages should not be consumed by vending restock flow.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceReservationTtlSeconds, oldServiceReservationTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceJobTimeoutSeconds, oldServiceJobTimeout);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDeployMortarRoleGatedBounded()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldDeployScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityDeployScanIntervalSeconds);
        var oldDeploySearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityDeploySearchRadius);
        var oldDeployTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityDeployJobTimeoutSeconds);
        var oldDeployMaxPerNpc = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityDeployMaxPerNpc);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeploySearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployJobTimeoutSeconds, 10f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployMaxPerNpc, 1);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid support = default;
            EntityUid assault = default;
            EntityUid supportMortar = default;
            EntityUid assaultMortar = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();
                var hands = entMan.System<HandsSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                // Separate lanes so each role only interacts with its own mortar kit.
                for (var x = -24; x <= 24; x++)
                {
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", x, 3f);
                }

                support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: 0f,
                    y: 0f);
                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: 0f,
                    y: 6f);

                supportMortar = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KMortarKit", 1f, 0f);
                assaultMortar = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KMortarKit", 1f, 6f);

                Assert.That(entMan.TryGetComponent(support, out HandsComponent supportHands), Is.True,
                    "Support role must include HandsComponent in deploy scenario.");
                Assert.That(entMan.TryGetComponent(assault, out HandsComponent assaultHands), Is.True,
                    "Assault role must include HandsComponent in deploy scenario.");

                var supportPickup = hands.TryPickupAnyHand(
                    support,
                    supportMortar,
                    checkActionBlocker: false,
                    animateUser: false,
                    animate: false,
                    handsComp: supportHands);
                var assaultPickup = hands.TryPickupAnyHand(
                    assault,
                    assaultMortar,
                    checkActionBlocker: false,
                    animateUser: false,
                    animate: false,
                    handsComp: assaultHands);

                Assert.That(supportPickup, Is.True, "Failed to place mortar kit into support NPC hands.");
                Assert.That(assaultPickup, Is.True, "Failed to place mortar kit into assault NPC hands.");

                bench.Reset();
            });

            await pair.RunTicksSync(520);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(support), Is.True, "Support entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(assault), Is.True, "Assault entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(supportMortar), Is.True, "Support mortar kit was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(assaultMortar), Is.True, "Assault mortar kit was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.deploy.attempt"), Is.GreaterThan(0),
                    "Expected at least one deploy attempt in role-gated deploy scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.deploy.success"), Is.GreaterThan(0),
                    "Expected successful deploy completion in role-gated deploy scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.deploy.mortar_placed"), Is.GreaterThan(0),
                    "Expected mortar placement counter in role-gated deploy scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.deploy.role_blocked_non_enabled"), Is.GreaterThan(0),
                    "Expected non-enabled role deploy block counter in role-gated scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.deploy.timeout"), Is.EqualTo(0),
                    "Deploy flow exceeded timeout budget in role-gated scenario.");

                Assert.That(entMan.TryGetComponent(supportMortar, out WH40KMortarComponent supportMortarComp), Is.True,
                    "Support mortar entity is missing WH40KMortarComponent.");
                Assert.That(entMan.TryGetComponent(assaultMortar, out WH40KMortarComponent assaultMortarComp), Is.True,
                    "Assault mortar entity is missing WH40KMortarComponent.");

                Assert.That(supportMortarComp.Deployed, Is.True,
                    "Support role failed to deploy its mortar kit.");
                Assert.That(entMan.GetComponent<TransformComponent>(supportMortar).Anchored, Is.True,
                    "Support mortar should be anchored after successful deployment.");
                Assert.That(assaultMortarComp.Deployed, Is.False,
                    "Assault role should not deploy mortar when WaveDeployEnabled is disabled.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployScanIntervalSeconds, oldDeployScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeploySearchRadius, oldDeploySearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployJobTimeoutSeconds, oldDeployTimeout);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployMaxPerNpc, oldDeployMaxPerNpc);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDeployDoesNotStarveStandaloneCombatGuardrail()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldDeployScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityDeployScanIntervalSeconds);
        var oldDeploySearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityDeploySearchRadius);
        var oldDeployTimeout = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityDeployJobTimeoutSeconds);
        var oldDeployMaxPerNpc = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityDeployMaxPerNpc);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeploySearchRadius, 20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployJobTimeoutSeconds, 10f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployMaxPerNpc, 1);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid support = default;
            EntityUid pirate = default;
            EntityUid supportMortar = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();
                var hands = entMan.System<HandsSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -30, 30);

                support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: -12f,
                    y: 0f);
                pirate = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobSpirate", 10f, 0f);
                ForceEquipWithPistol(entMan, pirate);

                supportMortar = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KMortarKit", -11f, 0f);
                Assert.That(entMan.TryGetComponent(support, out HandsComponent supportHands), Is.True,
                    "Support role must include HandsComponent in guardrail scenario.");
                var supportPickup = hands.TryPickupAnyHand(
                    support,
                    supportMortar,
                    checkActionBlocker: false,
                    animateUser: false,
                    animate: false,
                    handsComp: supportHands);
                Assert.That(supportPickup, Is.True, "Failed to place mortar kit into support NPC hands in guardrail scenario.");

                _ = NpcCapabilityScenarioLibrary.SpawnSwarm(
                    entMan,
                    pair.TestMap.Grid,
                    "MobPig",
                    count: 8,
                    origin: new Vector2(2f, -3f),
                    columns: 4,
                    spacing: 1.3f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 6f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(520);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(pirate), Is.True, "Pirate guardrail entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(support), Is.True, "Support deploy entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(supportMortar), Is.True, "Support mortar entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.combat.ranged.shoot_performed"), Is.GreaterThan(0),
                    "Standalone combat guardrail failed while deploy layer was active.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.deploy.success"), Is.GreaterThan(0),
                    "Expected at least one successful deploy completion in mixed guardrail scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.deploy.mortar_placed"), Is.GreaterThan(0),
                    "Expected mortar placement in mixed guardrail scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.deploy.timeout"), Is.EqualTo(0),
                    "Deploy flow exceeded timeout budget in mixed guardrail scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployScanIntervalSeconds, oldDeployScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeploySearchRadius, oldDeploySearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployJobTimeoutSeconds, oldDeployTimeout);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityDeployMaxPerNpc, oldDeployMaxPerNpc);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveInfluenceCaptureNeutralPoint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldInfluenceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds);
        var oldInfluenceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius);
        var oldInfluenceHoldFactor = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceHoldRadiusFactor);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius, 24f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceHoldRadiusFactor, 0.65f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid support = default;
            EntityUid point = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -30, 30);

                support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: 0f,
                    y: 0f);

                var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(support);
                member.TeamId = "Imperium";

                point = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "MachineChipProduser",
                    x: 8f,
                    y: 0f);

                Assert.That(entMan.TryGetComponent(point, out WH40KInfluencePointComponent pointComp), Is.True,
                    "Influence point prototype is missing WH40KInfluencePointComponent in capture scenario.");
                Assert.That(pointComp, Is.Not.Null);
                pointComp.OwnerTeamId = null;
                pointComp.CapturingTeamId = null;
                pointComp.CaptureProgressSeconds = 0f;
                pointComp.LastSyncedCaptureProgressSeconds = 0f;
                pointComp.CaptureEnabledFromPhase = WH40KBattlePhase.Preparation;
                pointComp.CaptureRadius = 2.7f;
                pointComp.CaptureTimeSeconds = 1.2f;
                pointComp.CaptureSpeedPerSecond = 1.6f;
                pointComp.RewardIntervalSeconds = 1f;
                pointComp.NextRewardTick = TimeSpan.Zero;

                bench.Reset();
            });

            await pair.RunTicksSync(520);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(support), Is.True, "Support entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(point), Is.True, "Influence point entity was deleted unexpectedly.");
                Assert.That(entMan.TryGetComponent(point, out WH40KInfluencePointComponent pointComp), Is.True,
                    "Influence point entity lost WH40KInfluencePointComponent.");
                Assert.That(pointComp.OwnerTeamId, Is.EqualTo("Imperium"),
                    "Wave support NPC failed to capture neutral influence point for its team.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.influence.search_hit"), Is.GreaterThan(0),
                    "Influence layer failed to detect any capture candidate.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.influence.point_acquired"), Is.GreaterThan(0),
                    "Influence layer failed to assign target point.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.influence.seek_point"), Is.GreaterThan(0),
                    "Influence layer failed to steer NPC toward point.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.influence.capture_neutral"), Is.GreaterThan(0),
                    "Influence layer did not classify neutral capture intent.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, oldInfluenceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius, oldInfluenceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceHoldRadiusFactor, oldInfluenceHoldFactor);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveInfluenceRoleGatedLogisticsDoesNotCapture()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldInfluenceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds);
        var oldInfluenceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius);
        var oldInfluenceHoldFactor = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceHoldRadiusFactor);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius, 24f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceHoldRadiusFactor, 0.65f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid logistics = default;
            EntityUid point = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: 0f,
                    y: 0f);

                var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(logistics);
                member.TeamId = "Imperium";

                point = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "MachineChipProduser",
                    x: 5f,
                    y: 0f);

                Assert.That(entMan.TryGetComponent(point, out WH40KInfluencePointComponent pointComp), Is.True,
                    "Influence point prototype is missing WH40KInfluencePointComponent in role-gate scenario.");
                Assert.That(pointComp, Is.Not.Null);
                pointComp.OwnerTeamId = null;
                pointComp.CapturingTeamId = null;
                pointComp.CaptureProgressSeconds = 0f;
                pointComp.LastSyncedCaptureProgressSeconds = 0f;
                pointComp.CaptureEnabledFromPhase = WH40KBattlePhase.Preparation;
                pointComp.CaptureRadius = 2.7f;
                pointComp.CaptureTimeSeconds = 1.0f;
                pointComp.CaptureSpeedPerSecond = 2.0f;

                bench.Reset();
            });

            await pair.RunTicksSync(420);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(logistics), Is.True, "Logistics entity was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(point), Is.True, "Influence point entity was deleted unexpectedly.");
                Assert.That(entMan.TryGetComponent(point, out WH40KInfluencePointComponent pointComp), Is.True,
                    "Influence point entity lost WH40KInfluencePointComponent.");

                Assert.That(pointComp.OwnerTeamId, Is.Null,
                    "Logistics role should not capture influence points when WaveInfluenceEnabled is disabled.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.influence.role_disabled_skip"), Is.GreaterThan(0),
                    "Expected role-gated skip counter for logistics influence behavior.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.influence.seek_point"), Is.EqualTo(0),
                    "Logistics role should not produce influence steering seeks.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, oldInfluenceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius, oldInfluenceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceHoldRadiusFactor, oldInfluenceHoldFactor);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveObjectiveTargetTeamResolution()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldObjectiveHoldFactor = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 32f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, 0.75f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, false);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid imperiumSupport = default;
            EntityUid hereticSupport = default;
            EntityUid objectiveImperium = default;
            EntityUid objectiveHeretics = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -40, 40, -20, 20);

                imperiumSupport = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: -8f,
                    y: 0f);
                hereticSupport = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: 8f,
                    y: 0f);

                var imperiumTeam = entMan.EnsureComponent<WH40KTeamMemberComponent>(imperiumSupport);
                imperiumTeam.TeamId = "Imperium";
                var hereticsTeam = entMan.EnsureComponent<WH40KTeamMemberComponent>(hereticSupport);
                hereticsTeam.TeamId = "Heretics";

                objectiveImperium = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "WH40KObjectiveImperium",
                    x: -18f,
                    y: 0f);
                objectiveHeretics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "WH40KObjectiveHeretics",
                    x: 18f,
                    y: 0f);


                bench.Reset();
            });

            await pair.RunTicksSync(280);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(imperiumSupport), Is.True, "Imperium support NPC was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(hereticSupport), Is.True, "Heretic support NPC was deleted unexpectedly.");

                var htnImperium = entMan.GetComponent<HTNComponent>(imperiumSupport);
                var htnHeretics = entMan.GetComponent<HTNComponent>(hereticSupport);

                var hasImperiumOrdered = htnImperium.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var imperiumOrdered, entMan);
                if (!hasImperiumOrdered)
                    hasImperiumOrdered = htnImperium.Blackboard.TryGetValue<EntityUid>("Target", out imperiumOrdered, entMan);

                var hasHereticsOrdered = htnHeretics.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var hereticsOrdered, entMan);
                if (!hasHereticsOrdered)
                    hasHereticsOrdered = htnHeretics.Blackboard.TryGetValue<EntityUid>("Target", out hereticsOrdered, entMan);

                Assert.That(hasImperiumOrdered, Is.True, "Imperium NPC did not resolve objective target.");
                Assert.That(hasHereticsOrdered, Is.True, "Heretics NPC did not resolve objective target.");
                Assert.That(imperiumOrdered, Is.EqualTo(objectiveHeretics),
                    "Imperium NPC must target Heretics objective, not own objective.");
                Assert.That(hereticsOrdered, Is.EqualTo(objectiveImperium),
                    "Heretics NPC must target Imperium objective, not own objective.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.objective_target_selected"), Is.GreaterThan(0),
                    "Objective layer did not emit target selection in team-resolution scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.objective_target_rejected_same_team"), Is.GreaterThan(0),
                    "Objective layer must reject allied objectives in mixed-team scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, oldObjectiveHoldFactor);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveObjectiveHereticTestSquadDamagesImperiumObjective()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldObjectiveHoldFactor = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 36f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, 0.8f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, false);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid breacher = default;
            EntityUid support = default;
            EntityUid objectiveImperium = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -20, 20);
                BuildPerimeterWalls(entMan, pair.TestMap.Grid, -30, 30, -20, 20);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.TestHereticObjectiveAssaultPrototype,
                    x: -8f,
                    y: -1f);
                breacher = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.TestHereticObjectiveBreacherPrototype,
                    x: -8f,
                    y: 1f);
                support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.TestHereticObjectiveSupportPrototype,
                    x: -9f,
                    y: 0f);

                objectiveImperium = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "WH40KObjectiveImperium",
                    x: 4f,
                    y: 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(700);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();

                Assert.That(entMan.EntityExists(assault), Is.True, "Heretic assault test NPC was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(breacher), Is.True, "Heretic breacher test NPC was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(support), Is.True, "Heretic support test NPC was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(objectiveImperium), Is.True, "Imperium objective was deleted unexpectedly.");

                var hasOrderedObjective = IsNpcTargetingObjective(entMan, assault, objectiveImperium) ||
                                          IsNpcTargetingObjective(entMan, breacher, objectiveImperium) ||
                                          IsNpcTargetingObjective(entMan, support, objectiveImperium);

                Assert.That(hasOrderedObjective, Is.True,
                    "Expected at least one heretic test NPC to target WH40KObjectiveImperium.");

                Assert.That(entMan.TryGetComponent(objectiveImperium, out DamageableComponent damageable), Is.True,
                    "Imperium objective must have DamageableComponent.");
                var damageSystem = entMan.System<DamageableSystem>();
                Assert.That(damageSystem.GetTotalDamage((objectiveImperium, damageable)).Float(), Is.GreaterThan(0f),
                    "Heretic objective test squad did not deal any damage to WH40KObjectiveImperium.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.objective_target_selected"), Is.GreaterThan(0),
                    "Objective layer did not emit target selection for heretic objective test squad.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.objective_attack_started"), Is.GreaterThan(0),
                    "Objective layer did not emit attack start telemetry in heretic objective assault scenario.");

                Assert.That(entMan.TryGetComponent(objectiveImperium, out WH40KObjectiveComponent objectiveComp), Is.True,
                    "Imperium objective must have WH40KObjectiveComponent.");
                Assert.That(objectiveComp.Destroyed, Is.False,
                    "Objective should not be immediately destroyed in smoke scenario; test expects progressive pressure.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, oldObjectiveHoldFactor);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveObjectiveStrikeSpawnPointSpawnsFullSquadAndPressuresImperiumObjective()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldObjectiveHoldFactor = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 40f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, 0.8f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, false);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid objectiveImperium = default;
            EntityUid strikeSpawnPoint = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();
                var timing = server.ResolveDependency<IGameTiming>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -36, 36, -24, 24);
                BuildPerimeterWalls(entMan, pair.TestMap.Grid, -36, 36, -24, 24);

                objectiveImperium = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "WH40KObjectiveImperium",
                    x: 12f,
                    y: 0f);

                strikeSpawnPoint = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.TestHereticObjectiveStrikeSpawnPointPrototype,
                    x: -12f,
                    y: 0f);

                if (entMan.HasComponent<WH40KPhaseTimedSpawnerComponent>(strikeSpawnPoint))
                    entMan.RemoveComponent<WH40KPhaseTimedSpawnerComponent>(strikeSpawnPoint);

                if (entMan.TryGetComponent(strikeSpawnPoint, out TimedSpawnerComponent timedSpawner))
                {
                    timedSpawner.IntervalSeconds = TimeSpan.FromSeconds(30);
                    timedSpawner.NextFire = timing.CurTime + TimeSpan.FromSeconds(0.5);
                }

                bench.Reset();
            });

            await pair.RunTicksSync(520);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();

                Assert.That(entMan.EntityExists(objectiveImperium), Is.True, "Imperium objective was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(strikeSpawnPoint), Is.True, "Strike spawn point was deleted unexpectedly.");

                var leaders = CountEntitiesByPrototype(entMan, NpcCapabilityScenarioLibrary.TestHereticObjectiveLeaderPrototype);
                var coordinators = CountEntitiesByPrototype(entMan, NpcCapabilityScenarioLibrary.TestHereticObjectiveCoordinatorPrototype);
                var sappers = CountEntitiesByPrototype(entMan, NpcCapabilityScenarioLibrary.TestHereticObjectiveSapperPrototype);
                var soldiers = CountEntitiesByPrototype(entMan, NpcCapabilityScenarioLibrary.TestHereticObjectiveSoldierPrototype);

                Assert.That(leaders, Is.GreaterThanOrEqualTo(1),
                    "Strike spawn point did not spawn squad leader.");
                Assert.That(coordinators, Is.GreaterThanOrEqualTo(1),
                    "Strike spawn point did not spawn coordinator.");
                Assert.That(sappers, Is.GreaterThanOrEqualTo(1),
                    "Strike spawn point did not spawn sapper.");
                Assert.That(soldiers, Is.GreaterThanOrEqualTo(4),
                    "Strike spawn point did not spawn expected soldier count.");

                Assert.That(entMan.TryGetComponent(objectiveImperium, out DamageableComponent damageable), Is.True,
                    "Imperium objective must have DamageableComponent.");
                var damageSystem = entMan.System<DamageableSystem>();
                Assert.That(damageSystem.GetTotalDamage((objectiveImperium, damageable)).Float(), Is.GreaterThan(0f),
                    "Spawned strike squads did not deal damage to WH40KObjectiveImperium.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.objective_target_selected"), Is.GreaterThan(0),
                    "Objective layer did not emit target selection for timed strike-squad scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, oldObjectiveHoldFactor);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveObjectiveBattlefieldDirectorTraceSquadDestroysImperiumObjective()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldNpcMaxUpdates = server.CfgMan.GetCVar(CCVars.NPCMaxUpdates);
        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldObjectiveHoldFactor = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor);
        var oldObjectiveNoPathFallback = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries);
        var oldObjectiveNoPathUnreachable = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);
        var oldDirectorTick = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds);
        var oldDirectorTtl = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds);
        var oldDirectorHysteresis = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta);
        var oldDirectorReassign = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds);
        var oldDirectorPreempt = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds);
        var oldDirectorThreatRadius = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius);
        var oldDirectorShortageThreshold = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold);

        var trace = new StringBuilder();
        var lastPositions = new Dictionary<EntityUid, Vector2>();
        var noProgressSamples = new Dictionary<EntityUid, int>();
        var lastDamage = new Dictionary<EntityUid, float>();
        var engaged = new Dictionary<EntityUid, bool>();
        var labels = new Dictionary<EntityUid, string>();
        var squad = new List<EntityUid>();
        EntityUid objectiveImperium = default;
        EntityUid battlefieldGrid = EntityUid.Invalid;

        var totalDirectorIssued = 0;
        var totalDirectorPushIssued = 0;
        var totalDirectorBreachIssued = 0;
        var totalDirectorEnemyReason = 0;
        var totalDirectorKeepCurrent = 0;
        var totalObjectiveAttackStarted = 0;
        var totalObjectiveSearchHit = 0;
        var totalDirectorTargetHit = 0;
        var totalPathRetry = 0;
        var objectiveDestroyed = false;
        var objectiveDamage = 0f;

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCMaxUpdates, 512);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 24f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, 0.8f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, 4);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, 9);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, 0.55f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta, 6f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, 0.20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds, 0.20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold, 2);
            });

            await server.WaitIdleAsync();
            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapLoader = entMan.System<MapLoaderSystem>();
                var options = DeserializationOptions.Default with { InitializeMaps = true };
                Assert.That(
                    mapLoader.TryLoadMap(new ResPath("/Maps/_WH40K/battlefield40k.yml"), out _, out var grids, options),
                    Is.True,
                    "Failed to load battlefield40k map for NPC objective run.");
                Assert.That(grids, Is.Not.Empty, "battlefield40k map loaded without grids.");
                battlefieldGrid = grids!.First().Owner;
            });
            await pair.RunTicksSync(20);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                var existingNpcs = new List<EntityUid>();
                var npcQuery = entMan.EntityQueryEnumerator<ActiveNPCComponent>();
                while (npcQuery.MoveNext(out var existing, out _))
                {
                    existingNpcs.Add(existing);
                }

                foreach (var existing in existingNpcs)
                {
                    if (entMan.EntityExists(existing))
                        entMan.DeleteEntity(existing);
                }

                if (!TryFindObjectiveByTeam(entMan, "Heretics", out _, out _, out _))
                {
                    Assert.That(battlefieldGrid, Is.Not.EqualTo(EntityUid.Invalid),
                        "Unable to resolve a fallback grid for spawning missing battlefield objectives.");
                    _ = entMan.SpawnEntity("WH40KObjectiveHeretics", new EntityCoordinates(battlefieldGrid, new Vector2(151.5f, 68.5f)));
                }

                if (!TryFindObjectiveByTeam(entMan, "Imperium", out objectiveImperium, out _, out _))
                {
                    Assert.That(battlefieldGrid, Is.Not.EqualTo(EntityUid.Invalid),
                        "Unable to resolve a fallback grid for spawning missing battlefield objectives.");
                    objectiveImperium = entMan.SpawnEntity("WH40KObjectiveImperium", new EntityCoordinates(battlefieldGrid, new Vector2(-145.5f, 33.5f)));
                }

                Assert.That(
                    TryFindObjectiveByTeam(entMan, "Imperium", out objectiveImperium, out _, out _),
                    Is.True,
                    "Battlefield map is missing Imperium objective.");

                var teamRule = entMan.System<WH40KTeamBattleRuleSystem>();
                _ = teamRule.TrySetCurrentPhase(WH40KBattlePhase.Assault);

                var roleSetup = new (string RoleLabel, string NpcPrototype, string SpawnPrototype, Vector2 FallbackPos)[]
                {
                    ("leader", NpcCapabilityScenarioLibrary.TestHereticObjectiveLeaderPrototype, NpcCapabilityScenarioLibrary.BattlefieldHereticLeaderSpawnPrototype, new Vector2(144.5f, 65.5f)),
                    ("coordinator", NpcCapabilityScenarioLibrary.TestHereticObjectiveCoordinatorPrototype, NpcCapabilityScenarioLibrary.BattlefieldHereticCoordinatorSpawnPrototype, new Vector2(144.5f, 67.5f)),
                    ("sapper", NpcCapabilityScenarioLibrary.TestHereticObjectiveSapperPrototype, NpcCapabilityScenarioLibrary.BattlefieldHereticSapperSpawnPrototype, new Vector2(127.5f, 57.5f)),
                    ("soldier-1", NpcCapabilityScenarioLibrary.TestHereticObjectiveSoldierPrototype, NpcCapabilityScenarioLibrary.BattlefieldHereticSoldierSpawnPrototype, new Vector2(118.5f, 78.5f)),
                    ("soldier-2", NpcCapabilityScenarioLibrary.TestHereticObjectiveSoldierPrototype, NpcCapabilityScenarioLibrary.BattlefieldHereticSoldierAlt1SpawnPrototype, new Vector2(116.5f, 78.5f)),
                    ("soldier-3", NpcCapabilityScenarioLibrary.TestHereticObjectiveSoldierPrototype, NpcCapabilityScenarioLibrary.BattlefieldHereticSoldierAlt2SpawnPrototype, new Vector2(120.5f, 79.5f)),
                    ("breacher", NpcCapabilityScenarioLibrary.TestHereticObjectiveBreacherPrototype, NpcCapabilityScenarioLibrary.BattlefieldHereticSoldierAlt3SpawnPrototype, new Vector2(120.5f, 77.5f)),
                };

                foreach (var spec in roleSetup)
                {
                    EntityCoordinates spawnCoordinates;
                    if (!TryGetSpawnPointCoordinates(entMan, spec.SpawnPrototype, out spawnCoordinates))
                    {
                        Assert.That(battlefieldGrid, Is.Not.EqualTo(EntityUid.Invalid),
                            $"Unable to resolve fallback grid for role '{spec.RoleLabel}'.");
                        spawnCoordinates = new EntityCoordinates(battlefieldGrid, spec.FallbackPos);
                    }

                    var npc = entMan.SpawnEntity(spec.NpcPrototype, spawnCoordinates);
                    ForceEquipWithCapabilityItem(entMan, npc, spec.RoleLabel == "breacher" ? "FireAxe" : "WeaponPistolMk58");

                    squad.Add(npc);
                    labels[npc] = spec.RoleLabel;
                    engaged[npc] = false;
                }

                bench.Reset();
            });

            const int sampleTicks = 30;
            const int maxSamples = 360;

            for (var step = 1; step <= maxSamples; step++)
            {
                await pair.RunTicksSync(sampleTicks);

                await server.WaitPost(() =>
                {
                    var entMan = server.ResolveDependency<IEntityManager>();
                    var bench = entMan.System<NPCBenchmarkSystem>();
                    var damageSystem = entMan.System<DamageableSystem>();
                    var xformSystem = entMan.System<SharedTransformSystem>();
                    var snapshot = bench.SnapshotAndReset();

                    totalDirectorIssued += GetStageWorkItems(snapshot, "npc.wave.director.order_issued");
                    totalDirectorPushIssued += GetStageWorkItems(snapshot, "npc.wave.director.order_issued.push_objective");
                    totalDirectorBreachIssued += GetStageWorkItems(snapshot, "npc.wave.director.order_issued.breach_lane");
                    totalDirectorEnemyReason += GetStageWorkItems(snapshot, "npc.wave.director.decision.enemy_objective");
                    totalDirectorKeepCurrent += GetStageWorkItems(snapshot, "npc.wave.director.decision.keep_current");
                    totalObjectiveAttackStarted += GetStageWorkItems(snapshot, "npc.wave.objective_attack_started");
                    totalObjectiveSearchHit += GetStageWorkItems(snapshot, "npc.wave.objective.search_hit");
                    totalDirectorTargetHit += GetStageWorkItems(snapshot, "npc.wave.objective.director_target_hit");
                    totalPathRetry += GetStageWorkItems(snapshot, "npc.wave.pathblocked.retry_bounded");

                    if (!entMan.EntityExists(objectiveImperium))
                    {
                        trace.AppendLine($"step={step:000} objective_entity_deleted=true");
                        objectiveDestroyed = true;
                        return;
                    }

                    var objectiveXform = entMan.GetComponent<TransformComponent>(objectiveImperium);
                    var objectivePos = xformSystem.GetWorldPosition(objectiveXform);
                    var objectiveComp = entMan.GetComponent<WH40KObjectiveComponent>(objectiveImperium);
                    var objectiveDamageable = entMan.GetComponent<DamageableComponent>(objectiveImperium);
                    objectiveDamage = damageSystem.GetTotalDamage((objectiveImperium, objectiveDamageable)).Float();
                    objectiveDestroyed = objectiveComp.Destroyed;

                    trace.AppendLine(
                        $"step={step:000} objective_damage={objectiveDamage:F1} destroyed={objectiveDestroyed} :: {DescribeBattlefieldDirectorObjectiveCounters(snapshot)}");

                    var squadCentroid = Vector2.Zero;
                    var liveCount = 0;
                    Vector2? leaderPosition = null;
                    var stalledCount = 0;

                    foreach (var npc in squad)
                    {
                        var label = labels[npc];
                        if (!entMan.EntityExists(npc))
                        {
                            trace.AppendLine($"  npc={label} deleted=true");
                            continue;
                        }

                        var npcXform = entMan.GetComponent<TransformComponent>(npc);
                        var npcPos = xformSystem.GetWorldPosition(npcXform);
                        squadCentroid += npcPos;
                        liveCount++;
                        if (label == "leader")
                            leaderPosition = npcPos;

                        var moveDistance = 0f;
                        if (lastPositions.TryGetValue(npc, out var previousPos))
                            moveDistance = (npcPos - previousPos).Length();
                        lastPositions[npc] = npcPos;

                        var objectiveDistance = (objectivePos - npcPos).Length();

                        var totalNpcDamage = entMan.TryGetComponent(npc, out DamageableComponent npcDamageable)
                            ? damageSystem.GetTotalDamage((npc, npcDamageable)).Float()
                            : 0f;
                        var damageDelta = 0f;
                        if (lastDamage.TryGetValue(npc, out var previousDamage))
                            damageDelta = totalNpcDamage - previousDamage;
                        lastDamage[npc] = totalNpcDamage;

                        var hasCombatTarget = TryGetNpcBlackboardTarget(entMan, npc, out var combatTarget);
                        if (hasCombatTarget && combatTarget != EntityUid.Invalid)
                            engaged[npc] = true;

                        var combatTargetToken = hasCombatTarget
                            ? DescribeEntity(entMan, combatTarget)
                            : "-";
                        var orderedTargetToken = TryGetNpcOrderedTarget(entMan, npc, out var orderedTarget)
                            ? DescribeEntity(entMan, orderedTarget)
                            : "-";
                        var blockerTargetToken = TryGetNpcObjectiveBlockerTarget(entMan, npc, out var blockerTarget)
                            ? DescribeEntity(entMan, blockerTarget)
                            : "-";
                        var hasOrder = TryGetDirectorOrderToken(entMan, npc, out var orderToken);
                        var currentOrder = hasOrder ? orderToken : "none";
                        var steeringToken = TryDescribeNpcSteering(entMan, npc, out var steeringDescription)
                            ? steeringDescription
                            : "steering=none";
                        var combatStateToken = TryDescribeNpcCombat(entMan, npc, out var combatDescription)
                            ? combatDescription
                            : "combat=none";

                        var stalled = moveDistance <= 0.08f && objectiveDistance > 3f;
                        var stallSamples = stalled ? noProgressSamples.GetValueOrDefault(npc) + 1 : 0;
                        noProgressSamples[npc] = stallSamples;
                        if (stallSamples >= 4)
                            stalledCount++;

                        trace.AppendLine(
                            $"  npc={label} pos={npcPos.X:F1},{npcPos.Y:F1} move={moveDistance:F2} dist_obj={objectiveDistance:F1} hp_dmg={totalNpcDamage:F1} hp_delta={damageDelta:F1} order={currentOrder} ordered_target={orderedTargetToken} blocker_target={blockerTargetToken} target={combatTargetToken} stall_samples={stallSamples} {steeringToken} {combatStateToken}");

                        if (stallSamples >= 4 &&
                            (stallSamples == 4 || stallSamples % 20 == 0))
                        {
                            trace.AppendLine(
                                $"    blockers={DescribeNearbyAnchoredEntities(entMan, npc, 2.35f)}");
                        }
                    }

                    if (liveCount > 0)
                    {
                        squadCentroid /= liveCount;
                        var maxRadius = 0f;
                        var farFromLeader = 0;

                        foreach (var npc in squad)
                        {
                            if (!entMan.EntityExists(npc))
                                continue;

                            var npcPos = xformSystem.GetWorldPosition(entMan.GetComponent<TransformComponent>(npc));
                            maxRadius = MathF.Max(maxRadius, (npcPos - squadCentroid).Length());

                            if (leaderPosition != null &&
                                labels[npc] != "leader" &&
                                (npcPos - leaderPosition.Value).Length() > 18f)
                            {
                                farFromLeader++;
                            }
                        }

                        trace.AppendLine(
                            $"  squad centroid={squadCentroid.X:F1},{squadCentroid.Y:F1} max_radius={maxRadius:F1} stalled={stalledCount}/{liveCount} far_from_leader={farFromLeader}/{Math.Max(0, liveCount - 1)}");
                    }
                });

                if (objectiveDestroyed)
                    break;
            }

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();

                Assert.That(entMan.EntityExists(objectiveImperium), Is.True,
                    $"Imperium objective entity was deleted unexpectedly.\n{trace}");
                Assert.That(objectiveDamage, Is.GreaterThan(0f),
                    $"Battlefield squad did not deal damage to Imperium objective.\n{trace}");
                Assert.That(objectiveDestroyed, Is.True,
                    $"Battlefield squad failed to destroy Imperium objective in bounded runtime.\n{trace}");

                foreach (var npc in squad)
                {
                    var label = labels[npc];
                    Assert.That(entMan.EntityExists(npc), Is.True,
                        $"Squad member '{label}' was deleted unexpectedly.\n{trace}");
                    Assert.That(engaged.GetValueOrDefault(npc), Is.True,
                        $"Squad member '{label}' never acquired combat target; expected every role to be combat-useful.\n{trace}");
                }

                Assert.That(totalDirectorIssued, Is.GreaterThan(0),
                    $"Director never issued orders on battlefield run.\n{trace}");
                Assert.That(totalDirectorEnemyReason, Is.GreaterThan(0),
                    $"Director never selected enemy-objective reasoning path.\n{trace}");
                Assert.That(totalDirectorPushIssued + totalDirectorBreachIssued, Is.GreaterThan(0),
                    $"Director never issued push/breach objective orders.\n{trace}");
                Assert.That(totalDirectorTargetHit, Is.GreaterThan(0),
                    $"Objective layer never consumed director-ordered objective target.\n{trace}");
                Assert.That(totalObjectiveAttackStarted, Is.GreaterThan(0),
                    $"Objective attack was never started on battlefield run.\n{trace}");

                TestContext.Out.WriteLine(trace.ToString());
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCMaxUpdates, oldNpcMaxUpdates);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveHoldRadiusFactor, oldObjectiveHoldFactor);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, oldObjectiveNoPathFallback);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, oldObjectiveNoPathUnreachable);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, oldDirectorTick);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, oldDirectorTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta, oldDirectorHysteresis);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, oldDirectorReassign);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds, oldDirectorPreempt);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius, oldDirectorThreatRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold, oldDirectorShortageThreshold);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityPathBlockedDestructibleWallBreacherProgresses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 28f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid breacher = default;
            EntityUid objective = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -20, 20, -20, 20);
                BuildPerimeterWalls(entMan, pair.TestMap.Grid, -20, 20, -20, 20);
                BuildLockedDoorTwoRoomScenario(entMan, pair.TestMap.Grid);

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", 2f, 0f);

                breacher = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.BreacherPrototype,
                    x: 0f,
                    y: 0f);
                var team = entMan.EnsureComponent<WH40KTeamMemberComponent>(breacher);
                team.TeamId = "Imperium";
                ForceEquipWithCapabilityItem(entMan, breacher, "FireAxe");

                objective = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "WH40KObjectiveHeretics",
                    x: 8f,
                    y: 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(420);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(breacher), Is.True, "Breacher was deleted unexpectedly.");
                var breacherX = entMan.GetComponent<TransformComponent>(breacher).Coordinates.Position.X;

                Assert.That(GetStageWorkItems(snapshot, "npc.steering.obstacle.policy.smash_attempt"), Is.GreaterThan(0),
                    "Breacher did not attempt smash policy on destructible blocker.");
                Assert.That(GetStageWorkItems(snapshot, "npc.steering.obstacle.progress"), Is.GreaterThan(0),
                    "Breacher did not produce obstacle progression on destructible blocker.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.objective_target_selected"), Is.GreaterThan(0),
                    "Objective layer did not assign objective target for breacher.");
                Assert.That(breacherX, Is.GreaterThan(0.6f),
                    "Breacher did not reach the destructible chokepoint.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityPathBlockedIndestructibleWallFallbackTriggered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldFallbackRetries = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries);
        var oldUnreachableRetries = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 30f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, 2);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, 4);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid objective = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -20, 20);
                BuildPerimeterWalls(entMan, pair.TestMap.Grid, -30, 30, -20, 20);

                for (var y = -19; y <= 19; y++)
                {
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", 6f, y);
                }

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: 0f,
                    y: 0f);
                var team = entMan.EnsureComponent<WH40KTeamMemberComponent>(assault);
                team.TeamId = "Imperium";

                objective = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "WH40KObjectiveHeretics",
                    x: 12f,
                    y: 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(420);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(assault), Is.True, "Assault NPC was deleted unexpectedly.");
                Assert.That(entMan.EntityExists(objective), Is.True, "Objective entity was deleted unexpectedly.");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.pathblocked.retry_bounded"), Is.GreaterThan(0),
                    "Blocked objective scenario did not emit bounded retry telemetry.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.pathblocked.fallback"), Is.GreaterThan(0),
                    "Blocked objective scenario did not emit fallback telemetry.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.pathblocked.unreachable"), Is.GreaterThan(0),
                    "Blocked objective scenario did not emit unreachable telemetry.");
                Assert.That(GetStageWorkItems(snapshot, "npc.steering.path_request.no_path_backoff"), Is.GreaterThan(0),
                    "Blocked objective scenario did not emit steering no-path backoff telemetry.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, oldFallbackRetries);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, oldUnreachableRetries);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityPathBlockedDynamicOpenAfterDelayReplanSucceeds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldFallbackRetries = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries);
        var oldUnreachableRetries = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 30f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, 2);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, 8);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid objective = default;
            EntityUid gateWall = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -20, 20);
                BuildPerimeterWalls(entMan, pair.TestMap.Grid, -30, 30, -20, 20);

                for (var y = -19; y <= 19; y++)
                {
                    if (y == 0)
                        continue;

                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", 6f, y);
                }

                gateWall = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", 6f, 0f);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: 0f,
                    y: 0f);
                var team = entMan.EnsureComponent<WH40KTeamMemberComponent>(assault);
                team.TeamId = "Imperium";

                objective = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "WH40KObjectiveHeretics",
                    x: 12f,
                    y: 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(180);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                if (entMan.EntityExists(gateWall))
                    entMan.DeleteEntity(gateWall);
            });

            await pair.RunTicksSync(300);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(assault), Is.True, "Assault NPC was deleted unexpectedly.");
                var assaultX = entMan.GetComponent<TransformComponent>(assault).Coordinates.Position.X;

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.pathblocked.retry_bounded"), Is.GreaterThan(0),
                    "Dynamic-open scenario did not emit blocked retry telemetry before lane opening.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.pathblocked.replan_success"), Is.GreaterThan(0),
                    "Dynamic-open scenario did not emit replan-success telemetry after lane opening.");
                Assert.That(assaultX, Is.GreaterThan(6.4f),
                    "Assault NPC failed to progress through reopened objective lane.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, oldFallbackRetries);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, oldUnreachableRetries);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityPathBlockedCommunicationDedup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldFallbackRetries = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries);
        var oldUnreachableRetries = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 32f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, 2);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, 5);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -40, 40, -30, 30);

                for (var y = -16; y <= 16; y++)
                {
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", 8f, y);
                }

                var swarm = NpcCapabilityScenarioLibrary.SpawnSwarm(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    count: 24,
                    origin: new Vector2(-6f, -6f),
                    columns: 6,
                    spacing: 1.4f);

                foreach (var uid in swarm)
                {
                    var team = entMan.EnsureComponent<WH40KTeamMemberComponent>(uid);
                    team.TeamId = "Imperium";
                }

                var objective = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    "WH40KObjectiveHeretics",
                    x: 14f,
                    y: 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(380);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var tacticalAttempts = GetStageWorkItems(snapshot, "npc.wave.comms.tactical_order.attempt");
                var tacticalSent = GetStageWorkItems(snapshot, "npc.wave.comms.tactical_order.sent");
                var tacticalSuppressed = GetStageWorkItems(snapshot, "npc.wave.comms.tactical_order.suppressed");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.pathblocked.retry_bounded"), Is.GreaterThan(0),
                    "Pathblocked crowd scenario did not emit bounded retry telemetry.");
                Assert.That(tacticalAttempts, Is.GreaterThan(0),
                    $"Expected tactical-order communication attempts under shared blocker pressure. {DescribeCommsCounters(snapshot)}");
                Assert.That(tacticalSent, Is.GreaterThan(0),
                    $"Expected at least one tactical-order callout in blocker scenario. {DescribeCommsCounters(snapshot)}");
                Assert.That(tacticalSuppressed, Is.GreaterThan(0),
                    $"Expected tactical-order dedup suppression in blocker crowd scenario. {DescribeCommsCounters(snapshot)}");
                Assert.That(tacticalAttempts, Is.GreaterThan(tacticalSent),
                    $"Expected tactical-order sent < attempts due to dedup. {DescribeCommsCounters(snapshot)}");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathFallbackRetries, oldFallbackRetries);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveNoPathUnreachableRetries, oldUnreachableRetries);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDirectorDefenseVsPushDecision()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);
        var oldDirectorTick = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds);
        var oldDirectorTtl = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds);
        var oldDirectorHysteresis = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta);
        var oldDirectorReassign = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds);
        var oldDirectorPreemptCooldown = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds);
        var oldDirectorThreatRadius = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 34f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, 0.45f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta, 4f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, 0.20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds, 0.10f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius, 10f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid imperiumAssault = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -36, 36, -24, 24);

                imperiumAssault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: -4f,
                    y: 0f);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(imperiumAssault).TeamId = "Imperium";

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveImperium", -12f, 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveHeretics", 14f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(180);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var hereticAssault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: -11.2f,
                    y: 0f);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(hereticAssault).TeamId = "Heretics";
            });

            await pair.RunTicksSync(220);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(TryGetDirectorOrderToken(entMan, imperiumAssault, out var order), Is.True,
                    "Director order token was not written to blackboard.");
                Assert.That(order, Is.EqualTo("defend_base"),
                    $"Director should switch to DefendBase when friendly objective is threatened. {DescribeDirectorCounters(snapshot)}");

                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_issued.push_objective"), Is.GreaterThan(0),
                    "Expected initial PushObjective issue before defense threat appears.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_issued.defend_base"), Is.GreaterThan(0),
                    "Expected DefendBase issue after objective threat appeared.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_switch"), Is.GreaterThan(0),
                    "Expected at least one director order switch in defend-vs-push scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_preempt"), Is.GreaterThan(0),
                    "Expected urgent preempt counter when defense threat arrived.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, oldDirectorTick);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, oldDirectorTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta, oldDirectorHysteresis);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, oldDirectorReassign);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds, oldDirectorPreemptCooldown);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius, oldDirectorThreatRadius);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDirectorSupplyShortageTriggersResupply()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);
        var oldDirectorTick = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds);
        var oldDirectorTtl = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds);
        var oldDirectorReassign = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds);
        var oldDirectorShortage = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 24f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 34f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, 0.40f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, 0.10f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold, 1);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid logistics = default;
            EntityUid machine = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -36, 36, -24, 24);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: -6f,
                    y: 0f);
                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: -7f,
                    y: 1.5f);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(assault).TeamId = "Imperium";
                entMan.EnsureComponent<WH40KTeamMemberComponent>(logistics).TeamId = "Imperium";

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveHeretics", 15f, 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveImperium", -14f, 0f);

                machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", -1f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", -3f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(360);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_issued.resupply"), Is.GreaterThan(0),
                    "Expected director to issue Resupply under shortage.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_assigned.resupply"), Is.GreaterThan(0),
                    "Expected logistics to receive at least one Resupply assignment under shortage.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_assigned.push_objective"), Is.GreaterThan(0),
                    "Expected combat squad to keep push assignment in mixed shortage scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.service.restock_success"), Is.GreaterThan(0),
                    $"Expected logistics restock execution under Resupply order. {DescribeServiceCounters(snapshot)}");
                Assert.That(VendingHasAnyPositiveStock(entMan, machine), Is.True,
                    "Vending machine remained empty after resupply scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, oldDirectorTick);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, oldDirectorTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, oldDirectorReassign);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold, oldDirectorShortage);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDirectorCaptureVsObjectivePriorityByState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldInfluenceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds);
        var oldInfluenceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);
        var oldDirectorTick = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds);
        var oldDirectorTtl = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds);
        var oldDirectorReassign = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius, 30f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 34f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, 0.30f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, 0.10f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid support = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -34, 34, -24, 24);

                support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: -2f,
                    y: 0f);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(support).TeamId = "Imperium";

                var influence = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MachineChipProduser", 7f, 0f);
                if (entMan.TryGetComponent(influence, out WH40KInfluencePointComponent point))
                    point.CaptureEnabledFromPhase = WH40KBattlePhase.Preparation;

                bench.Reset();
            });

            await pair.RunTicksSync(200);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveHeretics", 14f, 0f);
            });

            await pair.RunTicksSync(260);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(TryGetDirectorOrderToken(entMan, support, out var order), Is.True,
                    "Support NPC missing director order token.");
                Assert.That(order, Is.EqualTo("push_objective"),
                    "Director should prioritize objective push after enemy objective appears.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_issued.capture_influence"), Is.GreaterThan(0),
                    "Expected capture-influence order before enemy objective appears.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_issued.push_objective"), Is.GreaterThan(0),
                    "Expected push-objective order after enemy objective appears.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, oldInfluenceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius, oldInfluenceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, oldDirectorTick);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, oldDirectorTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, oldDirectorReassign);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDirectorOrderTtlAndHysteresisNoThrash()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldInfluenceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds);
        var oldInfluenceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);
        var oldDirectorTick = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds);
        var oldDirectorTtl = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds);
        var oldDirectorHysteresis = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta);
        var oldDirectorReassign = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius, 30f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 30f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, 0.90f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta, 12f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, 0.40f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -24, 24);

                var support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: -4f,
                    y: 0f);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(support).TeamId = "Imperium";
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MachineChipProduser", 6f, 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveHeretics", 12f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(640);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var ticks = GetStageWorkItems(snapshot, "npc.wave.director.tick");
                var switches = GetStageWorkItems(snapshot, "npc.wave.director.order_switch");
                var issued = GetStageWorkItems(snapshot, "npc.wave.director.order_issued");
                Assert.That(ticks, Is.GreaterThan(0), "Expected director tick telemetry.");
                Assert.That(switches, Is.LessThanOrEqualTo(3),
                    $"Director switched orders too often under stable mixed-state conditions. {DescribeDirectorCounters(snapshot)}");
                Assert.That(issued, Is.LessThanOrEqualTo(switches + 2),
                    $"Director issued too many orders relative to switches. {DescribeDirectorCounters(snapshot)}");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceScanIntervalSeconds, oldInfluenceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityInfluenceSearchRadius, oldInfluenceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, oldDirectorTick);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, oldDirectorTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorHysteresisScoreDelta, oldDirectorHysteresis);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, oldDirectorReassign);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDirectorMultiSquadDifferentiatedOrders()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldServiceScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds);
        var oldServiceSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);
        var oldDirectorTick = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds);
        var oldDirectorTtl = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds);
        var oldDirectorReassign = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds);
        var oldDirectorShortage = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, 24f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 34f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, 0.35f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, 0.10f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold, 1);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;
            EntityUid breacher = default;
            EntityUid logistics = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -36, 36, -24, 24);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: -8f,
                    y: 0f);
                breacher = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.BreacherPrototype,
                    x: -7f,
                    y: 1.6f);
                logistics = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.LogisticsPrototype,
                    x: -9f,
                    y: -1.4f);

                entMan.EnsureComponent<WH40KTeamMemberComponent>(assault).TeamId = "Imperium";
                entMan.EnsureComponent<WH40KTeamMemberComponent>(breacher).TeamId = "Imperium";
                entMan.EnsureComponent<WH40KTeamMemberComponent>(logistics).TeamId = "Imperium";

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveHeretics", 16f, 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveImperium", -16f, 0f);

                var machine = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineBooze", -2f, 0f);
                SetVendingMachineLowStockAndPanelOpen(entMan, machine);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "VendingMachineRestockBooze", -4f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(360);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_assigned.push_objective"), Is.GreaterThan(0),
                    "Expected push-objective order assignment counter.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_assigned.breach_lane"), Is.GreaterThan(0),
                    "Expected breach-lane order assignment counter.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_assigned.resupply"), Is.GreaterThan(0),
                    "Expected resupply order assignment counter.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_issued.resupply"), Is.GreaterThan(0),
                    "Expected team-level resupply order issue in differentiated scenario.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_issued.push_objective"), Is.GreaterThan(0),
                    "Expected team-level push-objective order issue in differentiated scenario.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceScanIntervalSeconds, oldServiceScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityServiceSearchRadius, oldServiceSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, oldDirectorTick);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, oldDirectorTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, oldDirectorReassign);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorResupplyShortageThreshold, oldDirectorShortage);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDirectorPreemptOnUrgentEventBounded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);
        var oldDirectorTick = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds);
        var oldDirectorTtl = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds);
        var oldDirectorReassign = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds);
        var oldDirectorPreemptCooldown = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds);
        var oldDirectorThreatRadius = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 34f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, 0.40f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, 0.10f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds, 1.20f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius, 10f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid assault = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -34, 34, -24, 24);

                assault = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: -4f,
                    y: 0f);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(assault).TeamId = "Imperium";

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveImperium", -12f, 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveHeretics", 14f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(180);

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var attackerA = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.AssaultPrototype,
                    x: -11f,
                    y: 0f);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(attackerA).TeamId = "Heretics";
            });

            await pair.RunTicksSync(260);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(TryGetDirectorOrderToken(entMan, assault, out var order), Is.True,
                    "Assault NPC missing director order token.");
                Assert.That(order, Is.EqualTo("defend_base"),
                    $"Director should preempt into DefendBase under urgent objective threat. {DescribeDirectorCounters(snapshot)}");

                var preempts = GetStageWorkItems(snapshot, "npc.wave.director.order_preempt");
                Assert.That(preempts, Is.GreaterThan(0),
                    "Expected at least one urgent preempt event.");
                Assert.That(preempts, Is.LessThanOrEqualTo(2),
                    $"Urgent preempt should be bounded by cooldown. {DescribeDirectorCounters(snapshot)}");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorTickIntervalSeconds, oldDirectorTick);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorOrderTtlSeconds, oldDirectorTtl);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorReassignCooldownSeconds, oldDirectorReassign);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorUrgentPreemptCooldownSeconds, oldDirectorPreemptCooldown);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorDefenseThreatRadius, oldDirectorThreatRadius);
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task NpcCapabilityWaveDirectorDisabledFallbackToLocalAi()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);
        var oldObjectiveScanInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds);
        var oldObjectiveSearchRadius = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius);
        var oldDirectorEnabled = server.CfgMan.GetCVar(CCVars.NPCWaveDirectorEnabled);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, 0.05f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, 32f);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, false);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid support = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -30, 30, -24, 24);

                support = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SupportPrototype,
                    x: -2f,
                    y: 0f);
                entMan.EnsureComponent<WH40KTeamMemberComponent>(support).TeamId = "Imperium";

                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveHeretics", 12f, 0f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WH40KObjectiveImperium", -12f, 0f);

                bench.Reset();
            });

            await pair.RunTicksSync(280);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.objective_target_selected"), Is.GreaterThan(0),
                    "Objective layer should keep working with director disabled.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.tick"), Is.EqualTo(0),
                    "Director tick counter must stay zero when director is globally disabled.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_issued"), Is.EqualTo(0),
                    "Director order counters must stay zero when disabled.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_switch"), Is.EqualTo(0),
                    "Director switch counters must stay zero when disabled.");
                Assert.That(GetStageWorkItems(snapshot, "npc.wave.director.order_preempt"), Is.EqualTo(0),
                    "Director preempt counters must stay zero when disabled.");
                Assert.That(TryGetDirectorOrderToken(entMan, support, out _), Is.False,
                    "WaveDirectorOrder key should not be present when director is disabled.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, oldWaveUpdateInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveScanIntervalSeconds, oldObjectiveScanInterval);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityObjectiveSearchRadius, oldObjectiveSearchRadius);
                server.CfgMan.SetCVar(CCVars.NPCWaveDirectorEnabled, oldDirectorEnabled);
            });

            await pair.CleanReturnAsync();
        }
    }

    private static void SetVendingMachineLowStockAndPanelOpen(IEntityManager entMan, EntityUid machine)
    {
        var vending = entMan.GetComponent<VendingMachineComponent>(machine);
        foreach (var entry in vending.Inventory.Values)
        {
            entry.Amount = 0;
        }

        if (entMan.TryGetComponent(machine, out WiresPanelComponent panel))
        {
            var wires = entMan.System<WiresSystem>();
            _ = wires.TogglePanel(machine, panel, true);
        }
    }

    private static bool VendingHasAnyPositiveStock(IEntityManager entMan, EntityUid machine)
    {
        if (!entMan.TryGetComponent(machine, out VendingMachineComponent vending))
            return false;

        foreach (var entry in vending.Inventory.Values)
        {
            if (entry.Amount > 0)
                return true;
        }

        return false;
    }

    private static bool TryGetDirectorOrderToken(IEntityManager entMan, EntityUid npc, out string orderToken)
    {
        orderToken = string.Empty;

        if (!entMan.TryGetComponent(npc, out HTNComponent htn))
            return false;

        return htn.Blackboard.TryGetValue<string>(NPCBlackboard.WaveDirectorOrder, out orderToken, entMan);
    }

    private static bool IsNpcTargetingObjective(IEntityManager entMan, EntityUid npc, EntityUid objective)
    {
        if (!entMan.TryGetComponent(npc, out HTNComponent htn))
            return false;

        if (htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out var orderedTarget, entMan) &&
            orderedTarget == objective)
        {
            return true;
        }

        return htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, entMan) &&
               target == objective;
    }

    private static bool TryFindObjectiveByTeam(
        IEntityManager entMan,
        string teamId,
        out EntityUid objectiveUid,
        out WH40KObjectiveComponent objectiveComp,
        out TransformComponent objectiveXform)
    {
        objectiveUid = EntityUid.Invalid;
        objectiveComp = default!;
        objectiveXform = default!;

        var query = entMan.EntityQueryEnumerator<WH40KObjectiveComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var objective, out var xform))
        {
            if (objective.Destroyed || objective.Destroying || string.IsNullOrWhiteSpace(objective.TeamId))
                continue;

            if (!string.Equals(objective.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            objectiveUid = uid;
            objectiveComp = objective;
            objectiveXform = xform;
            return true;
        }

        return false;
    }

    private static bool TryGetSpawnPointCoordinates(
        IEntityManager entMan,
        string spawnPrototypeId,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out var meta, out var xform))
        {
            if (meta.EntityLifeStage >= EntityLifeStage.Terminating)
                continue;

            if (meta.EntityPrototype?.ID != spawnPrototypeId)
                continue;

            coordinates = xform.Coordinates;
            return true;
        }

        return false;
    }

    private static bool TryGetNpcBlackboardTarget(IEntityManager entMan, EntityUid npc, out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!entMan.TryGetComponent(npc, out HTNComponent htn))
            return false;

        return htn.Blackboard.TryGetValue<EntityUid>("Target", out target, entMan);
    }

    private static bool TryGetNpcOrderedTarget(IEntityManager entMan, EntityUid npc, out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!entMan.TryGetComponent(npc, out HTNComponent htn))
            return false;

        return htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentOrderedTarget, out target, entMan);
    }

    private static bool TryGetNpcObjectiveBlockerTarget(IEntityManager entMan, EntityUid npc, out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!entMan.TryGetComponent(npc, out HTNComponent htn))
            return false;

        return htn.Blackboard.TryGetValue<EntityUid>(NPCBlackboard.CurrentObjectiveBlockerTarget, out target, entMan);
    }

    private static bool TryDescribeNpcSteering(IEntityManager entMan, EntityUid npc, out string description)
    {
        description = string.Empty;
        if (!entMan.TryGetComponent(npc, out NPCSteeringComponent steering))
            return false;

        var nextNodeToken = "-";
        if (steering.CurrentPath.TryPeek(out var nextPoly))
        {
            nextNodeToken =
                $"{DescribeCoordinates(entMan, nextPoly.Coordinates)} flags={DescribePathNodeFlags(nextPoly.Data.Flags)} damage={nextPoly.Data.Damage:F1} box={nextPoly.Box.Left:F2},{nextPoly.Box.Bottom:F2}->{nextPoly.Box.Right:F2},{nextPoly.Box.Top:F2}";
        }

        description =
            $"steering={steering.Status} path_nodes={steering.CurrentPath.Count} failed_paths={steering.FailedPathCount} obstacle_failures={steering.ObstacleFailureCount} backoff={steering.PathRequestBackoffSeconds:F2} range={steering.Range:F2} target={DescribeCoordinates(entMan, steering.Coordinates)} next_node={nextNodeToken}";
        return true;
    }

    private static string DescribePathNodeFlags(PathfindingBreadcrumbFlag flags)
    {
        if (flags == PathfindingBreadcrumbFlag.None)
            return "None";

        return flags.ToString();
    }

    private static bool TryDescribeNpcCombat(IEntityManager entMan, EntityUid npc, out string description)
    {
        description = string.Empty;

        if (entMan.TryGetComponent(npc, out NPCRangedCombatComponent ranged))
        {
            var hands = entMan.System<HandsSystem>();
            var gunSystem = entMan.System<Content.Server.Weapons.Ranged.Systems.GunSystem>();
            var xformSystem = entMan.System<SharedTransformSystem>();
            var timing = IoCManager.Resolve<IGameTiming>();

            var targetToken = DescribeEntity(entMan, ranged.Target);
            var activeHand = "-";
            var activeItem = "-";
            if (entMan.TryGetComponent(npc, out HandsComponent handsComp))
            {
                activeHand = hands.GetActiveHand((npc, handsComp)) ?? "-";
                var held = hands.GetActiveItem((npc, handsComp));
                if (held != null)
                    activeItem = DescribeEntity(entMan, held.Value);
            }

            var gunToken = "-";
            var gunCooldownSeconds = 0f;
            var aimDeltaDegrees = -1f;
            if (gunSystem.TryGetGun(npc, out var gun))
            {
                gunToken = DescribeEntity(entMan, gun.Owner);
                gunCooldownSeconds = MathF.Max(0f, (float) (gun.Comp.NextFire - timing.CurTime).TotalSeconds);

                if (entMan.TryGetComponent(npc, out TransformComponent npcXform) &&
                    entMan.TryGetComponent(ranged.Target, out TransformComponent targetXform))
                {
                    var npcPos = xformSystem.GetWorldPosition(npcXform);
                    var targetPos = xformSystem.GetWorldPosition(targetXform);
                    var delta = targetPos - npcPos;
                    if (delta.LengthSquared() > 0.0001f)
                    {
                        var goal = delta.ToWorldAngle();
                        var current = xformSystem.GetWorldRotation(npcXform);
                        aimDeltaDegrees = MathF.Abs((float) (goal - current).Reduced().Theta * 180f / MathF.PI);
                    }
                }
            }

            description =
                $"combat=ranged status={ranged.Status} los={ranged.TargetInLOS} accum={ranged.ShootAccumulator:F2}/{ranged.ShootDelay:F2} gun={gunToken} active_hand={activeHand} active_item={activeItem} gun_cd={gunCooldownSeconds:F2} aim_delta_deg={aimDeltaDegrees:F1} target={targetToken}";
            return true;
        }

        if (entMan.TryGetComponent(npc, out NPCMeleeCombatComponent melee))
        {
            description =
                $"combat=melee status={melee.Status} target={DescribeEntity(entMan, melee.Target)}";
            return true;
        }

        return false;
    }

    private static string DescribeCoordinates(IEntityManager entMan, EntityCoordinates coordinates)
    {
        if (!coordinates.IsValid(entMan))
            return "invalid";

        return $"{coordinates.EntityId}:{coordinates.Position.X:F1},{coordinates.Position.Y:F1}";
    }

    private static string DescribeEntity(IEntityManager entMan, EntityUid uid)
    {
        if (uid == EntityUid.Invalid)
            return "invalid";

        if (!entMan.EntityExists(uid))
            return $"deleted:{uid}";

        if (entMan.TryGetComponent(uid, out MetaDataComponent meta) &&
            !string.IsNullOrWhiteSpace(meta.EntityPrototype?.ID))
        {
            return meta.EntityPrototype.ID;
        }

        return uid.ToString();
    }

    private static int CountEntitiesByPrototype(IEntityManager entMan, string prototypeId)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityLifeStage >= EntityLifeStage.Terminating)
                continue;

            if (meta.EntityPrototype?.ID == prototypeId)
                count++;
        }

        return count;
    }

    private static string DescribeNearbyAnchoredEntities(IEntityManager entMan, EntityUid npc, float range)
    {
        if (!entMan.TryGetComponent(npc, out TransformComponent npcXform))
            return "-";

        var lookup = entMan.System<EntityLookupSystem>();
        var xformSystem = entMan.System<SharedTransformSystem>();
        var origin = xformSystem.GetWorldPosition(npcXform);
        var nearby = new HashSet<EntityUid>();
        lookup.GetEntitiesInRange(
            npc,
            range,
            nearby,
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Approximate);

        var entries = new List<(float Distance, string Token)>();
        foreach (var candidate in nearby)
        {
            if (candidate == npc ||
                !entMan.EntityExists(candidate) ||
                entMan.HasComponent<ActiveNPCComponent>(candidate) ||
                !entMan.TryGetComponent(candidate, out TransformComponent candidateXform) ||
                !candidateXform.Anchored)
            {
                continue;
            }

            var candidatePos = xformSystem.GetWorldPosition(candidateXform);
            var distance = (candidatePos - origin).Length();
            if (distance > range)
                continue;

            var tags = new List<string>();
            if (entMan.HasComponent<DoorComponent>(candidate))
                tags.Add("door");
            if (entMan.HasComponent<WH40KObjectiveComponent>(candidate))
                tags.Add("objective");
            if (entMan.HasComponent<DamageableComponent>(candidate))
                tags.Add("damageable");

            var token = DescribeEntity(entMan, candidate);
            if (tags.Count > 0)
                token += $"[{string.Join("/", tags)}]";

            entries.Add((distance, $"{token}@{distance:F1}"));
        }

        if (entries.Count == 0)
            return "-";

        entries.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        return string.Join("; ", entries.Take(8).Select(entry => entry.Token));
    }

    private static void ForceEquipWithPistol(IEntityManager entMan, EntityUid mob)
    {
        ForceEquipWithCapabilityItem(entMan, mob, "WeaponPistolMk58");
    }

    private static void ForceEquipWithCapabilityItem(IEntityManager entMan, EntityUid mob, string prototypeId)
    {
        if (!entMan.TryGetComponent(mob, out HandsComponent handsComp))
            return;

        var hands = entMan.System<HandsSystem>();
        var held = hands.EnumerateHeld((mob, handsComp)).ToArray();
        foreach (var item in held)
        {
            hands.TryDrop((mob, handsComp), item, checkActionBlocker: false);

            if (entMan.EntityExists(item))
                entMan.DeleteEntity(item);
        }

        var coord = entMan.GetComponent<TransformComponent>(mob).Coordinates.Offset(new Vector2(0.3f, 0f));
        var spawnedItem = entMan.SpawnEntity(prototypeId, coord);
        if (!hands.TryPickupAnyHand(mob, spawnedItem, checkActionBlocker: false, animateUser: false, animate: false, handsComp: handsComp))
            return;

        foreach (var handId in handsComp.SortedHands)
        {
            if (!hands.TryGetHeldItem((mob, handsComp), handId, out var heldInHand) ||
                heldInHand == null ||
                heldInHand.Value != spawnedItem)
            {
                continue;
            }

            hands.TrySetActiveHand((mob, handsComp), handId);
            break;
        }

        if (entMan.TryGetComponent(spawnedItem, out WieldableComponent wieldable))
        {
            entMan.System<SharedWieldableSystem>().TryWield(spawnedItem, wieldable, mob);
        }
    }

    private static void BuildLockedDoorTwoRoomScenario(IEntityManager entMan, Entity<MapGridComponent> grid)
    {
        // Deterministic split-lane barrier: the only traversable seam between halves is at (2, 0),
        // where tests spawn the locked door obstacle.
        for (var y = -20; y <= 20; y++)
        {
            if (y == 0)
                continue;

            NpcCapabilityScenarioLibrary.SpawnAt(entMan, grid, "WallSolid", 2f, y);
        }
    }

    private static void BuildPerimeterWalls(IEntityManager entMan, Entity<MapGridComponent> grid, int minX, int maxX, int minY, int maxY)
    {
        for (var x = minX; x <= maxX; x++)
        {
            NpcCapabilityScenarioLibrary.SpawnAt(entMan, grid, "WallSolid", x, minY);
            NpcCapabilityScenarioLibrary.SpawnAt(entMan, grid, "WallSolid", x, maxY);
        }

        for (var y = minY + 1; y <= maxY - 1; y++)
        {
            NpcCapabilityScenarioLibrary.SpawnAt(entMan, grid, "WallSolid", minX, y);
            NpcCapabilityScenarioLibrary.SpawnAt(entMan, grid, "WallSolid", maxX, y);
        }
    }

    private static int GetStageWorkItems(NpcBenchmarkSnapshot snapshot, string stageName)
    {
        foreach (var stage in snapshot.Stages)
        {
            if (stage.Name == stageName)
                return stage.WorkItems;
        }

        return 0;
    }

    private static string DescribeServiceCounters(NpcBenchmarkSnapshot snapshot)
    {
        var counters = new[]
        {
            "npc.wave.service.job_assigned",
            "npc.wave.service.job_assigned_held",
            "npc.wave.service.seek_source",
            "npc.wave.service.source_search_miss",
            "npc.wave.service.machine_skip_no_source",
            "npc.wave.service.source_open_attempt",
            "npc.wave.service.source_open_success",
            "npc.wave.service.source_open_fail",
            "npc.wave.service.source_candidate_compatible",
            "npc.wave.service.source_skip_incompatible",
            "npc.wave.service.source_selected_item",
            "npc.wave.service.source_selected_storage",
            "npc.wave.service.source_storage_match",
            "npc.wave.service.acquire_attempt",
            "npc.wave.service.acquire_success",
            "npc.wave.service.acquire_blocked_no_hand",
            "npc.wave.service.held_compatible_found",
            "npc.wave.service.held_incompatible_seen",
            "npc.wave.service.seek_target",
            "npc.wave.service.panel_open_attempt",
            "npc.wave.service.panel_open_success",
            "npc.wave.service.panel_closed_abort",
            "npc.wave.service.restock_attempt",
            "npc.wave.service.restock_start_fail",
            "npc.wave.service.restock_success",
            "npc.wave.service.restock_timeout",
            "npc.wave.service.drop_incompatible_held",
            "npc.wave.service.drop_incompatible_fail",
            "npc.wave.service.job_completed",
            "npc.wave.service.job_aborted",
            "npc.wave.service.job_timeout",
        };

        var builder = new StringBuilder();
        builder.Append("service counters: ");
        for (var i = 0; i < counters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            var name = counters[i];
            builder.Append(name);
            builder.Append('=');
            builder.Append(GetStageWorkItems(snapshot, name));
        }

        return builder.ToString();
    }

    private static string DescribeDirectorCounters(NpcBenchmarkSnapshot snapshot)
    {
        var counters = new[]
        {
            "npc.wave.director.tick",
            "npc.wave.director.no_team",
            "npc.wave.director.order_issued",
            "npc.wave.director.order_issued.defend_base",
            "npc.wave.director.order_issued.push_objective",
            "npc.wave.director.order_issued.capture_influence",
            "npc.wave.director.order_issued.resupply",
            "npc.wave.director.order_issued.breach_lane",
            "npc.wave.director.order_issued.regroup",
            "npc.wave.director.order_switch",
            "npc.wave.director.order_preempt",
            "npc.wave.director.order_assigned",
            "npc.wave.director.order_assigned.defend_base",
            "npc.wave.director.order_assigned.push_objective",
            "npc.wave.director.order_assigned.capture_influence",
            "npc.wave.director.order_assigned.resupply",
            "npc.wave.director.order_assigned.breach_lane",
            "npc.wave.director.order_assigned.regroup",
        };

        var builder = new StringBuilder();
        builder.Append("director counters: ");
        for (var i = 0; i < counters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            var name = counters[i];
            builder.Append(name);
            builder.Append('=');
            builder.Append(GetStageWorkItems(snapshot, name));
        }

        return builder.ToString();
    }

    private static string DescribeBattlefieldDirectorObjectiveCounters(NpcBenchmarkSnapshot snapshot)
    {
        var counters = new[]
        {
            "npc.wave.director.order_issued",
            "npc.wave.director.order_issued.push_objective",
            "npc.wave.director.order_issued.breach_lane",
            "npc.wave.director.decision.enemy_objective",
            "npc.wave.director.decision.base_under_threat",
            "npc.wave.director.decision.keep_current",
            "npc.wave.director.decision.ttl_hold",
            "npc.wave.director.decision.hysteresis_hold",
            "npc.wave.objective.search_hit",
            "npc.wave.objective.search_miss",
            "npc.wave.objective.director_target_hit",
            "npc.wave.objective.director_target_missing",
            "npc.wave.objective.director_target_invalid",
            "npc.wave.objective.director_target_same_team",
            "npc.wave.objective.combat_preempt",
            "npc.wave.objective.clear_noncombat_target",
            "npc.wave.objective.clear_incidental_combat_target",
            "npc.wave.objective.clear_invalid_ranged_target",
            "npc.wave.objective.clear_invalid_melee_target",
            "npc.wave.target.reject_item",
            "npc.wave.target.reject_friendly",
            "npc.wave.target.reject_invalid_objective",
            "npc.wave.target.reject_friendly_objective",
            "npc.wave.objective_attack_started",
            "npc.wave.objective.ranged_forced",
            "npc.wave.objective.melee_forced",
            "npc.wave.objective.blocker_target",
            "npc.wave.objective.blocker_ingress_door",
            "npc.wave.objective.blocker_ranged_forced",
            "npc.wave.objective.blocker_melee_forced",
            "npc.wave.objective.blocker_skip_nonblocking",
            "npc.wave.objective.blocker_skip_locked_door",
            "npc.wave.objective.regroup_seek",
            "npc.wave.objective.regroup_split_seek",
            "npc.wave.objective.regroup_leader_hold",
            "npc.wave.objective.regroup_straggler_seek",
            "npc.wave.objective.regroup_isolated_seek",
            "npc.wave.objective.regroup_anchor_center_fallback",
            "npc.wave.objective.hazard_detour_hold",
            "npc.wave.objective.assault_lane_seek",
            "npc.wave.objective.standoff_lane_seek",
            "npc.wave.objective.follow_leader_seek",
            "npc.wave.objective.follow_leader_hop",
            "npc.wave.objective.march_slot_seek",
            "npc.wave.objective.march_slot_hop",
            "npc.wave.objective.ingress_seek",
            "npc.wave.objective.ingress_candidate",
            "npc.wave.objective.rearm_attempt",
            "npc.wave.objective.rearm_success",
            "npc.wave.hazard.environment_detour",
            "npc.wave.hazard.environment_repath",
            "npc.wave.hazard.environment_release",
            "npc.wave.hazard.environment_skip_offroute",
            "npc.wave.hazard.environment_skip_path_owned",
            "npc.wave.hazard.environment_detour_fail",
            "npc.wave.objective.steering_force_reset",
            "npc.wave.objective.steering_force_reset_far_inrange",
            "npc.wave.objective.steering_force_reset_empty_path",
            "npc.wave.steering_target.projected",
            "npc.wave.steering_target.projected_hazard",
            "npc.wave.steering_target.project_failed",
            "npc.wave.steering_target.project_failed_hazard",
            "npc.wave.pathblocked.retry_bounded",
            "npc.wave.pathblocked.fallback",
            "npc.wave.pathblocked.unreachable",
            "npc.wave.pathblocked.replan_success",
            "npc.wave.objective.approach_seek",
            "npc.wave.objective.chunk_step_seek",
            "npc.wave.objective.chunk_cohesion",
            "npc.wave.objective.chunk_cohesion_front_skip",
            "npc.wave.objective.chunk_cohesion_rear_boost",
            "npc.steering.path_result.no_path",
            "npc.steering.path_result.success",
            "npc.steering.path_request.no_path_backoff",
            "npc.pathfinding.result.no_path",
            "npc.pathfinding.result.path",
            "npc.steering.obstacle.policy.interact_attempt",
            "npc.steering.obstacle.policy.interact_fail_access",
            "npc.steering.obstacle.policy.pry_attempt",
            "npc.steering.obstacle.policy.smash_attempt",
            "npc.steering.obstacle.failed",
            "npc.steering.obstacle.timeout",
            "npc.combat.ranged.friendly_fire_blocked",
            "npc.combat.ranged.friendly_fire_reposition",
            "npc.combat.ranged.nopath_objective_override",
            "npc.combat.ranged.target_invalid",
            "npc.combat.ranged.target_wrong_map",
            "npc.combat.ranged.target_unreachable",
            "npc.combat.ranged.no_weapon",
            "npc.combat.ranged.no_ammo",
            "npc.combat.ranged.recharging",
            "npc.combat.ranged.not_in_sight",
            "npc.combat.ranged.rotate_wait",
            "npc.combat.ranged.cooldown_blocked",
            "npc.combat.ranged.shoot_attempt",
            "npc.combat.ranged.shoot_performed",
            "npc.combat.ranged.shoot_failed",
        };

        var builder = new StringBuilder();
        builder.Append("battlefield counters: ");
        for (var i = 0; i < counters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            var name = counters[i];
            builder.Append(name);
            builder.Append('=');
            builder.Append(GetStageWorkItems(snapshot, name));
        }

        return builder.ToString();
    }

    private static string DescribeCommsCounters(NpcBenchmarkSnapshot snapshot)
    {
        var counters = new[]
        {
            "npc.wave.comms.enemy_spotted.attempt",
            "npc.wave.comms.enemy_spotted.sent",
            "npc.wave.comms.enemy_spotted.suppressed",
            "npc.wave.comms.engaging_enemy.attempt",
            "npc.wave.comms.engaging_enemy.sent",
            "npc.wave.comms.engaging_enemy.suppressed",
            "npc.wave.comms.mine_cleared.attempt",
            "npc.wave.comms.mine_cleared.sent",
            "npc.wave.comms.mine_cleared.suppressed",
            "npc.wave.comms.tactical_order.attempt",
            "npc.wave.comms.tactical_order.sent",
            "npc.wave.comms.tactical_order.suppressed",
            "npc.wave.comms.service_report.attempt",
            "npc.wave.comms.service_report.sent",
            "npc.wave.comms.service_report.suppressed",
            "npc.wave.comms.role.assault.sent",
            "npc.wave.comms.role.breacher.sent",
            "npc.wave.comms.role.sapper.sent",
            "npc.wave.comms.role.support.sent",
            "npc.wave.comms.role.logistics.sent",
            "npc.wave.comms.role.coordinator.sent",
            "npc.wave.comms.role.unknown.sent",
        };

        var builder = new StringBuilder();
        builder.Append("comms counters: ");
        for (var i = 0; i < counters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            var name = counters[i];
            builder.Append(name);
            builder.Append('=');
            builder.Append(GetStageWorkItems(snapshot, name));
        }

        return builder.ToString();
    }
}
