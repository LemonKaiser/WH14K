using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    private PathResult UpdateAStarPath(AStarPathRequest request)
    {
        if (request.Start.Equals(request.End))
        {
            return PathResult.Path;
        }

        if (request.Task.IsCanceled || request.CancelToken.IsCancellationRequested)
        {
            return PathResult.NoPath;
        }

        request.ContinuationSlices++;

        PathPoly? currentNode = null;
        PathPoly endNode;

        // First run
        if (!request.Started)
        {
            request.Frontier = new PriorityQueue<(float, PathPoly)>(PathPolyComparer);
            request.Started = true;

            var startNode = GetPoly(request.Start);
            var foundEndNode = GetPoly(request.End) ?? GetNearestPathablePoly(request.End, request);

            if (startNode == null || foundEndNode == null)
            {
                return PathResult.NoPath;
            }

            endNode = foundEndNode;
            request.StartNode = startNode;
            request.EndNode = endNode;
            request.BestNode = startNode;
            request.BestNodeHeuristic = OctileDistance(endNode, startNode);
            request.Frontier.Add((0.0f, startNode));
            request.CostSoFar[startNode] = 0.0f;
        }
        // Re-validate nodes
        else
        {
            // Theoretically this shouldn't be happening, but practically...
            if (request.Frontier.Count == 0)
            {
                return PathResult.NoPath;
            }

            (_, currentNode) = request.Frontier.Peek();

            if (!currentNode.IsValid())
            {
                return PathResult.NoPath;
            }

            // Re-validate parents too.
            if (request.CameFrom.TryGetValue(currentNode, out var parentNode) && !parentNode.IsValid())
            {
                return PathResult.NoPath;
            }

            if (request.StartNode == null ||
                request.EndNode == null ||
                !request.StartNode.IsValid() ||
                !request.EndNode.IsValid())
            {
                return PathResult.NoPath;
            }

            endNode = request.EndNode;
        }

        DebugTools.Assert(!request.Task.IsCompleted);
        request.Stopwatch.Restart();

        var count = 0;
        var arrived = false;

        while (request.Frontier.Count > 0 &&
               count < NodeLimit &&
               request.ExpandedNodes < NodeSearchLimit)
        {
            // Handle whether we need to pause if we've taken too long
            if (count % 20 == 0 && count > 0 && request.Stopwatch.Elapsed > PathRequestTime)
            {
                if (request.CancelToken.IsCancellationRequested)
                    return PathResult.NoPath;

                // I had this happen once in testing but I don't think it should be possible?
                DebugTools.Assert(request.Frontier.Count > 0);
                return PathResult.Continuing;
            }

            count++;
            request.ExpandedNodes++;

            // Actual pathfinding here
            (_, currentNode) = request.Frontier.Take();

            if (!currentNode.IsValid())
            {
                return PathResult.NoPath;
            }

            TrackBestAStarNode(request, currentNode, endNode);

            // If we're inside the required distance OR we're at the end node.
            if ((request.Distance > 0f &&
                currentNode.Coordinates.TryDistance(EntityManager, request.End, out var distance) &&
                distance <= request.Distance) ||
                currentNode.Equals(endNode))
            {
                arrived = true;
                break;
            }

            foreach (var neighbor in currentNode.Neighbors)
            {
                if (!neighbor.IsValid())
                {
                    continue;
                }

                var tileCost = GetTileCost(request, currentNode, neighbor);

                if (tileCost.Equals(0f))
                {
                    continue;
                }

                // f = g + h
                // gScore is distance to the start node
                // hScore is distance to the end node
                var gScore = request.CostSoFar[currentNode] + tileCost;
                if (request.CostSoFar.TryGetValue(neighbor, out var nextValue) && gScore >= nextValue)
                {
                    continue;
                }

                request.CameFrom[neighbor] = currentNode;
                request.CostSoFar[neighbor] = gScore;
                // pFactor is tie-breaker where the fscore is otherwise equal.
                // See http://theory.stanford.edu/~amitp/GameProgramming/Heuristics.html#breaking-ties
                // There's other ways to do it but future consideration
                // The closer the fScore is to the actual distance then the better the pathfinder will be
                // (i.e. somewhere between 1 and infinite)
                // Can use hierarchical pathfinder or whatever to improve the heuristic but this is fine for now.
                var hScore = OctileDistance(endNode, neighbor) * (1.0f + 1.0f / 1000.0f);
                TrackBestAStarNode(request, neighbor, endNode);
                var fScore = gScore + hScore;
                request.Frontier.Add((fScore, neighbor));
            }
        }

        if (!arrived)
        {
            if (request.ExpandedNodes >= NodeSearchLimit ||
                request.ContinuationSlices >= PathContinuationSliceLimit)
                return TryReturnPartialAStarPath(request, currentNode);

            return request.Frontier.Count > 0
                ? PathResult.Continuing
                : TryReturnPartialAStarPath(request, currentNode);
        }

        if (currentNode == null)
        {
            return PathResult.NoPath;
        }

        var route = ReconstructPath(request.CameFrom, currentNode);
        var path = new Queue<EntityCoordinates>(route.Count);

        foreach (var node in route)
        {
            // Due to partial planning some nodes may have been invalidated.
            if (!node.IsValid())
            {
                return PathResult.NoPath;
            }

            path.Enqueue(node.Coordinates);
        }

        DebugTools.Assert(route.Count > 0);
        request.Polys = route;
        return PathResult.Path;
    }

    private void TrackBestAStarNode(AStarPathRequest request, PathPoly node, PathPoly endNode)
    {
        var heuristic = OctileDistance(endNode, node);
        if (heuristic >= request.BestNodeHeuristic)
            return;

        request.BestNodeHeuristic = heuristic;
        request.BestNode = node;
    }

    private PathResult TryReturnPartialAStarPath(AStarPathRequest request, PathPoly? fallbackNode)
    {
        var node = request.BestNode ?? fallbackNode;
        if (node == null ||
            request.StartNode == null ||
            !node.IsValid() ||
            !request.StartNode.IsValid() ||
            node.Equals(request.StartNode))
        {
            return PathResult.NoPath;
        }

        var route = ReconstructPath(new Dictionary<PathPoly, PathPoly>(request.CameFrom), node);
        if (route.Count <= 1)
            return PathResult.NoPath;

        foreach (var pathNode in route)
        {
            if (!pathNode.IsValid())
                return PathResult.NoPath;
        }

        request.Polys = route;
        return PathResult.PartialPath;
    }
}
