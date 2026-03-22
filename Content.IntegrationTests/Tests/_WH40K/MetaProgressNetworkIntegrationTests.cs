#nullable enable
using System;
using System.Linq;
using System.Net;
using Content.IntegrationTests.Pair;
using Content.Server.Database;
using Content.Server._WH40K.Stats;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using ClientMetaProgressSystem = Content.Client._WH40K.MetaProgress.WH40KMetaProgressSystem;
using ServerMetaProgressSystem = Content.Server._WH40K.MetaProgress.WH40KMetaProgressSystem;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
[NonParallelizable]
public sealed class MetaProgressNetworkIntegrationTests
{
    [Test]
    public async Task BackgroundStatChangesStayServerSideUntilClientRequestsSnapshot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            InLobby = true,
            Dirty = true,
            Fresh = true
        });

        var server = pair.Server;
        var client = pair.Client;
        var db = server.ResolveDependency<IServerDbManager>();
        NetUserId userId = default;

        await server.WaitAssertion(() =>
        {
            userId = server.ResolveDependency<IPlayerManager>().Sessions.Single().UserId;
        });

        await db.UpdatePlayerRecordAsync(userId, "MetaNetworkSubscriberTest", IPAddress.Loopback, null);

        var updateCount = 0;
        await client.WaitPost(() =>
        {
            var meta = client.System<ClientMetaProgressSystem>();
            meta.SnapshotUpdated += _ => updateCount++;
        });

        await server.WaitPost(() =>
        {
            var meta = server.System<ServerMetaProgressSystem>();
            var stats = server.System<WH40KPlayerStatsSystem>();

            _ = meta.GetSnapshot(userId);
            stats.Record(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccess, 1);
        });

        await RunForServerTimeAsync(pair, TimeSpan.FromMilliseconds(700));

        await client.WaitAssertion(() =>
        {
            var meta = client.System<ClientMetaProgressSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(updateCount, Is.EqualTo(0),
                    "Background MetaProgress stat changes should not push snapshots to clients that never requested them.");
                Assert.That(meta.HasCache, Is.False,
                    "Client unexpectedly received a MetaProgress snapshot without opening/requesting the UI.");
            });
        });

        await client.WaitPost(() =>
        {
            var meta = client.System<ClientMetaProgressSystem>();
            meta.RequestSnapshot(force: true);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var meta = client.System<ClientMetaProgressSystem>();
            Assert.That(updateCount, Is.EqualTo(1), "Client should receive exactly one MetaProgress snapshot after the explicit request.");
            Assert.That(meta.TryGetCachedSnapshot(out var snapshot), Is.True);

            var frontlineAnchor = snapshot.Achievements.Single(entry => entry.Id == "wh40k-ach-frontline-anchor");
            Assert.That(frontlineAnchor.Progress, Is.EqualTo(1),
                "Explicit snapshot request did not return the latest server-side achievement progress.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RequestedMetaSnapshotCoalescesRapidStatBurstsIntoSingleDelayedUpdate()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            InLobby = true,
            Dirty = true,
            Fresh = true
        });

        var server = pair.Server;
        var client = pair.Client;
        var db = server.ResolveDependency<IServerDbManager>();
        NetUserId userId = default;

        await server.WaitAssertion(() =>
        {
            userId = server.ResolveDependency<IPlayerManager>().Sessions.Single().UserId;
        });

        await db.UpdatePlayerRecordAsync(userId, "MetaNetworkBurstTest", IPAddress.Loopback, null);

        var updateCount = 0;
        await client.WaitPost(() =>
        {
            var meta = client.System<ClientMetaProgressSystem>();
            meta.SnapshotUpdated += _ => updateCount++;
            meta.RequestSnapshot(force: true);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var meta = client.System<ClientMetaProgressSystem>();
            Assert.That(updateCount, Is.EqualTo(1), "Client failed to receive its initial MetaProgress snapshot request.");
            Assert.That(meta.TryGetCachedSnapshot(out var snapshot), Is.True);

            var frontlineAnchor = snapshot.Achievements.Single(entry => entry.Id == "wh40k-ach-frontline-anchor");
            Assert.That(frontlineAnchor.Progress, Is.EqualTo(0));
        });

        await client.WaitPost(() => updateCount = 0);

        await server.WaitPost(() =>
        {
            var meta = server.System<ServerMetaProgressSystem>();
            var stats = server.System<WH40KPlayerStatsSystem>();

            _ = meta.GetSnapshot(userId);
            stats.Record(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccess, 1);
            stats.Record(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccess, 1);
            stats.Record(userId, WH40KPlayerStatKeys.ObjectiveDefenseSuccess, 1);
        });

        await RunForServerTimeAsync(pair, TimeSpan.FromMilliseconds(250));

        await client.WaitAssertion(() =>
        {
            Assert.That(updateCount, Is.EqualTo(0),
                "Queued MetaProgress updates should stay throttled until the delayed background push window expires.");
        });

        await RunForServerTimeAsync(pair, TimeSpan.FromMilliseconds(400));

        await client.WaitAssertion(() =>
        {
            var meta = client.System<ClientMetaProgressSystem>();
            Assert.That(updateCount, Is.EqualTo(1),
                "Bursting multiple stat changes inside the delay window should coalesce into one MetaProgress network update.");
            Assert.That(meta.TryGetCachedSnapshot(out var snapshot), Is.True);

            var frontlineAnchor = snapshot.Achievements.Single(entry => entry.Id == "wh40k-ach-frontline-anchor");
            Assert.That(frontlineAnchor.Progress, Is.EqualTo(3),
                "Coalesced MetaProgress update did not carry the combined achievement delta.");
        });

        await pair.CleanReturnAsync();
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
