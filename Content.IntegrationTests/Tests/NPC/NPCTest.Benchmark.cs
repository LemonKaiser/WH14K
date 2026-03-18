using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.IntegrationTests.Pair;
using Content.Server.Hands.Systems;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.Hands.Components;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.NPC;

public sealed partial class NPCTest
{
    private const int BenchmarkWarmupTicks = 120;
    private const int BenchmarkMeasureTicks = 600;

    [Test]
    [Explicit("Manual NPC benchmark matrix (idle/combat/items/path/actions/100+ NPC).")]
    public async Task NpcBenchmarkMatrix()
    {
        var pair = await PoolManager.GetServerClient();
        try
        {
            var server = pair.Server;
            var oldEnabled = server.CfgMan.GetCVar(CCVars.NPCBenchmarkEnabled);
            var oldDetailed = server.CfgMan.GetCVar(CCVars.NPCBenchmarkDetailed);
            var oldInterval = server.CfgMan.GetCVar(CCVars.NPCBenchmarkLogIntervalSeconds);

            var results = new List<BenchmarkScenarioResult>();

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

                results.Add(await RunBenchmarkScenario(pair, "idle_swarm_60", SetupIdleSwarmScenario));
                results.Add(await RunBenchmarkScenario(pair, "melee_swarm_80", SetupMeleeSwarmScenario));
                results.Add(await RunBenchmarkScenario(pair, "item_interaction_pickup", SetupItemInteractionScenario));
                results.Add(await RunBenchmarkScenario(pair, "complex_path_maze", SetupPathComplexityScenario));
                results.Add(await RunBenchmarkScenario(pair, "blocked_path_no_solution", SetupBlockedPathScenario));
                results.Add(await RunBenchmarkScenario(pair, "action_use_goliath", SetupActionUseScenario));
                results.Add(await RunBenchmarkScenario(pair, "combat_guardrail_small_8", SetupCombatGuardrailSmallScenario));
                results.Add(await RunBenchmarkScenario(pair, "standalone_guardrail_small", SetupStandaloneGuardrailSmallScenario));
                results.Add(await RunBenchmarkScenario(pair, "wave_defense_pig_swarm_100_single_pirate", SetupWaveDefensePigSwarmSinglePirateScenario));
                results.Add(await RunBenchmarkScenario(pair, "wave_defense_pig_swarm_100_fireteam_6", SetupWaveDefensePigSwarmFireteamScenario));
                results.Add(await RunBenchmarkScenario(pair, "mixed_100_plus_full_pack", SetupMixed100PlusFullPackScenario));
                results.Add(await RunBenchmarkScenario(pair, "stress_mixed_100_npc", SetupStressMixedScenario));
            }
            finally
            {
                await server.WaitPost(() =>
                {
                    server.CfgMan.SetCVar(CCVars.NPCBenchmarkEnabled, oldEnabled);
                    server.CfgMan.SetCVar(CCVars.NPCBenchmarkDetailed, oldDetailed);
                    server.CfgMan.SetCVar(CCVars.NPCBenchmarkLogIntervalSeconds, oldInterval);
                });
            }

            foreach (var result in results)
            {
                Console.WriteLine($"NPC-BENCH SCENARIO={result.Name} WINDOW_S={result.Snapshot.WindowSeconds:F2} STAGES={result.Snapshot.Stages.Count}");

                foreach (var stage in result.Snapshot.Stages.Take(24))
                {
                    Console.WriteLine(
                        $"NPC-BENCH STAGE={stage.Name} samples={stage.Samples} work={stage.WorkItems} total_ms={stage.TotalMilliseconds:F3} avg_ms={stage.AverageMilliseconds:F4} max_ms={stage.MaxMilliseconds:F4} avg_item_us={stage.AverageItemMicroseconds:F3}");
                }
            }

            ExportBenchmarkResults(results);

            Assert.That(results, Is.Not.Empty);
            Assert.That(results.All(r => r.Snapshot.Stages.Count > 0), Is.True, "Each scenario must emit benchmark stages.");
            Assert.That(HasStage(results, "complex_path_maze", "npc.pathfinding.request_enqueued"), Is.True);
            Assert.That(HasStage(results, "blocked_path_no_solution", "npc.pathfinding.request_enqueued"), Is.True);
            Assert.That(HasStage(results, "action_use_goliath", "npc.action_on_target.attempts"), Is.True);
            Assert.That(GetStageWorkItems(results, "combat_guardrail_small_8", "npc.combat.ranged.shoot_performed"), Is.GreaterThan(0),
                "Small-scale combat guardrail failed: ranged NPC did not fire.");
            Assert.That(GetStageWorkItems(results, "standalone_guardrail_small", "npc.combat.ranged.shoot_performed"), Is.GreaterThan(0),
                "Standalone guardrail failed: ranged NPC did not fire.");
            Assert.That(GetStageWorkItems(results, "wave_defense_pig_swarm_100_single_pirate", "npc.combat.ranged.shoot_performed"), Is.GreaterThan(0),
                "Wave-defense single-ranged scenario failed: ranged NPC did not fire under passive swarm load.");
            Assert.That(GetStageWorkItems(results, "wave_defense_pig_swarm_100_fireteam_6", "npc.combat.ranged.shoot_performed"), Is.GreaterThan(0),
                "Wave-defense fireteam scenario failed: no ranged shots were performed.");
            Assert.That(GetStageWorkItems(results, "mixed_100_plus_full_pack", "npc.combat.ranged.shoot_performed"), Is.GreaterThan(0),
                "Mixed 100+ full-pack scenario failed: no ranged shots were performed.");
            Assert.That(GetStageWorkItems(results, "mixed_100_plus_full_pack", "npc.wave.deploy.success"), Is.GreaterThan(0),
                "Mixed 100+ full-pack scenario failed: deploy layer produced no successful placements.");
            Assert.That(GetStageWorkItems(results, "mixed_100_plus_full_pack", "npc.wave.service.restock_success"), Is.GreaterThan(0),
                "Mixed 100+ full-pack scenario failed: service layer produced no restock success.");
            Assert.That(HasStage(results, "stress_mixed_100_npc", "npc.active.count"), Is.True);
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static bool HasStage(IEnumerable<BenchmarkScenarioResult> results, string scenario, string stage)
    {
        foreach (var result in results)
        {
            if (!string.Equals(result.Name, scenario, StringComparison.Ordinal))
                continue;

            if (result.Snapshot.Stages.Any(s => string.Equals(s.Name, stage, StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private static int GetStageWorkItems(IEnumerable<BenchmarkScenarioResult> results, string scenario, string stage)
    {
        foreach (var result in results)
        {
            if (!string.Equals(result.Name, scenario, StringComparison.Ordinal))
                continue;

            foreach (var snapshotStage in result.Snapshot.Stages)
            {
                if (string.Equals(snapshotStage.Name, stage, StringComparison.Ordinal))
                    return snapshotStage.WorkItems;
            }
        }

        return 0;
    }

    private static async Task<BenchmarkScenarioResult> RunBenchmarkScenario(
        TestPair pair,
        string name,
        Action<BenchmarkScenarioContext> setup)
    {
        var spawned = new List<EntityUid>(512);
        var bench = pair.Server.System<NPCBenchmarkSystem>();

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var mapSystem = entMan.System<SharedMapSystem>();

            FillFloorRect(mapSystem, pair.TestMap.Grid, -80, 80, -80, 80);
            setup(new BenchmarkScenarioContext(entMan, pair.TestMap.Grid, spawned));
            bench.Reset();
        });

        await pair.RunTicksSync(BenchmarkWarmupTicks);
        await pair.Server.WaitPost(() => bench.Reset());
        await pair.RunTicksSync(BenchmarkMeasureTicks);

        NpcBenchmarkSnapshot snapshot = default;
        await pair.Server.WaitPost(() => snapshot = bench.SnapshotAndReset());

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            foreach (var uid in spawned)
            {
                if (entMan.EntityExists(uid))
                    entMan.DeleteEntity(uid);
            }
        });

        await pair.RunTicksSync(40);
        return new BenchmarkScenarioResult(name, snapshot);
    }

    private static void ExportBenchmarkResults(IReadOnlyList<BenchmarkScenarioResult> results)
    {
        var outputPath = Environment.GetEnvironmentVariable("WH14K_NPC_BENCHMARK_EXPORT");
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            benchmarkWarmupTicks = BenchmarkWarmupTicks,
            benchmarkMeasureTicks = BenchmarkMeasureTicks,
            scenarios = results.Select(result => new
            {
                name = result.Name,
                windowSeconds = result.Snapshot.WindowSeconds,
                stages = result.Snapshot.Stages.Select(stage => new
                {
                    name = stage.Name,
                    samples = stage.Samples,
                    workItems = stage.WorkItems,
                    totalMilliseconds = stage.TotalMilliseconds,
                    averageMilliseconds = stage.AverageMilliseconds,
                    maxMilliseconds = stage.MaxMilliseconds,
                    averageItemMicroseconds = stage.AverageItemMicroseconds
                }).ToArray()
            }).ToArray()
        };

        File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void SetupIdleSwarmScenario(BenchmarkScenarioContext ctx)
    {
        SpawnSwarm(ctx, "MobCarp", count: 60, origin: new Vector2(-20f, -10f), columns: 10, spacing: 1.4f);
    }

    private static void SetupMeleeSwarmScenario(BenchmarkScenarioContext ctx)
    {
        SpawnSwarm(ctx, "MobCarp", count: 40, origin: new Vector2(-14f, -10f), columns: 8, spacing: 1.45f);
        SpawnSwarm(ctx, "MobCivilian", count: 40, origin: new Vector2(-2f, -10f), columns: 8, spacing: 1.45f);
    }

    private static void SetupItemInteractionScenario(BenchmarkScenarioContext ctx)
    {
        var attackers = SpawnSwarm(ctx, "MobSalvager", count: 24, origin: new Vector2(-16f, -10f), columns: 6, spacing: 1.6f);
        SpawnSwarm(ctx, "MobCivilian", count: 24, origin: new Vector2(-3f, -10f), columns: 6, spacing: 1.6f);

        var hands = ctx.EntMan.System<HandsSystem>();
        foreach (var attacker in attackers)
        {
            if (!ctx.EntMan.TryGetComponent(attacker, out HandsComponent handsComp))
                continue;

            var held = hands.EnumerateHeld((attacker, handsComp)).ToArray();
            foreach (var item in held)
            {
                hands.TryDrop((attacker, handsComp), item, checkActionBlocker: false);

                if (ctx.EntMan.EntityExists(item))
                    ctx.EntMan.DeleteEntity(item);
            }

            var coord = ctx.EntMan.GetComponent<TransformComponent>(attacker).Coordinates.Offset(new Vector2(0.6f, 0f));
            var gun = ctx.EntMan.SpawnEntity("WeaponPistolMk58", coord);
            ctx.Spawned.Add(gun);
        }
    }

    private static void SetupPathComplexityScenario(BenchmarkScenarioContext ctx)
    {
        for (var y = -24; y <= 24; y++)
        {
            if (y != -16 && y != 0 && y != 16)
                SpawnAt(ctx, "WallSolid", 0f, y);

            if (y != -12 && y != 8)
                SpawnAt(ctx, "WallSolid", 10f, y);

            if (y != -8 && y != 20)
                SpawnAt(ctx, "WallSolid", 20f, y);
        }

        SpawnAt(ctx, "Airlock", 0f, 0f);
        SpawnAt(ctx, "Airlock", 10f, 8f);

        SpawnSwarm(ctx, "MobSalvager", count: 30, origin: new Vector2(-18f, -10f), columns: 6, spacing: 1.6f);
        SpawnSwarm(ctx, "MobCivilian", count: 30, origin: new Vector2(28f, -10f), columns: 6, spacing: 1.6f);
    }

    private static void SetupBlockedPathScenario(BenchmarkScenarioContext ctx)
    {
        const string blocker = "WallPlastitaniumIndestructible";

        for (var x = 14; x <= 30; x++)
        {
            SpawnAt(ctx, blocker, x, -16f);
            SpawnAt(ctx, blocker, x, 16f);
        }

        for (var y = -15; y <= 15; y++)
        {
            SpawnAt(ctx, blocker, 14f, y);
            SpawnAt(ctx, blocker, 30f, y);
        }

        SpawnSwarm(ctx, "MobSalvager", count: 25, origin: new Vector2(-20f, -12f), columns: 5, spacing: 1.8f);
        SpawnSwarm(ctx, "MobCivilian", count: 25, origin: new Vector2(18f, -12f), columns: 5, spacing: 1.8f);
    }

    private static void SetupActionUseScenario(BenchmarkScenarioContext ctx)
    {
        SpawnSwarm(ctx, "MobGoliath", count: 12, origin: new Vector2(-8f, -6f), columns: 4, spacing: 2.4f);
        SpawnSwarm(ctx, "MobCivilian", count: 24, origin: new Vector2(2f, -9f), columns: 6, spacing: 1.8f);
    }

    private static void SetupCombatGuardrailSmallScenario(BenchmarkScenarioContext ctx)
    {
        var pirate = SpawnAt(ctx, "MobSpirate", 6f, 0f);
        EquipWithPistol(ctx, pirate);

        SpawnSwarm(ctx, "MobPig", count: 8, origin: new Vector2(-2f, -3f), columns: 4, spacing: 1.3f);
        // Add a guaranteed hostile surrogate target to validate ranged reaction under ambient animal load.
        SpawnNanoTrasenTargets(ctx, count: 1, origin: new Vector2(3f, 0f), columns: 1, spacing: 1f);
    }

    private static void SetupStandaloneGuardrailSmallScenario(BenchmarkScenarioContext ctx)
    {
        SetupCombatGuardrailSmallScenario(ctx);
    }

    private static void SetupWaveDefensePigSwarmSinglePirateScenario(BenchmarkScenarioContext ctx)
    {
        var pirate = SpawnAt(ctx, "MobSpirate", 10f, 0f);
        EquipWithPistol(ctx, pirate);

        SpawnSwarm(ctx, "MobPig", count: 100, origin: new Vector2(-8f, -8f), columns: 10, spacing: 1.6f);
        // Keep pigs as passive stress load and inject explicit hostile targets for combat non-regression.
        SpawnNanoTrasenTargets(ctx, count: 3, origin: new Vector2(5.5f, -1f), columns: 3, spacing: 1.2f);
    }

    private static void SetupWaveDefensePigSwarmFireteamScenario(BenchmarkScenarioContext ctx)
    {
        var pirates = SpawnSwarm(ctx, "MobSpirate", count: 6, origin: new Vector2(12f, -4f), columns: 3, spacing: 1.8f);
        foreach (var pirate in pirates)
        {
            EquipWithPistol(ctx, pirate);
        }

        SpawnSwarm(ctx, "MobPig", count: 100, origin: new Vector2(-8f, -8f), columns: 10, spacing: 1.6f);
        // Multiple hostile dummies prevent false "idle fireteam" pass/fail under heavy passive crowd load.
        SpawnNanoTrasenTargets(ctx, count: 6, origin: new Vector2(6f, -3f), columns: 3, spacing: 1.4f);
    }

    private static void SetupStressMixedScenario(BenchmarkScenarioContext ctx)
    {
        SpawnSwarm(ctx, "MobCarp", count: 50, origin: new Vector2(-32f, -16f), columns: 10, spacing: 1.6f);
        SpawnSwarm(ctx, "MobSalvager", count: 30, origin: new Vector2(-18f, -16f), columns: 6, spacing: 1.8f);
        SpawnSwarm(ctx, "MobGoliath", count: 20, origin: new Vector2(-6f, -12f), columns: 5, spacing: 2.4f);
        SpawnSwarm(ctx, "MobCivilian", count: 70, origin: new Vector2(10f, -18f), columns: 10, spacing: 1.7f);
    }

    private static void SetupMixed100PlusFullPackScenario(BenchmarkScenarioContext ctx)
    {
        var supports = SpawnSwarm(
            ctx,
            NpcCapabilityScenarioLibrary.SupportPrototype,
            count: 12,
            origin: new Vector2(-24f, -4f),
            columns: 4,
            spacing: 1.6f);
        SpawnSwarm(
            ctx,
            NpcCapabilityScenarioLibrary.AssaultPrototype,
            count: 28,
            origin: new Vector2(-10f, -6f),
            columns: 7,
            spacing: 1.6f);
        SpawnSwarm(
            ctx,
            NpcCapabilityScenarioLibrary.SapperPrototype,
            count: 8,
            origin: new Vector2(-16f, 6f),
            columns: 4,
            spacing: 1.6f);
        SpawnSwarm(
            ctx,
            NpcCapabilityScenarioLibrary.LogisticsPrototype,
            count: 10,
            origin: new Vector2(-4f, 8f),
            columns: 5,
            spacing: 1.4f);

        SpawnSwarm(ctx, "MobPig", count: 55, origin: new Vector2(6f, -10f), columns: 11, spacing: 1.4f);
        SpawnNanoTrasenTargets(ctx, count: 20, origin: new Vector2(10f, -2f), columns: 5, spacing: 1.5f);
        // Deterministic ranged guardrail anchor for this high-variance mixed scenario.
        var pirate = SpawnAt(ctx, "MobSpirate", 8f, 10f);
        EquipWithPistol(ctx, pirate);
        SpawnNanoTrasenTargets(ctx, count: 1, origin: new Vector2(11f, 10f), columns: 1, spacing: 1f);

        var hands = ctx.EntMan.System<HandsSystem>();
        for (var i = 0; i < Math.Min(6, supports.Count); i++)
        {
            var support = supports[i];
            if (!ctx.EntMan.TryGetComponent(support, out HandsComponent supportHands))
                continue;

            var mortar = SpawnAt(ctx, "WH40KMortarKit", -23f + i * 1.5f, -4f + (i % 2 == 0 ? 0.5f : -0.5f));
            _ = hands.TryPickupAnyHand(
                support,
                mortar,
                checkActionBlocker: false,
                animateUser: false,
                animate: false,
                handsComp: supportHands);
        }

        for (var i = 0; i < 6; i++)
        {
            var mine = SpawnAt(ctx, "LandMineModular", -14f + i * 1.2f, 6f);
            if (ctx.EntMan.TryGetComponent(mine, out ItemToggleComponent toggle))
                toggle.Activated = true;
        }

        var machine = SpawnAt(ctx, "VendingMachineBooze", 0f, 12f);
        SetVendingMachineLowStockAndPanelOpen(ctx.EntMan, machine);
        SpawnAt(ctx, "VendingMachineRestockBooze", -2f, 11.5f);
        SpawnAt(ctx, "VendingMachineRestockBooze", -2f, 12.5f);
    }

    private static List<EntityUid> SpawnSwarm(
        BenchmarkScenarioContext ctx,
        string prototype,
        int count,
        Vector2 origin,
        int columns,
        float spacing)
    {
        var spawned = new List<EntityUid>(count);

        for (var i = 0; i < count; i++)
        {
            var x = origin.X + (i % columns) * spacing;
            var y = origin.Y + (i / columns) * spacing;
            spawned.Add(SpawnAt(ctx, prototype, x, y));
        }

        return spawned;
    }

    private static EntityUid SpawnAt(BenchmarkScenarioContext ctx, string prototype, float x, float y)
    {
        var uid = ctx.EntMan.SpawnEntity(prototype, new EntityCoordinates(ctx.Grid.Owner, x, y));
        ctx.Spawned.Add(uid);
        return uid;
    }

    private static List<EntityUid> SpawnNanoTrasenTargets(
        BenchmarkScenarioContext ctx,
        int count,
        Vector2 origin,
        int columns,
        float spacing)
    {
        return SpawnSwarm(ctx, "MobCivilian", count, origin, columns, spacing);
    }

    private static void EquipWithPistol(BenchmarkScenarioContext ctx, EntityUid mob)
    {
        if (!ctx.EntMan.TryGetComponent(mob, out HandsComponent handsComp))
            return;

        var hands = ctx.EntMan.System<HandsSystem>();
        var held = hands.EnumerateHeld((mob, handsComp)).ToArray();

        foreach (var item in held)
        {
            hands.TryDrop((mob, handsComp), item, checkActionBlocker: false);

            if (ctx.EntMan.EntityExists(item))
                ctx.EntMan.DeleteEntity(item);
        }

        var coord = ctx.EntMan.GetComponent<TransformComponent>(mob).Coordinates.Offset(new Vector2(0.3f, 0f));
        var gun = ctx.EntMan.SpawnEntity("WeaponPistolMk58", coord);
        ctx.Spawned.Add(gun);
        hands.TryPickupAnyHand(mob, gun, checkActionBlocker: false, animateUser: false, animate: false, handsComp: handsComp);
    }

    private static void FillFloorRect(
        SharedMapSystem mapSystem,
        Entity<MapGridComponent> grid,
        int minX,
        int maxX,
        int minY,
        int maxY)
    {
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(1));
            }
        }
    }

    private sealed record BenchmarkScenarioResult(string Name, NpcBenchmarkSnapshot Snapshot);

    private sealed class BenchmarkScenarioContext(
        IEntityManager entMan,
        Entity<MapGridComponent> grid,
        List<EntityUid> spawned)
    {
        public IEntityManager EntMan { get; } = entMan;
        public Entity<MapGridComponent> Grid { get; } = grid;
        public List<EntityUid> Spawned { get; } = spawned;
    }
}
