using System.Linq;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Client._WH40K.AccountLoad;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Robust.Client.State;
using Robust.Server.Player;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests.Lobby;

[TestFixture]
public sealed class AccountLoadStateTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        InLobby = true,
        Destructive = true,
    };

    [Test]
    public async Task PlayerStaysOnMigrationScreenUntilUserDbLoadFinishes()
    {
        var pair = Pair;
        var server = pair.Server;
        var client = pair.Client;

        var clientNet = client.ResolveDependency<IClientNetManager>();
        var clientState = client.ResolveDependency<IStateManager>();
        var playerManager = server.ResolveDependency<IPlayerManager>();
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await PoolManager.WaitUntil(client, () => clientState.CurrentState is LobbyState, maxTicks: 120, tickStep: 2);
        var reconnectName = playerManager.Sessions.Single().Name;

        await client.WaitPost(() => clientNet.ClientDisconnect("Account load gate test"));
        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            var userDb = server.ResolveDependency<UserDbDataManager>();
            userDb.AddOnLoadPlayer(async (_, cancel) =>
            {
                loadStarted.TrySetResult();
                await releaseLoad.Task.WaitAsync(cancel);
            });
        });

        client.SetConnectTarget(server);
        await client.WaitPost(() => clientNet.ClientConnect(null!, 0, reconnectName));
        await pair.RunTicksSync(10);
        await loadStarted.Task;
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            Assert.That(clientState.CurrentState, Is.TypeOf<WH40KAccountLoadState>());
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(playerManager.PlayerCount, Is.EqualTo(1));
            var session = playerManager.Sessions.Single();
            Assert.That(session.AttachedEntity, Is.Null);
        });

        releaseLoad.SetResult();
        await PoolManager.WaitUntil(server, () =>
        {
            if (playerManager.PlayerCount != 1)
                return false;

            var session = playerManager.Sessions.Single();
            var userDb = server.ResolveDependency<UserDbDataManager>();
            return userDb.IsLoadComplete(session);
        }, maxTicks: 240, tickStep: 2);
    }
}
