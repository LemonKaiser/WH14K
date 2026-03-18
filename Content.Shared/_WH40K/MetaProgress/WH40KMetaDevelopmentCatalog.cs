using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._WH40K.MetaProgress;

public static class WH40KMetaDevelopmentCatalog
{
	public static readonly IReadOnlyList<WH40KMetaDevelopmentBranchDefinition> Branches;

	public static readonly IReadOnlyDictionary<string, WH40KMetaDevelopmentNodeDefinition> Nodes;

	public static readonly IReadOnlyList<WH40KMetaDevelopmentNodeDefinition> NodesInValidationOrder;

	static WH40KMetaDevelopmentCatalog()
	{
		List<WH40KMetaDevelopmentBranchDefinition> list = BuildBranches();
		Dictionary<string, WH40KMetaDevelopmentNodeDefinition> dictionary = BuildNodes(list);
		List<WH40KMetaDevelopmentNodeDefinition> list2 = new List<WH40KMetaDevelopmentNodeDefinition>(dictionary.Values);
		list2.Sort(delegate(WH40KMetaDevelopmentNodeDefinition left, WH40KMetaDevelopmentNodeDefinition right)
		{
			int num = left.SortOrder.CompareTo(right.SortOrder);
			return (num == 0) ? string.CompareOrdinal(left.Id, right.Id) : num;
		});
		Branches = list.ToArray();
		Nodes = dictionary;
		NodesInValidationOrder = list2.ToArray();
	}

	public static bool TryGetNode(string nodeId, [NotNullWhen(true)] out WH40KMetaDevelopmentNodeDefinition? node)
	{
		if (string.IsNullOrWhiteSpace(nodeId))
		{
			node = null;
			return false;
		}

		if (Nodes.TryGetValue(nodeId.Trim(), out var resolvedNode))
		{
			node = resolvedNode;
			return true;
		}

		node = null;
		return false;
	}

	public static bool TryGetBranch(string branchId, [NotNullWhen(true)] out WH40KMetaDevelopmentBranchDefinition? branch)
	{
		foreach (WH40KMetaDevelopmentBranchDefinition branch2 in Branches)
		{
			if (string.Equals(branch2.Id, branchId, StringComparison.Ordinal))
			{
				branch = branch2;
				return true;
			}
		}

		branch = null;
		return false;
	}

	private static List<WH40KMetaDevelopmentBranchDefinition> BuildBranches()
	{
		List<WH40KMetaDevelopmentBranchDefinition> list = new List<WH40KMetaDevelopmentBranchDefinition>();
		list.Add(BuildBranch(0, "brain", WH40KCharacterDevelopmentOrganType.Brain, "root", "surge", "tactics", "forecast", "synapse", "filter", "coldmind"));
		list.Add(BuildBranch(1, "lungs", WH40KCharacterDevelopmentOrganType.Lungs, "root", "reserve", "spore", "cascade", "drainage", "anoxia", "storm"));
		list.Add(BuildBranch(2, "kidneys", WH40KCharacterDevelopmentOrganType.Kidneys, "root", "filtration", "electrolytes", "purge", "reclaim", "buffer", "field"));
		list.Add(BuildBranch(3, "heart", WH40KCharacterDevelopmentOrganType.Heart, "root", "surge", "rhythm", "output", "reserve", "hemostasis", "ironblood"));
		list.Add(BuildBranch(4, "liver", WH40KCharacterDevelopmentOrganType.Liver, "root", "glycogen", "detox", "catalyst", "synthesis", "enzymes", "seal"));
		list.Add(BuildBranch(5, "stomach", WH40KCharacterDevelopmentOrganType.Stomach, "root", "metabolism", "ration", "impulse", "bile", "acid", "furnace"));
		return list;
	}

	private static Dictionary<string, WH40KMetaDevelopmentNodeDefinition> BuildNodes(IReadOnlyList<WH40KMetaDevelopmentBranchDefinition> branches)
	{
		Dictionary<string, WH40KMetaDevelopmentNodeDefinition> dictionary = new Dictionary<string, WH40KMetaDevelopmentNodeDefinition>();
		foreach (WH40KMetaDevelopmentBranchDefinition branch in branches)
		{
			AddNode(dictionary, branch, branch.RootNodeId, branch.UpperPathKeys[0], 1, null, isRoot: true, upperPath: true, 0, 0);
			for (int i = 0; i < branch.UpperPathNodeIds.Count; i++)
			{
				int num = i + 1;
				string parentId = ((num == 1) ? branch.RootNodeId : branch.UpperPathNodeIds[i - 1]);
				int cost = ((num != 3) ? 1 : 2);
				AddNode(dictionary, branch, branch.UpperPathNodeIds[i], branch.UpperPathKeys[i + 1], cost, parentId, isRoot: false, upperPath: true, num, num);
			}
			for (int j = 0; j < branch.LowerPathNodeIds.Count; j++)
			{
				int num2 = j + 1;
				string parentId2 = ((num2 == 1) ? branch.RootNodeId : branch.LowerPathNodeIds[j - 1]);
				int cost2 = ((num2 != 3) ? 1 : 2);
				AddNode(dictionary, branch, branch.LowerPathNodeIds[j], branch.LowerPathKeys[j + 1], cost2, parentId2, isRoot: false, upperPath: false, num2, 10 + num2);
			}
		}
		return dictionary;
	}

	private static WH40KMetaDevelopmentBranchDefinition BuildBranch(int sortOrder, string branchId, WH40KCharacterDevelopmentOrganType organ, params string[] nodeKeys)
	{
		if (nodeKeys.Length != 7)
		{
			throw new InvalidOperationException("Branch '" + branchId + "' must define exactly 7 node keys.");
		}
		string rootNodeId = branchId + "-" + nodeKeys[0];
		string[] upperPathNodeIds = new string[3]
		{
			branchId + "-u1",
			branchId + "-u2",
			branchId + "-u3"
		};
		string[] lowerPathNodeIds = new string[3]
		{
			branchId + "-d1",
			branchId + "-d2",
			branchId + "-d3"
		};
		string[] upperPathKeys = new string[4]
		{
			nodeKeys[0],
			nodeKeys[1],
			nodeKeys[2],
			nodeKeys[3]
		};
		string[] lowerPathKeys = new string[4]
		{
			nodeKeys[0],
			nodeKeys[4],
			nodeKeys[5],
			nodeKeys[6]
		};
		return new WH40KMetaDevelopmentBranchDefinition(branchId, organ, sortOrder, rootNodeId, upperPathNodeIds, lowerPathNodeIds, upperPathKeys, lowerPathKeys);
	}

	private static void AddNode(IDictionary<string, WH40KMetaDevelopmentNodeDefinition> nodes, WH40KMetaDevelopmentBranchDefinition branch, string nodeId, string nodeKey, int cost, string? parentId, bool isRoot, bool upperPath, int depth, int branchLocalSortOrder)
	{
		nodes[nodeId] = new WH40KMetaDevelopmentNodeDefinition(nodeId, branch.Id, branch.Organ, nodeKey, cost, parentId, isRoot, upperPath, depth, branch.SortOrder * 100 + branchLocalSortOrder);
	}
}
