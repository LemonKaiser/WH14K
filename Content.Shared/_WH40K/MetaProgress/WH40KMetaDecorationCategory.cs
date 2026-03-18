using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public enum WH40KMetaDecorationCategory : byte
{
	GhostSkins,
	OocTitles,
	OocNameColors
}
