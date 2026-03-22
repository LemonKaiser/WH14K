using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaAchievementSnapshotEntry
{
	public string Id { get; }

	public WH40KMetaAchievementCategory Category { get; }

	public string TitleKey { get; }

	public string DescriptionKey { get; }

	public string TaskKey { get; }

	public string RewardKey { get; }

	public int RewardXp { get; }

	public List<string> RewardDecorationIds { get; }

	public int Progress { get; }

	public int Target { get; }

	public bool Hidden { get; }

	public bool Completed { get; }

	public WH40KMetaAchievementSnapshotEntry(string id, WH40KMetaAchievementCategory category, string titleKey, string descriptionKey, string taskKey, string rewardKey, int rewardXp, List<string> rewardDecorationIds, int progress, int target, bool hidden, bool completed)
	{
		Id = id;
		Category = category;
		TitleKey = titleKey;
		DescriptionKey = descriptionKey;
		TaskKey = taskKey;
		RewardKey = rewardKey;
		RewardXp = rewardXp;
		RewardDecorationIds = rewardDecorationIds;
		Progress = progress;
		Target = target;
		Hidden = hidden;
		Completed = completed;
	}
}
