using System;
using System.Linq;
using System.Net;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Psyker;
using Content.Server._WH40K.Stats;
using Content.Shared.GameTicking;
using Content.Shared._WH40K.LateJoin;
using Content.Shared._WH40K.Psyker;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class MetaProgressStatsIntegrationTests
{
    /// <summary>
    /// Merged: round counter reset, achievement sync from stats, lifetime stat delta.
    /// All share a default (server-only) pair.
    /// </summary>
    [Test]
    public async Task StatsRoundCountersAchievementSyncAndLifetimeDelta()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();

        // --- Round counter reset while lifetime persists ---
        var uid1 = new NetUserId(Guid.NewGuid());
        await server.WaitPost(() =>
        {
            var entManager = server.ResolveDependency<IEntityManager>();
            var stats = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<WH40KPlayerStatsSystem>();

            stats.Record(uid1, WH40KPlayerStatKeys.CombatEnemyKills, 30);
            Assert.That(stats.GetRoundCounter(uid1, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(30));
            Assert.That(stats.GetLifetimeCounter(uid1, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(30));

            entManager.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());

            Assert.That(stats.GetRoundCounter(uid1, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(0));
            Assert.That(stats.GetLifetimeCounter(uid1, WH40KPlayerStatKeys.CombatEnemyKills), Is.EqualTo(30));
        });

        // --- Achievement sync from round stats + blocker ---
        var uid2 = new NetUserId(Guid.NewGuid());
        await db.UpdatePlayerRecordAsync(uid2, "StatsSync", IPAddress.Loopback, null);

        await server.WaitPost(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();

            _ = meta.GetSnapshot(uid2);
            stats.Record(uid2, WH40KPlayerStatKeys.CombatEnemyKills, 30);

            var after = meta.GetSnapshot(uid2);
            var firstContact = after.Achievements.Single(a => a.Id == "wh40k-ach-first-contact");
            var fireline = after.Achievements.Single(a => a.Id == "wh40k-ach-fireline-initiation");

            Assert.That(firstContact.Progress, Is.EqualTo(20));
            Assert.That(firstContact.Completed, Is.True);
            Assert.That(fireline.Progress, Is.EqualTo(30));
            Assert.That(fireline.Completed, Is.False);

            // Death blocker resets fireline
            stats.Record(uid2, WH40KPlayerStatKeys.CombatDeaths, 1);

            var afterDeath = meta.GetSnapshot(uid2);
            Assert.That(afterDeath.Achievements.Single(a => a.Id == "wh40k-ach-fireline-initiation").Progress, Is.EqualTo(0));
        });

        // --- Lifetime stat-driven achievement keeps manual progress + delta ---
        var uid3 = new NetUserId(Guid.NewGuid());
        await db.UpdatePlayerRecordAsync(uid3, "StatsLifetimeDelta", IPAddress.Loopback, null);

        await server.WaitPost(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();

            _ = meta.GetSnapshot(uid3);

            var setResult = meta.TrySetAchievementProgress(uid3, "wh40k-ach-veteran-of-wars", 99,
                out var resolvedProgress, out _, out var completedBefore, out var setError);

            Assert.That(setResult, Is.True, setError);
            Assert.That(resolvedProgress, Is.EqualTo(99));
            Assert.That(completedBefore, Is.False);

            stats.Record(uid3, WH40KPlayerStatKeys.RoundCompletedFaction, 1);

            var afterFirst = meta.GetSnapshot(uid3).Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars");
            Assert.That(afterFirst.Progress, Is.EqualTo(100));
            Assert.That(afterFirst.Completed, Is.True);

            // Second delta must not push past cap
            stats.Record(uid3, WH40KPlayerStatKeys.RoundCompletedFaction, 1);
            var afterSecond = meta.GetSnapshot(uid3).Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars");
            Assert.That(afterSecond.Progress, Is.EqualTo(100));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Merged: RoundStarting event seeding + patron attunement stat recording.
    /// Both need Connected=true, DummyTicker=false.
    /// </summary>
    [Test]
    public async Task RoundStartingSeedAndPatronAttunementStats()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
            InLobby = true,
            DummyTicker = false,
            Fresh = true
        });
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var playerMan = server.ResolveDependency<IPlayerManager>();
        var session = playerMan.Sessions.Single();
        var userId = session.UserId;

        await db.UpdatePlayerRecordAsync(userId, "StatsRoundSeedPatron", IPAddress.Loopback, null);

        await pair.WaitCommand("forcemap Battlefield40k");
        await pair.WaitCommand("setgamepreset WH40KTeamBattle 9999");
        await pair.WaitCommand("startround");
        await pair.RunTicksSync(60);

        // WH40K requires faction selection before late-join.
        await pair.Client.WaitPost(() =>
        {
            var factionSys = pair.Client.System<Content.Client._WH40K.LateJoin.WH40KFactionSystem>();
            factionSys.SelectFaction("Imperium", WH40KFactionSelectionPurpose.LateJoin);
        });
        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            var ticker = server.System<GameTicker>();
            ticker.MakeJoinGame(session, EntityUid.Invalid, "Guardsman");
        });
        await pair.RunTicksSync(20);

        Assert.That(session.AttachedEntity, Is.Not.Null);
        var actor = session.AttachedEntity!.Value;

        // --- Patron attunement stat recording (needs entity-session mapping, must run before cleanup) ---

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();
            var progressionSystem = server.System<WH40KChaosGiftProgressionSystem>();

            _ = meta.GetSnapshot(userId);

            entMan.EnsureComponent<WH40KChaosGiftRoleComponent>(actor);
            entMan.EnsureComponent<WH40KChaosGiftProgressionComponent>(actor);

            var actorCoords = entMan.GetComponent<TransformComponent>(actor).Coordinates;
            var skrizhal = entMan.SpawnEntity("WH40KRuneSkrizhalChaos", actorCoords);
            var skrizhalComp = entMan.GetComponent<WH40KChaosSkrizhalComponent>(skrizhal);

            var progression = entMan.GetComponent<WH40KChaosGiftProgressionComponent>(actor);
            progressionSystem.ApplyPatronSelection(
                (skrizhal, skrizhalComp),
                actor,
                WH40KChaosPatron.Khorne,
                progression,
                updateUi: false);

            var afterFirst = meta.GetSnapshot(userId);
            var khorne = afterFirst.Achievements.Single(a => a.Id == "wh40k-ach-khorne-attunement-veteran");

            Assert.Multiple(() =>
            {
                Assert.That(progression.AttunedPatron, Is.EqualTo(WH40KChaosPatron.Khorne));
                Assert.That(stats.GetRoundCounter(userId, WH40KPlayerStatKeys.ChaosPatronAttunementKhorne), Is.EqualTo(1));
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.ChaosPatronAttunementKhorne), Is.EqualTo(1));
                Assert.That(khorne.Progress, Is.EqualTo(1));
            });

            // Switch patron
            progression.AllowPatronSwitch = true;
            progressionSystem.ApplyPatronSelection(
                (skrizhal, skrizhalComp),
                actor,
                WH40KChaosPatron.Tzeentch,
                progression,
                updateUi: false);

            var afterSwitch = meta.GetSnapshot(userId);
            Assert.Multiple(() =>
            {
                Assert.That(progression.AttunedPatron, Is.EqualTo(WH40KChaosPatron.Tzeentch));
                Assert.That(stats.GetRoundCounter(userId, WH40KPlayerStatKeys.ChaosPatronAttunementKhorne), Is.EqualTo(1));
                Assert.That(stats.GetRoundCounter(userId, WH40KPlayerStatKeys.ChaosPatronAttunementTzeentch), Is.EqualTo(0));
                Assert.That(afterSwitch.Achievements.Single(a => a.Id == "wh40k-ach-tzeentch-attunement-veteran").Progress, Is.EqualTo(0));
            });
        });

        // --- RoundStarting seeds runtime state (uses userId only, safe after cleanup) ---
        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();

            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundStartingEvent(4242));

            stats.Record(userId, WH40KPlayerStatKeys.RoundCompletedFaction, 1);

            var veteran = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars");
            Assert.That(veteran.Progress, Is.EqualTo(1));
            Assert.That(veteran.Completed, Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
