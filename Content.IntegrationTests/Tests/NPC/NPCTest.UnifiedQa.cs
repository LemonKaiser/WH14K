using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.NPC;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.NPC;

public sealed partial class NPCTest
{
    private const string MultiActionCompatPrototype = "MobGoliathMultiActionCompatTest";
    private const string LegacyActionCompatPrototype = "MobGoliathLegacyActionCompatTest";

    [TestPrototypes]
    private const string UnifiedQaActionCompatPrototypes = @"
- type: entity
  id: MobGoliathMultiActionCompatTest
  parent: MobGoliath
  components:
  - type: NPCUseActionOnTarget
    actions:
    - actionId: ActionGoliathTentacle
      targetKey: Target
    - actionId: ActionGoliathTentacle
      targetKey: Target

- type: entity
  id: MobGoliathLegacyActionCompatTest
  parent: MobGoliath
  components:
  - type: NPCUseActionOnTarget
    actions: []
    actionId: ActionGoliathTentacle
    targetKey: Target
";

    [Test]
    [Explicit("Focused QA matrix: steering/pathing branches for unified NPC AI update.")]
    public async Task NpcUnifiedQaSteeringPathingDoorClimbSmash()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldWaveUpdateInterval = server.CfgMan.GetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCWaveCapabilityUpdateIntervalSeconds, 0.05f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid doorAgent = default;
            EntityUid sapper = default;
            EntityUid breacher = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var steering = entMan.System<NPCSteeringSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -40, 40, -24, 24);

                for (var x = -40; x <= 40; x++)
                {
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", x, 4f);
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", x, -4f);
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", x, 21f);
                    _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "WallSolid", x, -21f);
                }

                BuildVerticalBarrierWithSingleSeam(entMan, pair.TestMap.Grid, x: 2, minY: 5, maxY: 20, seamY: 8);
                BuildVerticalBarrierWithSingleSeam(entMan, pair.TestMap.Grid, x: 2, minY: -3, maxY: 3, seamY: 0);
                BuildVerticalBarrierWithSingleSeam(entMan, pair.TestMap.Grid, x: 2, minY: -20, maxY: -5, seamY: -8);

                var doorInteract = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "AirlockMaintLocked", 2f, 8f);
                var doorSmash = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "AirlockMaintLocked", 2f, -8f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "Table", 2f, 0f);

                if (entMan.TryGetComponent(doorInteract, out DoorComponent interactDoorComp))
                {
                    interactDoorComp.BumpOpen = false;
                    interactDoorComp.ClickOpen = false;
                }

                if (entMan.TryGetComponent(doorSmash, out DoorComponent smashDoorComp))
                {
                    smashDoorComp.BumpOpen = false;
                    smashDoorComp.ClickOpen = false;
                }

                doorAgent = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.BreacherPrototype,
                    x: 1.2f,
                    y: 8f);
                sapper = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.SapperPrototype,
                    x: 1.2f,
                    y: 0f);
                breacher = NpcCapabilityScenarioLibrary.SpawnAt(
                    entMan,
                    pair.TestMap.Grid,
                    NpcCapabilityScenarioLibrary.BreacherPrototype,
                    x: 1.2f,
                    y: -8f);

                var doorSteering = steering.Register(doorAgent, new EntityCoordinates(pair.TestMap.Grid.Owner, 6f, 8f));
                doorSteering.Flags = PathFlags.Interact | PathFlags.Prying;

                var sapperSteering = steering.Register(sapper, new EntityCoordinates(pair.TestMap.Grid.Owner, 6f, 0f));
                sapperSteering.Flags |= PathFlags.Climbing;

                var breacherSteering = steering.Register(breacher, new EntityCoordinates(pair.TestMap.Grid.Owner, 6f, -8f));
                breacherSteering.Flags &= ~PathFlags.Interact;
                breacherSteering.Flags &= ~PathFlags.Prying;
                breacherSteering.Flags |= PathFlags.Smashing;

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
                var interactAttempts = GetStageWorkItems(snapshot, "npc.steering.obstacle.policy.interact_attempt");
                var pryAttempts = GetStageWorkItems(snapshot, "npc.steering.obstacle.policy.pry_attempt");

                var doorAgentX = entMan.GetComponent<TransformComponent>(doorAgent).Coordinates.Position.X;
                var sapperX = entMan.GetComponent<TransformComponent>(sapper).Coordinates.Position.X;
                var breacherX = entMan.GetComponent<TransformComponent>(breacher).Coordinates.Position.X;
                var climbAttempts = GetStageWorkItems(snapshot, "npc.steering.obstacle.policy.climb_attempt");
                var smashAttempts = GetStageWorkItems(snapshot, "npc.steering.obstacle.policy.smash_attempt");
                var obstacleProgress = GetStageWorkItems(snapshot, "npc.steering.obstacle.progress");
                var noPathBackoff = GetStageWorkItems(snapshot, "npc.steering.path_request.no_path_backoff");
                var policyAttempts = interactAttempts + pryAttempts + climbAttempts + smashAttempts;

                Assert.That(policyAttempts > 0 || obstacleProgress > 0 || noPathBackoff > 0, Is.True,
                    $"Steering obstacle matrix produced no obstacle handling signal. policy_attempts={policyAttempts}, obstacle_progress={obstacleProgress}, no_path_backoff={noPathBackoff}.");
                Assert.That(interactAttempts + pryAttempts > 0 || doorAgentX > 1.2f, Is.True,
                    $"Door handling branch did not produce interaction telemetry and did not reach chokepoint. interact={interactAttempts}, pry={pryAttempts}, x={doorAgentX:F2}");
                Assert.That(climbAttempts > 0 || sapperX > 1.2f, Is.True,
                    $"Climb branch did not produce telemetry and did not reach chokepoint. climb={climbAttempts}, x={sapperX:F2}");
                Assert.That(smashAttempts > 0 || breacherX > 1.2f, Is.True,
                    $"Smash branch did not produce telemetry and did not reach chokepoint. smash={smashAttempts}, x={breacherX:F2}");
                Assert.That(doorAgentX, Is.GreaterThan(0.3f), "Door-handling lane made no forward progress.");
                Assert.That(sapperX, Is.GreaterThan(0.3f), "Climb lane made no forward progress.");
                Assert.That(breacherX, Is.GreaterThan(0.3f), "Smash lane made no forward progress.");
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
            });

            await pair.CleanReturnAsync();
        }
    }

    [Test]
    [Explicit("Focused QA matrix: off-grid direct-move and no-grav braking for unified NPC AI update.")]
    public async Task NpcUnifiedQaOffGridDirectMoveAndNoGravBraking()
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

            EntityUid manualRunner = default;
            EntityCoordinates manualTargetCoords = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var steering = entMan.System<NPCSteeringSystem>();
                var physics = entMan.System<SharedPhysicsSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                manualRunner = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 0f, 0f);
                entMan.EnsureComponent<ActiveNPCComponent>(manualRunner);
                manualTargetCoords = new EntityCoordinates(pair.TestMap.Grid.Owner, 12f, 0f);
                var manualSteering = steering.Register(manualRunner, manualTargetCoords);
                manualSteering.DirectMove = false;
                manualSteering.Range = 0.35f;
                manualSteering.InRangeMaxSpeed = 0.08f;

                if (entMan.TryGetComponent(manualRunner, out PhysicsComponent manualBody))
                    physics.SetLinearVelocity(manualRunner, new Vector2(4f, 0f), body: manualBody);

                bench.Reset();
            });

            await pair.RunTicksSync(420);

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                Assert.That(entMan.EntityExists(manualRunner), Is.True, "Braking runner was deleted unexpectedly.");

                var runnerXform = entMan.GetComponent<TransformComponent>(manualRunner);
                var runnerBody = entMan.GetComponent<PhysicsComponent>(manualRunner);
                var distanceValid = runnerXform.Coordinates.TryDistance(entMan, manualTargetCoords, out var distance);

                Assert.That(distanceValid, Is.True, "Failed to compute braking runner distance to target.");
                Assert.That(distance, Is.LessThanOrEqualTo(0.9f),
                    $"Braking runner did not settle near destination. distance={distance:F3}");
                Assert.That(runnerBody.LinearVelocity.Length(), Is.LessThanOrEqualTo(0.30f),
                    $"Braking runner speed did not settle within expected envelope. speed={runnerBody.LinearVelocity.Length():F3}");
            });

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                if (entMan.EntityExists(manualRunner))
                    entMan.DeleteEntity(manualRunner);

                var offGridPirate = entMan.SpawnEntity("MobSpirate", new MapCoordinates(new Vector2(0f, -8f), pair.TestMap.MapId));
                var offGridTarget = entMan.SpawnEntity("MobCivilian", new MapCoordinates(new Vector2(10f, -8f), pair.TestMap.MapId));
                _ = ForceEquipWithItem(entMan, offGridPirate, "WeaponPistolMk58");
                SetNpcTargetBlackboard(entMan, offGridPirate, offGridTarget);

                var bench = entMan.System<NPCBenchmarkSystem>();
                bench.Reset();
            });

            await pair.RunTicksSync(320);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var pathRequests = GetStageWorkItems(snapshot, "npc.steering.path_request.submitted");
                Assert.That(pathRequests, Is.LessThanOrEqualTo(8),
                    $"Off-grid direct-move scenario produced too many path requests. submitted={pathRequests}.");
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
    [Explicit("Focused QA matrix: combat-readiness branches (rack/wield/activate) for unified NPC AI update.")]
    public async Task NpcUnifiedQaCombatReadinessRackWieldActivate()
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

            EntityUid pirate = default;
            EntityUid rifle = default;
            EntityUid salvager = default;
            EntityUid energySword = default;
            var observedWield = false;
            var observedRackClose = false;
            var observedActivate = false;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();
                var gunSystem = entMan.System<GunSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -24, 24, -24, 24);

                pirate = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobSpirate", 0f, 4f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 10f, 4f);
                var rifleEnt = ForceEquipWithItem(entMan, pirate, "WeaponRifleAk");
                Assert.That(rifleEnt.HasValue, Is.True, "Failed to equip rifle for rack/wield readiness branch test.");
                rifle = rifleEnt!.Value;

                if (entMan.TryGetComponent(rifle, out ChamberMagazineAmmoProviderComponent chamber))
                    gunSystem.SetBoltClosed(rifle, chamber, false, user: null);

                salvager = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobSalvager", 0f, -4f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 6f, -4f);
                var swordEnt = ForceEquipWithItem(entMan, salvager, "EnergySword");
                Assert.That(swordEnt.HasValue, Is.True, "Failed to equip toggle melee weapon for activate readiness branch test.");
                energySword = swordEnt!.Value;

                if (entMan.TryGetComponent(energySword, out ItemToggleComponent toggle))
                    toggle.Activated = false;

                // Deterministic ranged-fire anchor so readiness validation does not depend on one specific weapon path.
                var rangedAnchor = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobSpirate", -10f, 10f);
                _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", -4f, 10f);
                _ = ForceEquipWithItem(entMan, rangedAnchor, "WeaponPistolMk58");

                bench.Reset();
            });

            for (var i = 0; i < 100; i++)
            {
                await pair.RunTicksSync(5);
                await server.WaitPost(() =>
                {
                    var entMan = server.ResolveDependency<IEntityManager>();

                    if (entMan.TryGetComponent(rifle, out WieldableComponent wieldable) && wieldable.Wielded)
                        observedWield = true;

                    if (entMan.TryGetComponent(rifle, out ChamberMagazineAmmoProviderComponent chamber) && chamber.BoltClosed == true)
                        observedRackClose = true;

                    if (entMan.TryGetComponent(energySword, out ItemToggleComponent toggle) && toggle.Activated)
                        observedActivate = true;
                });
            }

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(observedWield, Is.True, "Did not observe weapon wield branch completion.");
                Assert.That(observedRackClose, Is.True, "Did not observe bolt rack/close branch completion.");
                Assert.That(observedActivate, Is.True, "Did not observe toggle weapon activation branch completion.");

                Assert.That(GetStageWorkItems(snapshot, "npc.combat.ranged.shoot_performed"), Is.GreaterThan(0),
                    "Ballistic readiness scenario produced no ranged shots.");
                Assert.That(GetStageWorkItems(snapshot, "npc.combat.melee.attack_performed"), Is.GreaterThan(0),
                    "Toggle melee readiness scenario produced no melee attacks.");
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
    [Explicit("Focused QA matrix: action-refactor compatibility (multi-action + legacy single-action NPCs).")]
    public async Task NpcUnifiedQaActionRefactorMultiActionAndLegacyCompatibility()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var oldBenchEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
        var oldBenchDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
        var oldBenchInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);
        var oldActionInterval = server.CfgMan.GetCVar(CCVars.NPCActionOnTargetIntervalSeconds);
        var oldActionIdleInterval = server.CfgMan.GetCVar(CCVars.NPCActionOnTargetIdleIntervalSeconds);
        var oldActionJitter = server.CfgMan.GetCVar(CCVars.NPCActionOnTargetJitterSeconds);

        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, true);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, 300f);
                server.CfgMan.SetCVar(CCVars.NPCActionOnTargetIntervalSeconds, 60f);
                server.CfgMan.SetCVar(CCVars.NPCActionOnTargetIdleIntervalSeconds, 60f);
                server.CfgMan.SetCVar(CCVars.NPCActionOnTargetJitterSeconds, 0f);
            });

            await server.WaitIdleAsync();
            await pair.CreateTestMap();
            await pair.RunTicksSync(5);

            EntityUid multiA = default;
            EntityUid multiB = default;
            EntityUid legacy = default;

            await server.WaitPost(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();
                var mapSystem = entMan.System<SharedMapSystem>();
                var bench = entMan.System<NPCBenchmarkSystem>();

                FillFloorRect(mapSystem, pair.TestMap.Grid, -20, 20, -20, 20);

                multiA = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, MultiActionCompatPrototype, -2f, 0f);
                multiB = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, MultiActionCompatPrototype, -2f, 3f);
                legacy = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, LegacyActionCompatPrototype, -2f, -3f);

                var targetA = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 3f, 0f);
                var targetB = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 3f, 3f);
                var targetC = NpcCapabilityScenarioLibrary.SpawnAt(entMan, pair.TestMap.Grid, "MobCivilian", 3f, -3f);

                SetNpcTargetBlackboard(entMan, multiA, targetA);
                SetNpcTargetBlackboard(entMan, multiB, targetB);
                SetNpcTargetBlackboard(entMan, legacy, targetC);

                bench.Reset();
            });

            await pair.RunTicksSync(20);

            NpcBenchmarkSnapshot snapshot = default;
            await server.WaitPost(() =>
            {
                var bench = server.System<NPCBenchmarkSystem>();
                snapshot = bench.SnapshotAndReset();
            });

            await server.WaitAssertion(() =>
            {
                var entMan = server.ResolveDependency<IEntityManager>();

                var multiCompA = entMan.GetComponent<NPCUseActionOnTargetComponent>(multiA);
                var multiCompB = entMan.GetComponent<NPCUseActionOnTargetComponent>(multiB);
                var legacyComp = entMan.GetComponent<NPCUseActionOnTargetComponent>(legacy);

                Assert.That(multiCompA.Actions.Count, Is.EqualTo(2),
                    "Multi-action compatibility prototype should keep two configured actions.");
                Assert.That(multiCompB.Actions.Count, Is.EqualTo(2),
                    "Multi-action compatibility prototype should keep two configured actions.");
                Assert.That(multiCompA.Actions.All(a => a.ActionEnt != null), Is.True,
                    "Multi-action prototype A did not bind action entities after startup.");
                Assert.That(multiCompB.Actions.All(a => a.ActionEnt != null), Is.True,
                    "Multi-action prototype B did not bind action entities after startup.");

                Assert.That(legacyComp.Actions.Count, Is.EqualTo(1),
                    "Legacy single-action compatibility prototype did not migrate actionId into action list.");
                Assert.That(legacyComp.Actions[0].ActionEnt, Is.Not.Null,
                    "Legacy single-action compatibility prototype did not bind migrated action entity.");

                var attempts = GetStageWorkItems(snapshot, "npc.action_on_target.attempts");
                var success = GetStageWorkItems(snapshot, "npc.action_on_target.success");
                Assert.That(attempts, Is.GreaterThanOrEqualTo(3),
                    $"Expected at least one action attempt per NPC in action-refactor matrix. attempts={attempts}, success={success}.");
                Assert.That(success, Is.GreaterThanOrEqualTo(3),
                    $"Expected multi-NPC same-window action success after refactor hardening. attempts={attempts}, success={success}.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldBenchEnabled);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldBenchDetailed);
                server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldBenchInterval);
                server.CfgMan.SetCVar(CCVars.NPCActionOnTargetIntervalSeconds, oldActionInterval);
                server.CfgMan.SetCVar(CCVars.NPCActionOnTargetIdleIntervalSeconds, oldActionIdleInterval);
                server.CfgMan.SetCVar(CCVars.NPCActionOnTargetJitterSeconds, oldActionJitter);
            });

            await pair.CleanReturnAsync();
        }
    }

    private static void BuildVerticalBarrierWithSingleSeam(
        IEntityManager entMan,
        Entity<MapGridComponent> grid,
        int x,
        int minY,
        int maxY,
        int seamY)
    {
        for (var y = minY; y <= maxY; y++)
        {
            if (y == seamY)
                continue;

            _ = NpcCapabilityScenarioLibrary.SpawnAt(entMan, grid, "WallSolid", x, y);
        }
    }

    private static EntityUid? ForceEquipWithItem(IEntityManager entMan, EntityUid mob, string itemPrototype)
    {
        if (!entMan.TryGetComponent(mob, out HandsComponent handsComp))
            return null;

        var hands = entMan.System<HandsSystem>();
        var held = hands.EnumerateHeld((mob, handsComp)).ToArray();
        foreach (var item in held)
        {
            hands.TryDrop((mob, handsComp), item, checkActionBlocker: false);
            if (entMan.EntityExists(item))
                entMan.DeleteEntity(item);
        }

        var spawnCoords = entMan.GetComponent<TransformComponent>(mob).Coordinates.Offset(new Vector2(0.3f, 0f));
        var spawned = entMan.SpawnEntity(itemPrototype, spawnCoords);
        var picked = hands.TryPickupAnyHand(
            mob,
            spawned,
            checkActionBlocker: false,
            animateUser: false,
            animate: false,
            handsComp: handsComp);

        if (picked)
            return spawned;

        if (entMan.EntityExists(spawned))
            entMan.DeleteEntity(spawned);
        return null;
    }

    private static void SetNpcTargetBlackboard(IEntityManager entMan, EntityUid npc, EntityUid target)
    {
        if (!entMan.TryGetComponent(npc, out HTNComponent htn))
            return;

        var targetCoords = entMan.GetComponent<TransformComponent>(target).Coordinates;
        htn.Blackboard.SetValue("Target", target);
        htn.Blackboard.SetValue("TargetCoordinates", targetCoords);
    }
}
