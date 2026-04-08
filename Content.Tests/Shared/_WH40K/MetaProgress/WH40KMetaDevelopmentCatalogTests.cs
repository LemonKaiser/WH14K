using System.Collections.Generic;
using System.Linq;
using Content.Shared._WH40K.MetaProgress;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.MetaProgress;

[TestFixture]
public sealed class WH40KMetaDevelopmentCatalogTests
{
    private static readonly string[] ExpectedBranchIds =
    [
        "brain", "lungs", "kidneys", "heart", "liver", "stomach"
    ];

    [Test]
    public void CatalogContainsExactlySixBranches()
    {
        Assert.That(WH40KMetaDevelopmentCatalog.Branches, Has.Count.EqualTo(6));
    }

    [Test]
    public void AllExpectedBranchIdsExist()
    {
        var branchIds = WH40KMetaDevelopmentCatalog.Branches.Select(b => b.Id).ToList();
        foreach (var expectedId in ExpectedBranchIds)
        {
            Assert.That(branchIds, Does.Contain(expectedId), $"Missing branch: {expectedId}");
        }
    }

    [Test]
    public void EachBranchHasExactlySevenNodes()
    {
        foreach (var branch in WH40KMetaDevelopmentCatalog.Branches)
        {
            var nodeCount = WH40KMetaDevelopmentCatalog.Nodes.Values
                .Count(n => n.BranchId == branch.Id);
            Assert.That(nodeCount, Is.EqualTo(7), $"Branch {branch.Id} has {nodeCount} nodes instead of 7.");
        }
    }

    [Test]
    public void TotalNodeCountIs42()
    {
        Assert.That(WH40KMetaDevelopmentCatalog.Nodes, Has.Count.EqualTo(42));
    }

    [Test]
    public void EachBranchHasExactlyOneRoot()
    {
        foreach (var branch in WH40KMetaDevelopmentCatalog.Branches)
        {
            var roots = WH40KMetaDevelopmentCatalog.Nodes.Values
                .Where(n => n.BranchId == branch.Id && n.IsRoot)
                .ToList();
            Assert.That(roots, Has.Count.EqualTo(1), $"Branch {branch.Id} must have exactly 1 root.");
        }
    }

    [Test]
    public void RootNodesHaveNullParent()
    {
        foreach (var node in WH40KMetaDevelopmentCatalog.Nodes.Values.Where(n => n.IsRoot))
        {
            Assert.That(node.ParentId, Is.Null, $"Root node {node.Id} should have null parent.");
        }
    }

    [Test]
    public void NonRootNodesHaveValidParent()
    {
        foreach (var node in WH40KMetaDevelopmentCatalog.Nodes.Values.Where(n => !n.IsRoot))
        {
            Assert.That(node.ParentId, Is.Not.Null.And.Not.Empty, $"Non-root node {node.Id} must have a parent.");
            Assert.That(
                WH40KMetaDevelopmentCatalog.Nodes.ContainsKey(node.ParentId!),
                Is.True,
                $"Node {node.Id} references unknown parent {node.ParentId}.");
        }
    }

    [Test]
    public void PrerequisiteChainsDoNotFormCycles()
    {
        foreach (var node in WH40KMetaDevelopmentCatalog.Nodes.Values)
        {
            var visited = new HashSet<string>();
            var current = node;
            while (current.ParentId != null)
            {
                Assert.That(visited.Add(current.Id), Is.True,
                    $"Cycle detected starting from node {node.Id}.");
                WH40KMetaDevelopmentCatalog.TryGetNode(current.ParentId, out current!);
            }
        }
    }

    [Test]
    public void ThirdDepthNodesHaveCostTwo()
    {
        foreach (var node in WH40KMetaDevelopmentCatalog.Nodes.Values.Where(n => n.Depth == 3))
        {
            Assert.That(node.Cost, Is.EqualTo(2), $"Depth-3 node {node.Id} should cost 2.");
        }
    }

    [Test]
    public void NonThirdDepthNodesHaveCostOne()
    {
        foreach (var node in WH40KMetaDevelopmentCatalog.Nodes.Values.Where(n => n.Depth > 0 && n.Depth < 3))
        {
            Assert.That(node.Cost, Is.EqualTo(1), $"Depth-{node.Depth} node {node.Id} should cost 1.");
        }
    }

    [Test]
    public void RootNodesHaveCostOne()
    {
        foreach (var node in WH40KMetaDevelopmentCatalog.Nodes.Values.Where(n => n.IsRoot))
        {
            Assert.That(node.Cost, Is.EqualTo(1), $"Root node {node.Id} should cost 1.");
        }
    }

    [Test]
    public void TotalCostForAllNodesIsConsistent()
    {
        // Each branch: root(1) + u1(1) + u2(1) + u3(2) + d1(1) + d2(1) + d3(2) = 9
        // 6 branches * 9 = 54
        var totalCost = WH40KMetaDevelopmentCatalog.Nodes.Values.Sum(n => n.Cost);
        Assert.That(totalCost, Is.EqualTo(54));
    }

    [Test]
    public void EachBranchHasThreeUpperAndThreeLowerPathNodes()
    {
        foreach (var branch in WH40KMetaDevelopmentCatalog.Branches)
        {
            var branchNodes = WH40KMetaDevelopmentCatalog.Nodes.Values
                .Where(n => n.BranchId == branch.Id && !n.IsRoot)
                .ToList();

            var upperCount = branchNodes.Count(n => n.UpperPath);
            var lowerCount = branchNodes.Count(n => !n.UpperPath);

            Assert.Multiple(() =>
            {
                Assert.That(upperCount, Is.EqualTo(3), $"Branch {branch.Id} upper path count.");
                Assert.That(lowerCount, Is.EqualTo(3), $"Branch {branch.Id} lower path count.");
            });
        }
    }

    [Test]
    public void TryGetNodeFindsKnownNodes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KMetaDevelopmentCatalog.TryGetNode("brain-root", out var node), Is.True);
            Assert.That(node!.BranchId, Is.EqualTo("brain"));
            Assert.That(node.IsRoot, Is.True);
        });
    }

    [Test]
    public void TryGetNodeRejectsInvalidIds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WH40KMetaDevelopmentCatalog.TryGetNode("nonexistent", out _), Is.False);
            Assert.That(WH40KMetaDevelopmentCatalog.TryGetNode("", out _), Is.False);
            Assert.That(WH40KMetaDevelopmentCatalog.TryGetNode(null!, out _), Is.False);
            Assert.That(WH40KMetaDevelopmentCatalog.TryGetNode("  ", out _), Is.False);
        });
    }

    [Test]
    public void TryGetBranchFindsKnownBranches()
    {
        foreach (var expectedId in ExpectedBranchIds)
        {
            Assert.That(WH40KMetaDevelopmentCatalog.TryGetBranch(expectedId, out var branch), Is.True,
                $"Branch {expectedId} not found.");
            Assert.That(branch!.Id, Is.EqualTo(expectedId));
        }
    }

    [Test]
    public void TryGetBranchRejectsInvalidIds()
    {
        Assert.That(WH40KMetaDevelopmentCatalog.TryGetBranch("nonexistent", out _), Is.False);
    }

    [Test]
    public void NodesInValidationOrderIsSorted()
    {
        var nodes = WH40KMetaDevelopmentCatalog.NodesInValidationOrder;
        for (var i = 1; i < nodes.Count; i++)
        {
            var cmp = nodes[i - 1].SortOrder.CompareTo(nodes[i].SortOrder);
            if (cmp == 0)
                cmp = string.CompareOrdinal(nodes[i - 1].Id, nodes[i].Id);

            Assert.That(cmp, Is.LessThanOrEqualTo(0),
                $"NodesInValidationOrder not sorted at index {i}: {nodes[i - 1].Id} vs {nodes[i].Id}.");
        }
    }

    [Test]
    public void NodesInValidationOrderContainsAllNodes()
    {
        Assert.That(WH40KMetaDevelopmentCatalog.NodesInValidationOrder, Has.Count.EqualTo(42));
    }

    [Test]
    public void AllNodeIdsAreUnique()
    {
        var ids = WH40KMetaDevelopmentCatalog.Nodes.Values.Select(n => n.Id).ToList();
        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void BranchSortOrdersAreUnique()
    {
        var sortOrders = WH40KMetaDevelopmentCatalog.Branches.Select(b => b.SortOrder).ToList();
        Assert.That(sortOrders, Is.Unique);
    }

    [Test]
    public void EachBranchMapsToDistinctOrganType()
    {
        var organs = WH40KMetaDevelopmentCatalog.Branches.Select(b => b.Organ).ToList();
        Assert.That(organs, Is.Unique);
    }
}
