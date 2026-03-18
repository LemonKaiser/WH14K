using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaProgressSetDecorationSelectionEvent : EntityEventArgs
{
	public WH40KMetaDecorationCategory Category { get; }

	public string DecorationId { get; }

	public WH40KMetaProgressSetDecorationSelectionEvent(WH40KMetaDecorationCategory category, string decorationId)
	{
		Category = category;
		DecorationId = decorationId;
	}
}
