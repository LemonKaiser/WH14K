using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared.Database;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Roles;
using Content.Shared.Speech;
using Content.Shared._WH40K.Administration.Mute;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Preferences
{
    [TestFixture]
    public sealed class ServerDbSqliteTests : GameTest
    {
        [TestPrototypes]
        private const string Prototypes = @"
- type: dataset
  id: sqlite_test_names_first_male
  values:
  - Aaden

- type: dataset
  id: sqlite_test_names_first_female
  values:
  - Aaliyah

- type: dataset
  id: sqlite_test_names_last
  values:
  - Ackerley";

        private static HumanoidCharacterProfile CharlieCharlieson()
        {
            return new HumanoidCharacterProfile
            {
                Name = "Charlie Charlieson",
                FlavorText = "The biggest boy around.",
                Species = "Human",
                Age = 21,
                Appearance = new(
                    Color.Azure,
                    Color.Beige,
                    new ())
            }.WithVoiceTone(VoiceTone.High);
        }

        private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server, bool synchronous = true)
        {
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var serialization = server.ResolveDependency<ISerializationManager>();
            var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
            var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            builder.UseSqlite(conn);
            return new ServerDbSqlite(() => builder.Options, true, cfg, synchronous, opsLog, serialization);
        }

        [Test]
        public async Task TestUserDoesNotExist()
        {
            var pair = Pair;
            var db = GetDb(pair.Server);
            // Database should be empty so a new GUID should do it.
            Assert.That(await db.GetPlayerPreferencesAsync(NewUserId()), Is.Null);
        }

        [Test]
        public async Task TestAppearanceValidationAndSave()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));

            var profile = CharlieCharlieson();
            profile.Appearance.Markings["Head"] = new Dictionary<HumanoidVisualLayers, List<Marking>>
            {
                [HumanoidVisualLayers.Hair] = [],
                [HumanoidVisualLayers.FacialHair] = [],
            };
            profile.Appearance.Markings["OrganFake"] = new Dictionary<HumanoidVisualLayers, List<Marking>>();

            await pair.Server.WaitAssertion(() =>
            {
                var updated = HumanoidCharacterAppearance.EnsureValid(profile.Appearance, profile.Species, profile.Sex);
                Assert.That(updated.Markings["Head"], Is.Empty);
                Assert.That(updated.Markings.ContainsKey("OrganFake"), Is.False);
                profile.Appearance = updated;
            });

            Assert.DoesNotThrowAsync(async () => await db.InitPrefsAsync(username, profile));

            var preferences = (ServerPreferencesManager)pair.Server.ResolveDependency<IServerPreferencesManager>();
            var prefs = await db.GetPlayerPreferencesAsync(username);
            var fetchedProfile = preferences.ConvertProfiles(prefs!.Profiles.Find(p => p.Slot == 0));
            Assert.That(fetchedProfile.MemberwiseEquals(profile));

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestInitPrefs()
        {
            var pair = Pair;
            var db = GetDb(pair.Server);
            var preferences = (ServerPreferencesManager)pair.Server.ResolveDependency<IServerPreferencesManager>();
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            const int slot = 0;
            var originalProfile = CharlieCharlieson();
            await db.InitPrefsAsync(username, originalProfile);
            var prefs = await db.GetPlayerPreferencesAsync(username);
            var profile = preferences.ConvertProfiles(prefs!.Profiles.Find(p => p.Slot == slot));
            Assert.That(profile.MemberwiseEquals(originalProfile));
        }

        [Test]
        public async Task TestDeleteCharacter()
        {
            var pair = Pair;
            var server = pair.Server;
            var db = GetDb(server);
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            await db.InitPrefsAsync(username, new HumanoidCharacterProfile());
            await db.SaveCharacterSlotAsync(username, CharlieCharlieson(), 1);
            await db.SaveSelectedCharacterIndexAsync(username, 1);
            await db.SaveCharacterSlotAsync(username, null, 1);
            var prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(prefs!.Profiles, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TestWH40KMetaProgressRoundTrip()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "MetaProgressTester", IPAddress.Loopback, null);

            var original = new WH40KMetaProgressDbData(
                LifetimeXp: 334,
                SeasonXp: 12,
                LastProgressAt: new DateTimeOffset(2026, 2, 19, 10, 0, 0, TimeSpan.Zero),
                LastAccountResetAt: new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero),
                SelectedGhostSkinId: "decor.ghost.standard",
                SelectedOocTitleId: "decor.title.none",
                SelectedOocNameColorId: "decor.color.default");

            await db.SetWH40KMetaProgress(userId, original);

            var loaded = await db.GetWH40KMetaProgress(userId, CancellationToken.None);
            Assert.That(loaded, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(loaded!.LifetimeXp, Is.EqualTo(334));
                Assert.That(loaded.SeasonXp, Is.EqualTo(12));
                Assert.That(loaded.LastProgressAt, Is.EqualTo(original.LastProgressAt));
                Assert.That(loaded.LastAccountResetAt, Is.EqualTo(original.LastAccountResetAt));
                Assert.That(loaded.SelectedGhostSkinId, Is.EqualTo("decor.ghost.standard"));
                Assert.That(loaded.SelectedOocTitleId, Is.EqualTo("decor.title.none"));
                Assert.That(loaded.SelectedOocNameColorId, Is.EqualTo("decor.color.default"));
            });

            var updated = new WH40KMetaProgressDbData(
                LifetimeXp: 777,
                SeasonXp: 15,
                LastProgressAt: new DateTimeOffset(2026, 2, 19, 12, 30, 0, TimeSpan.Zero),
                LastAccountResetAt: null,
                SelectedGhostSkinId: "decor.ghost.iron",
                SelectedOocTitleId: "decor.title.legend",
                SelectedOocNameColorId: "decor.color.gold");

            await db.SetWH40KMetaProgress(userId, updated);

            var reloaded = await db.GetWH40KMetaProgress(userId, CancellationToken.None);
            Assert.That(reloaded, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded!.LifetimeXp, Is.EqualTo(777));
                Assert.That(reloaded.SeasonXp, Is.EqualTo(15));
                Assert.That(reloaded.LastProgressAt, Is.EqualTo(updated.LastProgressAt));
                Assert.That(reloaded.LastAccountResetAt, Is.Null);
                Assert.That(reloaded.SelectedGhostSkinId, Is.EqualTo("decor.ghost.iron"));
                Assert.That(reloaded.SelectedOocTitleId, Is.EqualTo("decor.title.legend"));
                Assert.That(reloaded.SelectedOocNameColorId, Is.EqualTo("decor.color.gold"));
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaReadsForUnknownUserAreEmpty()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            var progress = await db.GetWH40KMetaProgress(userId, CancellationToken.None);
            var achievements = await db.GetWH40KMetaAchievements(userId, CancellationToken.None);
            var decorations = await db.GetWH40KMetaDecorations(userId, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(progress, Is.Null);
                Assert.That(achievements, Is.Empty);
                Assert.That(decorations, Is.Empty);
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaProgressNormalizationAndSelectionReset()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "MetaProgressNormalizeTester", IPAddress.Loopback, null);

            var first = new WH40KMetaProgressDbData(
                LifetimeXp: 250,
                SeasonXp: 20,
                LastProgressAt: new DateTimeOffset(2026, 2, 20, 10, 0, 0, TimeSpan.Zero),
                LastAccountResetAt: new DateTimeOffset(2026, 1, 25, 18, 15, 0, TimeSpan.Zero),
                SelectedGhostSkinId: "decor.ghost.iron",
                SelectedOocTitleId: "decor.title.legend",
                SelectedOocNameColorId: "decor.color.gold");

            await db.SetWH40KMetaProgress(userId, first);

            var normalized = new WH40KMetaProgressDbData(
                LifetimeXp: -50,
                SeasonXp: -5,
                LastProgressAt: new DateTimeOffset(2026, 2, 20, 10, 30, 0, TimeSpan.Zero),
                LastAccountResetAt: null,
                SelectedGhostSkinId: "   ",
                SelectedOocTitleId: "\t",
                SelectedOocNameColorId: string.Empty);

            await db.SetWH40KMetaProgress(userId, normalized);

            var loaded = await db.GetWH40KMetaProgress(userId, CancellationToken.None);
            Assert.That(loaded, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(loaded!.LifetimeXp, Is.EqualTo(0));
                Assert.That(loaded.SeasonXp, Is.EqualTo(0));
                Assert.That(loaded.LastProgressAt, Is.EqualTo(normalized.LastProgressAt));
                Assert.That(loaded.LastAccountResetAt, Is.Null);
                Assert.That(loaded.SelectedGhostSkinId, Is.Null);
                Assert.That(loaded.SelectedOocTitleId, Is.Null);
                Assert.That(loaded.SelectedOocNameColorId, Is.Null);
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaAchievementProgressRoundTrip()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "MetaAchievementTester", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 2, 19, 11, 0, 0, TimeSpan.Zero);
            var initial = new List<WH40KMetaAchievementDbData>
            {
                new(
                    AchievementId: "wh40k-ach-frontline-anchor",
                    ProgressValue: 9,
                    Unlocked: false,
                    UnlockedAt: null,
                    Claimed: false,
                    Version: 1,
                    UpdatedAt: now),
                new(
                    AchievementId: "wh40k-ach-veteran",
                    ProgressValue: 100,
                    Unlocked: true,
                    UnlockedAt: now,
                    Claimed: false,
                    Version: 1,
                    UpdatedAt: now),
            };

            await db.SetWH40KMetaAchievements(userId, initial);

            var loadedInitial = (await db.GetWH40KMetaAchievements(userId, CancellationToken.None))
                .OrderBy(a => a.AchievementId)
                .ToList();

            Assert.That(loadedInitial.Count, Is.EqualTo(2));
            Assert.That(loadedInitial[0].AchievementId, Is.EqualTo("wh40k-ach-frontline-anchor"));
            Assert.That(loadedInitial[1].AchievementId, Is.EqualTo("wh40k-ach-veteran"));

            var updatedAt = now.AddHours(2);
            var updated = new List<WH40KMetaAchievementDbData>
            {
                new(
                    AchievementId: "wh40k-ach-frontline-anchor",
                    ProgressValue: 30,
                    Unlocked: true,
                    UnlockedAt: updatedAt,
                    Claimed: false,
                    Version: 2,
                    UpdatedAt: updatedAt),
                new(
                    AchievementId: "wh40k-ach-special-complete-all",
                    ProgressValue: 1,
                    Unlocked: true,
                    UnlockedAt: updatedAt,
                    Claimed: true,
                    Version: 1,
                    UpdatedAt: updatedAt),
            };

            await db.SetWH40KMetaAchievements(userId, updated);

            var loadedUpdated = (await db.GetWH40KMetaAchievements(userId, CancellationToken.None))
                .OrderBy(a => a.AchievementId)
                .ToList();

            Assert.That(loadedUpdated.Count, Is.EqualTo(2));

            var frontline = loadedUpdated.Single(a => a.AchievementId == "wh40k-ach-frontline-anchor");
            var special = loadedUpdated.Single(a => a.AchievementId == "wh40k-ach-special-complete-all");

            Assert.Multiple(() =>
            {
                Assert.That(frontline.ProgressValue, Is.EqualTo(30));
                Assert.That(frontline.Unlocked, Is.True);
                Assert.That(frontline.Version, Is.EqualTo(2));
                Assert.That(special.Claimed, Is.True);
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaAchievementsNormalizationAndBlankFiltering()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "MetaAchievementNormalizeTester", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 2, 20, 11, 0, 0, TimeSpan.Zero);
            var payload = new List<WH40KMetaAchievementDbData>
            {
                new(
                    AchievementId: " ",
                    ProgressValue: 999,
                    Unlocked: true,
                    UnlockedAt: now,
                    Claimed: true,
                    Version: 9,
                    UpdatedAt: now),
                new(
                    AchievementId: "wh40k-ach-frontline-anchor",
                    ProgressValue: -10,
                    Unlocked: false,
                    UnlockedAt: null,
                    Claimed: false,
                    Version: 0,
                    UpdatedAt: now),
            };

            await db.SetWH40KMetaAchievements(userId, payload);

            var loaded = await db.GetWH40KMetaAchievements(userId, CancellationToken.None);
            Assert.That(loaded.Count, Is.EqualTo(1));

            var entry = loaded[0];
            Assert.Multiple(() =>
            {
                Assert.That(entry.AchievementId, Is.EqualTo("wh40k-ach-frontline-anchor"));
                Assert.That(entry.ProgressValue, Is.EqualTo(0));
                Assert.That(entry.Version, Is.EqualTo(1));
                Assert.That(entry.Unlocked, Is.False);
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaAchievementsClearOnEmptyPayload()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "MetaAchievementClearTester", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 2, 20, 11, 30, 0, TimeSpan.Zero);
            await db.SetWH40KMetaAchievements(userId, new List<WH40KMetaAchievementDbData>
            {
                new(
                    AchievementId: "wh40k-ach-frontline-anchor",
                    ProgressValue: 3,
                    Unlocked: false,
                    UnlockedAt: null,
                    Claimed: false,
                    Version: 1,
                    UpdatedAt: now),
            });

            await db.SetWH40KMetaAchievements(userId, new List<WH40KMetaAchievementDbData>());

            var loaded = await db.GetWH40KMetaAchievements(userId, CancellationToken.None);
            Assert.That(loaded, Is.Empty);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaDecorationUnlockRoundTrip()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "MetaDecorTester", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 2, 19, 13, 0, 0, TimeSpan.Zero);
            var initial = new List<WH40KMetaDecorationDbData>
            {
                new(
                    UnlockId: "decor.ghost.standard",
                    Unlocked: true,
                    UnlockedAt: now,
                    SourceLevel: 1,
                    UpdatedAt: now),
                new(
                    UnlockId: "decor.title.legend",
                    Unlocked: false,
                    UnlockedAt: null,
                    SourceLevel: 0,
                    UpdatedAt: now),
            };

            await db.SetWH40KMetaDecorations(userId, initial);

            var loadedInitial = (await db.GetWH40KMetaDecorations(userId, CancellationToken.None))
                .OrderBy(a => a.UnlockId)
                .ToList();

            Assert.That(loadedInitial.Count, Is.EqualTo(2));
            Assert.That(loadedInitial[0].UnlockId, Is.EqualTo("decor.ghost.standard"));
            Assert.That(loadedInitial[1].UnlockId, Is.EqualTo("decor.title.legend"));

            var updatedAt = now.AddHours(2);
            var updated = new List<WH40KMetaDecorationDbData>
            {
                new(
                    UnlockId: "decor.ghost.standard",
                    Unlocked: true,
                    UnlockedAt: now,
                    SourceLevel: 1,
                    UpdatedAt: updatedAt),
                new(
                    UnlockId: "decor.color.gold",
                    Unlocked: true,
                    UnlockedAt: updatedAt,
                    SourceLevel: 5,
                    UpdatedAt: updatedAt),
            };

            await db.SetWH40KMetaDecorations(userId, updated);

            var loadedUpdated = (await db.GetWH40KMetaDecorations(userId, CancellationToken.None))
                .OrderBy(a => a.UnlockId)
                .ToList();

            Assert.That(loadedUpdated.Count, Is.EqualTo(2));

            var ghost = loadedUpdated.Single(a => a.UnlockId == "decor.ghost.standard");
            var gold = loadedUpdated.Single(a => a.UnlockId == "decor.color.gold");

            Assert.Multiple(() =>
            {
                Assert.That(ghost.Unlocked, Is.True);
                Assert.That(ghost.SourceLevel, Is.EqualTo(1));
                Assert.That(gold.Unlocked, Is.True);
                Assert.That(gold.SourceLevel, Is.EqualTo(5));
                Assert.That(gold.UnlockedAt, Is.EqualTo(updatedAt));
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaDecorationsNormalizationAndBlankFiltering()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "MetaDecorNormalizeTester", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 2, 20, 12, 0, 0, TimeSpan.Zero);
            var payload = new List<WH40KMetaDecorationDbData>
            {
                new(
                    UnlockId: " ",
                    Unlocked: true,
                    UnlockedAt: now,
                    SourceLevel: 10,
                    UpdatedAt: now),
                new(
                    UnlockId: "decor.ghost.standard",
                    Unlocked: true,
                    UnlockedAt: now,
                    SourceLevel: -7,
                    UpdatedAt: now),
            };

            await db.SetWH40KMetaDecorations(userId, payload);

            var loaded = await db.GetWH40KMetaDecorations(userId, CancellationToken.None);
            Assert.That(loaded.Count, Is.EqualTo(1));

            var entry = loaded[0];
            Assert.Multiple(() =>
            {
                Assert.That(entry.UnlockId, Is.EqualTo("decor.ghost.standard"));
                Assert.That(entry.SourceLevel, Is.EqualTo(0));
                Assert.That(entry.Unlocked, Is.True);
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaDecorationsClearOnEmptyPayload()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "MetaDecorClearTester", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 2, 20, 12, 30, 0, TimeSpan.Zero);
            await db.SetWH40KMetaDecorations(userId, new List<WH40KMetaDecorationDbData>
            {
                new(
                    UnlockId: "decor.ghost.standard",
                    Unlocked: true,
                    UnlockedAt: now,
                    SourceLevel: 1,
                    UpdatedAt: now),
            });

            await db.SetWH40KMetaDecorations(userId, new List<WH40KMetaDecorationDbData>());

            var loaded = await db.GetWH40KMetaDecorations(userId, CancellationToken.None);
            Assert.That(loaded, Is.Empty);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KMetaDataIsolationBetweenPlayers()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);

            var userA = NewUserId();
            var userB = NewUserId();
            await db.UpdatePlayerRecord(userA, "MetaIsolationA", IPAddress.Loopback, null);
            await db.UpdatePlayerRecord(userB, "MetaIsolationB", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 2, 20, 13, 0, 0, TimeSpan.Zero);

            await db.SetWH40KMetaProgress(userA, new WH40KMetaProgressDbData(
                LifetimeXp: 120,
                SeasonXp: 4,
                LastProgressAt: now,
                LastAccountResetAt: now.AddDays(-10),
                SelectedGhostSkinId: "decor.ghost.standard",
                SelectedOocTitleId: "decor.title.none",
                SelectedOocNameColorId: "decor.color.default"));
            await db.SetWH40KMetaProgress(userB, new WH40KMetaProgressDbData(
                LifetimeXp: 990,
                SeasonXp: 12,
                LastProgressAt: now,
                LastAccountResetAt: null,
                SelectedGhostSkinId: "decor.ghost.warp",
                SelectedOocTitleId: "decor.title.legend",
                SelectedOocNameColorId: "decor.color.gold"));

            await db.SetWH40KMetaAchievements(userA, new List<WH40KMetaAchievementDbData>
            {
                new(
                    AchievementId: "wh40k-ach-frontline-anchor",
                    ProgressValue: 5,
                    Unlocked: false,
                    UnlockedAt: null,
                    Claimed: false,
                    Version: 1,
                    UpdatedAt: now),
            });
            await db.SetWH40KMetaAchievements(userB, new List<WH40KMetaAchievementDbData>
            {
                new(
                    AchievementId: "wh40k-ach-veteran",
                    ProgressValue: 100,
                    Unlocked: true,
                    UnlockedAt: now,
                    Claimed: true,
                    Version: 2,
                    UpdatedAt: now),
            });

            await db.SetWH40KMetaDecorations(userA, new List<WH40KMetaDecorationDbData>
            {
                new(
                    UnlockId: "decor.ghost.standard",
                    Unlocked: true,
                    UnlockedAt: now,
                    SourceLevel: 1,
                    UpdatedAt: now),
            });
            await db.SetWH40KMetaDecorations(userB, new List<WH40KMetaDecorationDbData>
            {
                new(
                    UnlockId: "decor.color.gold",
                    Unlocked: true,
                    UnlockedAt: now,
                    SourceLevel: 5,
                    UpdatedAt: now),
            });

            var progressA = await db.GetWH40KMetaProgress(userA, CancellationToken.None);
            var progressB = await db.GetWH40KMetaProgress(userB, CancellationToken.None);
            var achA = await db.GetWH40KMetaAchievements(userA, CancellationToken.None);
            var achB = await db.GetWH40KMetaAchievements(userB, CancellationToken.None);
            var decorA = await db.GetWH40KMetaDecorations(userA, CancellationToken.None);
            var decorB = await db.GetWH40KMetaDecorations(userB, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(progressA, Is.Not.Null);
                Assert.That(progressB, Is.Not.Null);

                Assert.That(progressA!.LifetimeXp, Is.EqualTo(120));
                Assert.That(progressB!.LifetimeXp, Is.EqualTo(990));
                Assert.That(progressA.LastAccountResetAt, Is.EqualTo(now.AddDays(-10)));
                Assert.That(progressB.LastAccountResetAt, Is.Null);
                Assert.That(progressA.SelectedGhostSkinId, Is.EqualTo("decor.ghost.standard"));
                Assert.That(progressB.SelectedGhostSkinId, Is.EqualTo("decor.ghost.warp"));

                Assert.That(achA.Select(a => a.AchievementId), Is.EquivalentTo(new[] { "wh40k-ach-frontline-anchor" }));
                Assert.That(achB.Select(a => a.AchievementId), Is.EquivalentTo(new[] { "wh40k-ach-veteran" }));

                Assert.That(decorA.Select(d => d.UnlockId), Is.EquivalentTo(new[] { "decor.ghost.standard" }));
                Assert.That(decorB.Select(d => d.UnlockId), Is.EquivalentTo(new[] { "decor.color.gold" }));
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KDiscordAuthRoundTrip()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userId = NewUserId();

            await db.UpdatePlayerRecord(userId, "DiscordAuthTester", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 3, 18, 18, 0, 0, TimeSpan.Zero);
            var payload = new WH40KDiscordAuthDbData(
                DiscordUserId: "123456789012345678",
                Username: "MechanicusUser",
                GlobalName: "Forge Adept",
                AvatarHash: "avatar_hash",
                AccessToken: "access_token",
                RefreshToken: "refresh_token",
                TokenType: "Bearer",
                Scope: "identify guilds.members.read",
                LinkedAt: now,
                TokenExpiresAt: now.AddHours(1),
                LastRefreshAt: now,
                GuildIdCached: "987654321098765432",
                LastGuildRefreshAt: now,
                GuildMemberCached: true,
                GuildNickname: "Demiurge",
                RoleCacheJson: "[\"111\",\"222\"]");

            await db.SetWH40KDiscordLink(userId, payload);

            var loaded = await db.GetWH40KDiscordLink(userId, CancellationToken.None);
            Assert.That(loaded, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(loaded!.DiscordUserId, Is.EqualTo(payload.DiscordUserId));
                Assert.That(loaded.Username, Is.EqualTo(payload.Username));
                Assert.That(loaded.GlobalName, Is.EqualTo(payload.GlobalName));
                Assert.That(loaded.AccessToken, Is.EqualTo(payload.AccessToken));
                Assert.That(loaded.RefreshToken, Is.EqualTo(payload.RefreshToken));
                Assert.That(loaded.GuildIdCached, Is.EqualTo(payload.GuildIdCached));
                Assert.That(loaded.GuildMemberCached, Is.True);
                Assert.That(loaded.GuildNickname, Is.EqualTo(payload.GuildNickname));
                Assert.That(loaded.RoleCacheJson, Is.EqualTo(payload.RoleCacheJson));
            });

            await db.ClearWH40KDiscordLink(userId);
            var cleared = await db.GetWH40KDiscordLink(userId, CancellationToken.None);
            Assert.That(cleared, Is.Null);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KDiscordAuthRejectsDuplicateDiscordUserId()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var userA = NewUserId();
            var userB = NewUserId();

            await db.UpdatePlayerRecord(userA, "DiscordDupA", IPAddress.Loopback, null);
            await db.UpdatePlayerRecord(userB, "DiscordDupB", IPAddress.Loopback, null);

            var now = new DateTimeOffset(2026, 3, 18, 19, 0, 0, TimeSpan.Zero);
            var first = new WH40KDiscordAuthDbData(
                DiscordUserId: "555555555555555555",
                Username: "UniqueUser",
                GlobalName: null,
                AvatarHash: null,
                AccessToken: "a",
                RefreshToken: "b",
                TokenType: "Bearer",
                Scope: "identify guilds.members.read",
                LinkedAt: now,
                TokenExpiresAt: now.AddHours(1),
                LastRefreshAt: now,
                GuildIdCached: null,
                LastGuildRefreshAt: null,
                GuildMemberCached: false,
                GuildNickname: null,
                RoleCacheJson: "[]");

            await db.SetWH40KDiscordLink(userA, first);

            var second = first with
            {
                Username = "OtherUser",
            };

            Assert.That(
                async () => await db.SetWH40KDiscordLink(userB, second),
                Throws.TypeOf<InvalidOperationException>());

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KAuthMigrationMovesLegacyMetaDataToAuthenticatedAccount()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var legacyUserId = NewUserId();
            var authenticatedUserId = NewUserId();
            const string userName = "AuthMigrationMetaUser";

            var progress = new WH40KMetaProgressDbData(
                LifetimeXp: 540,
                SeasonXp: 18,
                LastProgressAt: new DateTimeOffset(2026, 6, 8, 12, 45, 0, TimeSpan.Zero),
                LastAccountResetAt: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                SelectedGhostSkinId: "decor.ghost.standard",
                SelectedOocTitleId: "decor.title.none",
                SelectedOocNameColorId: "decor.color.default");

            var achievements = new[]
            {
                new WH40KMetaAchievementDbData(
                    "wh40k-ach-test",
                    3,
                    true,
                    new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero),
                    true,
                    1,
                    new DateTimeOffset(2026, 6, 8, 12, 5, 0, TimeSpan.Zero))
            };

            var decorations = new[]
            {
                new WH40KMetaDecorationDbData(
                    "decor.ghost.standard",
                    true,
                    new DateTimeOffset(2026, 6, 8, 12, 10, 0, TimeSpan.Zero),
                    4,
                    new DateTimeOffset(2026, 6, 8, 12, 11, 0, TimeSpan.Zero))
            };

            var development = new[]
            {
                new WH40KMetaDevelopmentUnlockDbData(
                    "dev.test.node",
                    new DateTimeOffset(2026, 6, 8, 12, 20, 0, TimeSpan.Zero),
                    2,
                    new DateTimeOffset(2026, 6, 8, 12, 21, 0, TimeSpan.Zero))
            };

            await db.AssignUserIdAsync(userName, legacyUserId);
            await db.UpdatePlayerRecord(legacyUserId, userName, IPAddress.Loopback, null);
            await db.SetWH40KMetaProgress(legacyUserId, progress);
            await db.SetWH40KMetaAchievements(legacyUserId, achievements);
            await db.SetWH40KMetaDecorations(legacyUserId, decorations);
            await db.SetWH40KMetaDevelopmentUnlocks(legacyUserId, development);

            var result = await db.MigrateLegacyGuestAccountAsync(userName, authenticatedUserId, CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(WH40KAuthAccountMigrationOutcome.Migrated));
            Assert.That(await db.GetAssignedUserIdAsync(userName), Is.Null);

            var migratedProgress = await db.GetWH40KMetaProgress(authenticatedUserId, CancellationToken.None);
            var migratedAchievements = await db.GetWH40KMetaAchievements(authenticatedUserId, CancellationToken.None);
            var migratedDecorations = await db.GetWH40KMetaDecorations(authenticatedUserId, CancellationToken.None);
            var migratedDevelopment = await db.GetWH40KMetaDevelopmentUnlocks(authenticatedUserId, CancellationToken.None);
            var legacyProgressAfter = await db.GetWH40KMetaProgress(legacyUserId, CancellationToken.None);
            var legacyAchievementsAfter = await db.GetWH40KMetaAchievements(legacyUserId, CancellationToken.None);
            var legacyDecorationsAfter = await db.GetWH40KMetaDecorations(legacyUserId, CancellationToken.None);
            var legacyDevelopmentAfter = await db.GetWH40KMetaDevelopmentUnlocks(legacyUserId, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(migratedProgress, Is.EqualTo(progress));
                Assert.That(migratedAchievements, Is.EqualTo(achievements));
                Assert.That(migratedDecorations, Is.EqualTo(decorations));
                Assert.That(migratedDevelopment, Is.EqualTo(development));
            });

            Assert.Multiple(() =>
            {
                Assert.That(legacyProgressAfter, Is.Null);
                Assert.That(legacyAchievementsAfter, Is.Empty);
                Assert.That(legacyDecorationsAfter, Is.Empty);
                Assert.That(legacyDevelopmentAfter, Is.Empty);
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestWH40KAuthMigrationMergesExistingTargetMetaProgress()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var legacyUserId = NewUserId();
            var authenticatedUserId = NewUserId();
            const string userName = "AuthMigrationMergeUser";

            var legacyProgress = new WH40KMetaProgressDbData(
                LifetimeXp: 420,
                SeasonXp: 9,
                LastProgressAt: new DateTimeOffset(2026, 6, 7, 8, 30, 0, TimeSpan.Zero),
                LastAccountResetAt: new DateTimeOffset(2026, 5, 30, 8, 30, 0, TimeSpan.Zero),
                SelectedGhostSkinId: "decor.ghost.legacy",
                SelectedOocTitleId: "decor.title.legacy",
                SelectedOocNameColorId: "decor.color.legacy");

            var targetProgress = new WH40KMetaProgressDbData(
                LifetimeXp: 80,
                SeasonXp: 3,
                LastProgressAt: new DateTimeOffset(2026, 6, 8, 8, 30, 0, TimeSpan.Zero),
                LastAccountResetAt: null,
                SelectedGhostSkinId: "decor.ghost.target",
                SelectedOocTitleId: null,
                SelectedOocNameColorId: "decor.color.target");

            await db.AssignUserIdAsync(userName, legacyUserId);
            await db.UpdatePlayerRecord(legacyUserId, userName, IPAddress.Loopback, null);
            await db.UpdatePlayerRecord(authenticatedUserId, userName, IPAddress.Loopback, null);
            await db.SetWH40KMetaProgress(legacyUserId, legacyProgress);
            await db.SetWH40KMetaProgress(authenticatedUserId, targetProgress);

            var result = await db.MigrateLegacyGuestAccountAsync(userName, authenticatedUserId, CancellationToken.None);
            var mergedProgress = await db.GetWH40KMetaProgress(authenticatedUserId, CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(WH40KAuthAccountMigrationOutcome.Migrated));
            Assert.That(mergedProgress, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(mergedProgress!.LifetimeXp, Is.EqualTo(500));
                Assert.That(mergedProgress.SeasonXp, Is.EqualTo(12));
                Assert.That(mergedProgress.LastProgressAt, Is.EqualTo(targetProgress.LastProgressAt));
                Assert.That(mergedProgress.LastAccountResetAt, Is.EqualTo(legacyProgress.LastAccountResetAt));
                Assert.That(mergedProgress.SelectedGhostSkinId, Is.EqualTo("decor.ghost.target"));
                Assert.That(mergedProgress.SelectedOocTitleId, Is.EqualTo("decor.title.legacy"));
                Assert.That(mergedProgress.SelectedOocNameColorId, Is.EqualTo("decor.color.target"));
            });

            Assert.That(await db.GetWH40KMetaProgress(legacyUserId, CancellationToken.None), Is.Null);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestLegacyGuestAuthMigrationTransfersCriticalAccountData()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var preferences = (ServerPreferencesManager) server.ResolveDependency<IServerPreferencesManager>();

            var username = "Faragonda";
            var legacyUserId = NewUserId();
            var authenticatedUserId = NewUserId();
            var now = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

            await db.AssignUserIdAsync(username, legacyUserId);
            await db.UpdatePlayerRecord(legacyUserId, username, IPAddress.Parse("203.0.113.10"), null);
            await db.UpdatePlayerRecord(authenticatedUserId, username, IPAddress.Parse("198.51.100.42"), null);

            var legacyProfile = CharlieCharlieson().WithName("Legacy Hero");
            var targetProfile = CharlieCharlieson().WithName("Fresh Auth");

            await db.InitPrefsAsync(legacyUserId, legacyProfile);
            await db.InitPrefsAsync(authenticatedUserId, targetProfile);

            await db.UpdatePlayTimes(new[]
            {
                new PlayTimeUpdate(legacyUserId, PlayTimeTrackingShared.TrackerOverall, TimeSpan.FromHours(10)),
                new PlayTimeUpdate(legacyUserId, "Job:Captain", TimeSpan.FromMinutes(45)),
                new PlayTimeUpdate(authenticatedUserId, PlayTimeTrackingShared.TrackerOverall, TimeSpan.FromMinutes(5)),
            });

            await db.SetWH40KMetaProgress(legacyUserId, new WH40KMetaProgressDbData(
                LifetimeXp: 500,
                SeasonXp: 40,
                LastProgressAt: now.AddDays(-1),
                LastAccountResetAt: now.AddDays(-10),
                SelectedGhostSkinId: "decor.ghost.standard",
                SelectedOocTitleId: "decor.title.legacy",
                SelectedOocNameColorId: "decor.color.default"));
            await db.SetWH40KMetaProgress(authenticatedUserId, new WH40KMetaProgressDbData(
                LifetimeXp: 15,
                SeasonXp: 3,
                LastProgressAt: now,
                LastAccountResetAt: null,
                SelectedGhostSkinId: null,
                SelectedOocTitleId: "decor.title.auth",
                SelectedOocNameColorId: null));

            await db.SetWH40KMetaAchievements(legacyUserId, new List<WH40KMetaAchievementDbData>
            {
                new("wh40k-ach-frontline-anchor", 10, false, null, false, 1, now.AddDays(-1)),
                new("wh40k-ach-veteran", 100, true, now.AddDays(-5), true, 2, now.AddDays(-5)),
            });
            await db.SetWH40KMetaAchievements(authenticatedUserId, new List<WH40KMetaAchievementDbData>
            {
                new("wh40k-ach-frontline-anchor", 2, false, null, false, 1, now),
                new("wh40k-ach-special-complete-all", 1, true, now, false, 1, now),
            });

            await db.SetWH40KMetaDecorations(legacyUserId, new List<WH40KMetaDecorationDbData>
            {
                new("decor.ghost.standard", true, now.AddDays(-20), 1, now.AddDays(-1)),
            });
            await db.SetWH40KMetaDecorations(authenticatedUserId, new List<WH40KMetaDecorationDbData>
            {
                new("decor.color.gold", true, now, 5, now),
            });

            await db.SetWH40KMetaDevelopmentUnlocks(legacyUserId, new List<WH40KMetaDevelopmentUnlockDbData>
            {
                new("node_alpha", now.AddDays(-2), 1, now.AddDays(-1)),
            });
            await db.SetWH40KMetaDevelopmentUnlocks(authenticatedUserId, new List<WH40KMetaDevelopmentUnlockDbData>
            {
                new("node_beta", now, 2, now),
            });

            await db.AddToWhitelistAsync(legacyUserId);
            await db.AddToWhitelistAsync(authenticatedUserId);
            await db.AddJobWhitelist(legacyUserId.UserId, new ProtoId<JobPrototype>("Captain"));
            await db.AddJobWhitelist(authenticatedUserId.UserId, new ProtoId<JobPrototype>("ChiefEngineer"));
            await db.UpdateBanExemption(legacyUserId, ServerBanExemptFlags.Datacenter);
            await db.UpdateBanExemption(authenticatedUserId, ServerBanExemptFlags.IP);

            await db.AddAdminAsync(new Admin
            {
                UserId = legacyUserId.UserId,
                Title = "Legacy Admin",
                Flags = new List<AdminFlag>
                {
                    new()
                    {
                        AdminId = legacyUserId.UserId,
                        Flag = "Admin",
                        Negative = false
                    }
                }
            }, CancellationToken.None);

            var noteId = await db.AddAdminNote(new AdminNote
            {
                PlayerUserId = legacyUserId.UserId,
                CreatedById = legacyUserId.UserId,
                PlaytimeAtNote = TimeSpan.FromMinutes(15),
                Message = "Legacy note",
                Severity = NoteSeverity.High,
                CreatedAt = now.UtcDateTime,
                LastEditedAt = now.UtcDateTime,
                Secret = false,
            });

            var watchlistId = await db.AddAdminWatchlist(new AdminWatchlist
            {
                PlayerUserId = legacyUserId.UserId,
                CreatedById = legacyUserId.UserId,
                PlaytimeAtNote = TimeSpan.FromMinutes(20),
                Message = "Legacy watchlist",
                CreatedAt = now.UtcDateTime,
                LastEditedAt = now.UtcDateTime,
            });

            var messageId = await db.AddAdminMessage(new AdminMessage
            {
                PlayerUserId = legacyUserId.UserId,
                CreatedById = legacyUserId.UserId,
                PlaytimeAtNote = TimeSpan.FromMinutes(25),
                Message = "Legacy message",
                CreatedAt = now.UtcDateTime,
                LastEditedAt = now.UtcDateTime,
                Seen = false,
                Dismissed = false,
            });

            await db.AddMuteAsync(new WH40KMuteDef(
                id: null,
                userId: legacyUserId,
                type: WH40KMuteType.Chat,
                reason: "Legacy mute",
                mutingAdmin: legacyUserId,
                muteTime: now.AddDays(-1),
                expirationTime: now.AddDays(1),
                unmute: null));

            await db.AddBanAsync(new BanDef(
                id: null,
                type: BanType.Server,
                userIds: ImmutableArray.Create(legacyUserId),
                addresses: ImmutableArray<(IPAddress address, int cidrMask)>.Empty,
                hwIds: ImmutableArray<ImmutableTypedHwid>.Empty,
                banTime: now.AddDays(-3),
                expirationTime: null,
                roundIds: ImmutableArray<int>.Empty,
                playtimeAtNote: TimeSpan.FromHours(1),
                reason: "Legacy ban",
                severity: NoteSeverity.High,
                banningAdmin: legacyUserId,
                unban: null));

            var result = await db.MigrateLegacyGuestAccountAsync(username, authenticatedUserId, CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(WH40KAuthAccountMigrationOutcome.Migrated));
            Assert.That(result.LegacyUserId, Is.EqualTo(legacyUserId));
            Assert.That(await db.GetAssignedUserIdAsync(username), Is.Null);
            Assert.That(await db.GetPlayerPreferencesAsync(legacyUserId, CancellationToken.None), Is.Null);
            Assert.That(await db.GetPlayerRecordByUserId(legacyUserId, CancellationToken.None), Is.Null);

            var migratedPrefs = await db.GetPlayerPreferencesAsync(authenticatedUserId, CancellationToken.None);
            Assert.That(migratedPrefs, Is.Not.Null);
            var migratedProfile = preferences.ConvertProfiles(migratedPrefs!.Profiles.Single(profile => profile.Slot == 0));
            Assert.That(migratedProfile.Name, Is.EqualTo("Legacy Hero"));

            var playTimes = await db.GetPlayTimes(authenticatedUserId.UserId, CancellationToken.None);
            Assert.That(playTimes.Single(p => p.Tracker == PlayTimeTrackingShared.TrackerOverall).TimeSpent, Is.EqualTo(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(5)));
            Assert.That(playTimes.Single(p => p.Tracker == "Job:Captain").TimeSpent, Is.EqualTo(TimeSpan.FromMinutes(45)));

            var progress = await db.GetWH40KMetaProgress(authenticatedUserId, CancellationToken.None);
            Assert.That(progress, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(progress!.LifetimeXp, Is.EqualTo(515));
                Assert.That(progress.SeasonXp, Is.EqualTo(43));
                Assert.That(progress.SelectedGhostSkinId, Is.EqualTo("decor.ghost.standard"));
                Assert.That(progress.SelectedOocTitleId, Is.EqualTo("decor.title.auth"));
            });

            var achievements = (await db.GetWH40KMetaAchievements(authenticatedUserId, CancellationToken.None))
                .ToDictionary(achievement => achievement.AchievementId, StringComparer.Ordinal);
            Assert.Multiple(() =>
            {
                Assert.That(achievements["wh40k-ach-frontline-anchor"].ProgressValue, Is.EqualTo(12));
                Assert.That(achievements["wh40k-ach-veteran"].Claimed, Is.True);
                Assert.That(achievements["wh40k-ach-special-complete-all"].Unlocked, Is.True);
            });

            var decorations = (await db.GetWH40KMetaDecorations(authenticatedUserId, CancellationToken.None))
                .Select(decoration => decoration.UnlockId)
                .ToHashSet(StringComparer.Ordinal);
            Assert.That(decorations, Is.EquivalentTo(new[] { "decor.ghost.standard", "decor.color.gold" }));

            var development = (await db.GetWH40KMetaDevelopmentUnlocks(authenticatedUserId, CancellationToken.None))
                .Select(node => node.NodeId)
                .ToHashSet(StringComparer.Ordinal);
            Assert.That(development, Is.EquivalentTo(new[] { "node_alpha", "node_beta" }));

            Assert.That(await db.GetWhitelistStatusAsync(authenticatedUserId), Is.True);
            Assert.That(await db.GetJobWhitelists(authenticatedUserId.UserId, CancellationToken.None), Is.EquivalentTo(new[] { "Captain", "ChiefEngineer" }));
            Assert.That(await db.GetBanExemption(authenticatedUserId, CancellationToken.None), Is.EqualTo(ServerBanExemptFlags.Datacenter | ServerBanExemptFlags.IP));

            var adminData = await db.GetAdminDataForAsync(authenticatedUserId, CancellationToken.None);
            Assert.That(adminData, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(adminData!.Title, Is.EqualTo("Legacy Admin"));
                Assert.That(adminData.Flags.Select(flag => flag.Flag), Is.EquivalentTo(new[] { "Admin" }));
            });

            var note = await db.GetAdminNote(noteId);
            var watchlist = await db.GetAdminWatchlist(watchlistId);
            var message = await db.GetAdminMessage(messageId);
            Assert.Multiple(() =>
            {
                Assert.That(note!.Player!.UserId, Is.EqualTo(authenticatedUserId));
                Assert.That(note.CreatedBy!.UserId, Is.EqualTo(authenticatedUserId));
                Assert.That(watchlist!.Player!.UserId, Is.EqualTo(authenticatedUserId));
                Assert.That(watchlist.CreatedBy!.UserId, Is.EqualTo(authenticatedUserId));
                Assert.That(message!.Player!.UserId, Is.EqualTo(authenticatedUserId));
                Assert.That(message.CreatedBy!.UserId, Is.EqualTo(authenticatedUserId));
            });

            var mutes = await db.GetMutesAsync(authenticatedUserId, includeUnmuted: true);
            Assert.That(mutes, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(mutes[0].UserId, Is.EqualTo(authenticatedUserId));
                Assert.That(mutes[0].MutingAdmin, Is.EqualTo(authenticatedUserId));
                Assert.That(mutes[0].Reason, Is.EqualTo("Legacy mute"));
            });

            var bans = await db.GetBansAsync(
                address: null,
                userId: authenticatedUserId,
                hwId: null,
                modernHWIds: null,
                includeUnbanned: true,
                type: BanType.Server);
            Assert.That(bans, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(bans[0].UserIds, Is.EquivalentTo(new[] { authenticatedUserId }));
                Assert.That(bans[0].BanningAdmin, Is.EqualTo(authenticatedUserId));
                Assert.That(bans[0].Reason, Is.EqualTo("Legacy ban"));
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestLegacyGuestAuthMigrationCleansMatchingAssignment()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var userId = NewUserId();

            await db.AssignUserIdAsync("CleanAssignment", userId);
            var result = await db.MigrateLegacyGuestAccountAsync("CleanAssignment", userId, CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(WH40KAuthAccountMigrationOutcome.CleanedAssignment));
            Assert.That(await db.GetAssignedUserIdAsync("CleanAssignment"), Is.Null);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestNoPendingDatabaseChanges()
        {
            var pair = Pair;
            var server = pair.Server;
            var db = GetDb(server);
            Assert.That(async () => await db.HasPendingModelChanges(), Is.False,
                "The database has pending model changes. Add a new migration to apply them. See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations");
        }

        private static NetUserId NewUserId()
        {
            return new(Guid.NewGuid());
        }

        private const string InvalidSpecies = "WingusDingus";

        private static bool[] _trueFalse = [true, false];

        [Test]
        [TestCaseSource(nameof(_trueFalse))]
        public async Task InvalidSpeciesConversion(bool legacy)
        {
            var pair = Pair;
            var server = pair.Server;
            var db = GetDb(pair.Server);
            var preferences = (ServerPreferencesManager)pair.Server.ResolveDependency<IServerPreferencesManager>();

            var proto = server.ResolveDependency<IPrototypeManager>();
            Assert.That(!proto.HasIndex<SpeciesPrototype>(InvalidSpecies), "You should not have added a species called WingusDingus, but change it in this test to something else I guess");

            var bogus = new HumanoidCharacterProfile()
            {
                Species = InvalidSpecies,
            };

            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            await db.InitPrefsAsync(username, new HumanoidCharacterProfile());
            await db.SaveCharacterSlotAsync(username, bogus, 0);
            await db.SaveSelectedCharacterIndexAsync(username, 0);

            if (legacy)
                await db.MakeCharacterSlotLegacyAsync(username, 0);

            var prefs = await db.GetPlayerPreferencesAsync(username, CancellationToken.None);

            Assert.That(prefs, Is.Not.Null);
            await server.WaitAssertion(() =>
            {
                var converted = preferences.ConvertPreferences(prefs);

                Assert.That(converted.Characters, Has.Count.EqualTo(1));
                Assert.That(converted.Characters[0].Species, Is.Not.EqualTo(InvalidSpecies));
                Assert.That(converted.Characters[0].Species, Is.EqualTo(HumanoidCharacterProfile.DefaultSpecies));
            });
        }
    }
}
