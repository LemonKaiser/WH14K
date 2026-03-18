using System;
using System.Linq;
using System.Net;
using Content.Server.Database;
using Content.Server.GameTicking.Events;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Stats;
using Content.Shared.GameTicking;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class MetaProgressStatsIntegrationTests
{
    [Test]
    public async Task StatsRoundCountersResetWhileLifetimePersists()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var userId = new NetUserId(Guid.NewGuid());

        await server.WaitPost(() =>
        {
            var entManager = server.ResolveDependency<IEntityManager>();
            var stats = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<WH40KPlayerStatsSystem>();

            stats.Record(userId, WH40KPlayerStatKeys.CombatEnemyKills, 30);

            Assert.Multiple(() =>
            {
                Assert.That(stats.GetRoundCounter(userId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(30));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(30));
            });

            entManager.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());

            Assert.Multiple(() =>
            {
                Assert.That(stats.GetRoundCounter(userId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(0));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(30));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MetaAchievementsSyncFromRoundStatsAndBlockers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var userId = new NetUserId(Guid.NewGuid());
        var db = server.ResolveDependency<IServerDbManager>();

        await db.UpdatePlayerRecordAsync(userId, "MetaStatsSyncTest", IPAddress.Loopback, null);

        await server.WaitPost(() =>
        {
            var systems = server.ResolveDependency<IEntitySystemManager>();
            var stats = systems.GetEntitySystem<WH40KPlayerStatsSystem>();
            var meta = systems.GetEntitySystem<WH40KMetaProgressSystem>();

            _ = meta.GetSnapshot(userId);
            stats.Record(userId, WH40KPlayerStatKeys.CombatEnemyKills, 30);

            var afterKills = meta.GetSnapshot(userId);
            var firstContact = afterKills.Achievements.Single(a => a.Id == "wh40k-ach-first-contact");
            var fireline = afterKills.Achievements.Single(a => a.Id == "wh40k-ach-fireline-initiation");

            Assert.Multiple(() =>
            {
                Assert.That(firstContact.Progress, Is.EqualTo(20));
                Assert.That(firstContact.Completed, Is.True);
                Assert.That(fireline.Progress, Is.EqualTo(30));
                Assert.That(fireline.Completed, Is.False);
            });

            stats.Record(userId, WH40KPlayerStatKeys.CombatDeaths, 1);

            var afterDeath = meta.GetSnapshot(userId);
            var firstContactAfterDeath = afterDeath.Achievements.Single(a => a.Id == "wh40k-ach-first-contact");
            var firelineAfterDeath = afterDeath.Achievements.Single(a => a.Id == "wh40k-ach-fireline-initiation");

            Assert.Multiple(() =>
            {
                Assert.That(firstContactAfterDeath.Progress, Is.EqualTo(20));
                Assert.That(firstContactAfterDeath.Completed, Is.True);
                Assert.That(firelineAfterDeath.Progress, Is.EqualTo(0));
                Assert.That(firelineAfterDeath.Completed, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LifetimeStatDrivenAchievementKeepsManualProgressAndAppliesDelta()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var userId = new NetUserId(Guid.NewGuid());
        var db = server.ResolveDependency<IServerDbManager>();

        await db.UpdatePlayerRecordAsync(userId, "MetaStatsLifetimeSyncTest", IPAddress.Loopback, null);

        await server.WaitPost(() =>
        {
            var systems = server.ResolveDependency<IEntitySystemManager>();
            var stats = systems.GetEntitySystem<WH40KPlayerStatsSystem>();
            var meta = systems.GetEntitySystem<WH40KMetaProgressSystem>();

            _ = meta.GetSnapshot(userId);

            var setResult = meta.TrySetAchievementProgress(
                userId,
                "wh40k-ach-veteran-of-wars",
                99,
                out var resolvedProgress,
                out _,
                out var completedBeforeDelta,
                out var setError);

            Assert.Multiple(() =>
            {
                Assert.That(setResult, Is.True, setError);
                Assert.That(resolvedProgress, Is.EqualTo(99));
                Assert.That(completedBeforeDelta, Is.False);
            });

            stats.Record(userId, WH40KPlayerStatKeys.RoundCompletedFaction, 1);

            var afterFirstDelta = meta.GetSnapshot(userId)
                .Achievements
                .Single(a => a.Id == "wh40k-ach-veteran-of-wars");

            Assert.Multiple(() =>
            {
                Assert.That(afterFirstDelta.Progress, Is.EqualTo(100));
                Assert.That(afterFirstDelta.Completed, Is.True);
            });

            stats.Record(userId, WH40KPlayerStatKeys.RoundCompletedFaction, 1);

            var afterSecondDelta = meta.GetSnapshot(userId)
                .Achievements
                .Single(a => a.Id == "wh40k-ach-veteran-of-wars");

            Assert.Multiple(() =>
            {
                Assert.That(afterSecondDelta.Progress, Is.EqualTo(100));
                Assert.That(afterSecondDelta.Completed, Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundStartingSeedsRuntimeStateForConnectedPlayers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var playerMan = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        NetUserId userId = default;

        await server.WaitPost(() => userId = playerMan.Sessions.Single().UserId);
        await db.UpdatePlayerRecordAsync(userId, "MetaRoundStartSeedTest", IPAddress.Loopback, null);

        await server.WaitPost(() =>
        {
            var systems = server.ResolveDependency<IEntitySystemManager>();
            var entMan = server.ResolveDependency<IEntityManager>();
            var stats = systems.GetEntitySystem<WH40KPlayerStatsSystem>();
            var meta = systems.GetEntitySystem<WH40KMetaProgressSystem>();

            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundStartingEvent(4242));

            stats.Record(userId, WH40KPlayerStatKeys.RoundCompletedFaction, 1);

            var veteran = meta.GetSnapshot(userId)
                .Achievements
                .Single(a => a.Id == "wh40k-ach-veteran-of-wars");

            Assert.Multiple(() =>
            {
                Assert.That(veteran.Progress, Is.EqualTo(1));
                Assert.That(veteran.Completed, Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }
}
