using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public enum WH40KMetaAchievementCategory : byte
{
	Combat,
	Support,
	Logistics,
	Objective,
	Participation,
	Hidden,
	Special
}
