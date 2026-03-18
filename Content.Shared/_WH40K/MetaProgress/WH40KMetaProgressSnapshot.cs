using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaProgressSnapshot
{
	public int Level { get; }

	public int CurrentXp { get; }

	public int RequiredXp { get; }

	public int LifetimeXp { get; }

	public int LevelCap { get; }

	public int CompletedAchievements { get; }

	public int TotalAchievements { get; }

	public List<WH40KMetaAchievementSnapshotEntry> Achievements { get; }

	public WH40KMetaNextRewardPreview? NextReward { get; }

	public List<WH40KMetaDecorationSnapshotEntry> Decorations { get; }

	public WH40KMetaDecorationSelectionSnapshot DecorationSelection { get; }

	public WH40KMetaDevelopmentSnapshot Development { get; }

	public WH40KMetaProgressSnapshot(int level, int currentXp, int requiredXp, int lifetimeXp, int levelCap, int completedAchievements, int totalAchievements, List<WH40KMetaAchievementSnapshotEntry> achievements, WH40KMetaNextRewardPreview? nextReward, List<WH40KMetaDecorationSnapshotEntry> decorations, WH40KMetaDecorationSelectionSnapshot decorationSelection, WH40KMetaDevelopmentSnapshot development)
	{
		Level = level;
		CurrentXp = currentXp;
		RequiredXp = requiredXp;
		LifetimeXp = lifetimeXp;
		LevelCap = levelCap;
		CompletedAchievements = completedAchievements;
		TotalAchievements = totalAchievements;
		Achievements = achievements;
		NextReward = nextReward;
		Decorations = decorations;
		DecorationSelection = decorationSelection;
		Development = development;
	}
}
