using Content.Shared.Gravity;
using Content.Shared.Maps;
using Content.Shared.NPC;
using Robust.Shared.Map.Components;
using Robust.Shared.Spawners;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /*
     * Code that is common to all pathfinding methods.
     */

    /// <summary>
    /// Maximum amount of nodes we're allowed to expand per update slice.
    /// </summary>
    private const int NodeLimit = 512;

    /// <summary>
    /// Hard cap for one search, to avoid spending forever on disconnected graphs.
    /// </summary>
    private const int NodeSearchLimit = 65536;

    /// <summary>
    /// Maximum number of time-sliced continuations before a far A* request returns the best partial route it has.
    /// This prevents wave mobs from standing still for seconds while a huge/disconnected graph is still being searched.
    /// </summary>
    private const int PathContinuationSliceLimit = 48;

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
        if (!TryGetTileCostModifier(request, end, out var modifier))
        {
            return 0f;
        }

        return modifier * OctileDistance(end, start);
    }

    private bool CanPathThrough(PathRequest request, PathPoly poly)
    {
        if (request.BlockedPolys.Contains(poly))
            return false;

        return TryGetTileCostModifier(request, poly, out _);
    }

    private bool TryGetTileCostModifier(PathRequest request, PathPoly poly, out float modifier)
    {
        modifier = 1f;

        if (request.BlockedPolys.Contains(poly))
        {
            return false;
        }

        // TODO
        if ((poly.Data.Flags & PathfindingBreadcrumbFlag.Space) != 0x0)
        {
            return false;
        }

        if ((request.CollisionLayer & poly.Data.CollisionMask) != 0x0 ||
            (request.CollisionMask & poly.Data.CollisionLayer) != 0x0)
        {
            var isDoor = (poly.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0;
            var isAccess = (poly.Data.Flags & PathfindingBreadcrumbFlag.Access) != 0x0;
            var isClimb = (poly.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

            // TODO: Handling power + door prying
            // Door we should be able to open
            if (isDoor && !isAccess && (request.Flags & PathFlags.Interact) != 0x0)
            {
                modifier += 0.5f;
            }
            // Door we can force open one way or another
            else if (isDoor && isAccess && (request.Flags & PathFlags.Prying) != 0x0)
            {
                modifier += 18f;
            }
            else if (isClimb && (request.Flags & PathFlags.Climbing) != 0x0)
            {
                modifier += 3f;
            }
            else if ((request.Flags & PathFlags.Smashing) != 0x0 && poly.Data.Damage > 0f)
            {
                modifier += 35f + poly.Data.Damage / 20f;
            }
            else
            {
                return false;
            }
        }

        return true;
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
