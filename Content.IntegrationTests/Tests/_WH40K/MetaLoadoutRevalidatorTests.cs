using System.Collections.Generic;
using System.Linq;
using Content.Server._WH40K.MetaProgress;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class MetaLoadoutRevalidatorTests
{
	[TestPrototypes]
	private const string Prototypes = @"
- type: wh40kMetaAchievement
  id: TestMetaAchievement
  category: Special
  title: test-meta-achievement-title
  description: test-meta-achievement-description
  task: test-meta-achievement-task
  target: 1

- type: loadout
  id: TestMetaLevelDefault

- type: loadout
  id: TestMetaLevelLocked
  effects:
  - !type:WH40KMetaLevelLoadoutEffect
    requiredLevel: 5

- type: loadoutGroup
  id: TestMetaLevelGroup
  name: generic-unknown
  minLimit: 1
  defaultSelected: 1
  loadouts:
  - TestMetaLevelDefault
  - TestMetaLevelLocked

- type: loadout
  id: TestMetaAchievementDefault

- type: loadout
  id: TestMetaAchievementLocked
  effects:
  - !type:WH40KMetaAchievementLoadoutEffect
    achievement: TestMetaAchievement

- type: loadoutGroup
  id: TestMetaAchievementGroup
  name: generic-unknown
  minLimit: 1
  defaultSelected: 1
  loadouts:
  - TestMetaAchievementDefault
  - TestMetaAchievementLocked

- type: roleLoadout
  id: TestMetaRoleLoadout
  groups:
  - TestMetaLevelGroup
  - TestMetaAchievementGroup
";

	[Test]
	public async Task MetaLockedSelectionsResetToDefaults()
	{
		var pair = await PoolManager.GetServerClient(new PoolSettings
		{
			Dirty = true,
		});

		var server = pair.Server;
		var prototypeManager = server.ResolveDependency<IPrototypeManager>();

		var roleLoadout = new RoleLoadout("TestMetaRoleLoadout");
		roleLoadout.SelectedLoadouts["TestMetaLevelGroup"] =
		[
			new Loadout
			{
				Prototype = "TestMetaLevelLocked"
			}
		];
		roleLoadout.SelectedLoadouts["TestMetaAchievementGroup"] =
		[
			new Loadout
			{
				Prototype = "TestMetaAchievementLocked"
			}
		];

		var profile = new HumanoidCharacterProfile();
		profile.SetLoadout(roleLoadout);

		var preferences = new PlayerPreferences(
			new[]
			{
				new KeyValuePair<int, HumanoidCharacterProfile>(0, profile)
			},
			0,
			default,
			[]);

		var result = WH40KMetaLoadoutRevalidator.Revalidate(
			preferences,
			prototypeManager,
			CreateSnapshot(level: 1),
			unlockRequirementsBypassed: false);

		var updatedLoadout = result.Preferences.GetProfile(0).Loadouts["TestMetaRoleLoadout"];

		Assert.Multiple(() =>
		{
			Assert.That(result.Changed, Is.True);
			Assert.That(result.ProfilesChanged, Is.EqualTo(1));
			Assert.That(result.RemovedSelections, Is.EqualTo(2));
			Assert.That(result.DefaultSelectionsApplied, Is.EqualTo(2));
			Assert.That(updatedLoadout.SelectedLoadouts["TestMetaLevelGroup"].Single().Prototype, Is.EqualTo(new ProtoId<LoadoutPrototype>("TestMetaLevelDefault")));
			Assert.That(updatedLoadout.SelectedLoadouts["TestMetaAchievementGroup"].Single().Prototype, Is.EqualTo(new ProtoId<LoadoutPrototype>("TestMetaAchievementDefault")));
		});

		await pair.CleanReturnAsync();
	}

	private static WH40KMetaProgressSnapshot CreateSnapshot(int level, params string[] completedAchievements)
	{
		var achievementEntries = completedAchievements
			.Select(id => new WH40KMetaAchievementSnapshotEntry(
				id,
				WH40KMetaAchievementCategory.Special,
				id,
				id,
				id,
				string.Empty,
				0,
				[],
				1,
				1,
				false,
				true))
			.ToList();

		return new WH40KMetaProgressSnapshot(
			level,
			0,
			0,
			0,
			0,
			achievementEntries.Count,
			achievementEntries.Count,
			achievementEntries,
			null,
			[],
			new WH40KMetaDecorationSelectionSnapshot(string.Empty, string.Empty, string.Empty),
			new WH40KMetaDevelopmentSnapshot(0, 0, 0, []));
	}
}
