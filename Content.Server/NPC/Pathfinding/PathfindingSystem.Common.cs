using Content.Shared.NPC;
using Robust.Shared.Physics;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /*
     * Code that is common to all pathfinding methods.
     */

    /// <summary>
    /// Maximum amount of nodes we're allowed to expand.
    /// </summary>
    private const int NodeLimit = 512;

    private sealed class PathComparer : IComparer<ValueTuple<float, PathPoly>>
    {
        public int Compare((float, PathPoly) x, (float, PathPoly) y)
        {
            return y.Item1.CompareTo(x.Item1);
        }
    }

    private static readonly PathComparer PathPolyComparer = new();

    private List<PathPoly> ReconstructPath(Dictionary<PathPoly, PathPoly> path, PathPoly currentNodeRef)
    {
        var running = new List<PathPoly> { currentNodeRef };
        while (path.ContainsKey(currentNodeRef))
        {
            var previousCurrent = currentNodeRef;
            currentNodeRef = path[currentNodeRef];
            path.Remove(previousCurrent);
            running.Add(currentNodeRef);
        }

        running.Reverse();
        return running;
    }

    private float GetTileCost(PathRequest request, PathPoly start, PathPoly end)
    {
        return GetTileCost(request.Flags, request.CollisionLayer, request.CollisionMask, start, end);
    }

    private float GetTileCost(
        PathFlags flags,
        int collisionLayer,
        int collisionMask,
        PathPoly start,
        PathPoly end)
    {
        var modifier = 1f;

        // TODO
        if ((end.Data.Flags & PathfindingBreadcrumbFlag.Space) != 0x0)
        {
            return 0f;
        }

        if ((collisionLayer & end.Data.CollisionMask) != 0x0 ||
            (collisionMask & end.Data.CollisionLayer) != 0x0)
        {
            var isDoor = (end.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0;
            var isAccess = (end.Data.Flags & PathfindingBreadcrumbFlag.Access) != 0x0;
            var isClimb = (end.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

            // TODO: Handling power + door prying
            // Door we should be able to open
            if (isDoor)
            {
                if (!isAccess && (flags & PathFlags.Interact) != 0x0)
                    modifier += 0.5f;
                else if (isAccess && (flags & PathFlags.Prying) != 0x0)
                    modifier += 10f;
                else if ((flags & PathFlags.Smashing) != 0x0 && end.Data.Damage > 0f)
                    modifier += 12f + end.Data.Damage / 10f;
                else
                    modifier += 20f;
            }
            else if ((flags & PathFlags.Smashing) != 0x0 && end.Data.Damage > 0f)
            {
                modifier += 10f + end.Data.Damage / 10f;
            }
            else if (isClimb && (flags & PathFlags.Climbing) != 0x0)
            {
                modifier += 0.5f;
            }
            else
            {
                return 0f;
            }
        }

        return modifier * OctileDistance(end, start);
    }

    public float EstimatePathCost(EntityUid entity, IReadOnlyList<PathPoly> path, PathFlags flags)
    {
        if (path.Count <= 1)
            return 0f;

        var layer = 0;
        var mask = 0;
        if (TryComp<FixturesComponent>(entity, out var fixtures))
            (layer, mask) = _physics.GetHardCollision(entity, fixtures);

        var total = 0f;
        for (var i = 1; i < path.Count; i++)
        {
            total += GetTileCost(flags, layer, mask, path[i - 1], path[i]);
        }

        return total;
    }

    public List<float> BuildCumulativePathCosts(EntityUid entity, IReadOnlyList<PathPoly> path, PathFlags flags)
    {
        var costs = new List<float>(path.Count);
        if (path.Count == 0)
            return costs;

        var layer = 0;
        var mask = 0;
        if (TryComp<FixturesComponent>(entity, out var fixtures))
            (layer, mask) = _physics.GetHardCollision(entity, fixtures);

        var total = 0f;
        costs.Add(0f);

        for (var i = 1; i < path.Count; i++)
        {
            total += GetTileCost(flags, layer, mask, path[i - 1], path[i]);
            costs.Add(total);
        }

        return costs;
    }

    #region Simplifier

    public List<PathPoly> Simplify(List<PathPoly> vertices, float tolerance = 0)
    {
        // TODO: Needs more work
        if (vertices.Count <= 3)
            return vertices;

        var simplified = new List<PathPoly>();

        for (var i = 0; i < vertices.Count; i++)
        {
            // No wraparound for negative sooooo
            var prev = vertices[i == 0 ? vertices.Count - 1 : i - 1];
            var current = vertices[i];
            var next = vertices[(i + 1) % vertices.Count];

            var prevData = prev.Data;
            var currentData = current.Data;
            var nextData = next.Data;

            // If they collinear, continue
            if (i != 0 && i != vertices.Count - 1 &&
                prevData.Equals(currentData) &&
                currentData.Equals(nextData) &&
                IsCollinear(prev, current, next, tolerance))
            {
                continue;
            }

            simplified.Add(current);
        }

        // Farseer didn't seem to handle straight lines and nuked all points
        if (simplified.Count == 0)
        {
            simplified.Add(vertices[0]);
            simplified.Add(vertices[^1]);
        }

        // Check LOS and cut out more nodes
        // TODO: Grid cast
        // https://github.com/recastnavigation/recastnavigation/blob/c5cbd53024c8a9d8d097a4371215e3342d2fdc87/Detour/Source/DetourNavMeshQuery.cpp#L2455
        // Essentially you just do a raycast but a specialised version.

        return simplified;
    }

    private bool IsCollinear(PathPoly prev, PathPoly current, PathPoly next, float tolerance)
    {
        return FloatInRange(Area(prev, current, next), -tolerance, tolerance);
    }

    private float Area(PathPoly a, PathPoly b, PathPoly c)
    {
        var (ax, ay) = a.Box.Center;
        var (bx, by) = b.Box.Center;
        var (cx, cy) = c.Box.Center;

        return ax * (by - cy) + bx * (cy - ay) + cx * (ay - by);
    }

    private bool FloatInRange(float value, float min, float max)
    {
        return (value >= min && value <= max);
    }

    #endregion
}
