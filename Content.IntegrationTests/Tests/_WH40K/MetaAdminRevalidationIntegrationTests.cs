#nullable enable

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server._WH40K.MetaProgress;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class MetaAdminRevalidationIntegrationTests
{
	[TestPrototypes]
	private const string Prototypes = @"
- type: wh40kMetaAchievement
  id: AdminTestMetaAchievement
  category: Special
  title: test-meta-achievement-title
  description: test-meta-achievement-description
  task: test-meta-achievement-task
  target: 1

- type: loadout
  id: AdminTestMetaLevelDefault

- type: loadout
  id: AdminTestMetaLevelLocked
  effects:
  - !type:WH40KMetaLevelLoadoutEffect
    requiredLevel: 5

- type: loadoutGroup
  id: AdminTestMetaLevelGroup
  name: generic-unknown
  minLimit: 1
  defaultSelected: 1
  loadouts:
  - AdminTestMetaLevelDefault
  - AdminTestMetaLevelLocked

- type: loadout
  id: AdminTestMetaAchievementDefault

- type: loadout
  id: AdminTestMetaAchievementLocked
  effects:
  - !type:WH40KMetaAchievementLoadoutEffect
    achievement: AdminTestMetaAchievement

- type: loadoutGroup
  id: AdminTestMetaAchievementGroup
  name: generic-unknown
  minLimit: 1
  defaultSelected: 1
  loadouts:
  - AdminTestMetaAchievementDefault
  - AdminTestMetaAchievementLocked

- type: roleLoadout
  id: AdminTestMetaRoleLoadout
  groups:
  - AdminTestMetaLevelGroup
  - AdminTestMetaAchievementGroup
";

	[Test]
	public async Task RevalidateCommandRepairsOfflineUserByCkeyAndPersistsDb()
	{
		await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
		var server = pair.Server;
		var db = server.ResolveDependency<IServerDbManager>();
		var prefsManager = server.ResolveDependency<IServerPreferencesManager>();

		var userId = new NetUserId(Guid.NewGuid());
		const string userName = "metarepairckey";
		await SeedInvalidOfflineUserAsync(db, userId, userName, createPlayerRecord: true);

		// Start async revalidation on the server thread — EnsureStateLoadedAsync needs
		// server ticks to process RunOnMainThread callbacks from the DB load path.
		Task<WH40KMetaProgressSystem.WH40KMetaDecorationRevalidationResult> decorationTask = null!;
		server.Post(() =>
		{
			var meta = server.System<WH40KMetaProgressSystem>();
			decorationTask = meta.RevalidateUnlocksForAdminAsync(userId);
		});
		while (decorationTask == null || !decorationTask.IsCompleted)
			await pair.RunTicksSync(1);
		var decorationResult = await decorationTask;
		var loadoutResult = await prefsManager.RevalidateWH40KMetaLoadoutsAsync(userId, decorationResult.Snapshot);

		await pair.RunTicksSync(10);
		await server.WaitIdleAsync();

		var prefs = await db.GetPlayerPreferencesAsync(userId, CancellationToken.None);
		var progress = await db.GetWH40KMetaProgress(userId, CancellationToken.None);
		var decorations = await db.GetWH40KMetaDecorations(userId, CancellationToken.None);

		Assert.That(prefs, Is.Not.Null);
		Assert.That(progress, Is.Not.Null);

		Assert.Multiple(() =>
		{
			Assert.That(GetSelectedLoadoutNames(prefs!, "AdminTestMetaRoleLoadout", "AdminTestMetaLevelGroup"), Is.EqualTo(new[] { "AdminTestMetaLevelDefault" }));
			Assert.That(GetSelectedLoadoutNames(prefs!, "AdminTestMetaRoleLoadout", "AdminTestMetaAchievementGroup"), Is.EqualTo(new[] { "AdminTestMetaAchievementDefault" }));
			Assert.That(progress!.SelectedGhostSkinId, Is.EqualTo("decor-ghost-standard"));
			Assert.That(decorations.Single(entry => entry.UnlockId == "decor-ghost-star").Unlocked, Is.False);
		});

		await pair.CleanReturnAsync();
	}

	[Test]
	public async Task ResetSelectionsCommandClearsValidSelectionsForOfflineUser()
	{
		await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
		var server = pair.Server;
		var db = server.ResolveDependency<IServerDbManager>();
		var prefsManager = server.ResolveDependency<IServerPreferencesManager>();

		var userId = new NetUserId(Guid.NewGuid());
		const string userName = "metaresetckey";
		await SeedOfflineUserWithRoleLoadoutAsync(db, userId, userName, CreateLockedSelectionsProfile(), createPlayerRecord: true);
		await SeedValidMetaSelectionsAsync(pair, userId);

		Task<WH40KMetaProgressSystem.WH40KMetaSelectionResetResult> selectionTask = null!;
		server.Post(() =>
		{
			var meta = server.System<WH40KMetaProgressSystem>();
			selectionTask = meta.ResetSelectionsForAdminAsync(userId);
		});
		while (selectionTask == null || !selectionTask.IsCompleted)
			await pair.RunTicksSync(1);
		var selectionResult = await selectionTask;
		await prefsManager.ResetWH40KMetaSelectionsAsync(userId, selectionResult.Snapshot);

		await pair.RunTicksSync(10);
		await server.WaitIdleAsync();

		var prefs = await db.GetPlayerPreferencesAsync(userId, CancellationToken.None);
		var progress = await db.GetWH40KMetaProgress(userId, CancellationToken.None);

		Assert.That(prefs, Is.Not.Null);
		Assert.That(progress, Is.Not.Null);

		Assert.Multiple(() =>
		{
			Assert.That(GetSelectedLoadoutNames(prefs!, "AdminTestMetaRoleLoadout", "AdminTestMetaLevelGroup"), Is.EqualTo(new[] { "AdminTestMetaLevelDefault" }));
			Assert.That(GetSelectedLoadoutNames(prefs!, "AdminTestMetaRoleLoadout", "AdminTestMetaAchievementGroup"), Is.EqualTo(new[] { "AdminTestMetaAchievementDefault" }));
			Assert.That(progress!.SelectedGhostSkinId, Is.EqualTo("decor-ghost-standard"));
			Assert.That(progress.SelectedOocTitleId, Is.Not.EqualTo("decor-title-chromatic"));
			Assert.That(progress.SelectedOocNameColorId, Is.Not.EqualTo("decor-color-violet"));
		});

		await pair.CleanReturnAsync();
	}

	[Test]
	public async Task RevalidateAllScansStoredUsersAndPreservesValidDbState()
	{
		await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
		var server = pair.Server;
		var db = server.ResolveDependency<IServerDbManager>();
		var prefsManager = server.ResolveDependency<IServerPreferencesManager>();

		var invalidUser = new NetUserId(Guid.NewGuid());
		var validUser = new NetUserId(Guid.NewGuid());

		await SeedInvalidOfflineUserAsync(db, invalidUser, "allscan-invalid", createPlayerRecord: false);
		await SeedOfflineUserWithRoleLoadoutAsync(db, validUser, "allscan-valid", CreateLockedSelectionsProfile(), createPlayerRecord: false);
		await SeedValidMetaSelectionsAsync(pair, validUser);

		// Start async revalidation on the server thread for both users
		Task<WH40KMetaProgressSystem.WH40KMetaDecorationRevalidationResult> invalidDecorTask = null!;
		server.Post(() =>
		{
			var meta = server.System<WH40KMetaProgressSystem>();
			invalidDecorTask = meta.RevalidateUnlocksForAdminAsync(invalidUser);
		});
		while (invalidDecorTask == null || !invalidDecorTask.IsCompleted)
			await pair.RunTicksSync(1);
		var invalidDecorResult = await invalidDecorTask;
		await prefsManager.RevalidateWH40KMetaLoadoutsAsync(invalidUser, invalidDecorResult.Snapshot);

		Task<WH40KMetaProgressSystem.WH40KMetaDecorationRevalidationResult> validDecorTask = null!;
		server.Post(() =>
		{
			var meta = server.System<WH40KMetaProgressSystem>();
			validDecorTask = meta.RevalidateUnlocksForAdminAsync(validUser);
		});
		while (validDecorTask == null || !validDecorTask.IsCompleted)
			await pair.RunTicksSync(1);
		var validDecorResult = await validDecorTask;
		await prefsManager.RevalidateWH40KMetaLoadoutsAsync(validUser, validDecorResult.Snapshot);

		await pair.RunTicksSync(10);
		await server.WaitIdleAsync();

		var invalidPrefs = await db.GetPlayerPreferencesAsync(invalidUser, CancellationToken.None);
		var invalidProgress = await db.GetWH40KMetaProgress(invalidUser, CancellationToken.None);
		var invalidDecorations = await db.GetWH40KMetaDecorations(invalidUser, CancellationToken.None);
		var validPrefs = await db.GetPlayerPreferencesAsync(validUser, CancellationToken.None);
		var validProgress = await db.GetWH40KMetaProgress(validUser, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(GetSelectedLoadoutNames(invalidPrefs!, "AdminTestMetaRoleLoadout", "AdminTestMetaLevelGroup"), Is.EqualTo(new[] { "AdminTestMetaLevelDefault" }));
			Assert.That(GetSelectedLoadoutNames(invalidPrefs!, "AdminTestMetaRoleLoadout", "AdminTestMetaAchievementGroup"), Is.EqualTo(new[] { "AdminTestMetaAchievementDefault" }));
			Assert.That(invalidProgress!.SelectedGhostSkinId, Is.EqualTo("decor-ghost-standard"));
			Assert.That(invalidDecorations.Single(entry => entry.UnlockId == "decor-ghost-star").Unlocked, Is.False);
			Assert.That(GetSelectedLoadoutNames(validPrefs!, "AdminTestMetaRoleLoadout", "AdminTestMetaLevelGroup"), Is.EqualTo(new[] { "AdminTestMetaLevelLocked" }));
			Assert.That(GetSelectedLoadoutNames(validPrefs!, "AdminTestMetaRoleLoadout", "AdminTestMetaAchievementGroup"), Is.EqualTo(new[] { "AdminTestMetaAchievementLocked" }));
			Assert.That(validProgress!.SelectedGhostSkinId, Is.EqualTo("decor-ghost-warp"));
		});

		await pair.CleanReturnAsync();
	}

	[Test]
	public async Task PreferencesHelpersReturnNoPreferencesForMissingUserWithoutDbCorruption()
	{
		await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
		var server = pair.Server;
		var db = server.ResolveDependency<IServerDbManager>();
		var prefsManager = server.ResolveDependency<IServerPreferencesManager>();
		var missingUser = new NetUserId(Guid.NewGuid());

		WH40KMetaProgressSnapshot snapshot = null!;
		await server.WaitPost(() => snapshot = server.System<WH40KMetaProgressSystem>().GetSnapshot(missingUser));

		var revalidate = await prefsManager.RevalidateWH40KMetaLoadoutsAsync(missingUser, snapshot);
		var reset = await prefsManager.ResetWH40KMetaSelectionsAsync(missingUser, snapshot);
		var persistedPrefs = await db.GetPlayerPreferencesAsync(missingUser, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(revalidate.PreferencesFound, Is.False);
			Assert.That(reset.PreferencesFound, Is.False);
			Assert.That(revalidate.Changed, Is.False);
			Assert.That(reset.Changed, Is.False);
			Assert.That(persistedPrefs, Is.Null);
		});

		await pair.CleanReturnAsync();
	}

	private static async Task SeedInvalidOfflineUserAsync(IServerDbManager db, NetUserId userId, string userName, bool createPlayerRecord)
	{
		await SeedOfflineUserWithRoleLoadoutAsync(db, userId, userName, CreateLockedSelectionsProfile(), createPlayerRecord);

		var now = DateTimeOffset.UtcNow;
		await db.SetWH40KMetaProgress(userId, new WH40KMetaProgressDbData(
			LifetimeXp: 0,
			SeasonXp: 0,
			LastProgressAt: now,
			SelectedGhostSkinId: "decor-ghost-star",
			SelectedOocTitleId: null,
			SelectedOocNameColorId: null));
		await db.SetWH40KMetaDecorations(userId, new[]
		{
			new WH40KMetaDecorationDbData("decor-ghost-star", true, now, 5, now)
		});
		await db.SetWH40KMetaAchievements(userId, Array.Empty<WH40KMetaAchievementDbData>());
	}

	private static async Task SeedOfflineUserWithRoleLoadoutAsync(IServerDbManager db, NetUserId userId, string userName, HumanoidCharacterProfile profile, bool createPlayerRecord)
	{
		if (createPlayerRecord)
		{
			await db.UpdatePlayerRecordAsync(userId, userName, IPAddress.Loopback, null);
		}

		await db.InitPrefsAsync(userId, profile, CancellationToken.None);
	}

	private static HumanoidCharacterProfile CreateLockedSelectionsProfile()
	{
		var profile = new HumanoidCharacterProfile();
		var loadout = new RoleLoadout("AdminTestMetaRoleLoadout");
		loadout.SelectedLoadouts["AdminTestMetaLevelGroup"] =
		[
			new Loadout
			{
				Prototype = "AdminTestMetaLevelLocked"
			}
		];
		loadout.SelectedLoadouts["AdminTestMetaAchievementGroup"] =
		[
			new Loadout
			{
				Prototype = "AdminTestMetaAchievementLocked"
			}
		];
		profile.SetLoadout(loadout);
		return profile;
	}

	private static string[] GetSelectedLoadoutNames(Preference prefs, string roleId, string groupId)
	{
		var profile = prefs.Profiles.Single(entry => entry.Slot == 0);
		var role = profile.Loadouts.Single(entry => entry.RoleName == roleId);
		var group = role.Groups.Single(entry => entry.GroupName == groupId);
		return group.Loadouts.Select(entry => entry.LoadoutName).OrderBy(entry => entry, StringComparer.Ordinal).ToArray();
	}

	private static async Task SeedValidMetaSelectionsAsync(TestPair pair, NetUserId userId)
	{
		var server = pair.Server;

		// Initialize state and wait for async DB load before mutating
		await server.WaitPost(() =>
		{
			var meta = server.System<WH40KMetaProgressSystem>();
			_ = meta.GetSnapshot(userId);
		});
		await pair.RunTicksSync(30);

		await server.WaitPost(() =>
		{
			var meta = server.System<WH40KMetaProgressSystem>();

			Assert.That(meta.TrySetLevel(userId, 6, out _, out _), Is.True);
			Assert.That(meta.TrySetAchievementUnlocked(userId, "AdminTestMetaAchievement", true, out _, out _, out _, out var achievementError), Is.True, achievementError);
			Assert.That(meta.TrySetDecorationSelection(userId, WH40KMetaDecorationCategory.GhostSkins, "decor-ghost-warp", out _, out var ghostError), Is.True, ghostError);
			Assert.That(meta.TrySetDecorationSelection(userId, WH40KMetaDecorationCategory.OocTitles, "decor-title-chromatic", out _, out var titleError), Is.True, titleError);
			Assert.That(meta.TrySetDecorationSelection(userId, WH40KMetaDecorationCategory.OocNameColors, "decor-color-violet", out _, out var colorError), Is.True, colorError);
		});

		await pair.RunTicksSync(10);
		await server.WaitIdleAsync();
	}
}
