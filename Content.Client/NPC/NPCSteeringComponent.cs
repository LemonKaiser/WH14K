using System.Numerics;
using Content.Shared.NPC;
using Robust.Shared.Map;

namespace Content.Client.NPC;

[RegisterComponent]
public sealed partial class NPCSteeringComponent : Component
{
    /* Not hooked up to the server component as it's used for debugging only.
     */

    public Vector2 Direction;

    public float[] DangerMap = Array.Empty<float>();
    public float[] InterestMap = Array.Empty<float>();
    public List<Vector2> DangerPoints = new();
    public NetCoordinates Destination = NetCoordinates.Invalid;
    public float Radius = 0.35f;
    public List<NetCoordinates> CurrentPath = new();
    public List<DebugPathPoly> CurrentPathPolys = new();
}
