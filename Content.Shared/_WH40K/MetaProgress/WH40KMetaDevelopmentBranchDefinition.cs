using System.Collections.Generic;

namespace Content.Shared._WH40K.MetaProgress;

public sealed class WH40KMetaDevelopmentBranchDefinition
{
	public string Id { get; }

	public WH40KCharacterDevelopmentOrganType Organ { get; }

	public int SortOrder { get; }

	public string RootNodeId { get; }

	public IReadOnlyList<string> UpperPathNodeIds { get; }

	public IReadOnlyList<string> LowerPathNodeIds { get; }

	public IReadOnlyList<string> UpperPathKeys { get; }

	public IReadOnlyList<string> LowerPathKeys { get; }

	public WH40KMetaDevelopmentBranchDefinition(string id, WH40KCharacterDevelopmentOrganType organ, int sortOrder, string rootNodeId, IReadOnlyList<string> upperPathNodeIds, IReadOnlyList<string> lowerPathNodeIds, IReadOnlyList<string> upperPathKeys, IReadOnlyList<string> lowerPathKeys)
	{
		Id = id;
		Organ = organ;
		SortOrder = sortOrder;
		RootNodeId = rootNodeId;
		UpperPathNodeIds = upperPathNodeIds;
		LowerPathNodeIds = lowerPathNodeIds;
		UpperPathKeys = upperPathKeys;
		LowerPathKeys = lowerPathKeys;
	}
}
