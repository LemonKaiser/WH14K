namespace Content.Shared._WH40K.MetaProgress;

public sealed class WH40KMetaDevelopmentNodeDefinition
{
	public string Id { get; }

	public string BranchId { get; }

	public WH40KCharacterDevelopmentOrganType Organ { get; }

	public string NodeKey { get; }

	public int Cost { get; }

	public string? ParentId { get; }

	public bool IsRoot { get; }

	public bool UpperPath { get; }

	public int Depth { get; }

	public int SortOrder { get; }

	public WH40KMetaDevelopmentNodeDefinition(string id, string branchId, WH40KCharacterDevelopmentOrganType organ, string nodeKey, int cost, string? parentId, bool isRoot, bool upperPath, int depth, int sortOrder)
	{
		Id = id;
		BranchId = branchId;
		Organ = organ;
		NodeKey = nodeKey;
		Cost = cost;
		ParentId = parentId;
		IsRoot = isRoot;
		UpperPath = upperPath;
		Depth = depth;
		SortOrder = sortOrder;
	}
}
