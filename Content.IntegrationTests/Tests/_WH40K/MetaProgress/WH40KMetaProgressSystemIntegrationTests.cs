#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server.Database;
using Content.Server.GameTicking.Events;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Stats;
using Content.Shared.GameTicking;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WH40K.MetaProgress;

[TestFixture]
public sealed class WH40KMetaProgressSystemIntegrationTests
{
    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private static async Task<(TestPair Pair, NetUserId UserId)> SetupPairAndUser(string name, bool waitDbLoad = true)
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var userId = new NetUserId(Guid.NewGuid());
        await db.UpdatePlayerRecordAsync(userId, name, IPAddress.Loopback, null);
        await server.WaitPost(() =>
        {
            _ = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
        });
        if (waitDbLoad)
            await pair.RunTicksSync(30);
        return (pair, userId);
    }

    private static async Task<NetUserId> CreateAdditionalUser(TestPair pair, string name)
    {
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var userId = new NetUserId(Guid.NewGuid());
        await db.UpdatePlayerRecordAsync(userId, name, IPAddress.Loopback, null);
        await server.WaitPost(() =>
        {
            _ = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
        });
        await pair.RunTicksSync(30);
        return userId;
    }

    // ─────────────────────────────────────────────
    // Data integrity — XP, levels, DB round-trip
    // ─────────────────────────────────────────────

    [Test]
    public async Task XpGrantCausesLevelUpAndCorrectSnapshot()
    {
        var (pair, userId) = await SetupPairAndUser("XpLevelUp", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();
            var req = WH40KMetaProgressMath.GetRequiredXpForLevel(1);

            meta.AddLifetimeXp(userId, req - 1);
            var s = meta.GetSnapshot(userId);
            Assert.That(s.Level, Is.EqualTo(1));
            Assert.That(s.CurrentXp, Is.EqualTo(req - 1));

            meta.AddLifetimeXp(userId, 1);
            s = meta.GetSnapshot(userId);
            Assert.That(s.Level, Is.EqualTo(2));
            Assert.That(s.CurrentXp, Is.EqualTo(0));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SetLifetimeXpPersistsToDatabase()
    {
        var (pair, userId) = await SetupPairAndUser("XpPersist");
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();

        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().SetLifetimeXp(userId, 5000));

        WH40KMetaProgressDbData? persisted = null;
        for (var i = 0; i < 120; i++)
        {
            persisted = await db.GetWH40KMetaProgress(userId);
            if (persisted?.LifetimeXp == 5000) break;
            await pair.RunTicksSync(1);
        }
        Assert.That(persisted?.LifetimeXp, Is.EqualTo(5000));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DbRoundTripPreservesFullState()
    {
        var (pair, userId) = await SetupPairAndUser("DbRoundTrip");
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();
            meta.TrySetLevel(userId, 10, out _, out _);
            meta.TrySetAchievementProgress(userId, "wh40k-ach-veteran-of-wars", 42, out _, out _, out _, out _);
            meta.TrySetDevelopmentNodeUnlocked(userId, "brain-root", true, out _);
        });

        WH40KMetaProgressDbData? persisted = null;
        for (var i = 0; i < 120; i++)
        {
            persisted = await db.GetWH40KMetaProgress(userId);
            if (persisted is { LifetimeXp: > 0 }) break;
            await pair.RunTicksSync(1);
        }
        Assert.That(persisted, Is.Not.Null);

        await server.WaitPost(() =>
        {
            server.ResolveDependency<IEntityManager>()
                .EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
        });
        await pair.RunTicksSync(5);

        WH40KMetaProgressSnapshot? reloaded = null;
        for (var i = 0; i < 180; i++)
        {
            await server.WaitPost(() => reloaded = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId));
            if (reloaded!.Level >= 10) break;
            await pair.RunTicksSync(1);
        }
        Assert.Multiple(() =>
        {
            Assert.That(reloaded!.Level, Is.EqualTo(10));
            Assert.That(reloaded.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars").Progress, Is.EqualTo(42));
            Assert.That(reloaded.Development.OpenedNodeIds, Does.Contain("brain-root"));
        });
        await pair.CleanReturnAsync();
    }

    // ─────────────────────────────────────────────
    // Admin reset — all 5 scopes in one pair
    // ─────────────────────────────────────────────

    [Test]
    public async Task ResetForAdminAllScopesWorkCorrectly()
    {
        var (pair, _) = await SetupPairAndUser("ResetScopesDummy");
        var server = pair.Server;

        // — Progress scope —
        var uProg = await CreateAdditionalUser(pair, "ResetProg");
        await server.WaitPost(() =>
        {
            var m = server.System<WH40KMetaProgressSystem>();
            m.TrySetLevel(uProg, 10, out _, out _);
            m.TrySetAchievementProgress(uProg, "wh40k-ach-veteran-of-wars", 25, out _, out _, out _, out _);
            m.TrySetDevelopmentNodeUnlocked(uProg, "brain-root", true, out _);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().ResetForAdmin(uProg, WH40KMetaProgressSystem.AdminResetScope.Progress));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(uProg);
            Assert.That(s.Level, Is.EqualTo(1), "Progress reset must clear level.");
            Assert.That(s.LifetimeXp, Is.EqualTo(0), "Progress reset must clear XP.");
            Assert.That(s.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars").Progress, Is.EqualTo(25), "Progress reset must NOT clear achievements.");
        });

        // — Development scope —
        var uDev = await CreateAdditionalUser(pair, "ResetDev");
        await server.WaitPost(() =>
        {
            var m = server.System<WH40KMetaProgressSystem>();
            m.TrySetLevel(uDev, 10, out _, out _);
            m.TrySetDevelopmentNodeUnlocked(uDev, "brain-root", true, out _);
            m.TrySetAchievementProgress(uDev, "wh40k-ach-veteran-of-wars", 15, out _, out _, out _, out _);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().ResetForAdmin(uDev, WH40KMetaProgressSystem.AdminResetScope.Development));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(uDev);
            Assert.That(s.Level, Is.GreaterThanOrEqualTo(10), "Dev reset must NOT clear level.");
            Assert.That(s.Development.OpenedNodeIds, Is.Empty, "Dev reset must clear nodes.");
            Assert.That(s.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars").Progress, Is.EqualTo(15), "Dev reset must NOT clear achievements.");
        });

        // — Achievements scope —
        var uAch = await CreateAdditionalUser(pair, "ResetAch");
        await server.WaitPost(() =>
        {
            var m = server.System<WH40KMetaProgressSystem>();
            m.TrySetLevel(uAch, 10, out _, out _);
            m.TrySetAchievementProgress(uAch, "wh40k-ach-veteran-of-wars", 50, out _, out _, out _, out _);
            m.TrySetDevelopmentNodeUnlocked(uAch, "brain-root", true, out _);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().ResetForAdmin(uAch, WH40KMetaProgressSystem.AdminResetScope.Achievements));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(uAch);
            Assert.That(s.Level, Is.GreaterThanOrEqualTo(10), "Ach reset must NOT clear level.");
            Assert.That(s.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars").Progress, Is.EqualTo(0), "Ach reset must clear progress.");
            Assert.That(s.Development.OpenedNodeIds, Does.Contain("brain-root"), "Ach reset must NOT clear development.");
        });

        // — Decorations scope —
        var uDecor = await CreateAdditionalUser(pair, "ResetDecor");
        await server.WaitPost(() =>
        {
            var m = server.System<WH40KMetaProgressSystem>();
            m.TrySetLevel(uDecor, 10, out _, out _);
            m.TrySetDecorationUnlocked(uDecor, "decor-ghost-star", true, out _);
            m.TrySetDecorationSelection(uDecor, WH40KMetaDecorationCategory.GhostSkins, "decor-ghost-star", out _, out _);
            m.TrySetAchievementProgress(uDecor, "wh40k-ach-veteran-of-wars", 25, out _, out _, out _, out _);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().ResetForAdmin(uDecor, WH40KMetaProgressSystem.AdminResetScope.Decorations));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(uDecor);
            Assert.That(s.Level, Is.GreaterThanOrEqualTo(10), "Decor reset must NOT clear level.");
            Assert.That(s.Decorations.Single(x => x.Id == "decor-ghost-star").Unlocked, Is.False, "Decor reset must clear unlock.");
            Assert.That(s.DecorationSelection.SelectedGhostSkinId, Is.Not.EqualTo("decor-ghost-star"), "Decor reset must clear selection.");
            Assert.That(s.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars").Progress, Is.EqualTo(25), "Decor reset must NOT clear achievements.");
        });

        // — All scope —
        var uAll = await CreateAdditionalUser(pair, "ResetAll");
        await server.WaitPost(() =>
        {
            var m = server.System<WH40KMetaProgressSystem>();
            m.TrySetLevel(uAll, 10, out _, out _);
            m.TrySetDecorationUnlocked(uAll, "decor-ghost-star", true, out _);
            m.TrySetAchievementProgress(uAll, "wh40k-ach-veteran-of-wars", 50, out _, out _, out _, out _);
            m.TrySetDevelopmentNodeUnlocked(uAll, "brain-root", true, out _);
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().ResetForAdmin(uAll, WH40KMetaProgressSystem.AdminResetScope.All));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(uAll);
            Assert.That(s.Level, Is.EqualTo(1), "Reset All must clear level.");
            Assert.That(s.LifetimeXp, Is.EqualTo(0), "Reset All must clear XP.");
            Assert.That(s.Decorations.Single(x => x.Id == "decor-ghost-star").Unlocked, Is.False, "Reset All must clear decorations.");
            Assert.That(s.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars").Progress, Is.EqualTo(0), "Reset All must clear achievements.");
            Assert.That(s.Development.OpenedNodeIds, Is.Empty, "Reset All must clear development.");
        });

        await pair.CleanReturnAsync();
    }

    // ─────────────────────────────────────────────
    // Achievement rewards — grant + no-double-grant
    // ─────────────────────────────────────────────

    [Test]
    public async Task AchievementRewardGrantsXpAndIsNotGrantedTwice()
    {
        var (pair, userId) = await SetupPairAndUser("AchReward", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();
            Assert.That(meta.GetSnapshot(userId).LifetimeXp, Is.EqualTo(0));
            meta.TrySetAchievementUnlocked(userId, "wh40k-ach-fireline-initiation", true, out _, out _, out _, out _);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            Assert.That(s.LifetimeXp, Is.EqualTo(200), "Achievement reward XP must be granted.");
            Assert.That(s.Achievements.Single(a => a.Id == "wh40k-ach-fireline-initiation").Completed, Is.True);
        });

        // Re-unlock — must not grant double XP
        await server.WaitPost(() =>
            server.System<WH40KMetaProgressSystem>().TrySetAchievementUnlocked(userId, "wh40k-ach-fireline-initiation", true, out _, out _, out _, out _));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(server.System<WH40KMetaProgressSystem>().GetSnapshot(userId).LifetimeXp, Is.EqualTo(200), "Double-unlock must not grant double XP."));

        await pair.CleanReturnAsync();
    }

    // ─────────────────────────────────────────────
    // Decorations — lock, unlock, selection fallback
    // ─────────────────────────────────────────────

    [Test]
    public async Task RoundAchievementBlockerPreventsUnlockUntilFreshRound()
    {
        var (pair, userId) = await SetupPairAndUser("RoundBlocker", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();

            stats.Record(userId, WH40KPlayerStatKeys.CombatEnemyKills, 11);
            stats.Record(userId, WH40KPlayerStatKeys.CombatDeaths, 1);
            stats.Record(userId, WH40KPlayerStatKeys.CombatEnemyKills, 1);

            var achievement = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-fireline-initiation");
            Assert.That(achievement.Target, Is.EqualTo(12));
            Assert.That(achievement.Progress, Is.EqualTo(0), "Death blocker must zero round progress before unlock.");
            Assert.That(achievement.Completed, Is.False);
        });

        await server.WaitPost(() =>
        {
            server.ResolveDependency<IEntityManager>()
                .EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
        });
        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();

            stats.Record(userId, WH40KPlayerStatKeys.CombatEnemyKills, 12);

            var achievement = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-fireline-initiation");
            Assert.That(achievement.Progress, Is.EqualTo(12));
            Assert.That(achievement.Completed, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LifetimeStatAchievementsUseDeltaProgressAcrossMultipleRecords()
    {
        var (pair, userId) = await SetupPairAndUser("LifetimeStats", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();

            stats.Record(userId, WH40KPlayerStatKeys.CombatEnemyKills, 100);

            var huntmaster = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-huntmaster");
            Assert.That(huntmaster.Target, Is.EqualTo(150));
            Assert.That(huntmaster.Progress, Is.EqualTo(100));
            Assert.That(huntmaster.Completed, Is.False);

            stats.Record(userId, WH40KPlayerStatKeys.CombatEnemyKills, 50);

            huntmaster = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-huntmaster");
            Assert.That(huntmaster.Progress, Is.EqualTo(150), "Lifetime achievements must advance by delta, not re-add prior total.");
            Assert.That(huntmaster.Completed, Is.True);
        });

        await server.WaitPost(() =>
        {
            server.ResolveDependency<IEntityManager>()
                .EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var huntmaster = server.System<WH40KMetaProgressSystem>()
                .GetSnapshot(userId)
                .Achievements.Single(a => a.Id == "wh40k-ach-huntmaster");
            Assert.That(huntmaster.Progress, Is.EqualTo(150), "Round cleanup must not wipe lifetime achievement progress.");
            Assert.That(huntmaster.Completed, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LogisticsAchievementsMatchCargoMissionStatsSemantics()
    {
        var (pair, userId) = await SetupPairAndUser("CargoAch", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();

            stats.Record(userId, WH40KPlayerStatKeys.LogisticsDeliverySuccess, 1);

            var freightRunner = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-freight-runner");
            var convoyMaster = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-convoy-master");
            Assert.That(freightRunner.Target, Is.EqualTo(1));
            Assert.That(freightRunner.Completed, Is.True, "One completed cargo mission should satisfy the entry logistics achievement.");
            Assert.That(convoyMaster.Target, Is.EqualTo(2));
            Assert.That(convoyMaster.Progress, Is.EqualTo(1));
            Assert.That(convoyMaster.Completed, Is.False);

            stats.Record(userId, WH40KPlayerStatKeys.LogisticsDeliverySuccess, 1);
            convoyMaster = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-convoy-master");
            Assert.That(convoyMaster.Progress, Is.EqualTo(2));
            Assert.That(convoyMaster.Completed, Is.True);

            stats.Record(userId, WH40KPlayerStatKeys.LogisticsDeliveryValue, 14);
            var supplyChief = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-supply-line-chief");
            Assert.That(supplyChief.Target, Is.EqualTo(14));
            Assert.That(supplyChief.Progress, Is.EqualTo(14));
            Assert.That(supplyChief.Completed, Is.True);

            stats.Record(userId, WH40KPlayerStatKeys.LogisticsDeliveryValue, 106);
            var highValueCargo = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-high-value-cargo");
            Assert.That(highValueCargo.Target, Is.EqualTo(120));
            Assert.That(highValueCargo.Progress, Is.EqualTo(120));
            Assert.That(highValueCargo.Completed, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PatronAttunementAchievementsAdvanceFromRecordedAttunementStats()
    {
        var (pair, userId) = await SetupPairAndUser("PatronAttune", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var meta = server.System<WH40KMetaProgressSystem>();

            stats.Record(userId, WH40KPlayerStatKeys.ChaosPatronAttunementKhorne, 8);

            var achievement = meta.GetSnapshot(userId).Achievements.Single(a => a.Id == "wh40k-ach-khorne-attunement-veteran");
            Assert.That(achievement.Target, Is.EqualTo(8));
            Assert.That(achievement.Progress, Is.EqualTo(8));
            Assert.That(achievement.Completed, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DecorationLockUnlockAndSelectionFallback()
    {
        var (pair, userId) = await SetupPairAndUser("DecorLockUnlock");
        var server = pair.Server;

        // At level 1, high-level decorations must be locked
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            var locked = s.Decorations.Where(d => d.Requirement.RequiredLevel > 1).ToList();
            if (locked.Count > 0)
                Assert.That(locked.First().Unlocked, Is.False, $"{locked.First().Id} must be locked at level 1.");
        });

        // Raise level — level-only decorations unlock
        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().TrySetLevel(userId, 40, out _, out _));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            var levelOnly = s.Decorations.Where(d => d.Requirement.RequiredLevel <= 40 && d.Requirement.RequiredAchievements.Count == 0 && !d.Requirement.RequiredDiscordGuildMember && !d.Requirement.AdminOnly).ToList();
            Assert.That(levelOnly.Count, Is.GreaterThan(0));
            foreach (var d in levelOnly)
                Assert.That(d.Unlocked, Is.True, $"{d.Id} (req {d.Requirement.RequiredLevel}) must be unlocked at 40.");
        });

        // Force-unlock, select, then lock → selection must fallback
        await server.WaitPost(() =>
        {
            var m = server.System<WH40KMetaProgressSystem>();
            m.TrySetDecorationUnlocked(userId, "decor-ghost-star", true, out _);
            m.TrySetDecorationSelection(userId, WH40KMetaDecorationCategory.GhostSkins, "decor-ghost-star", out _, out _);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(server.System<WH40KMetaProgressSystem>().GetSnapshot(userId).DecorationSelection.SelectedGhostSkinId, Is.EqualTo("decor-ghost-star")));
        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().TrySetDecorationUnlocked(userId, "decor-ghost-star", false, out _));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(server.System<WH40KMetaProgressSystem>().GetSnapshot(userId).DecorationSelection.SelectedGhostSkinId,
                Is.Not.EqualTo("decor-ghost-star"), "Selection must fallback when decoration becomes locked."));

        await pair.CleanReturnAsync();
    }

    // ─────────────────────────────────────────────
    // Development plan — reject + valid
    // ─────────────────────────────────────────────

    [Test]
    public async Task DevelopmentPlanRejectionsAndValidOrder()
    {
        var (pair, userId) = await SetupPairAndUser("DevPlan", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();

            // Budget rejection at level 1
            var r1 = meta.TryConfirmDevelopmentPlan(userId,
                new[] { "brain-root", "brain-u1", "brain-u2", "brain-u3", "brain-d1", "brain-d2", "brain-d3" },
                out var cnt1, out _);
            Assert.That(r1, Is.False, "Plan exceeding budget must be rejected.");
            Assert.That(cnt1, Is.EqualTo(0));

            // Prerequisite rejection (skip root)
            meta.TrySetLevel(userId, 40, out _, out _);
            var r2 = meta.TrySetDevelopmentNodeUnlocked(userId, "brain-u2", true, out _);
            Assert.That(r2, Is.False, "Node with unmet parent prerequisite must be rejected.");

            // Valid plan
            var r3 = meta.TryConfirmDevelopmentPlan(userId,
                new[] { "brain-root", "brain-u1", "brain-u2" }, out var cnt3, out var err3);
            Assert.That(r3, Is.True, err3 ?? "Valid plan must succeed.");
            Assert.That(cnt3, Is.EqualTo(3));
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            Assert.That(s.Development.OpenedNodeIds, Does.Contain("brain-root"));
            Assert.That(s.Development.OpenedNodeIds, Does.Contain("brain-u1"));
            Assert.That(s.Development.OpenedNodeIds, Does.Contain("brain-u2"));
        });
        await pair.CleanReturnAsync();
    }

    // ─────────────────────────────────────────────
    // Edge cases — level cap, negative, invalid IDs, skill points
    // ─────────────────────────────────────────────

    [Test]
    public async Task LevelEdgeCasesAndInvalidIds()
    {
        var (pair, userId) = await SetupPairAndUser("EdgeCases", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();

            // Level cap
            meta.TrySetLevel(userId, 9999, out var capped, out _);
            Assert.That(capped, Is.LessThanOrEqualTo(40), "Level must be capped.");

            // Negative delta
            meta.TrySetLevel(userId, 10, out _, out _);
            meta.TryAddLevels(userId, -5, out var reduced, out _);
            Assert.That(reduced, Is.EqualTo(5));

            // Negative XP clamped
            meta.SetLifetimeXp(userId, -100);
            var s = meta.GetSnapshot(userId);
            Assert.That(s.LifetimeXp, Is.EqualTo(0));
            Assert.That(s.Level, Is.EqualTo(1));

            // Invalid IDs
            Assert.That(meta.TrySetAchievementUnlocked(userId, "nonexistent-achievement", true, out _, out _, out _, out _), Is.False);
            Assert.That(meta.TrySetDecorationUnlocked(userId, "nonexistent-decoration", true, out _), Is.False);
            Assert.That(meta.TrySetDecorationSelection(userId, WH40KMetaDecorationCategory.GhostSkins, "nonexistent-decoration", out _, out _), Is.False);
            Assert.That(meta.TrySetDevelopmentNodeUnlocked(userId, "nonexistent-node", true, out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SnapshotSkillPointsMatchLevelRewardTable()
    {
        var (pair, userId) = await SetupPairAndUser("SkillPoints", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().TrySetLevel(userId, 20, out _, out _));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var s = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            Assert.That(s.Level, Is.EqualTo(20));
            Assert.That(s.Development.TotalSkillPoints, Is.GreaterThan(0));
            Assert.That(s.Development.AvailableSkillPoints, Is.EqualTo(s.Development.TotalSkillPoints));
            Assert.That(s.Development.SpentSkillPoints, Is.EqualTo(0));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RevalidateUnlocksReturnsConsistentResult()
    {
        var (pair, userId) = await SetupPairAndUser("Revalidate", waitDbLoad: false);
        var server = pair.Server;

        await server.WaitPost(() => server.System<WH40KMetaProgressSystem>().TrySetLevel(userId, 10, out _, out _));
        await pair.RunTicksSync(5);

        WH40KMetaProgressSystem.WH40KMetaDecorationRevalidationResult? result = null;
        await server.WaitPost(async () =>
        {
            result = await server.System<WH40KMetaProgressSystem>().RevalidateUnlocksForAdminAsync(userId);
        });
        await pair.RunTicksSync(10);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Snapshot, Is.Not.Null);
        Assert.That(result.Snapshot.Level, Is.EqualTo(10));
        await pair.CleanReturnAsync();
    }
}
