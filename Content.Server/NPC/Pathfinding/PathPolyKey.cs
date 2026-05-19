using Robust.Shared.Maths;

namespace Content.Server.NPC.Pathfinding;

public readonly record struct PathPolyKey(EntityUid GraphUid, Vector2i ChunkOrigin, byte TileIndex)
{
    public static PathPolyKey FromPoly(PathPoly poly)
    {
        return new PathPolyKey(poly.GraphUid, poly.ChunkOrigin, poly.TileIndex);
    }
}
