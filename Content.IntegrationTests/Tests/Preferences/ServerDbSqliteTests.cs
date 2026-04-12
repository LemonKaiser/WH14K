using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Speech;
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
                Assert.That(loaded.SelectedGhostSkinId, Is.EqualTo("decor.ghost.standard"));
                Assert.That(loaded.SelectedOocTitleId, Is.EqualTo("decor.title.none"));
                Assert.That(loaded.SelectedOocNameColorId, Is.EqualTo("decor.color.default"));
            });

            var updated = new WH40KMetaProgressDbData(
                LifetimeXp: 777,
                SeasonXp: 15,
                LastProgressAt: new DateTimeOffset(2026, 2, 19, 12, 30, 0, TimeSpan.Zero),
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
                SelectedGhostSkinId: "decor.ghost.iron",
                SelectedOocTitleId: "decor.title.legend",
                SelectedOocNameColorId: "decor.color.gold");

            await db.SetWH40KMetaProgress(userId, first);

            var normalized = new WH40KMetaProgressDbData(
                LifetimeXp: -50,
                SeasonXp: -5,
                LastProgressAt: new DateTimeOffset(2026, 2, 20, 10, 30, 0, TimeSpan.Zero),
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
                SelectedGhostSkinId: "decor.ghost.standard",
                SelectedOocTitleId: "decor.title.none",
                SelectedOocNameColorId: "decor.color.default"));
            await db.SetWH40KMetaProgress(userB, new WH40KMetaProgressDbData(
                LifetimeXp: 990,
                SeasonXp: 12,
                LastProgressAt: now,
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
