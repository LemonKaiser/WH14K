using Content.Shared.NPC;

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
        var modifier = 1f;
        var isHazard = (end.Data.Flags & PathfindingBreadcrumbFlag.Hazard) != 0x0;
        var profile = request.Profile;
        var doorScale = GetDoorPenaltyScale(profile);
        var smashScale = GetSmashPenaltyScale(profile);
        var climbScale = GetClimbPenaltyScale(profile);
        var hazardScale = GetHazardPenaltyScale(profile);

        // TODO
        if ((end.Data.Flags & PathfindingBreadcrumbFlag.Space) != 0x0)
        {
            return 0f;
        }

        if ((request.CollisionLayer & end.Data.CollisionMask) != 0x0 ||
            (request.CollisionMask & end.Data.CollisionLayer) != 0x0)
        {
            var isDoor = (end.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0;
            var isAccess = (end.Data.Flags & PathfindingBreadcrumbFlag.Access) != 0x0;
            var isClimb = (end.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

            // TODO: Handling power + door prying
            // Door we should be able to open
            if (isDoor)
            {
                if (!isAccess && (request.Flags & PathFlags.Interact) != 0x0)
                    modifier += 0.5f * doorScale;
                else if (isAccess && (request.Flags & PathFlags.Prying) != 0x0)
                    modifier += 10f * doorScale;
                else
                    // Last ditch - try to bump the door if it's the only feasible option.
                    modifier += 20f * doorScale;
            }
            else if ((request.Flags & PathFlags.Smashing) != 0x0 && end.Data.Damage > 0f)
            {
                // Breaking stuff should be usually last resort, especially because we WILL try to punch walls.
                modifier += (10f + end.Data.Damage / 10f) * smashScale;
            }
            else if (isClimb && (request.Flags & PathFlags.Climbing) != 0x0)
            {
                modifier += 0.5f * climbScale;
            }
            else
            {
                return 0f;
            }
        }

        if (isHazard)
        {
            // Severe slow/damage contacts such as barbed wire should be avoided even when they
            // don't fully block the tile. Keep them traversable as a last resort, but far costlier
            // than a normal detour so squads stop choosing hazard strips as the "shortest" lane.
            modifier += (16f + MathF.Min(12f, end.Data.Damage / 6f)) * hazardScale;
        }

        return modifier * OctileDistance(end, start);
    }

    private static float GetDoorPenaltyScale(PathCostProfile profile)
    {
        return profile switch
        {
            PathCostProfile.Assault => 0.78f,
            PathCostProfile.Breach => 0.55f,
            PathCostProfile.Safe => 1.35f,
            _ => 1f,
        };
    }

    private static float GetSmashPenaltyScale(PathCostProfile profile)
    {
        return profile switch
        {
            PathCostProfile.Assault => 0.72f,
            PathCostProfile.Breach => 0.45f,
            PathCostProfile.Safe => 1.5f,
            _ => 1f,
        };
    }

    private static float GetClimbPenaltyScale(PathCostProfile profile)
    {
        return profile switch
        {
            PathCostProfile.Assault => 0.9f,
            PathCostProfile.Breach => 0.82f,
            PathCostProfile.Safe => 1.15f,
            _ => 1f,
        };
    }

    private static float GetHazardPenaltyScale(PathCostProfile profile)
    {
        return profile switch
        {
            PathCostProfile.Assault => 0.82f,
            PathCostProfile.Breach => 0.9f,
            PathCostProfile.Safe => 1.65f,
            _ => 1f,
        };
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

