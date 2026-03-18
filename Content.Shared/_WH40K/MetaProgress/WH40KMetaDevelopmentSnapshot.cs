using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaDevelopmentSnapshot
{
	public int TotalSkillPoints { get; }

	public int SpentSkillPoints { get; }

	public int AvailableSkillPoints { get; }

	public List<string> OpenedNodeIds { get; }

	public WH40KMetaDevelopmentSnapshot(int totalSkillPoints, int spentSkillPoints, int availableSkillPoints, List<string> openedNodeIds)
	{
		TotalSkillPoints = totalSkillPoints;
		SpentSkillPoints = spentSkillPoints;
		AvailableSkillPoints = availableSkillPoints;
		OpenedNodeIds = openedNodeIds;
	}
}
