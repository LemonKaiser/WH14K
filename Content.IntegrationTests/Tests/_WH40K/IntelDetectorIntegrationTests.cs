#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Shared._WH40K.Intel.Detector;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
[NonParallelizable]
public sealed class IntelDetectorIntegrationTests
{
    private const string Imperium = "Imperium";
    private const string ImperiumDetectorPrototype = "WH40KIntelDetectorImperium";

    [Test]
    public async Task EmptyDetectorScansDoNotAdvanceClientLastScan()
    {
        await using var pair = await StartDetectorScenarioAsync();

        await RunForServerTimeAsync(pair, TimeSpan.FromSeconds(2.4));

        WH40KIntelDetectorComponent? clientState = null;
        await pair.Client.WaitAssertion(() =>
        {
            var clientEntMan = pair.Client.ResolveDependency<IEntityManager>();
            clientState = clientEntMan.GetComponent<WH40KIntelDetectorComponent>(pair.ToClientUid(_detector));
        });

        await pair.Server.WaitAssertion(() =>
        {
            var serverEntMan = pair.Server.ResolveDependency<IEntityManager>();
            var state = serverEntMan.GetComponent<WH40KIntelDetectorComponent>(_detector);
            Assert.That(state.NextScanAt, Is.GreaterThan(TimeSpan.Zero),
                "Server did not advance the detector scan timer while scans were running.");
        });

        Assert.That(clientState, Is.Not.Null);
        var state = clientState!;
        Assert.Multiple(() =>
        {
            Assert.That(state.Enabled, Is.True, "Detector did not stay enabled on the client.");
            Assert.That(state.LastScan, Is.EqualTo(TimeSpan.Zero),
                "Empty detector scans should not keep pushing identical network state to the client.");
            Assert.That(state.Blips, Is.Empty,
                "Empty detector scans should not populate any blips.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DetectorBlipsStayStableAcrossIdenticalScans()
    {
        await using var pair = await StartDetectorScenarioAsync(
            new[]
            {
                new Vector2(-2f, 0f),
                new Vector2(2f, 0f),
            });

        WH40KIntelDetectorComponent? initialState = null;
        WH40KIntelDetectorComponent? refreshedState = null;

        await RunForServerTimeAsync(pair, TimeSpan.FromSeconds(1.3));

        await pair.Client.WaitAssertion(() =>
        {
            var clientEntMan = pair.Client.ResolveDependency<IEntityManager>();
            initialState = clientEntMan.GetComponent<WH40KIntelDetectorComponent>(pair.ToClientUid(_detector));

            Assert.That(initialState.Blips, Has.Count.EqualTo(2), "Detector did not report both tracked markers.");
            Assert.That(initialState.Blips[0].Coordinates.Position.X, Is.LessThan(initialState.Blips[1].Coordinates.Position.X),
                "Detector blips were not returned in a stable sorted order.");
        });

        Assert.That(initialState, Is.Not.Null, "Detector never synchronized its initial blip state.");
        var initial = initialState!;
        var firstScan = initial.LastScan;
        var blipsSnapshot = initial.Blips.ToArray();

        await RunForServerTimeAsync(pair, TimeSpan.FromSeconds(1.2));

        await pair.Client.WaitAssertion(() =>
        {
            var clientEntMan = pair.Client.ResolveDependency<IEntityManager>();
            refreshedState = clientEntMan.GetComponent<WH40KIntelDetectorComponent>(pair.ToClientUid(_detector));
        });

        Assert.That(refreshedState, Is.Not.Null);
        var state = refreshedState!;
        Assert.Multiple(() =>
        {
            Assert.That(state.LastScan, Is.GreaterThan(firstScan),
                "Detector should still refresh the scan pulse when the blip set stays visible.");
            Assert.That(state.Blips, Is.EqualTo(blipsSnapshot).AsCollection,
                "Detector blips changed order or content even though the scene stayed unchanged.");
        });

        await pair.CleanReturnAsync();
    }

    private EntityUid _detector = EntityUid.Invalid;

    private async Task<TestPair> StartDetectorScenarioAsync(Vector2[]? markerPositions = null)
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
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound),
                    "WH40K round did not start for the intel detector test.");
                Assert.That(playerMan.Sessions.Single().AttachedEntity, Is.Not.Null,
                    "Test player was not attached to an in-round entity.");
            });
        });

        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.ResolveDependency<IEntityManager>();
            var mapMan = pair.Server.ResolveDependency<IMapManager>();
            var playerMan = pair.Server.ResolveDependency<IPlayerManager>();
            var mapSystem = entMan.System<SharedMapSystem>();
            var hands = entMan.EntitySysManager.GetEntitySystem<HandsSystem>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();

            var actor = playerMan.Sessions.Single().AttachedEntity!.Value;
            var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(actor);
            member.TeamId = Imperium;

            mapSystem.CreateMap(out var mapId);
            var grid = mapMan.CreateGridEntity(mapId);
            mapSystem.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(1));

            var actorCoords = new EntityCoordinates(grid.Owner, 0f, 0f);
            xform.SetCoordinates(actor, actorCoords);

            _detector = entMan.SpawnEntity(ImperiumDetectorPrototype, actorCoords);
            Assert.That(hands.TryForcePickupAnyHand(actor, _detector), Is.True,
                "Failed to place the intel detector in the actor's hand.");

            var detectorComp = entMan.GetComponent<WH40KIntelDetectorComponent>(_detector);
            detectorComp.Enabled = true;
            detectorComp.LastUser = actor;
            detectorComp.NextScanAt = TimeSpan.Zero;
            entMan.Dirty(_detector, detectorComp);

            if (markerPositions is not null)
            {
                foreach (var position in markerPositions)
                {
                    entMan.SpawnEntity("WH40KSignalFlareMarker", new EntityCoordinates(grid.Owner, position));
                }
            }
        });

        await pair.RunTicksSync(20);
        return pair;
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
}
