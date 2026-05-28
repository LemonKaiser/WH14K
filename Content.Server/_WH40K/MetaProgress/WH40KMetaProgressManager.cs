#nullable disable warnings

using System;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;

namespace Content.Server._WH40K.MetaProgress;

public sealed partial class WH40KMetaProgressManager : ISharedWH40KMetaProgressManager
{
	[Dependency]
	private  IEntitySystemManager _entitySystems = default!;

	public bool TryGetMetaLevel(ICommonSession session, out int level)
	{
		WH40KMetaProgressSnapshot snapshot = _entitySystems.GetEntitySystem<WH40KMetaProgressSystem>().GetSnapshot(session.UserId);
		level = Math.Max(1, snapshot.Level);
		return true;
	}

	public bool TryHasCompletedAchievement(ICommonSession session, string achievementId, out bool completed)
	{
		completed = false;

		if (string.IsNullOrWhiteSpace(achievementId))
			return true;

		var normalizedId = achievementId.Trim();
		WH40KMetaProgressSnapshot snapshot = _entitySystems.GetEntitySystem<WH40KMetaProgressSystem>().GetSnapshot(session.UserId);

		foreach (var entry in snapshot.Achievements)
		{
			if (!string.Equals(entry.Id, normalizedId, StringComparison.Ordinal))
				continue;

			completed = entry.Completed;
			break;
		}

		return true;
	}
}
