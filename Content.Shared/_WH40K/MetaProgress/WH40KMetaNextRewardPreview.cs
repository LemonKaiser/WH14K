using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaNextRewardPreview
{
	public int Level { get; }

	public int Decorations { get; }

	public int SkillPoints { get; }

	public WH40KMetaNextRewardPreview(int level, int decorations, int skillPoints)
	{
		Level = level;
		Decorations = decorations;
		SkillPoints = skillPoints;
	}
}
