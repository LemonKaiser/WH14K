#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WH40K.TacticalMap;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server._WH40K.TacticalMap;
using Content.Shared.GameTicking;
using Content.Shared._WH40K.TacticalMap;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class TacticalMapRoundIntegrationTests
{
    private const string Imperium = "Imperium";
    private const string Heretics = "Heretics";
    private const string LegacyTacticalMapTabletPrototype = "WH40KTacticalMapTablet";

    [Test]
    public async Task TacticalMapAnnotationsSavePersistAndReloadAfterReopen()
    {
        await using var pair = await StartWh40KRoundAsync();

        var context = await PrepareTabletAsync(pair, Imperium, "WH40KCommandTacticalMapTablet");
        var initialState = await OpenTabletAndReadStateAsync(pair, context.Tablet, context.ClientTablet, Imperium);
        var initialCallsigns = initialState.CapturePoints.Select(point => point.Callsign).ToArray();

        Assert.That(initialState.TeamId, Is.EqualTo(Imperium), "Tablet did not resolve the expected team before annotation save.");
        Assert.That(initialState.CapturePoints.Length, Is.GreaterThan(0), "Tactical-map state did not expose any capture points.");
        Assert.That(initialCallsigns, Does.Contain("Alpha"),
            "Capture points did not receive the expected auto-assigned tactical callsign.");
        Assert.That(initialCallsigns.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(initialState.CapturePoints.Length),
            "Capture point callsigns were not unique inside one round.");

        var expectedStroke = new WH40KTacticalMapAnnotationStroke(
            new[]
            {
                context.PointA,
                context.PointA + new Vector2(6f, 3f),
                context.PointA + new Vector2(10f, 5f),
            },
            Color.LimeGreen,
            2.25f);

        await pair.Client.WaitPost(() =>
        {
            var bui = GetOpenBui(pair, context.ClientTablet);
            bui.SendMessage(new WH40KTacticalMapSaveAnnotationsMessage(new[] { expectedStroke }));
        });

        await pair.RunTicksSync(20);

        await pair.Client.WaitAssertion(() =>
        {
            var state = GetCachedState(pair, context.ClientTablet);

            Assert.Multiple(() =>
            {
                Assert.That(state.TeamId, Is.EqualTo(Imperium));
                Assert.That(state.AnnotationStrokes.Length, Is.EqualTo(1), "Expected one saved annotation stroke in cached tactical-map state.");
                AssertStrokeEquivalent(state.AnnotationStrokes[0], expectedStroke);
            });
        });

        await CloseTabletAsync(pair, context.Tablet, context.Actor);
        await OpenTabletAsync(pair, context.Tablet, context.Actor);

        await pair.Client.WaitAssertion(() =>
        {
            var reopenedState = GetCachedState(pair, context.ClientTablet);
            var bui = GetOpenBui(pair, context.ClientTablet);

            Assert.Multiple(() =>
            {
                Assert.That(bui.IsOpened, Is.True, "Tactical-map BUI did not stay open after reopen.");
                Assert.That(reopenedState.TeamId, Is.EqualTo(Imperium));
                Assert.That(reopenedState.AnnotationStrokes.Length, Is.EqualTo(1), "Reopened tactical-map state did not rehydrate saved annotations.");
                AssertStrokeEquivalent(reopenedState.AnnotationStrokes[0], expectedStroke);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TacticalMapAlliedMarkersTrackSameTeamMembersOnly()
    {
        await using var pair = await StartWh40KRoundAsync();
        var context = await PrepareTabletAsync(pair, Imperium, "WH40KCommandTacticalMapTablet");

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var metaData = entMan.EntitySysManager.GetEntitySystem<MetaDataSystem>();

            var ally = entMan.SpawnEntity("MobHuman", new EntityCoordinates(context.GridUid, context.PointB));
            entMan.EnsureComponent<WH40KTeamMemberComponent>(ally).TeamId = Imperium;
            metaData.SetEntityName(ally, "Tactical Ally");

            var enemy = entMan.SpawnEntity("MobHuman", new EntityCoordinates(context.GridUid, context.PointB + new Vector2(3f, 0f)));
            entMan.EnsureComponent<WH40KTeamMemberComponent>(enemy).TeamId = Heretics;
            metaData.SetEntityName(enemy, "Tactical Enemy");
        });

        await pair.RunTicksSync(20);

        var state = await OpenTabletAndReadStateAsync(pair, context.Tablet, context.ClientTablet, Imperium);
        var labels = state.AlliedMarkers.Select(marker => marker.Label).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(labels, Does.Contain("Tactical Ally"),
                "Tactical-map allied markers did not include a same-team tracked mob without ActorComponent.");
            Assert.That(labels, Does.Not.Contain("Tactical Enemy"),
                "Tactical-map allied markers leaked an opposing-team mob into the same-team overlay.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TacticalMapOverlayRefreshIsThrottledButStillCatchesUp()
    {
        await using var pair = await StartWh40KRoundAsync();
        var context = await PrepareTabletAsync(pair, Imperium, "WH40KCommandTacticalMapTablet");

        EntityUid ally = default;
        const string allyName = "Moving Ally";
        var movedPosition = Vector2.Lerp(context.PointA, context.PointB, 0.35f);

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var metaData = entMan.EntitySysManager.GetEntitySystem<MetaDataSystem>();

            ally = entMan.SpawnEntity("MobHuman", new EntityCoordinates(context.GridUid, context.PointB));
            entMan.EnsureComponent<WH40KTeamMemberComponent>(ally).TeamId = Imperium;
            metaData.SetEntityName(ally, allyName);
        });

        await pair.RunTicksSync(20);

        var initialState = await OpenTabletAndReadStateAsync(pair, context.Tablet, context.ClientTablet, Imperium);
        var initialMarker = initialState.AlliedMarkers.Single(marker => marker.Label == allyName);

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            xform.SetCoordinates(ally, new EntityCoordinates(context.GridUid, movedPosition));
        });

        await RunForServerTimeAsync(pair, TimeSpan.FromMilliseconds(250));

        await pair.Client.WaitAssertion(() =>
        {
            var throttledState = GetCachedState(pair, context.ClientTablet);
            var throttledMarker = throttledState.AlliedMarkers.Single(marker => marker.Label == allyName);

            Assert.Multiple(() =>
            {
                Assert.That(throttledState.OverlayRevision, Is.EqualTo(initialState.OverlayRevision),
                    "Tactical-map overlay refreshed again before the server-side throttle window elapsed.");
                Assert.That(Vector2.Distance(throttledMarker.Position, initialMarker.Position), Is.LessThan(0.001f),
                    "Allied marker moved on the client before the throttled overlay refresh window elapsed.");
            });
        });

        await RunForServerTimeAsync(pair, TimeSpan.FromMilliseconds(400));

        await pair.Client.WaitAssertion(() =>
        {
            var refreshedState = GetCachedState(pair, context.ClientTablet);
            var refreshedMarker = refreshedState.AlliedMarkers.Single(marker => marker.Label == allyName);

            Assert.Multiple(() =>
            {
                Assert.That(refreshedState.OverlayRevision, Is.Not.EqualTo(initialState.OverlayRevision),
                    "Tactical-map overlay never refreshed after the throttle window elapsed.");
                Assert.That(Vector2.Distance(refreshedMarker.Position, movedPosition), Is.LessThan(0.15f),
                    "Allied marker did not catch up to the moved server position after the throttle window elapsed.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TacticalMapFogOfWarRemainsSeparatedPerTeam()
    {
        await using var pair = await StartWh40KRoundAsync();
        var context = await PrepareTabletAsync(pair, Imperium, "WH40KCommandTacticalMapTablet");

        var imperiumSeedChunks = new[]
        {
            new Vector2i(-20, 4),
            new Vector2i(-19, 4),
            new Vector2i(-18, 4),
        };

        var hereticSeedChunks = new[]
        {
            new Vector2i(12, -7),
            new Vector2i(13, -7),
            new Vector2i(14, -7),
        };

        await SeedServerFogAsync(pair, context.GridUid, Imperium, imperiumSeedChunks);
        await SeedServerFogAsync(pair, context.GridUid, Heretics, hereticSeedChunks);
        await pair.RunTicksSync(10);

        var imperiumServerFog = await WaitForServerFogSnapshotAsync(pair, context.GridUid, Imperium);
        var imperiumState = await OpenTabletAndReadStateAsync(
            pair,
            context.Tablet,
            context.ClientTablet,
            Imperium,
            imperiumServerFog.Revision);
        var imperiumChunks = imperiumState.RevealedChunks.ToHashSet();
        Assert.That(imperiumChunks.SetEquals(imperiumServerFog.RevealedChunks), Is.True,
            "Imperium tactical-map client state did not match the server fog snapshot.");
        var imperiumExclusiveChunk = imperiumChunks.First();

        await pair.Client.WaitAssertion(() =>
        {
            var bui = GetOpenBui(pair, context.ClientTablet);

            Assert.Multiple(() =>
            {
                Assert.That(bui.IsOpened, Is.True);
                Assert.That(imperiumState.TeamId, Is.EqualTo(Imperium));
                Assert.That(imperiumChunks, Does.Contain(imperiumExclusiveChunk));
            });
        });

        await CloseTabletAsync(pair, context.Tablet, context.Actor);

        await SetActorTeamAndPositionAsync(pair, context.Actor, Heretics, context.GridUid, context.PointB);
        await pair.RunTicksSync(10);

        var hereticsServerFog = await WaitForServerFogSnapshotAsync(pair, context.GridUid, Heretics);
        var hereticsState = await OpenTabletAndReadStateAsync(
            pair,
            context.Tablet,
            context.ClientTablet,
            Heretics,
            hereticsServerFog.Revision);
        var hereticChunks = hereticsState.RevealedChunks.ToHashSet();
        Assert.That(hereticChunks.SetEquals(hereticsServerFog.RevealedChunks), Is.True,
            "Heretics tactical-map client state did not match the server fog snapshot.");
        var hasHereticExclusiveChunk = hereticChunks.Any(chunk => !imperiumChunks.Contains(chunk));
        Assert.That(hasHereticExclusiveChunk, Is.True,
            "Heretics fog state did not produce any chunk outside the Imperium reveal set.");
        var hereticExclusiveChunk = hereticChunks.First(chunk => !imperiumChunks.Contains(chunk));

        await pair.Client.WaitAssertion(() =>
        {
            var bui = GetOpenBui(pair, context.ClientTablet);

            Assert.Multiple(() =>
            {
                Assert.That(bui.IsOpened, Is.True);
                Assert.That(hereticsState.TeamId, Is.EqualTo(Heretics));
                Assert.That(hereticChunks.Count, Is.GreaterThan(0));
                Assert.That(hereticChunks, Does.Not.Contain(imperiumExclusiveChunk),
                    "Heretics fog state unexpectedly included a chunk first revealed for Imperium.");
            });
        });
        await CloseTabletAsync(pair, context.Tablet, context.Actor);

        await SetActorTeamAsync(pair, context.Actor, Imperium);
        await pair.RunTicksSync(5);

        var imperiumStateReloaded = await OpenTabletAndReadStateAsync(
            pair,
            context.Tablet,
            context.ClientTablet,
            Imperium,
            imperiumServerFog.Revision);

        await pair.Client.WaitAssertion(() =>
        {
            var bui = GetOpenBui(pair, context.ClientTablet);

            Assert.Multiple(() =>
            {
                Assert.That(bui.IsOpened, Is.True);
                Assert.That(imperiumStateReloaded.TeamId, Is.EqualTo(Imperium));
                Assert.That(imperiumStateReloaded.RevealedChunks, Does.Contain(imperiumExclusiveChunk));
                Assert.That(imperiumStateReloaded.RevealedChunks, Does.Not.Contain(hereticExclusiveChunk),
                    "Imperium fog state unexpectedly included a chunk only revealed for Heretics.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TacticalMapRuntimeFogRevealOnlyOpensTheCurrentChunk()
    {
        await using var pair = await StartWh40KRoundAsync();
        var context = await PrepareTabletAsync(pair, Imperium, "WH40KCommandTacticalMapTablet");

        var initialFog = await WaitForServerFogSnapshotAsync(pair, context.GridUid, Imperium);
        var startChunk = initialFog.RevealedChunks.Single();
        var nextChunk = new Vector2i(startChunk.X + 1, startChunk.Y);
        var nextChunkCenter = GetChunkCenter(nextChunk, initialFog.ChunkSize);

        await SetActorTeamAndPositionAsync(pair, context.Actor, Imperium, context.GridUid, nextChunkCenter);
        await pair.RunTicksSync(20);

        var movedFog = await WaitForServerFogSnapshotAsync(pair, context.GridUid, Imperium, nextChunk);
        var newlyRevealed = movedFog.RevealedChunks.Except(initialFog.RevealedChunks).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(newlyRevealed, Has.Length.EqualTo(1),
                "Moving into a neighboring chunk revealed more than the single current chunk.");
            Assert.That(newlyRevealed, Does.Contain(nextChunk),
                "Moving into a neighboring chunk did not reveal the chunk the actor actually entered.");
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
            DummyTicker = false
        });

        await pair.WaitCommand("forcemap Battlefield40k");
        await pair.WaitCommand("setgamepreset WH40KTeamBattle 9999");
        await pair.WaitClientCommand("toggleready True");
        await pair.WaitCommand("startround");
        await pair.RunTicksSync(80);

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

    [Test]
    public async Task StandardTacticalMapTabletRejectsAnnotationSaveMessages()
    {
        await using var pair = await StartWh40KRoundAsync();
        var context = await PrepareTabletAsync(pair, Imperium, "WH40KStandardTacticalMapTablet");

        var state = await OpenTabletAndReadStateAsync(pair, context.Tablet, context.ClientTablet, Imperium);
        Assert.That(state.CanAnnotate, Is.False, "Standard tactical tablet unexpectedly exposed annotation capability in UI state.");

        var blockedStroke = new WH40KTacticalMapAnnotationStroke(
            new[]
            {
                context.PointA,
                context.PointA + new Vector2(4f, 0f),
            },
            Color.Red,
            1.5f);

        await pair.Client.WaitPost(() =>
        {
            var bui = GetOpenBui(pair, context.ClientTablet);
            bui.SendMessage(new WH40KTacticalMapSaveAnnotationsMessage(new[] { blockedStroke }));
        });

        await pair.RunTicksSync(20);

        await pair.Client.WaitAssertion(() =>
        {
            var nextState = GetCachedState(pair, context.ClientTablet);

            Assert.Multiple(() =>
            {
                Assert.That(nextState.CanAnnotate, Is.False);
                Assert.That(nextState.AnnotationStrokes, Has.Length.EqualTo(0),
                    "Read-only tactical tablet managed to persist a saved annotation through a direct BUI message.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TacticalMapConsolePrototypesExistWithCorrectCapabilitiesAndLegacyAliasIsRemoved()
    {
        await using var pair = await StartWh40KRoundAsync();

        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();

            Assert.That(protoMan.TryIndex<EntityPrototype>(LegacyTacticalMapTabletPrototype, out _), Is.False,
                "Legacy tactical-map tablet alias still exists after the prototype split.");

            var actor = playerMan.Sessions.Single().AttachedEntity!.Value;
            var actorCoords = entMan.GetComponent<TransformComponent>(actor).Coordinates;
            var commandConsole = entMan.SpawnEntity("WH40KCommandTacticalMapConsole", actorCoords);
            var standardConsole = entMan.SpawnEntity("WH40KStandardTacticalMapConsole", actorCoords);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WH40KTacticalMapComponent>(commandConsole).CanAnnotate, Is.True,
                    "Command tactical-map console unexpectedly spawned without annotation capability.");
                Assert.That(entMan.GetComponent<WH40KTacticalMapComponent>(standardConsole).CanAnnotate, Is.False,
                    "Standard tactical-map console unexpectedly spawned with annotation capability.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TacticalMapBlackoutStorageWritesGridMask()
    {
        await using var pair = await PoolManager.GetServerClient();
        var mapPath = new ResPath("/Maps/Test/empty.yml");
        EntityUid gridUid = default;
        Vector2i expectedTile = default;

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var loader = entMan.EntitySysManager.GetEntitySystem<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
            var blackoutSystem = entMan.EntitySysManager.GetEntitySystem<SharedWH40KTacticalMapBlackoutSystem>();
            var mapSystem = entMan.EntitySysManager.GetEntitySystem<SharedMapSystem>();

            Assert.That(loader.TryLoadMap(mapPath, out _, out var grids), Is.True,
                $"Failed to load tactical-map blackout test map '{mapPath}'.");
            Assert.That(grids, Has.Count.EqualTo(1), "Blackout test map did not produce exactly one grid.");

            gridUid = grids!.Single();
            var grid = entMan.GetComponent<MapGridComponent>(gridUid);
            expectedTile = mapSystem.LocalToTile(gridUid, grid, new EntityCoordinates(gridUid, new Vector2(-0.5f, -0.5f)));
            blackoutSystem.SetBlackout((gridUid, grid, null), expectedTile, true);
        });

        await pair.RunTicksSync(5);

        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var grid = entMan.GetComponent<MapGridComponent>(gridUid);
            var blackoutSystem = entMan.EntitySysManager.GetEntitySystem<SharedWH40KTacticalMapBlackoutSystem>();
            var hasBlackout = entMan.TryGetComponent<WH40KTacticalMapBlackoutComponent>(gridUid, out var blackout);

            Assert.That(hasBlackout, Is.True,
                "Tactical-map blackout storage did not create blackout data on its target grid.");
            Assert.That(blackout, Is.Not.Null,
                "Tactical-map blackout storage did not leave a readable blackout component on the grid.");

            var blackoutComp = blackout!;

            Assert.Multiple(() =>
            {
                Assert.That(blackoutSystem.IsBlackedOut((gridUid, grid, blackoutComp), expectedTile), Is.True,
                    "Tactical-map blackout storage did not mark its expected tile as blacked out.");
                Assert.That(blackoutSystem.IsBlackedOut((gridUid, grid, blackoutComp), expectedTile + Vector2i.Right), Is.False,
                    "Tactical-map blackout storage unexpectedly masked a neighboring tile.");
            });
        });

        await pair.CleanReturnAsync();
    }

    private static async Task<(EntityUid Tablet, EntityUid ClientTablet, EntityUid Actor, EntityUid GridUid, Vector2 PointA, Vector2 PointB)> PrepareTabletAsync(
        TestPair pair,
        string teamId,
        string tabletPrototype)
    {
        EntityUid tablet = default;
        EntityUid clientTablet = default;
        EntityUid actor = default;
        EntityUid gridUid = default;
        Vector2 pointA = default;
        Vector2 pointB = default;

        await pair.Server.WaitAssertion(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            var hands = entMan.EntitySysManager.GetEntitySystem<HandsSystem>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();

            actor = playerMan.Sessions.Single().AttachedEntity!.Value;
            var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(actor);
            member.TeamId = teamId;

            var actorXform = entMan.GetComponent<TransformComponent>(actor);
            Assert.That(actorXform.GridUid, Is.Not.Null, "Attached player is not standing on a grid.");
            gridUid = actorXform.GridUid!.Value;

            var grid = entMan.GetComponent<MapGridComponent>(gridUid);
            (pointA, pointB) = PickSeparatedPoints(grid.LocalAABB);

            xform.SetCoordinates(actor, new EntityCoordinates(gridUid, pointA));
            tablet = entMan.SpawnEntity(tabletPrototype, actorXform.Coordinates);
            Assert.That(hands.TryForcePickupAnyHand(actor, tablet), Is.True, "Failed to place tactical-map tablet in the actor hand.");
        });

        await pair.RunTicksSync(20);

        await pair.Client.WaitAssertion(() =>
        {
            clientTablet = pair.ToClientUid(tablet);
            Assert.That(clientTablet, Is.Not.EqualTo(EntityUid.Invalid));
        });

        return (tablet, clientTablet, actor, gridUid, pointA, pointB);
    }

    private static async Task SetActorTeamAsync(TestPair pair, EntityUid actor, string teamId)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(actor);
            member.TeamId = teamId;
        });
    }

    private static async Task SetActorTeamAndPositionAsync(TestPair pair, EntityUid actor, string teamId, EntityUid gridUid, Vector2 position)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();
            var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(actor);
            member.TeamId = teamId;
            xform.SetCoordinates(actor, new EntityCoordinates(gridUid, position));
        });
    }

    private static async Task OpenTabletAsync(TestPair pair, EntityUid tablet, EntityUid actor)
    {
        await pair.Server.WaitPost(() =>
        {
            var ui = pair.Server.ResolveDependency<IEntityManager>().EntitySysManager.GetEntitySystem<UserInterfaceSystem>();
            ui.TryOpenUi(tablet, WH40KTacticalMapUiKey.Key, actor);
        });

        await pair.RunTicksSync(20);
    }

    private static async Task CloseTabletAsync(TestPair pair, EntityUid tablet, EntityUid actor)
    {
        await pair.Server.WaitPost(() =>
        {
            var ui = pair.Server.ResolveDependency<IEntityManager>().EntitySysManager.GetEntitySystem<SharedUserInterfaceSystem>();
            ui.CloseUi(tablet, WH40KTacticalMapUiKey.Key, actor);
        });

        await pair.RunTicksSync(20);
    }

    private static async Task<WH40KTacticalMapBuiState> OpenTabletAndReadStateAsync(
        TestPair pair,
        EntityUid tablet,
        EntityUid clientTablet,
        string? expectedTeamId = null,
        int? minRevealRevision = null)
    {
        var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
        var actor = playerMan.Sessions.Single().AttachedEntity!.Value;
        await OpenTabletAsync(pair, tablet, actor);

        WH40KTacticalMapBuiState? state = null;
        await pair.Client.WaitAssertion(() =>
        {
            var bui = GetOpenBui(pair, clientTablet);

            state = GetCachedState(pair, clientTablet);

            Assert.Multiple(() =>
            {
                Assert.That(bui.IsOpened, Is.True);
                Assert.That(state, Is.Not.Null);
                if (expectedTeamId != null)
                    Assert.That(state!.TeamId, Is.EqualTo(expectedTeamId));
                if (minRevealRevision != null)
                    Assert.That(state!.RevealRevision, Is.GreaterThanOrEqualTo(minRevealRevision.Value));
            });
        });

        return state!;
    }

    private static async Task RunForServerTimeAsync(TestPair pair, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return;

        var targetTime = TimeSpan.Zero;
        await pair.Server.WaitPost(() =>
        {
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            targetTime = timing.CurTime + duration;
        });

        for (var i = 0; i < 240; i++)
        {
            await pair.RunTicksSync(1);

            var reachedTarget = false;
            await pair.Server.WaitPost(() =>
            {
                var timing = pair.Server.ResolveDependency<IGameTiming>();
                reachedTarget = timing.CurTime >= targetTime;
            });

            if (reachedTarget)
                return;
        }

        Assert.Fail($"Server time failed to advance by {duration} within the allotted tick budget.");
    }

    private static async Task SeedServerFogAsync(
        TestPair pair,
        EntityUid gridUid,
        string teamId,
        IReadOnlyCollection<Vector2i> chunks)
    {
        await pair.Server.WaitPost(() =>
        {
            var tacticalMap = pair.Server.ResolveDependency<IEntityManager>()
                .EntitySysManager
                .GetEntitySystem<WH40KTacticalMapSystem>();

            tacticalMap.RevealFogChunks(gridUid, teamId, chunks);
        });
    }

    private static async Task<(int ChunkSize, int Revision, HashSet<Vector2i> RevealedChunks)> WaitForServerFogSnapshotAsync(
        TestPair pair,
        EntityUid gridUid,
        string teamId,
        Vector2i? requiredChunk = null)
    {
        var result = default((int ChunkSize, int Revision, HashSet<Vector2i> RevealedChunks));

        await pair.Server.WaitAssertion(() =>
        {
            var tacticalMap = pair.Server.ResolveDependency<IEntityManager>()
                .EntitySysManager
                .GetEntitySystem<WH40KTacticalMapSystem>();

            Assert.That(
                tacticalMap.TryGetFogSnapshot(gridUid, teamId, out var chunkSize, out var revision, out var revealedChunks),
                Is.True,
                $"Server tactical-map fog state was not created for team '{teamId}'.");

            Assert.That(revealedChunks.Length, Is.GreaterThan(0),
                $"Server tactical-map fog state for team '{teamId}' did not reveal any chunks.");

            if (requiredChunk is { } chunk)
            {
                Assert.That(revealedChunks, Does.Contain(chunk),
                    $"Server tactical-map fog state for team '{teamId}' did not yet include required chunk {chunk}.");
            }

            result = (chunkSize, revision, revealedChunks.ToHashSet());
        });

        return result;
    }

    private static BoundUserInterface GetOpenBui(TestPair pair, EntityUid clientTablet)
    {
        var entMan = pair.Client.ResolveDependency<IEntityManager>();
        var uiComp = entMan.GetComponent<UserInterfaceComponent>(clientTablet);

        Assert.That(
            uiComp.ClientOpenInterfaces.TryGetValue(WH40KTacticalMapUiKey.Key, out var bui),
            Is.True,
            "Client tactical-map BUI did not open for the tablet.");

        Assert.That(bui, Is.Not.Null);
        return bui!;
    }

    private static WH40KTacticalMapBuiState GetCachedState(TestPair pair, EntityUid clientTablet)
    {
        var stateSystem = pair.Client.ResolveDependency<IEntityManager>().EntitySysManager.GetEntitySystem<WH40KTacticalMapStateSystem>();
        Assert.That(stateSystem.TryGetCachedState(clientTablet, out var state), Is.True, "Client tactical-map state cache did not contain the expected tablet.");
        Assert.That(state, Is.Not.Null);
        return state!;
    }

    private static void AssertStrokeEquivalent(WH40KTacticalMapAnnotationStroke actual, WH40KTacticalMapAnnotationStroke expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Color, Is.EqualTo(expected.Color));
            Assert.That(actual.Thickness, Is.EqualTo(expected.Thickness).Within(0.0001f));
            Assert.That(actual.Points.Length, Is.EqualTo(expected.Points.Length));
        });

        for (var i = 0; i < expected.Points.Length; i++)
        {
            Assert.That(Vector2.Distance(actual.Points[i], expected.Points[i]), Is.LessThan(0.001f),
                $"Annotation point {i} did not survive round-trip serialization.");
        }
    }

    private static (Vector2 PointA, Vector2 PointB) PickSeparatedPoints(Box2 bounds)
    {
        var marginX = Math.Clamp((bounds.Right - bounds.Left) * 0.15f, 6f, 48f);
        var marginY = Math.Clamp((bounds.Top - bounds.Bottom) * 0.15f, 6f, 48f);

        var pointA = new Vector2(bounds.Left + marginX, bounds.Bottom + marginY);
        var pointB = new Vector2(bounds.Right - marginX, bounds.Top - marginY);

        if (Vector2.DistanceSquared(pointA, pointB) < 256f)
            pointB = new Vector2(bounds.Right - marginX, bounds.Bottom + marginY);

        return (pointA, pointB);
    }

    private static Vector2 GetChunkCenter(Vector2i chunk, int chunkSize)
    {
        var safeChunkSize = Math.Max(1, chunkSize);
        return new Vector2(
            (chunk.X + 0.5f) * safeChunkSize,
            (chunk.Y + 0.5f) * safeChunkSize);
    }

}
