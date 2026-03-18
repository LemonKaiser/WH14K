#nullable enable
using System;
using System.Linq;
using Content.Client._WH40K.Command;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server._WH40K.Command.Components;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.Command;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class CommandNodeUiIntegrationTests
{
    private const string Imperium = "Imperium";
    private const string Heretics = "Heretics";

    [Test]
    public async Task CommandNodeUiStateAndNonSketchWindowsWorkForBothTeams()
    {
        await using var pair = await StartWh40KRoundAsync();

        var imperiumState = await OpenAndReadStateForTeamAsync(pair, Imperium);
        var hereticsState = await OpenAndReadStateForTeamAsync(pair, Heretics);

        AssertStateHasNonSketchSystems(imperiumState, Imperium);
        AssertStateHasNonSketchSystems(hereticsState, Heretics);

        await pair.Client.WaitAssertion(() =>
        {
            SmokeRenderNonSketchWindows(imperiumState);
            SmokeRenderNonSketchWindows(hereticsState);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CommandNodeUiDeniesOppositeTeamAccess()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;

        EntityUid hereticNode = default;
        EntityUid actor = default;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var ui = entMan.EntitySysManager.GetEntitySystem<UserInterfaceSystem>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();

            hereticNode = FindCommandNodeByTeam(entMan, Heretics);
            actor = playerMan.Sessions.Single().AttachedEntity!.Value;

            var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(actor);
            member.TeamId = Imperium;

            var nodeCoords = entMan.GetComponent<TransformComponent>(hereticNode).Coordinates;
            xform.SetCoordinates(actor, nodeCoords);

            ui.TryOpenUi(hereticNode, WH40KCommandNodeUiKey.Key, actor);
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var ui = entMan.EntitySysManager.GetEntitySystem<SharedUserInterfaceSystem>();

            Assert.That(
                ui.IsUiOpen((hereticNode, entMan.GetComponent<UserInterfaceComponent>(hereticNode)),
                    WH40KCommandNodeUiKey.Key,
                    actor),
                Is.False,
                "Opposite-team actor must not keep command-node UI open.");
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

    private static async Task<WH40KCommandNodeBoundUserInterfaceState> OpenAndReadStateForTeamAsync(
        TestPair pair,
        string teamId)
    {
        var server = pair.Server;
        WH40KCommandNodeBoundUserInterfaceState? state = null;

        await server.WaitAssertion(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var playerMan = server.ResolveDependency<IPlayerManager>();
            var ui = entMan.EntitySysManager.GetEntitySystem<UserInterfaceSystem>();
            var sharedUi = entMan.EntitySysManager.GetEntitySystem<SharedUserInterfaceSystem>();
            var xform = entMan.EntitySysManager.GetEntitySystem<SharedTransformSystem>();

            var node = FindCommandNodeByTeam(entMan, teamId);
            var actor = playerMan.Sessions.Single().AttachedEntity!.Value;

            var member = entMan.EnsureComponent<WH40KTeamMemberComponent>(actor);
            member.TeamId = teamId;

            var nodeCoords = entMan.GetComponent<TransformComponent>(node).Coordinates;
            xform.SetCoordinates(actor, nodeCoords);

            ui.TryOpenUi(node, WH40KCommandNodeUiKey.Key, actor);

            var uiComp = entMan.GetComponent<UserInterfaceComponent>(node);
            Assert.That(
                sharedUi.TryGetUiState<WH40KCommandNodeBoundUserInterfaceState>(
                    (node, uiComp),
                    WH40KCommandNodeUiKey.Key,
                    out var resolved),
                Is.True,
                $"Expected command-node UI state for team '{teamId}'.");

            state = resolved;
        });

        Assert.That(state, Is.Not.Null);
        return state!;
    }

    private static EntityUid FindCommandNodeByTeam(IEntityManager entMan, string teamId)
    {
        var query = entMan.EntityQueryEnumerator<WH40KCommandNodeComponent>();
        while (query.MoveNext(out var uid, out var node))
        {
            if (string.Equals(node.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                return uid;
        }

        Assert.Fail($"Could not find WH40K command node for team '{teamId}'.");
        return default;
    }

    private static void AssertStateHasNonSketchSystems(WH40KCommandNodeBoundUserInterfaceState state, string teamId)
    {
        Assert.Multiple(() =>
        {
            Assert.That(state.TeamId, Is.EqualTo(teamId));
            Assert.That(state.TeamName, Is.Not.Empty);
            Assert.That(state.ActiveBattleTacticId, Is.Not.Empty);
            Assert.That(state.TeamCompositionSummary, Is.Not.Empty);
            Assert.That(state.TeamCompositionLines, Is.Not.Null);
            Assert.That(state.TeamCompositionStaffingLines, Is.Not.Null);
            Assert.That(state.BonusIntel, Is.Not.Null);
            Assert.That(state.BonusIntel.NodePassiveIntervalSeconds, Is.GreaterThan(0f));
            Assert.That(state.TeamEventRuntime, Is.Not.Null);
            Assert.That(state.GlobalMissionRuntime, Is.Not.Null);
            Assert.That(state.TeamMissionRuntime, Is.Not.Null);
            Assert.That(state.MissionBoard, Is.Not.Null);
            Assert.That(state.MissionBoard.SystemTasks, Is.Not.Null);
            Assert.That(state.MissionBoard.SelectableTasks, Is.Not.Null);
        });
    }

    private static void SmokeRenderNonSketchWindows(WH40KCommandNodeBoundUserInterfaceState state)
    {
        var main = new WH40KCommandNodeWindow();
        main.UpdateState(state);
        main.Orphan();

        var composition = new WH40KCommandNodeTeamCompositionWindow();
        composition.UpdateState(state);
        composition.Orphan();

        var missionBoard = new WH40KCommandNodeMissionBoardWindow();
        missionBoard.UpdateState(state);
        missionBoard.Orphan();

        var tactical = new WH40KCommandNodeTacticalBonusesWindow();
        tactical.UpdateState(state, state.ActiveDoctrineId);
        tactical.Orphan();

        var doctrine = new WH40KCommandNodeDoctrineWindow();
        doctrine.UpdateState(state, state.ActiveDoctrineId, state.DoctrineLocked);
        doctrine.Orphan();

        var battleTactic = new WH40KCommandNodeBattleTacticWindow();
        battleTactic.UpdateState(state);
        battleTactic.Orphan();
    }
}
