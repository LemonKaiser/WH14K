#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.TacticalMap;
using Content.Shared.Access.Components;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared._WH40K.Influence;
using Content.Shared._WH40K.TacticalMap;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class BattlefieldLoadCharacterizationTests
{
    private const string BattlefieldMap = "Battlefield40k";
    private const string BattlefieldPreset = "WH40KTeamBattle 9999";
    private const string TacticalTabletPrototype = "WH40KCommandTacticalMapTablet";
    private const int TotalPlayers = 100;
    private const int TacticalViewerCount = 24;
    private const int WarmupTicks = 30;
    private const int MeasureTicks = 60;
    private const int DummyPvsTicks = 36;
    private const int HotspotBucketSize = 12;
    private const string Imperium = "Imperium";
    private const string Heretics = "Heretics";

    private static readonly Assembly RobustServerAssembly = typeof(ViewSubscriberSystem).Assembly;
    private static readonly Type PvsSystemType = RobustServerAssembly.GetType("Robust.Server.GameStates.PvsSystem", throwOnError: true)!;
    private static readonly Type PvsSessionType = RobustServerAssembly.GetType("Robust.Server.GameStates.PvsSession", throwOnError: true)!;
    private static readonly FieldInfo PvsPlayerDataField = PvsSystemType.GetField("PlayerData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static readonly FieldInfo PvsBudgetField = PvsSessionType.GetField("Budget", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static readonly FieldInfo PvsPreviouslySentField = PvsSessionType.GetField("PreviouslySent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static readonly FieldInfo MetaPvsDataField = typeof(MetaDataComponent).GetField("PvsData", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo PvsIndexValueProperty = MetaPvsDataField.FieldType.GetProperty("Index", BindingFlags.Instance | BindingFlags.Public)!;
    private static readonly FieldInfo PvsBudgetDirtyField = PvsBudgetField.FieldType.GetField("DirtyCount", BindingFlags.Instance | BindingFlags.Public)!;
    private static readonly FieldInfo PvsBudgetEnterField = PvsBudgetField.FieldType.GetField("EnterCount", BindingFlags.Instance | BindingFlags.Public)!;
    private static readonly FieldInfo PvsBudgetNewField = PvsBudgetField.FieldType.GetField("NewCount", BindingFlags.Instance | BindingFlags.Public)!;

    [Test]
    [Explicit("Manual Battlefield40k load characterization benchmark for movement, PVS, tactical map churn, and influence-point pressure.")]
    public async Task BattlefieldLoadCharacterization()
    {
        var pair = await StartWh40KRoundAsync(TotalPlayers - 1);

        try
        {
            await ConfigureBenchmarkCvarsAsync(pair);
            var ctx = await BuildContextAsync(pair);

            Console.WriteLine($"BF-BENCH START players={ctx.AllSessions.Length} dummy={ctx.DummySessions.Length} real={ctx.RealSession.Name} grid={ctx.GridUid}");

            foreach (var hotspot in ctx.AccessHotspots)
            {
                Console.WriteLine(
                    $"BF-BENCH HOTSPOT bucket={hotspot.Bucket} center=({hotspot.Center.X:F1},{hotspot.Center.Y:F1}) score={hotspot.Score} doors={hotspot.Doors} readers={hotspot.AccessReaders}");
            }

            var results = new List<ScenarioResult>
            {
                await MeasureScenarioAsync(
                    pair,
                    ctx,
                    "idle_100",
                    warmup: async () =>
                    {
                        await CloseAllTabletsAsync(pair, ctx);
                        await SpreadPlayersAsync(pair, ctx, ctx.WideWaypoints, tickOffset: 0);
                    }),

                await MeasureScenarioAsync(
                    pair,
                    ctx,
                    "observer_access_sweep",
                    warmup: async () =>
                    {
                        await CloseAllTabletsAsync(pair, ctx);
                        await SpreadPlayersAsync(pair, ctx, ctx.WideWaypoints, tickOffset: 7);
                    },
                    fullTickAction: tick => SweepObserverAsync(pair, ctx, tick),
                    dummyPvsAction: tick => SweepObserverAsync(pair, ctx, tick)),

                await MeasureScenarioAsync(
                    pair,
                    ctx,
                    "mass_redeploy_100",
                    warmup: async () =>
                    {
                        await CloseAllTabletsAsync(pair, ctx);
                        await SpreadPlayersAsync(pair, ctx, ctx.WideWaypoints, tickOffset: 11);
                    },
                    fullTickAction: tick => RedeployAllPlayersAsync(pair, ctx, tick, stride: 1),
                    dummyPvsAction: tick => RedeployAllPlayersAsync(pair, ctx, tick, stride: 2)),

                await MeasureScenarioAsync(
                    pair,
                    ctx,
                    "tactical_map_churn",
                    warmup: async () =>
                    {
                        await OpenTabletClusterAsync(pair, ctx, TacticalViewerCount);
                        await SpreadPlayersAsync(pair, ctx, ctx.WideWaypoints, tickOffset: 19);
                        await SetClientTabletOpenAsync(pair, ctx, false);
                    },
                    fullTickAction: tick => TacticalMapChurnTickAsync(pair, ctx, tick),
                    dummyPvsAction: tick => TacticalMapDummyTickAsync(pair, ctx, tick)),

                await MeasureScenarioAsync(
                    pair,
                    ctx,
                    "frontline_rotation",
                    warmup: async () =>
                    {
                        await OpenTabletClusterAsync(pair, ctx, 8);
                        await FrontlineRotationAsync(pair, ctx, phase: 0);
                        await SetClientTabletOpenAsync(pair, ctx, true);
                    },
                    fullTickAction: tick => FrontlineRotationAsync(pair, ctx, tick / 6),
                    dummyPvsAction: tick => FrontlineRotationAsync(pair, ctx, tick / 4))
            };

            foreach (var result in results)
            {
                Console.WriteLine(
                    $"BF-BENCH SCENARIO={result.Name} tick_avg_ms={result.FullTick.AverageMs:F2} tick_p95_ms={result.FullTick.P95Ms:F2} tick_max_ms={result.FullTick.MaxMs:F2} " +
                    $"sent_avg={result.FullTick.AverageSent:F1} sent_peak={result.FullTick.PeakSent} dirty_peak={result.FullTick.PeakDirty} enter_peak={result.FullTick.PeakEnter} new_peak={result.FullTick.PeakNew} " +
                    $"pvs_avg_ms={result.DummyPvs.AverageMs:F2} pvs_max_ms={result.DummyPvs.MaxMs:F2} dummy_sent_peak={result.DummyPvs.PeakSent} peak_session={result.FullTick.PeakSession}");

                if (result.FullTick.PeakCategories.Count > 0)
                {
                    var categories = string.Join(
                        ", ",
                        result.FullTick.PeakCategories
                            .OrderByDescending(p => p.Value)
                            .Take(6)
                            .Select(p => $"{p.Key}={p.Value}"));
                    Console.WriteLine($"BF-BENCH CATEGORIES scenario={result.Name} {categories}");
                }
            }

            ExportResults(results, ctx);

            Assert.That(results, Has.Count.EqualTo(5));
            Assert.That(results.Any(r => r.Name == "tactical_map_churn" && r.FullTick.PeakSent > 0), Is.True);
            Assert.That(results.Any(r => r.Name == "observer_access_sweep" && r.FullTick.PeakCategories.ContainsKey("door_access_reader")), Is.True,
                "Observer sweep did not surface any door/access-reader pressure.");
            Assert.That(results.Any(r => r.Name == "frontline_rotation" && r.FullTick.PeakCategories.ContainsKey("influence_point")), Is.True,
                "Frontline rotation did not surface influence-point traffic.");
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static async Task<TestPair> StartWh40KRoundAsync(int dummySessions)
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
            InLobby = true,
            DummyTicker = false,
            Fresh = true
        });

        await pair.WaitCommand($"forcemap {BattlefieldMap}");
        await pair.WaitCommand($"setgamepreset {BattlefieldPreset}");

        if (dummySessions > 0)
        {
            await pair.Server.AddDummySessions(dummySessions);
            await pair.RunTicksSync(10);
        }

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            ticker.ToggleReadyAll(true);
        });

        await pair.WaitCommand("startround");
        await pair.RunTicksSync(90);

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            var sessions = playerMan.Sessions.ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
                Assert.That(sessions, Has.Length.EqualTo(TotalPlayers));
                Assert.That(sessions.All(x => x.AttachedEntity != null), Is.True);
            });
        });

        return pair;
    }

    private static async Task ConfigureBenchmarkCvarsAsync(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
        {
            pair.Server.CfgMan.SetCVar(CVars.NetPVS, true);
            pair.Server.CfgMan.SetCVar(CVars.NetPvsAsync, false);
            pair.Server.CfgMan.SetCVar(CVars.ThreadParallelCount, 0);
            pair.Server.CfgMan.SetCVar(CCVars.AdminLogsEnabled, false);
        });
    }

    private static async Task<BattlefieldBenchContext> BuildContextAsync(TestPair pair)
    {
        var ctx = new BattlefieldBenchContext();

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            var xform = entMan.System<SharedTransformSystem>();

            ctx.AllSessions = playerMan.Sessions.OrderBy(session => session.Name, StringComparer.Ordinal).ToArray();
            ctx.DummySessions = ctx.AllSessions.Where(session => pair.Server.DummySessions.ContainsKey(session.UserId)).ToArray();
            ctx.RealSession = ctx.AllSessions.Single(session => session.UserId == pair.Client.User);
            ctx.RealActor = ctx.RealSession.AttachedEntity!.Value;

            var realXform = entMan.GetComponent<TransformComponent>(ctx.RealActor);
            ctx.GridUid = realXform.GridUid!.Value;
            var grid = entMan.GetComponent<MapGridComponent>(ctx.GridUid);
            ctx.Bounds = grid.LocalAABB;

            ctx.WideWaypoints = BuildWideWaypoints(ctx.Bounds, 12);
            ctx.AccessHotspots = FindAccessHotspots(entMan, xform, ctx.GridUid).ToArray();
            ctx.ObserverSweepPoints = BuildObserverSweepPoints(ctx.Bounds, ctx.AccessHotspots).ToArray();
            ctx.InfluencePoints = FindInfluencePoints(entMan, ctx.GridUid).ToArray();

            for (var i = 0; i < ctx.AllSessions.Length; i++)
            {
                var session = ctx.AllSessions[i];
                if (session.AttachedEntity is not { } actor)
                    continue;

                var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(actor);
                member.TeamId = i < ctx.AllSessions.Length / 2 ? Imperium : Heretics;

                if (string.Equals(member.TeamId, Imperium, StringComparison.Ordinal))
                    ctx.ImperiumSessions.Add(session);
                else if (string.Equals(member.TeamId, Heretics, StringComparison.Ordinal))
                    ctx.HereticsSessions.Add(session);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(ctx.AllSessions, Has.Length.EqualTo(TotalPlayers));
            Assert.That(ctx.DummySessions, Has.Length.EqualTo(TotalPlayers - 1));
            Assert.That(ctx.AccessHotspots.Count, Is.GreaterThan(0), "Battlefield40k did not surface any access hot spots.");
            Assert.That(ctx.InfluencePoints.Count, Is.GreaterThanOrEqualTo(3), "Battlefield40k did not expose enough influence points for frontline rotation.");
            Assert.That(ctx.ImperiumSessions.Count, Is.GreaterThan(0));
            Assert.That(ctx.HereticsSessions.Count, Is.GreaterThan(0));
        });

        return ctx;
    }

    private static async Task<ScenarioResult> MeasureScenarioAsync(
        TestPair pair,
        BattlefieldBenchContext ctx,
        string name,
        Func<Task> warmup,
        Func<int, Task>? fullTickAction = null,
        Func<int, Task>? dummyPvsAction = null)
    {
        await warmup();
        await pair.RunTicksSync(WarmupTicks);

        var tickMetrics = new ScenarioMetrics();
        for (var tick = 0; tick < MeasureTicks; tick++)
        {
            if (fullTickAction != null)
                await fullTickAction(tick);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await pair.RunTicksSync(1);
            sw.Stop();

            tickMetrics.TickSamples.Add(sw.Elapsed.TotalMilliseconds);
            var sample = await CapturePvsSnapshotAsync(pair, ctx.AllSessions, tickMetrics.PeakSentThreshold);
            tickMetrics.Register(sample);
        }

        var dummyMetrics = new ScenarioMetrics();
        var dummyPlayers = ctx.DummySessions.Cast<ICommonSession>().ToArray();
        for (var tick = 0; tick < DummyPvsTicks; tick++)
        {
            if (dummyPvsAction != null)
                await dummyPvsAction(tick);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            pair.Server.PvsTick(dummyPlayers);
            sw.Stop();

            dummyMetrics.TickSamples.Add(sw.Elapsed.TotalMilliseconds);
            var sample = await CapturePvsSnapshotAsync(pair, ctx.DummySessions, dummyMetrics.PeakSentThreshold);
            dummyMetrics.Register(sample);
        }

        await pair.Client.WaitRunTicks(DummyPvsTicks);
        await Task.WhenAll(pair.Client.WaitIdleAsync(), pair.Server.WaitIdleAsync());

        return new ScenarioResult(
            name,
            tickMetrics.ToSummary(),
            dummyMetrics.ToSummary());
    }

    private static async Task SpreadPlayersAsync(TestPair pair, BattlefieldBenchContext ctx, IReadOnlyList<Vector2> waypoints, int tickOffset)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var xform = entMan.System<SharedTransformSystem>();

            for (var i = 0; i < ctx.AllSessions.Length; i++)
            {
                var actor = ctx.AllSessions[i].AttachedEntity!.Value;
                var target = waypoints[(i + tickOffset) % waypoints.Count];
                xform.SetCoordinates(actor, new EntityCoordinates(ctx.GridUid, target));
            }
        });
    }

    private static async Task SweepObserverAsync(TestPair pair, BattlefieldBenchContext ctx, int tick)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var xform = entMan.System<SharedTransformSystem>();
            var target = ctx.ObserverSweepPoints[tick % ctx.ObserverSweepPoints.Count];
            xform.SetCoordinates(ctx.RealActor, new EntityCoordinates(ctx.GridUid, target));
        });
    }

    private static async Task RedeployAllPlayersAsync(TestPair pair, BattlefieldBenchContext ctx, int tick, int stride)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var xform = entMan.System<SharedTransformSystem>();

            for (var i = 0; i < ctx.AllSessions.Length; i++)
            {
                var actor = ctx.AllSessions[i].AttachedEntity!.Value;
                var target = ctx.WideWaypoints[(i * stride + tick) % ctx.WideWaypoints.Count];
                xform.SetCoordinates(actor, new EntityCoordinates(ctx.GridUid, target));
            }
        });
    }

    private static async Task TacticalMapChurnTickAsync(TestPair pair, BattlefieldBenchContext ctx, int tick)
    {
        await RedeployAllPlayersAsync(pair, ctx, tick, stride: 3);

        if (tick % 10 == 0)
            await SetClientTabletOpenAsync(pair, ctx, !ctx.ClientTabletOpen);

        if (ctx.ClientTabletOpen && tick % 3 == 1)
            await SendClientAnnotationsAsync(pair, ctx, tick);
    }

    private static async Task TacticalMapDummyTickAsync(TestPair pair, BattlefieldBenchContext ctx, int tick)
    {
        await RedeployAllPlayersAsync(pair, ctx, tick, stride: 4);

        if (tick % 9 != 0)
            return;

        var shouldOpen = tick % 18 == 0;
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>().EntitySysManager;
            var ui = entMan.GetEntitySystem<UserInterfaceSystem>();
            var sharedUi = entMan.GetEntitySystem<SharedUserInterfaceSystem>();

            foreach (var viewer in ctx.TabletOwners.Skip(1).Take(6))
            {
                if (!ctx.TabletsByUserId.TryGetValue(viewer.UserId, out var tablet))
                    continue;

                var actor = viewer.AttachedEntity!.Value;
                if (shouldOpen)
                    ui.TryOpenUi(tablet, WH40KTacticalMapUiKey.Key, actor);
                else
                    sharedUi.CloseUi(tablet, WH40KTacticalMapUiKey.Key, actor);
            }
        });
    }

    private static async Task FrontlineRotationAsync(TestPair pair, BattlefieldBenchContext ctx, int phase)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var xform = entMan.System<SharedTransformSystem>();
            var points = ctx.InfluencePoints;

            for (var i = 0; i < ctx.ImperiumSessions.Count; i++)
            {
                var point = points[(phase + (i / 8)) % points.Count];
                var position = BuildPointOffsetPosition(entMan, xform, ctx.GridUid, point, i, 1.5f);
                xform.SetCoordinates(ctx.ImperiumSessions[i].AttachedEntity!.Value, new EntityCoordinates(ctx.GridUid, position));
            }

            for (var i = 0; i < ctx.HereticsSessions.Count; i++)
            {
                var point = points[(phase + 1 + (i / 8)) % points.Count];
                var position = BuildPointOffsetPosition(entMan, xform, ctx.GridUid, point, i, 2.0f);
                xform.SetCoordinates(ctx.HereticsSessions[i].AttachedEntity!.Value, new EntityCoordinates(ctx.GridUid, position));
            }
        });
    }

    private static async Task OpenTabletClusterAsync(TestPair pair, BattlefieldBenchContext ctx, int viewerCount)
    {
        if (ctx.TabletOwners.Count == 0)
        {
            await pair.Server.WaitPost(() =>
            {
                var entMan = pair.Server.ResolveDependency<IEntityManager>();
                var hands = pair.Server.System<HandsSystem>();

                ctx.TabletOwners.Add(ctx.RealSession);
                foreach (var session in ctx.DummySessions.Take(Math.Max(0, viewerCount - 1)))
                {
                    ctx.TabletOwners.Add(session);
                }

                foreach (var owner in ctx.TabletOwners)
                {
                    var actor = owner.AttachedEntity!.Value;
                    var coords = entMan.GetComponent<TransformComponent>(actor).Coordinates;
                    var tablet = entMan.SpawnEntity(TacticalTabletPrototype, coords);
                    Assert.That(hands.TryForcePickupAnyHand(actor, tablet), Is.True, $"Failed to equip tactical tablet for {owner.Name}.");
                    ctx.TabletsByUserId[owner.UserId] = tablet;
                }
            });

            await pair.RunTicksSync(10);

            await pair.Client.WaitAssertion(() =>
            {
                if (!ctx.TabletsByUserId.TryGetValue(ctx.RealSession.UserId, out var realTablet))
                    Assert.Fail("Real tactical-map tablet was not created.");

                ctx.ClientTablet = pair.ToClientUid(realTablet);
                Assert.That(ctx.ClientTablet, Is.Not.EqualTo(EntityUid.Invalid));
            });
        }

        await pair.Server.WaitPost(() =>
        {
            var ui = pair.Server.ResolveDependency<IEntityManager>().EntitySysManager.GetEntitySystem<UserInterfaceSystem>();
            foreach (var owner in ctx.TabletOwners.Take(viewerCount))
            {
                var actor = owner.AttachedEntity!.Value;
                ui.TryOpenUi(ctx.TabletsByUserId[owner.UserId], WH40KTacticalMapUiKey.Key, actor);
            }
        });

        ctx.ClientTabletOpen = true;
        await pair.RunTicksSync(15);
    }

    private static async Task CloseAllTabletsAsync(TestPair pair, BattlefieldBenchContext ctx)
    {
        if (ctx.TabletOwners.Count == 0)
            return;

        await pair.Server.WaitPost(() =>
        {
            var ui = pair.Server.ResolveDependency<IEntityManager>().EntitySysManager.GetEntitySystem<SharedUserInterfaceSystem>();
            foreach (var owner in ctx.TabletOwners)
            {
                if (!ctx.TabletsByUserId.TryGetValue(owner.UserId, out var tablet))
                    continue;

                ui.CloseUi(tablet, WH40KTacticalMapUiKey.Key, owner.AttachedEntity!.Value);
            }
        });

        ctx.ClientTabletOpen = false;
        await pair.RunTicksSync(10);
    }

    private static async Task SetClientTabletOpenAsync(TestPair pair, BattlefieldBenchContext ctx, bool open)
    {
        if (!ctx.TabletsByUserId.TryGetValue(ctx.RealSession.UserId, out var tablet))
            return;

        await pair.Server.WaitPost(() =>
        {
            var actor = ctx.RealSession.AttachedEntity!.Value;
            var entMan = pair.Server.ResolveDependency<IEntityManager>().EntitySysManager;
            if (open)
                entMan.GetEntitySystem<UserInterfaceSystem>().TryOpenUi(tablet, WH40KTacticalMapUiKey.Key, actor);
            else
                entMan.GetEntitySystem<SharedUserInterfaceSystem>().CloseUi(tablet, WH40KTacticalMapUiKey.Key, actor);
        });

        ctx.ClientTabletOpen = open;
    }

    private static async Task SendClientAnnotationsAsync(TestPair pair, BattlefieldBenchContext ctx, int tick)
    {
        if (!ctx.ClientTabletOpen || ctx.ClientTablet == EntityUid.Invalid)
            return;

        var start = ctx.WideWaypoints[(tick * 2) % ctx.WideWaypoints.Count];
        var end = ctx.WideWaypoints[(tick * 2 + 3) % ctx.WideWaypoints.Count];
        var bend = Vector2.Lerp(start, end, 0.5f) + new Vector2(4f, -3f);
        var stroke = new WH40KTacticalMapAnnotationStroke(
            new[]
            {
                start,
                bend,
                end
            },
            tick % 2 == 0 ? Color.Orange : Color.Crimson,
            1.5f + (tick % 4) * 0.35f);

        await pair.Client.WaitPost(() =>
        {
            if (!TryGetOpenBui(pair, ctx.ClientTablet, out var bui) || bui is not { IsOpened: true })
                return;

            bui.SendMessage(new WH40KTacticalMapSaveAnnotationsMessage(new[] { stroke }));
        });
    }

    private static async Task<PvsSnapshot> CapturePvsSnapshotAsync(TestPair pair, IReadOnlyCollection<ICommonSession> sessions, int currentPeakThreshold)
    {
        PvsSnapshot snapshot = default;
        var trackedUsers = sessions.Select(session => session.UserId).ToHashSet();

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var pvsSystem = entMan.EntitySysManager.GetEntitySystem(PvsSystemType);
            var playerData = (IDictionary) PvsPlayerDataField.GetValue(pvsSystem)!;
            var lastTick = timing.CurTick - 1;

            var perSessionSent = new List<(string SessionName, IEnumerable Sent)>(trackedUsers.Count);
            var totalSent = 0;
            var totalDirty = 0;
            var totalEnter = 0;
            var totalNew = 0;
            var maxSessionSent = 0;
            var maxSessionName = string.Empty;

            foreach (DictionaryEntry entry in playerData)
            {
                if (entry.Key is not ICommonSession session || !trackedUsers.Contains(session.UserId))
                    continue;

                var pvsSession = entry.Value!;
                var budget = PvsBudgetField.GetValue(pvsSession)!;
                totalDirty += (int) PvsBudgetDirtyField.GetValue(budget)!;
                totalEnter += (int) PvsBudgetEnterField.GetValue(budget)!;
                totalNew += (int) PvsBudgetNewField.GetValue(budget)!;

                if (!TryGetPreviouslySentList(pvsSession, lastTick, out var sent))
                    continue;

                var sentCount = CountEnumerable(sent);
                totalSent += sentCount;
                perSessionSent.Add((session.Name, sent));

                if (sentCount <= maxSessionSent)
                    continue;

                maxSessionSent = sentCount;
                maxSessionName = session.Name;
            }

            Dictionary<string, int> categories = new(StringComparer.Ordinal);
            if (totalSent > currentPeakThreshold)
            {
                var indexToEntity = BuildPvsIndexMap(entMan);
                foreach (var (_, sent) in perSessionSent)
                {
                    foreach (var entry in sent)
                    {
                        if (entry == null)
                            continue;

                        var index = (int) PvsIndexValueProperty.GetValue(entry)!;
                        if (!indexToEntity.TryGetValue(index, out var uid) ||
                            !entMan.TryGetComponent(uid, out MetaDataComponent? meta))
                        {
                            continue;
                        }

                        var category = CategorizeEntity(entMan, uid, meta);
                        categories.TryGetValue(category, out var existing);
                        categories[category] = existing + 1;
                    }
                }
            }

            snapshot = new PvsSnapshot(
                totalSent,
                totalDirty,
                totalEnter,
                totalNew,
                maxSessionName,
                categories);
        });

        return snapshot;
    }

    private static bool TryGetPreviouslySentList(object pvsSession, GameTick lastTick, out IEnumerable sent)
    {
        var history = PvsPreviouslySentField.GetValue(pvsSession)!;
        var method = history.GetType().GetMethod("TryGetValue")!;
        var args = new object?[] { lastTick, null };
        var found = (bool) method.Invoke(history, args)!;
        sent = found && args[1] is IEnumerable enumerable ? enumerable : Array.Empty<object>();
        return found;
    }

    private static Dictionary<int, EntityUid> BuildPvsIndexMap(IEntityManager entMan)
    {
        var mapping = new Dictionary<int, EntityUid>(1024);
        foreach (var meta in entMan.AllComponentsList<MetaDataComponent>())
        {
            var boxedIndex = MetaPvsDataField.GetValue(meta.Component);
            if (boxedIndex == null)
                continue;

            var index = (int) PvsIndexValueProperty.GetValue(boxedIndex)!;
            if (index < 0)
                continue;

            mapping[index] = meta.Uid;
        }

        return mapping;
    }

    private static string CategorizeEntity(IEntityManager entMan, EntityUid uid, MetaDataComponent meta)
    {
        var isDoor = entMan.TryGetComponent(uid, out DoorComponent? _);
        var isReader = entMan.TryGetComponent(uid, out AccessReaderComponent? _);

        if (isDoor && isReader)
            return "door_access_reader";
        if (entMan.TryGetComponent(uid, out WH40KInfluencePointComponent? _))
            return "influence_point";
        if (entMan.TryGetComponent(uid, out WH40KTacticalMapComponent? _))
            return "tactical_map";
        if (entMan.TryGetComponent(uid, out ActorComponent? _))
            return "actor";
        if (isDoor)
            return "door";
        if (isReader)
            return "access_reader";
        if (entMan.TryGetComponent(uid, out MobStateComponent? _))
            return "mob";
        return meta.EntityPrototype?.ID is { Length: > 0 } prototype
            ? $"proto:{prototype}"
            : "other";
    }

    private static int CountEnumerable(IEnumerable enumerable)
    {
        var count = 0;
        foreach (var _ in enumerable)
        {
            count++;
        }

        return count;
    }

    private static IReadOnlyList<AccessHotspot> FindAccessHotspots(IEntityManager entMan, SharedTransformSystem xform, EntityUid gridUid)
    {
        var buckets = new Dictionary<Vector2i, AccessHotspotAccumulator>();
        var invGridMatrix = xform.GetInvWorldMatrix(entMan.GetComponent<TransformComponent>(gridUid));

        foreach (var door in entMan.AllComponentsList<DoorComponent>())
        {
            if (!entMan.TryGetComponent(door.Uid, out TransformComponent? transform) || transform.GridUid != gridUid)
                continue;

            var local = Vector2.Transform(xform.GetWorldPosition(transform), invGridMatrix);
            var bucket = new Vector2i(
                (int) MathF.Floor(local.X / HotspotBucketSize),
                (int) MathF.Floor(local.Y / HotspotBucketSize));

            ref var acc = ref CollectionsMarshal.GetValueRefOrAddDefault(buckets, bucket, out _);
            acc.Center += local;
            acc.Count++;
            acc.Doors++;
        }

        foreach (var reader in entMan.AllComponentsList<AccessReaderComponent>())
        {
            if (!entMan.TryGetComponent(reader.Uid, out TransformComponent? transform) || transform.GridUid != gridUid)
                continue;

            var local = Vector2.Transform(xform.GetWorldPosition(transform), invGridMatrix);
            var bucket = new Vector2i(
                (int) MathF.Floor(local.X / HotspotBucketSize),
                (int) MathF.Floor(local.Y / HotspotBucketSize));

            ref var acc = ref CollectionsMarshal.GetValueRefOrAddDefault(buckets, bucket, out _);
            acc.Center += local;
            acc.Count++;
            acc.AccessReaders++;
        }

        return buckets
            .Select(pair =>
            {
                var acc = pair.Value;
                var center = acc.Count > 0
                    ? acc.Center / acc.Count
                    : new Vector2((pair.Key.X + 0.5f) * HotspotBucketSize, (pair.Key.Y + 0.5f) * HotspotBucketSize);

                return new AccessHotspot(
                    pair.Key,
                    center,
                    acc.Doors,
                    acc.AccessReaders);
            })
            .OrderByDescending(h => h.Score)
            .Take(6)
            .ToArray();
    }

    private static IReadOnlyList<EntityUid> FindInfluencePoints(IEntityManager entMan, EntityUid gridUid)
    {
        return entMan.AllComponentsList<WH40KInfluencePointComponent>()
            .Where(entry => entMan.TryGetComponent(entry.Uid, out TransformComponent? transform) && transform.GridUid == gridUid)
            .Select(entry => entry.Uid)
            .ToArray();
    }

    private static IReadOnlyList<Vector2> BuildWideWaypoints(Box2 bounds, int count)
    {
        var columns = Math.Max(2, (int) Math.Ceiling(Math.Sqrt(count)));
        var rows = Math.Max(2, (int) Math.Ceiling(count / (float) columns));
        var points = new List<Vector2>(columns * rows);
        var stepX = (bounds.Right - bounds.Left) / (columns + 1);
        var stepY = (bounds.Top - bounds.Bottom) / (rows + 1);

        for (var y = 1; y <= rows; y++)
        {
            for (var x = 1; x <= columns && points.Count < count; x++)
            {
                points.Add(new Vector2(bounds.Left + x * stepX, bounds.Bottom + y * stepY));
            }
        }

        return points;
    }

    private static IReadOnlyList<Vector2> BuildObserverSweepPoints(Box2 bounds, IReadOnlyList<AccessHotspot> hotspots)
    {
        var points = new List<Vector2>
        {
            new(bounds.Left + 12f, bounds.Bottom + 12f),
            new(bounds.Right - 12f, bounds.Bottom + 12f),
            new(bounds.Right - 12f, bounds.Top - 12f),
            new(bounds.Left + 12f, bounds.Top - 12f),
            bounds.Center
        };

        foreach (var hotspot in hotspots)
        {
            points.Add(hotspot.Center);
        }

        return points;
    }

    private static Vector2 BuildPointOffsetPosition(
        IEntityManager entMan,
        SharedTransformSystem xform,
        EntityUid gridUid,
        EntityUid point,
        int index,
        float radius)
    {
        var invGridMatrix = xform.GetInvWorldMatrix(entMan.GetComponent<TransformComponent>(gridUid));
        var pointXform = entMan.GetComponent<TransformComponent>(point);
        var center = Vector2.Transform(xform.GetWorldPosition(pointXform), invGridMatrix);
        var angle = (index % 8) / 8f * MathF.Tau;
        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static bool TryGetOpenBui(TestPair pair, EntityUid clientTablet, out BoundUserInterface? bui)
    {
        var entMan = pair.Client.ResolveDependency<IEntityManager>();
        var uiComp = entMan.GetComponent<UserInterfaceComponent>(clientTablet);
        return uiComp.ClientOpenInterfaces.TryGetValue(WH40KTacticalMapUiKey.Key, out bui) && bui != null;
    }

    private static void ExportResults(IReadOnlyList<ScenarioResult> results, BattlefieldBenchContext ctx)
    {
        var outputPath = Environment.GetEnvironmentVariable("WH14K_BATTLEFIELD_BENCHMARK_EXPORT");
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            playerCount = ctx.AllSessions.Length,
            dummyCount = ctx.DummySessions.Length,
            hotspots = ctx.AccessHotspots.Select(h => new
            {
                bucketX = h.Bucket.X,
                bucketY = h.Bucket.Y,
                centerX = h.Center.X,
                centerY = h.Center.Y,
                h.Doors,
                h.AccessReaders,
                h.Score
            }).ToArray(),
            scenarios = results.Select(result => new
            {
                result.Name,
                fullTick = result.FullTick,
                dummyPvs = result.DummyPvs
            }).ToArray()
        };

        File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private sealed class BattlefieldBenchContext
    {
        public ICommonSession[] AllSessions = Array.Empty<ICommonSession>();
        public ICommonSession[] DummySessions = Array.Empty<ICommonSession>();
        public ICommonSession RealSession = default!;
        public EntityUid RealActor;
        public EntityUid GridUid;
        public Box2 Bounds;
        public IReadOnlyList<Vector2> WideWaypoints = Array.Empty<Vector2>();
        public IReadOnlyList<AccessHotspot> AccessHotspots = Array.Empty<AccessHotspot>();
        public IReadOnlyList<Vector2> ObserverSweepPoints = Array.Empty<Vector2>();
        public IReadOnlyList<EntityUid> InfluencePoints = Array.Empty<EntityUid>();
        public readonly List<ICommonSession> ImperiumSessions = new();
        public readonly List<ICommonSession> HereticsSessions = new();
        public readonly List<ICommonSession> TabletOwners = new();
        public readonly Dictionary<NetUserId, EntityUid> TabletsByUserId = new();
        public EntityUid ClientTablet = EntityUid.Invalid;
        public bool ClientTabletOpen;
    }

    private readonly record struct AccessHotspot(Vector2i Bucket, Vector2 Center, int Doors, int AccessReaders)
    {
        public int Score => Doors + AccessReaders;
    }

    private struct AccessHotspotAccumulator
    {
        public Vector2 Center;
        public int Doors;
        public int AccessReaders;
        public int Count;
    }

    private readonly record struct PvsSnapshot(
        int Sent,
        int Dirty,
        int Enter,
        int New,
        string PeakSession,
        Dictionary<string, int> Categories);

    private sealed class ScenarioMetrics
    {
        public readonly List<double> TickSamples = new(MeasureTicks);
        public readonly Dictionary<string, int> PeakCategories = new(StringComparer.Ordinal);
        public string PeakSession = string.Empty;
        public int PeakSent;
        public int PeakDirty;
        public int PeakEnter;
        public int PeakNew;
        public int PeakSentThreshold => PeakSent;
        private long _sumSent;

        public void Register(PvsSnapshot snapshot)
        {
            _sumSent += snapshot.Sent;

            if (snapshot.Sent <= PeakSent)
                return;

            PeakSent = snapshot.Sent;
            PeakDirty = snapshot.Dirty;
            PeakEnter = snapshot.Enter;
            PeakNew = snapshot.New;
            PeakSession = snapshot.PeakSession;
            PeakCategories.Clear();

            foreach (var (key, value) in snapshot.Categories)
            {
                PeakCategories[key] = value;
            }
        }

        public ScenarioSummary ToSummary()
        {
            var ordered = TickSamples.OrderBy(x => x).ToArray();
            var p95Index = ordered.Length == 0 ? 0 : Math.Clamp((int) Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);

            return new ScenarioSummary(
                TickSamples.Count == 0 ? 0 : TickSamples.Average(),
                ordered.Length == 0 ? 0 : ordered[p95Index],
                TickSamples.Count == 0 ? 0 : TickSamples.Max(),
                TickSamples.Count == 0 ? 0 : _sumSent / (double) TickSamples.Count,
                PeakSent,
                PeakDirty,
                PeakEnter,
                PeakNew,
                PeakSession,
                new Dictionary<string, int>(PeakCategories, StringComparer.Ordinal));
        }
    }

    public sealed record ScenarioSummary(
        double AverageMs,
        double P95Ms,
        double MaxMs,
        double AverageSent,
        int PeakSent,
        int PeakDirty,
        int PeakEnter,
        int PeakNew,
        string PeakSession,
        Dictionary<string, int> PeakCategories);

    public sealed record ScenarioResult(
        string Name,
        ScenarioSummary FullTick,
        ScenarioSummary DummyPvs);
}
