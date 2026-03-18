using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Destructible;
using Content.Server.DoAfter;
using Content.Server.Gravity;
using Content.Server.NPC.Components;
using Content.Server.NPC.Events;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.NPC.Events;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics;
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

    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly NPCBenchmarkSystem _bench = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly GravitySystem _gravity = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly PathfindingSystem _pathfindingSystem = default!;
    [Dependency] private readonly PryingSystem _pryingSystem = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;

    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<MovementSpeedModifierComponent> _modifierQuery;
    private EntityQuery<NpcFactionMemberComponent> _factionQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    // For obstacle detection:
    private EntityQuery<DoorComponent> _doorQuery;
    private EntityQuery<ClimbableComponent> _climbableQuery;
    private EntityQuery<DestructibleComponent> _destructibleQuery;

    private ObjectPool<HashSet<EntityUid>> _entSetPool =
        new DefaultObjectPool<HashSet<EntityUid>>(new SetPolicy<EntityUid>());

    /// <summary>
    /// Enabled antistuck detection so if an NPC is in the same spot for a while it will re-path.
    /// </summary>
    public bool AntiStuck = true;

    private bool _enabled;

    private bool _pathfinding = true;
    private float _pathRequestCooldownSeconds = 0.10f;
    private float _pathNoPathBackoffSeconds = 0.35f;
    private float _pathMaxBackoffSeconds = 1.50f;
    private float _obstacleFailureResetSeconds = 2.0f;
    private int _obstacleRetryLimit = 3;
    private float _obstacleLaneRotateWeight = 0.65f;

    public static readonly Vector2[] Directions = new Vector2[InterestDirections];

    private readonly HashSet<ICommonSession> _subscribedSessions = new();

    private int _activeSteeringCount;

    public override void Initialize()
    {
        base.Initialize();

        Log.Level = LogLevel.Info;
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _modifierQuery = GetEntityQuery<MovementSpeedModifierComponent>();
        _factionQuery = GetEntityQuery<NpcFactionMemberComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();
        _climbableQuery = GetEntityQuery<ClimbableComponent>();
        _destructibleQuery = GetEntityQuery<DestructibleComponent>();

        for (var i = 0; i < InterestDirections; i++)
        {
            Directions[i] = new Angle(InterestRadians * i).ToVec();
        }

        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        Subs.CVar(_configManager, CCVars.NPCEnabled, SetNPCEnabled, true);
        Subs.CVar(_configManager, CCVars.NPCPathfinding, SetNPCPathfinding, true);
        Subs.CVar(_configManager, CCVars.NPCSteeringPathRequestCooldownSeconds, value => _pathRequestCooldownSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_configManager, CCVars.NPCSteeringPathNoPathBackoffSeconds, value => _pathNoPathBackoffSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_configManager, CCVars.NPCSteeringPathMaxBackoffSeconds, value => _pathMaxBackoffSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_configManager, CCVars.NPCSteeringObstacleFailureResetSeconds, value => _obstacleFailureResetSeconds = MathF.Max(0.2f, value), true);
        Subs.CVar(_configManager, CCVars.NPCSteeringObstacleRetryLimit, value => _obstacleRetryLimit = Math.Max(1, value), true);
        Subs.CVar(_configManager, CCVars.NPCSteeringObstacleLaneRotateWeight, value => _obstacleLaneRotateWeight = Math.Clamp(value, 0f, 1f), true);

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
    }

    /// <summary>
    /// Adds the AI to the steering system to move towards a specific target
    /// </summary>
    public NPCSteeringComponent Register(EntityUid uid, EntityCoordinates coordinates, NPCSteeringComponent? component = null)
    {
        if (Resolve(uid, ref component, false))
        {
            if (component.Coordinates.Equals(coordinates))
                return component;

            component.PathfindToken?.Cancel();
            component.PathfindToken = null;
            component.CurrentPath.Clear();
        }
        else
        {
            component = AddComp<NPCSteeringComponent>(uid);
            component.Flags = _pathfindingSystem.GetFlags(uid);
        }

        ResetStuck(component, Transform(uid).Coordinates);
        component.Coordinates = coordinates;
        component.NextPathRequestTime = TimeSpan.Zero;
        component.PathRequestBackoffSeconds = 0f;
        component.ObstacleFailureCount = 0;
        component.LastObstacleFailureTime = TimeSpan.Zero;
        component.LaneRotateSign = 1;
        return component;
    }

    /// <summary>
    /// Attempts to register the entity. Does nothing if the coordinates already registered.
    /// </summary>
    public bool TryRegister(EntityUid uid, EntityCoordinates coordinates, NPCSteeringComponent? component = null)
    {
        if (Resolve(uid, ref component, false) && component.Coordinates.Equals(coordinates))
        {
            // Allow capability layers to recover steering when an NPC is stuck in no-path state
            // while still pursuing the same destination.
            if (component.Status == SteeringStatus.NoPath ||
                component.FailedPathCount > 0 ||
                component.PathRequestBackoffSeconds > 0f)
            {
                Register(uid, coordinates, component);
                _bench.RecordCount("npc.steering.register.recovered", 1);
                return true;
            }

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
        RemComp<NPCSteeringComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        using var benchScope = _bench.Measure("npc.steering.update");

        // Not every mob has the modifier component so do it as a separate query.
        var npcs = new (EntityUid, NPCSteeringComponent, InputMoverComponent, TransformComponent)[Count<ActiveNPCComponent>()];

        var query = EntityQueryEnumerator<ActiveNPCComponent, NPCSteeringComponent, InputMoverComponent, TransformComponent>();
        var index = 0;

        while (query.MoveNext(out var uid, out _, out var steering, out var mover, out var xform))
        {
            npcs[index] = (uid, steering, mover, xform);
            index++;
        }

        _bench.RecordCount("npc.steering.entities", index);

        var curTime = _timing.CurTime;

        _activeSteeringCount = 0;

        for (var i = 0; i < index; i++)
        {
            var (uid, steering, mover, xform) = npcs[i];
            Steer(uid, steering, mover, xform, frameTime, curTime);
        }

        ActiveSteeringGauge.Set(_activeSteeringCount);

        if (_subscribedSessions.Count > 0)
        {
            var data = new List<NPCSteeringDebugData>(index);

            for (var i = 0; i < index; i++)
            {
                var (uid, steering, mover, _) = npcs[i];

                data.Add(new NPCSteeringDebugData(
                    GetNetEntity(uid),
                    mover.CurTickSprintMovement,
                    steering.Interest,
                    steering.Danger,
                    steering.DangerPoints));
            }

            var filter = Filter.Empty();
            filter.AddPlayers(_subscribedSessions);

            RaiseNetworkEvent(new NPCSteeringDebugEvent(data), filter);
        }
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
        using var benchScope = _bench.Measure("npc.steering.entity");

        if (Deleted(steering.Coordinates.EntityId))
        {
            SetDirection(uid, mover, steering, Vector2.Zero);
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        // No path set from pathfinding or the likes.
        if (steering.Status == SteeringStatus.NoPath)
        {
            SetDirection(uid, mover, steering, Vector2.Zero);
            return;
        }

        // Can't move at all, just noop input.
        if (!mover.CanMove)
        {
            SetDirection(uid, mover, steering, Vector2.Zero);
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        _activeSteeringCount++;

        var agentRadius = steering.Radius;
        var worldPos = _transform.GetWorldPosition(xform);
        var (layer, mask) = _physics.GetHardCollision(uid);
        // Use rotation relative to parent to rotate our context vectors by.
        var offsetRot = -_mover.GetParentGridAngle(mover);

        _modifierQuery.TryGetComponent(uid, out var modifier);
        var body = _physicsQuery.GetComponent(uid);

        var weightless = _gravity.IsWeightless(uid);
        var moveSpeed = GetSprintSpeed(uid, modifier);
        var acceleration = GetAcceleration((uid, modifier), weightless);
        var friction = GetFriction((uid, modifier), weightless);

        var dangerPoints = steering.DangerPoints;
        dangerPoints.Clear();
        Span<float> interest = stackalloc float[InterestDirections];
        Span<float> danger = stackalloc float[InterestDirections];

        // TODO: This should be fly
        steering.CanSeek = true;

        var ev = new NPCSteeringEvent(steering, xform, worldPos, offsetRot);
        RaiseLocalEvent(uid, ref ev);
        // If seek has arrived at the target node for example then immediately re-steer.
        // Note: this seems like it's always true? Not sure when it should be false...
        var forceSteer = true;
        var moveMultiplier = 1f; // multiplier to acceleration we should actually move with

        if (steering.CanSeek && !TrySeek(uid, mover, steering, body, xform, offsetRot, moveSpeed, acceleration, friction, interest, frameTime, ref forceSteer, ref moveMultiplier))
        {
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
        CollisionAvoidance(uid, steering, offsetRot, worldPos, agentRadius, layer, mask, xform, danger);
        DebugTools.Assert(!float.IsNaN(danger[0]));

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
            resultDirection = new Angle(desiredDirection * InterestRadians).ToVec() * moveMultiplier;
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
        using var benchScope = _bench.Measure("npc.steering.request_path");

        if (TerminatingOrDeleted(uid) || !_transform.IsValid(steering.Coordinates))
        {
            steering.CurrentPath.Clear();
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        // If we already have a pathfinding request then don't grab another.
        // If we're in range then just beeline them; this can avoid stutter stepping and is an easy way to look nicer.
        if (steering.Pathfind || targetDistance < steering.RepathRange)
            return;

        var now = _timing.CurTime;
        if (now < steering.NextPathRequestTime)
        {
            _bench.RecordCount("npc.steering.path_request.throttled", 1);
            return;
        }

        var jitter = _random.NextFloat(0f, MathF.Min(0.05f, _pathRequestCooldownSeconds * 0.5f));
        steering.NextPathRequestTime = now + TimeSpan.FromSeconds(_pathRequestCooldownSeconds + jitter);
        _bench.RecordCount("npc.steering.path_request.submitted", 1);

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
            steering.FailedPathCount = 0;
            steering.PathRequestBackoffSeconds = 0f;
            return;
        }

        var pathfindToken = new CancellationTokenSource();
        steering.PathfindToken = pathfindToken;

        var flags = _pathfindingSystem.GetFlags(uid);

        var result = await _pathfindingSystem.GetPathSafe(
            uid,
            xform.Coordinates,
            steering.Coordinates,
            steering.Range,
            pathfindToken.Token,
            flags);

        if (!ReferenceEquals(steering.PathfindToken, pathfindToken))
            return;

        steering.PathfindToken = null;

        if (pathfindToken.IsCancellationRequested ||
            TerminatingOrDeleted(uid) ||
            !_transform.IsValid(steering.Coordinates) ||
            !TryComp(uid, out TransformComponent? refreshedXform))
        {
            return;
        }

        if (result.Result == PathResult.NoPath)
        {
            steering.CurrentPath.Clear();
            steering.FailedPathCount++;
            steering.PathRequestBackoffSeconds = steering.PathRequestBackoffSeconds <= 0f
                ? MathF.Min(_pathNoPathBackoffSeconds, _pathMaxBackoffSeconds)
                : MathF.Min(_pathMaxBackoffSeconds, steering.PathRequestBackoffSeconds * 2f);
            var retryJitter = _random.NextFloat(0f, 0.1f);
            steering.NextPathRequestTime = _timing.CurTime + TimeSpan.FromSeconds(
                _pathRequestCooldownSeconds + steering.PathRequestBackoffSeconds + retryJitter);
            _bench.RecordCount("npc.steering.path_result.no_path", 1);
            _bench.RecordCount("npc.steering.path_request.no_path_backoff", 1);
            _bench.RecordDuration("npc.steering.path_request.backoff_ms", steering.PathRequestBackoffSeconds * 1000f);

            if (steering.FailedPathCount >= NPCSteeringComponent.FailedPathLimit)
            {
                steering.Status = SteeringStatus.NoPath;
            }

            return;
        }

        var targetPos = _transform.ToMapCoordinates(steering.Coordinates, logError: false);
        if (targetPos.MapId == MapId.Nullspace)
        {
            steering.CurrentPath.Clear();
            steering.Status = SteeringStatus.NoPath;
            return;
        }

        var ourPos = _transform.GetMapCoordinates(uid, xform: refreshedXform);

        PrunePath(uid, ourPos, targetPos.Position - ourPos.Position, result.Path);
        steering.CurrentPath = new Queue<PathPoly>(result.Path);
        steering.FailedPathCount = 0;
        steering.PathRequestBackoffSeconds = 0f;
        steering.ObstacleFailureCount = 0;
        _bench.RecordCount("npc.steering.path_result.success", 1);
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

    private float GetAcceleration(Entity<MovementSpeedModifierComponent?> ent, bool weightless)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return weightless ? MovementSpeedModifierComponent.DefaultWeightlessAcceleration : MovementSpeedModifierComponent.DefaultAcceleration;

        return weightless ? ent.Comp.WeightlessAcceleration : ent.Comp.Acceleration;
    }

    private float GetFriction(Entity<MovementSpeedModifierComponent?> ent, bool weightless)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return weightless ? MovementSpeedModifierComponent.DefaultWeightlessFriction : MovementSpeedModifierComponent.DefaultFriction;

        return weightless ? ent.Comp.WeightlessFriction : ent.Comp.Friction;
    }
}
