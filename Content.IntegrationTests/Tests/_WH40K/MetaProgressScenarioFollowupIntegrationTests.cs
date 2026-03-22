#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server._WH40K.MetaProgress;
using Content.Server._WH40K.Stats;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared._WH40K.MetaProgress;
using Robust.Client.Console;
using Robust.Client.UserInterface;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class MetaProgressScenarioFollowupIntegrationTests
{
    [Test]
    public async Task DecorationsRespectRequirementsAndLegacyBypass()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var userId = new NetUserId(Guid.NewGuid());
        var config = server.ResolveDependency<IConfigurationManager>();

        await db.UpdatePlayerRecordAsync(userId, "MetaDecorBypassTest", IPAddress.Loopback, null);

        var originalBypass = config.GetCVar(CCVars.WH40KMetaUnlocksEnforced);

        try
        {
            await server.WaitPost(() =>
            {
                var systems = server.ResolveDependency<IEntitySystemManager>();
                var meta = systems.GetEntitySystem<WH40KMetaProgressSystem>();

                _ = meta.GetSnapshot(userId);

                var selectedLocked = meta.TrySetDecorationSelection(
                    userId,
                    WH40KMetaDecorationCategory.GhostSkins,
                    "decor.ghost.star",
                    out _,
                    out var selectionError);

                Assert.That(selectedLocked, Is.True, selectionError);
            });

            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);
                var star = snapshot.Decorations.Single(x => x.Id == "decor.ghost.star");

                Assert.Multiple(() =>
                {
                    Assert.That(star.Unlocked, Is.False, "Star ghost must remain locked at level 1 without bypass.");
                    Assert.That(
                        snapshot.DecorationSelection.SelectedGhostSkinId,
                        Is.EqualTo("decor.ghost.standard"),
                        "Locked selection must fallback to unlocked default skin.");
                });
            });

            await server.WaitPost(() => config.SetCVar(CCVars.WH40KMetaUnlocksEnforced, true));
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);
                var star = snapshot.Decorations.Single(x => x.Id == "decor.ghost.star");

                Assert.Multiple(() =>
                {
                    Assert.That(star.Unlocked, Is.True, "Bypass mode must unlock level/achievement-gated decoration.");
                    Assert.That(star.Requirement.RequiredLevel, Is.EqualTo(1));
                    Assert.That(star.Requirement.RequiredAchievements.Count, Is.EqualTo(0));
                });
            });

            await server.WaitPost(() =>
            {
                var meta = server.System<WH40KMetaProgressSystem>();
                var selectedBypass = meta.TrySetDecorationSelection(
                    userId,
                    WH40KMetaDecorationCategory.GhostSkins,
                    "decor.ghost.star",
                    out _,
                    out var selectionError);

                Assert.That(selectedBypass, Is.True, selectionError);
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);
                Assert.That(snapshot.DecorationSelection.SelectedGhostSkinId, Is.EqualTo("decor.ghost.star"));
            });
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.WH40KMetaUnlocksEnforced, originalBypass));
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DiscordDecorationRequirementsFallbackToLevelWhenAuthDisabled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var userId = new NetUserId(Guid.NewGuid());
        var config = server.ResolveDependency<IConfigurationManager>();

        await db.UpdatePlayerRecordAsync(userId, "MetaDiscordDecorFallbackTest", IPAddress.Loopback, null);

        var originalEnabled = config.GetCVar(CCVars.WH40KDiscordAuthEnabled);

        try
        {
            await server.WaitPost(() => config.SetCVar(CCVars.WH40KDiscordAuthEnabled, true));
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);
                var fish = snapshot.Decorations.Single(x => x.Id == "decor-title-fish");

                Assert.Multiple(() =>
                {
                    Assert.That(fish.Unlocked, Is.False, "Fish title must stay locked while Discord auth is enabled and no Discord link exists.");
                    Assert.That(fish.Requirement.RequiredDiscordGuildMember, Is.True);
                    Assert.That(fish.Requirement.RequiredDiscordRoleIds, Does.Contain("1479487752960737505"));
                });
            });

            await server.WaitPost(() => config.SetCVar(CCVars.WH40KDiscordAuthEnabled, false));
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var meta = server.System<WH40KMetaProgressSystem>();
                var snapshot = meta.GetSnapshot(userId);
                var fish = snapshot.Decorations.Single(x => x.Id == "decor-title-fish");

                Assert.Multiple(() =>
                {
                    Assert.That(fish.Unlocked, Is.True, "Fish title must fallback to level-only unlock when Discord auth is disabled.");
                    Assert.That(fish.Requirement.RequiredLevel, Is.EqualTo(1));
                    Assert.That(fish.Requirement.RequiredDiscordGuildMember, Is.False);
                    Assert.That(fish.Requirement.RequiredDiscordRoleIds, Is.Empty);
                });
            });
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(CCVars.WH40KDiscordAuthEnabled, originalEnabled));
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AchievementRewardsGrantXpAndDecorationOnlyOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var userId = new NetUserId(Guid.NewGuid());

        await db.UpdatePlayerRecordAsync(userId, "MetaAchievementRewardTest", IPAddress.Loopback, null);

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();
            var ok = meta.TrySetAchievementUnlocked(
                userId,
                "wh40k-ach-fireline-initiation",
                true,
                out _,
                out _,
                out _,
                out var error);

            Assert.That(ok, Is.True, error);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            var achievement = snapshot.Achievements.Single(x => x.Id == "wh40k-ach-fireline-initiation");
            var decoration = snapshot.Decorations.Single(x => x.Id == "decor-title-legend");

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.LifetimeXp, Is.EqualTo(200));
                Assert.That(achievement.Completed, Is.True);
                Assert.That(decoration.Unlocked, Is.True);
            });
        });

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();
            var ok = meta.TrySetAchievementUnlocked(
                userId,
                "wh40k-ach-fireline-initiation",
                true,
                out _,
                out _,
                out _,
                out var error);

            Assert.That(ok, Is.True, error);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            Assert.That(snapshot.LifetimeXp, Is.EqualTo(200));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LegacyDbLoadBackfillsMissingAchievementRewardsOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var userId = new NetUserId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;

        await db.UpdatePlayerRecordAsync(userId, "MetaLegacyRewardBackfillTest", IPAddress.Loopback, null);
        await db.SetWH40KMetaProgress(userId, new WH40KMetaProgressDbData(
            LifetimeXp: 0,
            SeasonXp: 0,
            LastProgressAt: now,
            SelectedGhostSkinId: null,
            SelectedOocTitleId: null,
            SelectedOocNameColorId: null));
        await db.SetWH40KMetaAchievements(userId, new[]
        {
            new WH40KMetaAchievementDbData(
                AchievementId: "wh40k-ach-fireline-initiation",
                ProgressValue: 50,
                Unlocked: true,
                UnlockedAt: now,
                Claimed: false,
                Version: 1,
                UpdatedAt: now)
        });

        WH40KMetaProgressSnapshot snapshot = null!;
        var loaded = false;
        for (var i = 0; i < 180; i++)
        {
            await server.WaitPost(() => snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId));

            var legend = snapshot.Decorations.Single(x => x.Id == "decor-title-legend");
            if (snapshot.LifetimeXp == 200 && legend.Unlocked)
            {
                loaded = true;
                break;
            }

            await pair.RunTicksSync(1);
        }

        Assert.That(loaded, Is.True, "Legacy completed achievement must backfill its missing reward on DB load.");

        WH40KMetaProgressDbData? persistedProgress = null;
        List<WH40KMetaAchievementDbData>? persistedAchievements = null;
        for (var i = 0; i < 180; i++)
        {
            persistedProgress = await db.GetWH40KMetaProgress(userId);
            persistedAchievements = await db.GetWH40KMetaAchievements(userId);

            if (persistedProgress?.LifetimeXp == 200 &&
                persistedAchievements?.Any(a => a.AchievementId == "wh40k-ach-fireline-initiation" && a.Claimed) == true)
            {
                break;
            }

            await pair.RunTicksSync(1);
        }

        Assert.That(persistedProgress?.LifetimeXp, Is.EqualTo(200));
        Assert.That(
            persistedAchievements?.Any(a => a.AchievementId == "wh40k-ach-fireline-initiation" && a.Claimed),
            Is.True,
            "Backfilled reward must be marked claimed in DB.");

        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
        });
        await pair.RunTicksSync(5);

        WH40KMetaProgressSnapshot reloaded = null!;
        var restored = false;
        for (var i = 0; i < 180; i++)
        {
            await server.WaitPost(() => reloaded = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId));

            if (reloaded.LifetimeXp == 200)
            {
                restored = true;
                break;
            }

            await pair.RunTicksSync(1);
        }

        Assert.That(restored, Is.True, "Reloaded runtime state must keep the already-backfilled XP without double-granting.");
        Assert.That(reloaded.LifetimeXp, Is.EqualTo(200));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClaimedAchievementRewardRepairsMissingDecorationWithoutGrantingExtraXp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var userId = new NetUserId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;

        await db.UpdatePlayerRecordAsync(userId, "MetaRewardDecorationRepairTest", IPAddress.Loopback, null);
        await db.SetWH40KMetaProgress(userId, new WH40KMetaProgressDbData(
            LifetimeXp: 0,
            SeasonXp: 0,
            LastProgressAt: now,
            SelectedGhostSkinId: null,
            SelectedOocTitleId: null,
            SelectedOocNameColorId: null));
        await db.SetWH40KMetaAchievements(userId, new[]
        {
            new WH40KMetaAchievementDbData(
                AchievementId: "wh40k-ach-fireline-initiation",
                ProgressValue: 50,
                Unlocked: true,
                UnlockedAt: now,
                Claimed: true,
                Version: 1,
                UpdatedAt: now)
        });

        WH40KMetaProgressSnapshot snapshot = null!;
        var loaded = false;
        for (var i = 0; i < 180; i++)
        {
            await server.WaitPost(() => snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId));

            var legend = snapshot.Decorations.Single(x => x.Id == "decor-title-legend");
            if (legend.Unlocked)
            {
                loaded = true;
                break;
            }

            await pair.RunTicksSync(1);
        }

        Assert.That(loaded, Is.True, "Completed claimed reward must restore its reward decoration if the unlock state is missing.");

        var legendSnapshot = snapshot.Decorations.Single(x => x.Id == "decor-title-legend");
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.LifetimeXp, Is.EqualTo(0), "Claimed reward repair must not duplicate XP.");
            Assert.That(legendSnapshot.Unlocked, Is.True);
        });

        List<WH40KMetaDecorationDbData>? persistedDecorations = null;
        for (var i = 0; i < 180; i++)
        {
            persistedDecorations = await db.GetWH40KMetaDecorations(userId);
            if (persistedDecorations.Any(d => d.UnlockId == "decor-title-legend" && d.Unlocked))
                break;

            await pair.RunTicksSync(1);
        }

        Assert.That(
            persistedDecorations?.Any(d => d.UnlockId == "decor-title-legend" && d.Unlocked),
            Is.True,
            "Repaired reward decoration must be persisted.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RuntimeStateReloadRestoresPersistedMetaProgress()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var userId = new NetUserId(Guid.NewGuid());

        await db.UpdatePlayerRecordAsync(userId, "MetaRuntimeReloadTest", IPAddress.Loopback, null);

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();

            var setLevel = meta.TrySetLevel(userId, 5, out _, out _);
            var setAchievement = meta.TrySetAchievementProgress(
                userId,
                "wh40k-ach-veteran-of-wars",
                42,
                out _,
                out _,
                out _,
                out var achievementError);
            var setDecoration = meta.TrySetDecorationSelection(
                userId,
                WH40KMetaDecorationCategory.GhostSkins,
                "decor.ghost.star",
                out _,
                out var decorationError);

            Assert.Multiple(() =>
            {
                Assert.That(setLevel, Is.True);
                Assert.That(setAchievement, Is.True, achievementError);
                Assert.That(setDecoration, Is.True, decorationError);
            });
        });

        // Wait until persisted values are visible in DB.
        WH40KMetaProgressDbData? persisted = null;
        List<WH40KMetaAchievementDbData>? persistedAchievements = null;
        for (var i = 0; i < 120; i++)
        {
            persisted = await db.GetWH40KMetaProgress(userId);
            persistedAchievements = await db.GetWH40KMetaAchievements(userId);

            if (persisted != null &&
                string.Equals(persisted.SelectedGhostSkinId, "decor.ghost.star", StringComparison.Ordinal) &&
                persistedAchievements != null &&
                persistedAchievements.Any(a => a.AchievementId == "wh40k-ach-veteran-of-wars" && a.ProgressValue == 42))
            {
                break;
            }

            await pair.RunTicksSync(1);
        }

        Assert.That(persisted, Is.Not.Null, "Meta progress row must be persisted before reload.");
        Assert.That(
            persistedAchievements != null &&
            persistedAchievements.Any(a => a.AchievementId == "wh40k-ach-veteran-of-wars" && a.ProgressValue == 42),
            Is.True,
            "Achievement progress must be persisted before reload.");

        // Simulate server-round cleanup that clears runtime caches.
        await server.WaitPost(() =>
        {
            var entMan = server.ResolveDependency<IEntityManager>();
            entMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
        });
        await pair.RunTicksSync(5);

        // Fresh runtime state should load persisted values from DB.
        WH40KMetaProgressSnapshot snapshot = null!;
        var restored = false;
        for (var i = 0; i < 180; i++)
        {
            await server.WaitPost(() => snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId));

            var veteran = snapshot.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars");
            if (snapshot.Level >= 5 &&
                string.Equals(snapshot.DecorationSelection.SelectedGhostSkinId, "decor.ghost.star", StringComparison.Ordinal) &&
                veteran.Progress == 42)
            {
                restored = true;
                break;
            }

            await pair.RunTicksSync(1);
        }

        Assert.That(restored, Is.True, "Persisted state must be restored after runtime cleanup/reload.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LeaveRejoinTracksEarlyLeaveOnceAndKeepsProgress()
    {
        await using var pair = await StartWh40KRoundAsync();
        var server = pair.Server;
        var client = pair.Client;

        var playerMan = server.ResolveDependency<IPlayerManager>();
        NetUserId userId = default;
        await server.WaitPost(() => userId = playerMan.Sessions.Single().UserId);

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();
            _ = meta.GetSnapshot(userId);
            meta.AddLifetimeXp(userId, 75);
        });
        await pair.RunTicksSync(5);

        var host = client.ResolveDependency<IClientConsoleHost>();
        var net = client.ResolveDependency<IClientNetManager>();

        await client.WaitPost(() => host.ExecuteCommand("disconnect"));
        await pair.RunTicksSync(10);
        await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundLeftEarly), Is.EqualTo(1));
        });

        client.SetConnectTarget(server);
        await client.WaitPost(() => net.ClientConnect(null!, 0, null!));
        await pair.RunTicksSync(20);
        await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());

        await server.WaitAssertion(() =>
        {
            var stats = server.System<WH40KPlayerStatsSystem>();
            var snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);

            Assert.Multiple(() =>
            {
                Assert.That(stats.GetLifetimeCounter(userId, WH40KPlayerStatKeys.RoundLeftEarly), Is.EqualTo(1));
                Assert.That(snapshot.LifetimeXp, Is.GreaterThanOrEqualTo(75));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdminCommandsApplyValidMutationsAndRejectInvalidIdsWithoutCorruption()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, InLobby = true });
        var server = pair.Server;
        var playerMan = server.ResolveDependency<IPlayerManager>();

        NetUserId userId = default;
        string userName = string.Empty;
        await server.WaitPost(() =>
        {
            var session = playerMan.Sessions.Single();
            userId = session.UserId;
            userName = session.Name;
        });

        await pair.WaitCommand($"wh40kmeta level set {userName} 5");
        await pair.WaitCommand($"wh40kmeta ach progress set {userName} wh40k-ach-veteran-of-wars 10");
        await pair.WaitCommand($"wh40kmeta decor unlock {userName} decor.ghost.star");
        await pair.WaitCommand($"wh40kmeta ghostskin set {userName} decor.ghost.star");

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            var veteran = snapshot.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars");
            var star = snapshot.Decorations.Single(a => a.Id == "decor.ghost.star");

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Level, Is.EqualTo(5));
                Assert.That(veteran.Progress, Is.EqualTo(10));
                Assert.That(star.Unlocked, Is.True);
                Assert.That(snapshot.DecorationSelection.SelectedGhostSkinId, Is.EqualTo("decor.ghost.star"));
            });
        });

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();

            var invalidAchievement = meta.TrySetAchievementUnlocked(
                userId,
                "invalid-achievement-id",
                true,
                out _,
                out _,
                out _,
                out _);

            var invalidDecorUnlock = meta.TrySetDecorationUnlocked(
                userId,
                "invalid.decoration.id",
                true,
                out _);

            var invalidSelection = meta.TrySetDecorationSelection(
                userId,
                WH40KMetaDecorationCategory.GhostSkins,
                "invalid.decoration.id",
                out _,
                out _);

            Assert.Multiple(() =>
            {
                Assert.That(invalidAchievement, Is.False);
                Assert.That(invalidDecorUnlock, Is.False);
                Assert.That(invalidSelection, Is.False);
            });
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(userId);
            var veteran = snapshot.Achievements.Single(a => a.Id == "wh40k-ach-veteran-of-wars");

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Level, Is.EqualTo(5));
                Assert.That(veteran.Progress, Is.EqualTo(10));
                Assert.That(snapshot.DecorationSelection.SelectedGhostSkinId, Is.EqualTo("decor.ghost.star"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OocOutputPriorityUsesDecorationsForUsersAndAdminOverridesWhenPrivileged()
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

        NetUserId userId = default;
        string userName = string.Empty;
        await server.WaitAssertion(() =>
        {
            var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            userId = session.UserId;
            userName = session.Name;
        });

        await server.WaitPost(() =>
        {
            var meta = server.System<WH40KMetaProgressSystem>();

            var setLevel = meta.TrySetLevel(userId, 6, out _, out _);
            var setTitle = meta.TrySetDecorationSelection(
                userId,
                WH40KMetaDecorationCategory.OocTitles,
                "decor.title.legend",
                out _,
                out var titleError);
            var setColor = meta.TrySetDecorationSelection(
                userId,
                WH40KMetaDecorationCategory.OocNameColors,
                "decor.color.gold",
                out _,
                out var colorError);

            Assert.Multiple(() =>
            {
                Assert.That(setLevel, Is.True);
                Assert.That(setTitle, Is.True, titleError);
                Assert.That(setColor, Is.True, colorError);
            });
        });
        await pair.RunTicksSync(5);

        ChatUIController chatController = null!;
        await client.WaitAssertion(() =>
        {
            var ui = client.ResolveDependency<IUserInterfaceManager>();
            chatController = ui.GetUIController<ChatUIController>();
            Assert.That(chatController, Is.Not.Null);
        });
        await client.WaitPost(() => chatController.History.Clear());

        await server.WaitPost(() =>
        {
            var admin = server.ResolveDependency<IAdminManager>();
            var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            admin.DeAdmin(session);
        });
        await pair.RunTicksSync(5);

        var userText = $"s007-user-{Guid.NewGuid():N}";
        var userMessage = await SendOocAndCaptureMessageAsync(pair, chatController, userText);
        Assert.Multiple(() =>
        {
            Assert.That(userMessage.MessageColorOverride, Is.Null);
            Assert.That(userMessage.WrappedMessage, Does.Contain(userName));
            Assert.That(userMessage.WrappedMessage, Does.Contain("("), "Meta OOC title should be visible for non-admin path.");
            Assert.That(userMessage.WrappedMessage, Does.Contain("D8BC6A").IgnoreCase);
        });

        await server.WaitPost(() =>
        {
            var admin = server.ResolveDependency<IAdminManager>();
            var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            admin.ReAdmin(session);
        });
        await pair.RunTicksSync(5);

        var adminText = $"s007-admin-{Guid.NewGuid():N}";
        var adminMessage = await SendOocAndCaptureMessageAsync(pair, chatController, adminText);
        Assert.Multiple(() =>
        {
            Assert.That(adminMessage.MessageColorOverride, Is.Not.Null, "Admin name color must override meta OOC color.");
            Assert.That(adminMessage.WrappedMessage, Does.Contain(userName));
            Assert.That(adminMessage.WrappedMessage, Does.Not.Contain("D8BC6A").IgnoreCase);
            Assert.That(adminMessage.WrappedMessage, Does.Not.Contain("("), "Forced admin title should suppress meta title prefix.");
        });

        await pair.CleanReturnAsync();
    }

    private static async Task<ChatMessage> SendOocAndCaptureMessageAsync(
        TestPair pair,
        ChatUIController chatController,
        string text,
        int maxTicks = 180)
    {
        var client = pair.Client;

        await client.WaitPost(() =>
        {
            var chat = client.ResolveDependency<Content.Client.Chat.Managers.IChatManager>();
            chat.SendMessage(text, ChatSelectChannel.OOC);
        });

        for (var i = 0; i < maxTicks; i++)
        {
            ChatMessage? message = null;
            await client.WaitPost(() =>
            {
                var matches = chatController.History
                    .Where(entry =>
                        entry.Msg.Channel == ChatChannel.OOC &&
                        string.Equals(entry.Msg.Message, text, StringComparison.Ordinal))
                    .Select(entry => entry.Msg)
                    .ToArray();

                if (matches.Length > 0)
                    message = matches[^1];
            });

            if (message != null)
                return message;

            await pair.RunTicksSync(1);
        }

        Assert.Fail($"Timed out waiting for OOC message capture: '{text}'.");
        return null!;
    }

    private static async Task<TestPair> StartWh40KRoundAsync()
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

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            ticker.ToggleReadyAll(true);
        });

        await pair.WaitCommand("startround");
        await pair.RunTicksSync(80);

        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
        });

        return pair;
    }
}
