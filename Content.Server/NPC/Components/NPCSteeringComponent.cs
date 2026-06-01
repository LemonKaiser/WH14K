using System.Numerics;
using System.Threading;
using Content.Server.NPC.Pathfinding;
using Content.Shared.DoAfter;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.NPC.Components;

/// <summary>
/// Added to NPCs that are moving.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class NPCSteeringComponent : Component
{
    #region Context Steering

    /// <summary>
    /// Used to override seeking behavior for context steering.
    /// </summary>
    [ViewVariables]
    public bool CanSeek = true;

    /// <summary>
    /// Radius for collision avoidance.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("radius")]
    public float Radius = 0.35f;

    [ViewVariables, DataField]
    public float[] Interest = new float[SharedNPCSteeringSystem.InterestDirections];

    [ViewVariables, DataField]
    public float[] Danger = new float[SharedNPCSteeringSystem.InterestDirections];

    // TODO: Update radius, also danger points debug only
    public readonly List<Vector2> DangerPoints = new();

    #endregion

    /// <summary>
    /// Set to true from other systems if you wish to force the NPC to move closer.
    /// </summary>
    [DataField("forceMove")]
    public bool ForceMove = false;

    [DataField("lastSteerDirection")]
    public Vector2 LastSteerDirection = Vector2.Zero;

    /// <summary>
    /// Last position we considered for being stuck.
    /// </summary>
    [DataField("lastStuckCoordinates")]
    public EntityCoordinates LastStuckCoordinates;

    [DataField("lastStuckTime", customTypeSerializer:typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan LastStuckTime;

    public const float StuckDistance = 1f;

    /// <summary>
    /// Have we currently requested a path.
    /// </summary>
    [ViewVariables]
    public bool Pathfind => PathfindToken != null;

    /// <summary>
    /// Are we considered arrived if we have line of sight of the target.
    /// </summary>
    [DataField("arriveOnLineOfSight")]
    public bool ArriveOnLineOfSight = false;

    /// <summary>
    /// How long the target has been in line of sight if applicable.
    /// </summary>
    [DataField("lineOfSightTimer")]
    public float LineOfSightTimer = 0f;

    [DataField("lineOfSightTimeRequired")]
    public float LineOfSightTimeRequired = 0.5f;

    [ViewVariables] public CancellationTokenSource? PathfindToken = null;

    /// <summary>
    /// Current path we're following to our coordinates.
    /// </summary>
    [ViewVariables] public Queue<PathPoly> CurrentPath = new();

    /// <summary>
    /// End target that we're trying to move to.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)] public EntityCoordinates Coordinates;

    /// <summary>
    /// How close are we trying to get to the coordinates before being considered in range.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("range")]
    public float Range = 0.2f;

    /// <summary>
    /// How far does the last node in the path need to be before considering re-pathfinding.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("repathRange")]
    public float RepathRange = 1.5f;

    public const int DefaultFailedPathLimit = 3;

    /// <summary>
    /// How many times we've failed to pathfind. Once this hits the limit we'll stop steering.
    /// </summary>
    [ViewVariables] public int FailedPathCount;

    /// <summary>
    /// How many failed path requests are allowed before steering gives up.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("failedPathLimit")]
    public int FailedPathLimit = DefaultFailedPathLimit;

    [ViewVariables] public SteeringStatus Status = SteeringStatus.Moving;

    [ViewVariables(VVAccess.ReadWrite)] public PathFlags Flags = PathFlags.None;

    /// <summary>
    /// If enabled, newly produced paths try to skip intermediate polygons when a swept ray stays unobstructed.
    /// This keeps the existing SS14 nav graph while reducing stutter and center-to-center polygon walking.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("enablePathShortcutting")]
    public bool EnablePathShortcutting = false;

    /// <summary>
    /// Adds a small per-NPC lateral offset while following path nodes, so groups do not stack into a single line.
    /// The offset is only used when the swept agent radius stays unobstructed.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("enablePathOffsets")]
    public bool EnablePathOffsets = true;

    [ViewVariables(VVAccess.ReadWrite), DataField("pathOffsetMin")]
    public float PathOffsetMin = 0.06f;

    [ViewVariables(VVAccess.ReadWrite), DataField("pathOffsetMax")]
    public float PathOffsetMax = 0.24f;

    [ViewVariables(VVAccess.ReadWrite), DataField("pathOffsetSafetyPadding")]
    public float PathOffsetSafetyPadding = 0.04f;

    /// <summary>
    /// Maximum amount of queued path polygons to probe for a direct shortcut.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("pathShortcutLookahead")]
    public int PathShortcutLookahead = 8;

    /// <summary>
    /// How often an NPC should check for a cheaper path while actively clearing an obstacle.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("obstacleRepathInterval")]
    public float ObstacleRepathInterval = 1.25f;

    [DataField("lastObstacleRepathTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan LastObstacleRepathTime;

    [ViewVariables(VVAccess.ReadWrite), DataField("fallbackNoProgressDistance")]
    public float FallbackNoProgressDistance = 3f;

    [ViewVariables(VVAccess.ReadWrite), DataField("fallbackNoProgressTime")]
    public float FallbackNoProgressTime = 20f;

    [ViewVariables(VVAccess.ReadWrite), DataField("fallbackRepathCooldown")]
    public float FallbackRepathCooldown = 3f;

    [ViewVariables(VVAccess.ReadWrite), DataField("livePathCheckInterval")]
    public float LivePathCheckInterval = 0.25f;

    [DataField("lastLivePathCheckTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan LastLivePathCheckTime;

    [ViewVariables] public float LastRouteProgressDistance = float.PositiveInfinity;

    [DataField("lastRouteProgressTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan LastRouteProgressTime;

    [DataField("lastFallbackRepathTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan LastFallbackRepathTime;

    [ViewVariables] public PathPoly? AvoidedPathPoly;

    [ViewVariables] public int PendingPathDirectMoveTicks;

    [ViewVariables(VVAccess.ReadWrite), DataField("pendingPathDirectMoveProbe")]
    public float PendingPathDirectMoveProbe = 2.5f;

    /// <summary>
    /// If the NPC is using a do_after to clear an obstacle.
    /// </summary>
    [DataField("doAfterId")]
    public DoAfterId? DoAfterId = null;

    /// <summary>
    /// Keeps this component on the entity after steering is stopped, preserving YAML tuning for future moves.
    /// </summary>
    [DataField("preserveOnUnregister")]
    public bool PreserveOnUnregister = false;
}

public enum SteeringStatus : byte
{
    /// <summary>
    /// If we can't reach the target (e.g. different map).
    /// </summary>
    NoPath,

    /// <summary>
    /// Are we moving towards our target
    /// </summary>
    Moving,

    /// <summary>
    /// Are we currently in range of our target.
    /// </summary>
    InRange,
}
