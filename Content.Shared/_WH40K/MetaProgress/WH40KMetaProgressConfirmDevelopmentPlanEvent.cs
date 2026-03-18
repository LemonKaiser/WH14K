using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.MetaProgress;

[Serializable]
[NetSerializable]
public sealed class WH40KMetaProgressConfirmDevelopmentPlanEvent : EntityEventArgs
{
	public List<string> NodeIds { get; }

	public WH40KMetaProgressConfirmDevelopmentPlanEvent(List<string> nodeIds)
	{
		NodeIds = nodeIds;
	}
}
