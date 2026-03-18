using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaProgressStateEvent : EntityEventArgs
{
	public WH40KMetaProgressSnapshot Snapshot { get; }

	public WH40KMetaProgressStateEvent(WH40KMetaProgressSnapshot snapshot)
	{
		Snapshot = snapshot;
	}
}
