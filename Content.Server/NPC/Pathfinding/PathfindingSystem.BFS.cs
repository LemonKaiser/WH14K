using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    private PathResult UpdateBFSPath(IRobustRandom random, BFSPathRequest request)
    {
        if (request.Task.IsCanceled || request.CancelToken.IsCancellationRequested)
        {
            return PathResult.NoPath;
        }

        PathPoly? currentNode = null;

        // First run
        if (!request.Started)
        {
            request.Frontier = new PriorityQueue<(float, PathPoly)>(PathPolyComparer);
            request.Started = true;

            var startNode = GetPoly(request.Start);

            if (startNode == null)
            {
                return PathResult.NoPath;
            }

            request.StartNode = startNode;
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

            if (request.StartNode == null || !request.StartNode.IsValid())
            {
                return PathResult.NoPath;
            }
        }

        DebugTools.Assert(!request.Task.IsCompleted);
        request.Stopwatch.Restart();

        var count = 0;

        while (request.Frontier.Count > 0 &&
               count < NodeLimit &&
               request.ExpandedNodes < request.ExpansionLimit)
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
                request.Frontier.Add((gScore, neighbor));
            }
        }

        if (request.Frontier.Count > 0 && request.ExpandedNodes < request.ExpansionLimit)
        {
            return PathResult.Continuing;
        }

        if (request.CostSoFar.Count == 0)
        {
            return PathResult.NoPath;
        }

        // Pick a random node to use?
        (currentNode, _) = random.Pick(request.CostSoFar);

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
}
