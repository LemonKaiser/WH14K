using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.DoAfter;
using Content.Server.Doors.Systems;
using Content.Server.Hands.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Events;
using Content.Server.NPC.Pathfinding;
using Content.Shared._WH40K.Combat;
using Content.Shared.CCVar;
using Content.Shared.Climbing.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.NPC.Events;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Prying.Components;
using Content.Shared.Turrets;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Shared.Prying.Systems;
using Microsoft.Extensions.ObjectPool;
using Prometheus;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem : SharedNPCSteeringSystem
{
    private static readonly Gauge ActiveSteeringGauge = Metrics.CreateGauge(
        "npc_steering_active_count",
        "Amount of NPCs trying to actively do steering");

    /*
     * We use context steering to determine which way to move.
     * This involves creating an array of possible directions and assigning a value for the desireability of each direction.
     *
     * There's multiple ways to implement this, e.g. you can average all directions, or you can choose the highest direction
     * , or you can remove the danger map entirely and only having an interest map (AKA game endeavour).
     * See http://www.gameaipro.com/GameAIPro2/GameAIPro2_Chapter18_Context_Steering_Behavior-Driven_Steering_at_the_Macro_Scale.pdf
     * (though in their case it was for an F1 game so used context steering across the width of the road).
     */

    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IConfigurationManager _configManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ClimbSystem _climb = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private DoorSystem _doorSystem = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private PathfindingSystem _pathfindingSystem = default!;
    [Dependency] private PryingSystem _pryingSystem = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private RayCastSystem _rayCast = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<DeployableTurretComponent> _deployableTurretQuery = default!;
    [Dependency] private EntityQuery<NPCGroupComponent> _groupQuery = default!;
    [Dependency] private EntityQuery<HandsComponent> _handsQuery = default!;
    [Dependency] private EntityQuery<MovementSpeedModifierComponent> _modifierQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;
    [Dependency] private EntityQuery<PryingComponent> _pryingQuery = default!;
    [Dependency] private EntityQuery<WH40KTurretProfileComponent> _turretProfileQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    private ObjectPool<HashSet<EntityUid>> _entSetPool =
        new DefaultObjectPool<HashSet<EntityUid>>(new SetPolicy<EntityUid>());

    /// <summary>
    /// Enabled antistuck detection so if an NPC is in the same spot for a while it will re-path.
    /// </summary>
    public bool AntiStuck = true;

    private bool _enabled;

    private bool _pathfinding = true;

    public static readonly Vector2[] Directions = new Vector2[InterestDirections];

    private readonly HashSet<ICommonSession> _subscribedSessions = new();

    private object _obstacles = new();

    private int _activeSteeringCount;

    public override void Initialize()
    {
        base.Initialize();

        Log.Level = LogLevel.Info;

        for (var i = 0; i < InterestDirections; i++)
        {
            Directions[i] = new Angle(InterestRadians * i).ToVec();
        }

        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        Subs.CVar(_configManager, CCVars.NPCEnabled, SetNPCEnabled, true);
        Subs.CVar(_configManager, CCVars.NPCPathfinding, SetNPCPathfinding, true);

        SubscribeLocalEvent<NPCSteeringComponent, ComponentShutdown>(OnSteeringShutdown);
        SubscribeNetworkEvent<RequestNPCSteeringDebugEvent>(OnDebugRequest);
    }

    private void SetNPCEnabled(bool obj)
    {
        if (!obj)
        {
            foreach (var (comp, mover) in EntityQuery<NPCSteeringComponent, InputMoverComponent>())
            {
                mover.CurTickSprintMovement = Vector2.Zero;
                comp.PathfindToken?.Cancel();
                comp.PathfindToken = null;
            }
        }

        _enabled = obj;
    }

    private void SetNPCPathfinding(bool value)
    {
        _pathfinding = value;

        if (!_pathfinding)
        {
            foreach (var comp in EntityQuery<NPCSteeringComponent>(true))
            {
                comp.PathfindToken?.Cancel();
                comp.PathfindToken = null;
            }
        }
    }

    private void OnDebugRequest(RequestNPCSteeringDebugEvent msg, EntitySessionEventArgs args)
    {
        if (!_admin.IsAdmin(args.SenderSession))
            return;

        if (msg.Enabled)
            _subscribedSessions.Add(args.SenderSession);
        else
            _subscribedSessions.Remove(args.SenderSession);
    }

    private void OnSteeringShutdown(EntityUid uid, NPCSteeringComponent component, ComponentShutdown args)
    {
        // Cancel any active pathfinding jobs as they're irrelevant.
        component.PathfindToken?.Cancel();
        component.PathfindToken = null;
        ReleaseObstacleClaims(uid);
        ReleaseGroupObstacleAction(uid);
    }

    /// <summary>
    /// Adds the AI to the steering system to move towards a specific target
    /// </summary>
    public NPCSteeringComponent Register(EntityUid uid, EntityCoordinates coordinates, NPCSteeringComponent? component = null)
    {
        if (Resolve(uid, ref component, false))
        {
            if (component.Coordinates.Equals(coordinates))
            {
                component.Flags = _pathfindingSystem.GetFlags(uid);
                return component;
            }

            component.PathfindToken?.Cancel();
            component.PathfindToken = null;
            component.CurrentPath.Clear();
        }
        else
        {
            component = AddComp<NPCSteeringComponent>(uid);
        }

        component.Flags = _pathfindingSystem.GetFlags(uid);
        ResetStuck(component, Transform(uid).Coordinates);
        component.Status = SteeringStatus.Moving;
        component.FailedPathCount = 0;
        component.DoAfterId = null;
        component.LastObstacleRepathTime = TimeSpan.Zero;
        component.LastLivePathCheckTime = TimeSpan.Zero;
        component.LastRouteProgressTime = TimeSpan.Zero;
        component.LastFallbackRepathTime = TimeSpan.Zero;
        component.LastRouteProgressDistance = float.PositiveInfinity;
        component.LastStallReason = string.Empty;
        component.LastStallLogTime = TimeSpan.Zero;
        component.PendingPathDirectMoveTicks = 0;
        component.AvoidedPathPoly = null;
        component.LineOfSightTimer = 0f;
        component.Coordinates = coordinates;
        return component;
    }

    /// <summary>
    /// Attempts to register the entity. Does nothing if the coordinates already registered.
    /// </summary>
    public bool TryRegister(EntityUid uid, EntityCoordinates coordinates, NPCSteeringComponent? component = null)
    {
        if (Resolve(uid, ref component, false) && component.Coordinates.Equals(coordinates))
        {
            return false;
        }

        Register(uid, coordinates, component);
        return true;
    }

    /// <summary>
    /// Stops the steering behavior for the AI and cleans up.
    /// </summary>
    public void Unregister(EntityUid uid, NPCSteeringComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (TryComp(uid, out InputMoverComponent? controller))
        {
            controller.CurTickSprintMovement = Vector2.Zero;

            var ev = new SpriteMoveEvent(false);
            RaiseLocalEvent(uid, ref ev);
        }

        component.PathfindToken?.Cancel();
        component.PathfindToken = null;
        ReleaseObstacleClaims(uid);
        ReleaseGroupObstacleAction(uid);
        component.AvoidedPathPoly = null;
        component.CurrentPath.Clear();
        Array.Clear(component.Interest);
        Array.Clear(component.Danger);

        if (component.PreserveOnUnregister)
        {
            component.Status = SteeringStatus.NoPath;
            component.Coordinates = EntityCoordinates.Invalid;
            return;
        }

        RemComp<NPCSteeringComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        // Not every mob has the modifier component so do it as a separate query.
        var npcs = new (EntityUid, NPCSteeringComponent, InputMoverComponent, TransformComponent)[Count<ActiveNPCComponent>()];

        var query = EntityQueryEnumerator<ActiveNPCComponent, NPCSteeringComponent, InputMoverComponent, TransformComponent>();
        var index = 0;

        while (query.MoveNext(out var uid, out _, out var steering, out var mover, out var xform))
        {
            npcs[index] = (uid, steering, mover, xform);
            index++;
        }

        // Dependency issues across threads.
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 1,
        };
        var curTime = _timing.CurTime;

        _activeSteeringCount = 0;

        Parallel.For(0, index, options, i =>
        {
            var (uid, steering, mover, xform) = npcs[i];
            Steer(uid, steering, mover, xform, frameTime, curTime);
        });

        ActiveSteeringGauge.Set(_activeSteeringCount);

        if (_subscribedSessions.Count > 0)
        {
            var data = new List<NPCSteeringDebugData>(index);

            for (var i = 0; i < index; i++)
            {
                var (uid, steering, mover, _) = npcs[i];

                var currentPath = steering.CurrentPath.ToArray();
                data.Add(new NPCSteeringDebugData(
                    GetNetEntity(uid),
                    mover.CurTickSprintMovement,
                    steering.Interest,
                    steering.Danger,
                    steering.DangerPoints,
                    GetNetCoordinates(steering.Coordinates),
                    steering.Radius,
                    currentPath.Select(poly => GetNetCoordinates(poly.Coordinates)).ToList(),
                    currentPath.Select(GetDebugPoly).ToList()));
            }

            var filter = Filter.Empty();
            filter.AddPlayers(_subscribedSessions);

            RaiseNetworkEvent(new NPCSteeringDebugEvent(data), filter);
        }
    }

    private DebugPathPoly GetDebugPoly(PathPoly poly)
    {
        var neighbors = new List<NetCoordinates>(poly.Neighbors.Count);

        foreach (var neighbor in poly.Neighbors)
        {
            neighbors.Add(GetNetCoordinates(neighbor.Coordinates));
        }

        return new DebugPathPoly()
        {
            GraphUid = GetNetEntity(poly.GraphUid),
            ChunkOrigin = poly.ChunkOrigin,
            TileIndex = poly.TileIndex,
            Box = poly.Box,
            Data = poly.Data,
            Neighbors = neighbors,
        };
    }

    private void SetDirection(EntityUid uid, InputMoverComponent component, NPCSteeringComponent steering, Vector2 value, bool clear = true)
    {
        if (clear && value.Equals(Vector2.Zero))
        {
            steering.CurrentPath.Clear();
            Array.Clear(steering.Interest);
            Array.Clear(steering.Danger);
        }

        component.CurTickSprintMovement = value;
        component.LastInputTick = _timing.CurTick;
        component.LastInputSubTick = ushort.MaxValue;

        var ev = new SpriteMoveEvent(true);
        RaiseLocalEvent(uid, ref ev);
    }

    private void LogSteeringStall(
        EntityUid uid,
        NPCSteeringComponent steering,
        TransformComponent xform,
        string reason,
        string details)
    {
        var now = _timing.CurTime;
        if (steering.LastStallReason == reason &&
            steering.LastStallLogTime != TimeSpan.Zero &&
            (now - steering.LastStallLogTime).TotalSeconds < 2.5f)
        {
            return;
        }

        steering.LastStallReason = reason;
        steering.LastStallLogTime = now;

        var target = steering.Coordinates.IsValid(EntityManager)
            ? _transform.ToMapCoordinates(steering.Coordinates)
            : MapCoordinates.Nullspace;
        var origin = _transform.GetMapCoordinates(uid, xform: xform);
        var distance = origin.MapId == target.MapId
            ? Vector2.Distance(origin.Position, target.Position)
            : float.NaN;

        Log.Info(
            $"NPC steering stall {reason}: {ToPrettyString(uid)} status={steering.Status} distance={distance:0.00} range={steering.Range:0.00} pathPending={steering.Pathfind} pathCount={steering.CurrentPath.Count} flags={steering.Flags} target={target} details={details}");
    }

    private static float GetMax(float[] values)
    {
        var result = 0f;
        foreach (var value in values)
        {
            result = MathF.Max(result, value);
        }

        return result;
    }

    /// <summary>
    /// Go through each steerer and combine their vectors
    /// </summary>
    private void Steer(
        EntityUid uid,
        NPCSteeringComponent steering,
        InputMoverComponent mover,
        TransformComponent xform,
        float frameTime,
        TimeSpan curTime)
    {
        if (!steering.Coordinates.IsValid(EntityManager) ||
            Deleted(steering.Coordinates.EntityId))
        {
            LogSteeringStall(uid, steering, xform, "invalid-target", "target coordinates are invalid or deleted");
            SetDirection(uid, mover, steering, Vector2.Zero);
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        // No path set from pathfinding or the likes.
        if (steering.Status == SteeringStatus.NoPath)
        {
            LogSteeringStall(uid, steering, xform, "status-nopath", "steering status is NoPath");
            SetDirection(uid, mover, steering, Vector2.Zero);
            return;
        }

        // Can't move at all, just noop input.
        if (!mover.CanMove)
        {
            LogSteeringStall(uid, steering, xform, "cannot-move", "InputMoverComponent.CanMove is false");
            SetDirection(uid, mover, steering, Vector2.Zero);
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        Interlocked.Increment(ref _activeSteeringCount);

        var agentRadius = steering.Radius;
        var worldPos = _transform.GetWorldPosition(xform);
        var (layer, mask) = _physics.GetHardCollision(uid);

        // Use rotation relative to parent to rotate our context vectors by.
        var offsetRot = -_mover.GetParentGridAngle(mover);
        _modifierQuery.TryGetComponent(uid, out var modifier);
        var moveSpeed = GetSprintSpeed(uid, modifier);
        var body = _physicsQuery.GetComponent(uid);
        var dangerPoints = steering.DangerPoints;
        dangerPoints.Clear();
        Span<float> interest = stackalloc float[InterestDirections];
        Span<float> danger = stackalloc float[InterestDirections];

        // TODO: This should be fly
        steering.CanSeek = true;

        var ev = new NPCSteeringEvent(steering, xform, worldPos, offsetRot);
        RaiseLocalEvent(uid, ref ev);
        // If seek has arrived at the target node for example then immediately re-steer.
        var forceSteer = true;

        if (steering.CanSeek && !TrySeek(uid, mover, steering, body, xform, offsetRot, moveSpeed, interest, frameTime, ref forceSteer))
        {
            LogSteeringStall(uid, steering, xform, "seek-failed", $"TrySeek failed; speed={moveSpeed:0.00} pathPending={steering.Pathfind} pathCount={steering.CurrentPath.Count}");
            SetDirection(uid, mover, steering, Vector2.Zero);
            return;
        }

        DebugTools.Assert(!float.IsNaN(interest[0]));

        // Don't steer too frequently to avoid twitchiness.
        // This should also implicitly solve tie situations.
        // I think doing this after all the ops above is best?
        // Originally I had it way above but sometimes mobs would overshoot their tile targets.

        if (!forceSteer)
        {
            SetDirection(uid, mover, steering, steering.LastSteerDirection, false);
            return;
        }

        // Avoid static objects like walls
        CollisionAvoidance(uid, offsetRot, worldPos, agentRadius, layer, mask, xform, danger);
        DebugTools.Assert(!float.IsNaN(danger[0]));

        IncomingProjectileAvoidance(uid, offsetRot, worldPos, agentRadius, xform, danger);
        TurretThreatAvoidance(uid, offsetRot, worldPos, agentRadius, xform, danger);

        Separation(uid, offsetRot, worldPos, agentRadius, layer, mask, body, xform, danger);

        // Blend last and current tick
        Blend(steering, frameTime, interest, danger);

        // Remove the danger map from the interest map.
        var desiredDirection = -1;
        var desiredValue = 0f;

        for (var i = 0; i < InterestDirections; i++)
        {
            var adjustedValue = Math.Clamp(steering.Interest[i] - steering.Danger[i], 0f, 1f);

            if (adjustedValue > desiredValue)
            {
                desiredDirection = i;
                desiredValue = adjustedValue;
            }
        }

        var resultDirection = Vector2.Zero;

        if (desiredDirection != -1)
        {
            resultDirection = new Angle(desiredDirection * InterestRadians).ToVec();
        }
        else
        {
            LogSteeringStall(
                uid,
                steering,
                xform,
                "no-steering-slot",
                $"all steering slots blocked or empty; maxInterest={GetMax(steering.Interest):0.00} maxDanger={GetMax(steering.Danger):0.00} pathPending={steering.Pathfind} pathCount={steering.CurrentPath.Count}");
        }

        steering.LastSteerDirection = resultDirection;
        DebugTools.Assert(!float.IsNaN(resultDirection.X));
        SetDirection(uid, mover, steering, resultDirection, false);
    }

    private EntityCoordinates GetCoordinates(PathPoly poly)
    {
        if (!poly.IsValid())
            return EntityCoordinates.Invalid;

        return new EntityCoordinates(poly.GraphUid, poly.Box.Center);
    }

    /// <summary>
    /// Get a new job from the pathfindingsystem
    /// </summary>
    private async void RequestPath(EntityUid uid, NPCSteeringComponent steering, TransformComponent xform, float targetDistance)
    {
        // If we already have a pathfinding request then don't grab another.
        // If we're in range then just beeline them; this can avoid stutter stepping and is an easy way to look nicer.
        if (steering.Pathfind)
            return;

        if (!xform.Coordinates.IsValid(EntityManager) ||
            !steering.Coordinates.IsValid(EntityManager))
        {
            steering.CurrentPath.Clear();
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        if (targetDistance < steering.RepathRange &&
            IsDirectPathClear(uid, xform.Coordinates, steering.Coordinates, steering.Radius))
        {
            return;
        }

        // Short-circuit with no path.
        var targetPoly = _pathfindingSystem.GetPoly(steering.Coordinates);

        // If this still causes issues future sloth adjust the collision mask.
        // Thanks past sloth I already realised.
        if (targetPoly != null &&
            steering.Coordinates.Position.Equals(Vector2.Zero) &&
            TryComp<PhysicsComponent>(uid, out var physics) &&
            _interaction.InRangeUnobstructed(uid, steering.Coordinates.EntityId, range: 30f, (CollisionGroup)physics.CollisionMask))
        {
            steering.CurrentPath.Clear();
            steering.CurrentPath.Enqueue(targetPoly);
            return;
        }

        var requestedCoordinates = steering.Coordinates;
        var pathfindToken = new CancellationTokenSource();
        steering.PathfindToken = pathfindToken;

        var flags = _pathfindingSystem.GetFlags(uid);
        var blockedPoly = steering.AvoidedPathPoly is { } avoided && avoided.IsValid()
            ? avoided
            : null;

        var result = await _pathfindingSystem.GetPreferredPathSafe(
            uid,
            xform.Coordinates,
            requestedCoordinates,
            steering.Range,
            pathfindToken.Token,
            flags,
            blockedPoly);

        if (Deleted(uid) ||
            !_xformQuery.TryGetComponent(uid, out var currentXform) ||
            !TryComp<NPCSteeringComponent>(uid, out var currentSteering) ||
            !ReferenceEquals(currentSteering, steering) ||
            steering.PathfindToken != pathfindToken ||
            !steering.Coordinates.Equals(requestedCoordinates) ||
            !currentXform.Coordinates.IsValid(EntityManager) ||
            !requestedCoordinates.IsValid(EntityManager))
        {
            if (steering.PathfindToken == pathfindToken)
                steering.PathfindToken = null;

            pathfindToken.Dispose();
            return;
        }

        steering.PathfindToken = null;
        pathfindToken.Dispose();

        if (result.Result == PathResult.NoPath)
        {
            steering.CurrentPath.Clear();
            steering.FailedPathCount++;
            steering.PendingPathDirectMoveTicks = 0;

            if (steering.FailedPathCount >= steering.FailedPathLimit)
            {
                steering.Status = SteeringStatus.NoPath;
            }

            return;
        }

        var targetPos = _transform.ToMapCoordinates(requestedCoordinates);
        var ourPos = _transform.GetMapCoordinates(uid, xform: currentXform);

        PrunePath(uid, ourPos, targetPos.Position - ourPos.Position, result.Path, steering);
        if (blockedPoly != null && result.Path.Contains(blockedPoly))
            steering.AvoidedPathPoly = null;

        steering.CurrentPath = new Queue<PathPoly>(result.Path);
        steering.PendingPathDirectMoveTicks = 0;
        steering.FailedPathCount = 0;
        ResetRouteProgress(steering, ourPos);
    }

    // TODO: Move these to movercontroller

    private float GetSprintSpeed(EntityUid uid, MovementSpeedModifierComponent? modifier = null)
    {
        if (!Resolve(uid, ref modifier, false))
        {
            return MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
        }

        return modifier.CurrentSprintSpeed;
    }
}
