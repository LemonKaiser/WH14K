using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.MetaProgress;

public sealed record WH40KMetaProfileRepairResult(
	bool PreferencesFound,
	int ProfilesChanged,
	int RemovedSelections,
	int DefaultSelectionsApplied)
{
	public bool Changed => ProfilesChanged > 0;
}

public sealed record WH40KMetaLoadoutRevalidationResult(
	PlayerPreferences Preferences,
	int ProfilesChanged,
	int RemovedSelections,
	int DefaultSelectionsApplied)
{
	public bool Changed => ProfilesChanged > 0;
}

public static class WH40KMetaLoadoutRevalidator
{
	private enum RepairMode : byte
	{
		RevalidateInvalidOnly,
		ResetAllMetaSelections
	}

	public static WH40KMetaLoadoutRevalidationResult Revalidate(
		PlayerPreferences preferences,
		IPrototypeManager prototypeManager,
		WH40KMetaProgressSnapshot snapshot,
		bool unlockRequirementsBypassed)
	{
		return Repair(preferences, prototypeManager, snapshot, unlockRequirementsBypassed, RepairMode.RevalidateInvalidOnly);
	}

	public static WH40KMetaLoadoutRevalidationResult ResetSelections(
		PlayerPreferences preferences,
		IPrototypeManager prototypeManager,
		WH40KMetaProgressSnapshot snapshot,
		bool unlockRequirementsBypassed)
	{
		return Repair(preferences, prototypeManager, snapshot, unlockRequirementsBypassed, RepairMode.ResetAllMetaSelections);
	}

	private static WH40KMetaLoadoutRevalidationResult Repair(
		PlayerPreferences preferences,
		IPrototypeManager prototypeManager,
		WH40KMetaProgressSnapshot snapshot,
		bool unlockRequirementsBypassed,
		RepairMode mode)
	{
		var access = new MetaLoadoutAccessState(snapshot, unlockRequirementsBypassed);
		var updatedCharacters = new Dictionary<int, HumanoidCharacterProfile>(preferences.Characters.Count);
		var profilesChanged = 0;
		var removedSelections = 0;
		var defaultSelectionsApplied = 0;

		foreach (var entry in preferences.Characters)
		{
			var updatedProfile = RevalidateProfile(entry.Value, prototypeManager, access, mode, out var profileRemoved, out var profileDefaulted, out var profileChanged);
			updatedCharacters[entry.Key] = updatedProfile;

			if (!profileChanged)
				continue;

			profilesChanged++;
			removedSelections += profileRemoved;
			defaultSelectionsApplied += profileDefaulted;
		}

		if (profilesChanged == 0)
			return new WH40KMetaLoadoutRevalidationResult(preferences, 0, 0, 0);

		var updatedPreferences = new PlayerPreferences(
			updatedCharacters,
			preferences.SelectedCharacterIndex,
			preferences.AdminOOCColor,
			new List<ProtoId<ConstructionPrototype>>(preferences.ConstructionFavorites));

		return new WH40KMetaLoadoutRevalidationResult(
			updatedPreferences,
			profilesChanged,
			removedSelections,
			defaultSelectionsApplied);
	}

	private static HumanoidCharacterProfile RevalidateProfile(
		HumanoidCharacterProfile profile,
		IPrototypeManager prototypeManager,
		MetaLoadoutAccessState access,
		RepairMode mode,
		out int removedSelections,
		out int defaultSelectionsApplied,
		out bool changed)
	{
		removedSelections = 0;
		defaultSelectionsApplied = 0;
		changed = false;

		HumanoidCharacterProfile? updatedProfile = null;

		foreach (var entry in profile.Loadouts)
		{
			var updatedLoadout = RevalidateRoleLoadout(entry.Value, prototypeManager, access, mode, out var roleRemoved, out var roleDefaulted, out var roleChanged);
			if (!roleChanged)
				continue;

			updatedProfile ??= profile.Clone();
			updatedProfile.SetLoadout(updatedLoadout);
			removedSelections += roleRemoved;
			defaultSelectionsApplied += roleDefaulted;
			changed = true;
		}

		return updatedProfile ?? profile;
	}

	private static RoleLoadout RevalidateRoleLoadout(
		RoleLoadout roleLoadout,
		IPrototypeManager prototypeManager,
		MetaLoadoutAccessState access,
		RepairMode mode,
		out int removedSelections,
		out int defaultSelectionsApplied,
		out bool changed)
	{
		removedSelections = 0;
		defaultSelectionsApplied = 0;
		changed = false;

		if (!prototypeManager.TryIndex(roleLoadout.Role, out var rolePrototype) || rolePrototype == null)
			return roleLoadout;

		var updatedLoadout = roleLoadout.Clone();

		foreach (var groupId in rolePrototype.Groups)
		{
			if (!prototypeManager.TryIndex(groupId, out var groupPrototype) || groupPrototype == null)
				continue;

			if (!updatedLoadout.SelectedLoadouts.TryGetValue(groupId, out var selections))
				continue;

			var removedFromGroup = false;

			for (var i = selections.Count - 1; i >= 0; i--)
			{
				var selected = selections[i];
				if (!prototypeManager.TryIndex(selected.Prototype, out var selectedPrototype) || selectedPrototype == null)
					continue;

				if (!groupPrototype.Loadouts.Contains(selected.Prototype))
					continue;

				if (!ShouldRemoveSelection(selectedPrototype, access, mode))
					continue;

				selections.RemoveAt(i);
				removedSelections++;
				removedFromGroup = true;
				changed = true;
			}

			if (!removedFromGroup)
				continue;

			var targetCount = Math.Min(groupPrototype.MaxLimit, Math.Max(groupPrototype.MinLimit, groupPrototype.DefaultSelected));
			for (var i = 0; i < groupPrototype.Loadouts.Count && selections.Count < targetCount; i++)
			{
				var candidateId = groupPrototype.Loadouts[i];
				if (selections.Exists(loadout => loadout.Prototype == candidateId))
					continue;

				if (!prototypeManager.TryIndex(candidateId, out var candidatePrototype) || candidatePrototype == null)
					continue;

				if (!MeetsMetaRequirements(candidatePrototype, access))
					continue;

				selections.Add(new Loadout
				{
					Prototype = candidatePrototype.ID
				});
				defaultSelectionsApplied++;
				changed = true;
			}
		}

		return changed ? updatedLoadout : roleLoadout;
	}

	private static bool ShouldRemoveSelection(LoadoutPrototype loadoutPrototype, MetaLoadoutAccessState access, RepairMode mode)
	{
		if (!HasMetaRequirements(loadoutPrototype))
			return false;

		if (mode == RepairMode.ResetAllMetaSelections)
			return true;

		return !MeetsMetaRequirements(loadoutPrototype, access);
	}

	private static bool HasMetaRequirements(LoadoutPrototype loadoutPrototype)
	{
		foreach (var effect in loadoutPrototype.Effects)
		{
			if (effect is WH40KMetaLevelLoadoutEffect or WH40KMetaAchievementLoadoutEffect)
				return true;
		}

		return false;
	}

	private static bool MeetsMetaRequirements(LoadoutPrototype loadoutPrototype, MetaLoadoutAccessState access)
	{
		if (access.UnlockRequirementsBypassed)
			return true;

		foreach (var effect in loadoutPrototype.Effects)
		{
			switch (effect)
			{
				case WH40KMetaLevelLoadoutEffect levelEffect:
					if (access.Level < Math.Max(1, levelEffect.RequiredLevel))
						return false;
					break;
				case WH40KMetaAchievementLoadoutEffect achievementEffect:
					if (!access.CompletedAchievements.Contains(achievementEffect.Achievement))
						return false;
					break;
			}
		}

		return true;
	}

	private sealed class MetaLoadoutAccessState
	{
		public readonly HashSet<string> CompletedAchievements;

		public readonly int Level;

		public readonly bool UnlockRequirementsBypassed;

		public MetaLoadoutAccessState(WH40KMetaProgressSnapshot snapshot, bool unlockRequirementsBypassed)
		{
			Level = snapshot.Level;
			UnlockRequirementsBypassed = unlockRequirementsBypassed;
			CompletedAchievements = snapshot.Achievements
				.Where(entry => entry.Completed)
				.Select(entry => entry.Id)
				.ToHashSet(StringComparer.Ordinal);
		}
	}
}
