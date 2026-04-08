using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Popups;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.Weapons.ServoSkulls.Components;
using Content.Shared.Database;
using Content.Shared.Explosion.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Weapons.ServoSkulls;

public sealed class WH40KServoSkullSystem : EntitySystem
{
    private const string DeployNeedsTeamLoc = "wh40k-servo-skull-popup-deploy-team-required";
    private const string FollowingLoc = "wh40k-servo-skull-popup-following";
    private const string HoldingLoc = "wh40k-servo-skull-popup-holding";
    private const string AlreadyArmedLoc = "wh40k-servo-skull-popup-already-armed";
    private const string VerbFollowLoc = "wh40k-servo-skull-verb-follow-me";
    private const string VerbHoldLoc = "wh40k-servo-skull-verb-hold-position";
    private const string ArmedLoc = "wh40k-servo-skull-popup-armed";
    private static readonly SoundPathSpecifier BreakSound = new("/Audio/Effects/metal_break5.ogg");

    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly NpcFactionSystem _npcFactions = default!;
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly WH40KTeamNpcFactionSystem _teamNpcFactions = default!;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
        SubscribeLocalEvent<WH40KDeployServoSkullComponent, UseInHandEvent>(OnDeployUseInHand);
        SubscribeLocalEvent<WH40KServoSkullMobComponent, InteractHandEvent>(OnMobInteractHand);
        SubscribeLocalEvent<WH40KServoSkullMobComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<WH40KServoSkullMobComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KServoSkullMobComponent, TransformComponent, PhysicsComponent>();

        while (query.MoveNext(out var uid, out var skull, out var xform, out var physics))
        {
            UpdateServoSkull(uid, skull, xform, physics, now);
        }
    }

    private void OnDeployUseInHand(Entity<WH40KDeployServoSkullComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!_teamRule.TryGetTeamIdFromEntity(args.User, out var teamId) || string.IsNullOrWhiteSpace(teamId))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.User, DeployNeedsTeamLoc), args.User, args.User, PopupType.SmallCaution);
            return;
        }

        var spawnCoords = _transform.GetMapCoordinates(args.User);
        var spawned = Spawn(ent.Comp.MobPrototype, spawnCoords);

        if (TryComp<WH40KServoSkullMobComponent>(spawned, out var skull))
        {
            skull.OwnerEntity = args.User;
            skull.TeamId = teamId;
            skull.FollowTarget = null;
            skull.HostileTarget = null;
            skull.CurrentMovementTarget = null;
            skull.NextScanTime = _timing.CurTime;
        }

        _teamNpcFactions.ApplyTeamFaction(spawned, teamId);

        _transform.DetachEntity(ent, Transform(ent));
        QueueDel(ent);
        args.Handled = true;
    }

    private void OnMobInteractHand(Entity<WH40KServoSkullMobComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!IsUserAuthorizedForSkull(args.User, ent.Comp))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.User, "wh40k-access-denied-wrong-team"), ent.Owner, args.User, PopupType.SmallCaution);
            return;
        }

        if (HasComp<ActiveTimerTriggerComponent>(ent))
        {
            _popup.PopupEntity(_culture.GetPlayerString(args.User, AlreadyArmedLoc), ent.Owner, args.User, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        ToggleFollow(ent, args.User);
        args.Handled = true;
    }

    private void OnGetAlternativeVerbs(Entity<WH40KServoSkullMobComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!IsUserAuthorizedForSkull(args.User, ent.Comp))
            return;

        using var scope = _culture.CreateScope(args.User);
        var isArmed = HasComp<ActiveTimerTriggerComponent>(ent);
        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 100,
            Disabled = isArmed,
            Text = isArmed
                ? Loc.GetString(AlreadyArmedLoc)
                : ent.Comp.FollowTarget == user
                    ? Loc.GetString(VerbHoldLoc)
                    : Loc.GetString(VerbFollowLoc),
            Act = () => ToggleFollow(ent, user),
            Impact = LogImpact.Low,
        });
    }

    private void OnMobStateChanged(Entity<WH40KServoSkullMobComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        ent.Comp.FollowTarget = null;
        ent.Comp.HostileTarget = null;
        ent.Comp.CurrentMovementTarget = null;
        ClearMovement(ent.Owner, ent.Comp, CompOrNull<PhysicsComponent>(ent));

        if (TryComp<ExplosiveComponent>(ent, out var explosive))
        {
            _explosion.TriggerExplosive(ent.Owner, explosive, user: ent.Comp.OwnerEntity ?? ent.Comp.FollowTarget);
            return;
        }

        _audio.PlayPvs(BreakSound, ent.Owner);
        QueueDel(ent);
    }

    private void UpdateServoSkull(
        EntityUid uid,
        WH40KServoSkullMobComponent skull,
        TransformComponent xform,
        PhysicsComponent physics,
        TimeSpan now)
    {
        if (TryComp<MobStateComponent>(uid, out var mobState) && mobState.CurrentState != MobState.Alive)
        {
            ClearMovement(uid, skull, physics);
            return;
        }

        var primed = HasComp<ActiveTimerTriggerComponent>(uid);

        if (now >= skull.NextScanTime)
        {
            skull.NextScanTime = now + TimeSpan.FromSeconds(skull.ScanInterval);
            RefreshTargets(uid, skull, primed);
            primed = HasComp<ActiveTimerTriggerComponent>(uid);
        }

        EntityUid? movementTarget = null;
        var speed = skull.FollowSpeed;
        var stopRange = skull.FollowStopRange;

        if (primed)
        {
            if (!IsValidTarget(uid, skull.HostileTarget))
                skull.HostileTarget = FindNearestHostile(uid, skull.HostileDetectionRange);

            movementTarget = skull.HostileTarget;
            speed = skull.ChargeSpeed;
            stopRange = skull.ChargeStopRange;
        }
        else
        {
            if (!IsValidTarget(uid, skull.FollowTarget))
                skull.FollowTarget = null;

            movementTarget = skull.FollowTarget;
        }

        if (movementTarget == null ||
            !TryComp(movementTarget, out TransformComponent? targetXform) ||
            xform.MapID != targetXform.MapID)
        {
            ClearMovement(uid, skull, physics);
            return;
        }

        var delta = _transform.GetWorldPosition(targetXform) - _transform.GetWorldPosition(xform);
        UpdateFacing(uid, xform, physics, delta);

        if (delta.LengthSquared() <= stopRange * stopRange || delta.LengthSquared() <= 0.0001f)
        {
            ClearMovement(uid, skull, physics);
            return;
        }

        DriveMovement(uid, skull, targetXform.Coordinates, movementTarget.Value, speed, stopRange);
    }

    private void RefreshTargets(EntityUid uid, WH40KServoSkullMobComponent skull, bool primed)
    {
        if (!skull.TriggerOnHostile)
            return;

        var hostile = FindNearestHostile(uid, skull.HostileDetectionRange);
        if (hostile != null)
            skull.HostileTarget = hostile;
        else if (!IsValidTarget(uid, skull.HostileTarget))
            skull.HostileTarget = null;

        if (primed || skull.HostileTarget == null || !TryComp<TimerTriggerComponent>(uid, out var timer))
            return;

        _trigger.ActivateTimerTrigger((uid, timer), skull.OwnerEntity ?? skull.FollowTarget);
        _popup.PopupEntity(Loc.GetString(ArmedLoc), uid, PopupType.SmallCaution);
    }

    private EntityUid? FindNearestHostile(EntityUid uid, float range)
    {
        if (!TryComp<NpcFactionMemberComponent>(uid, out var factions))
            return null;

        var origin = _transform.GetWorldPosition(uid);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;

        foreach (var hostile in _npcFactions.GetNearbyHostiles((uid, factions, CompOrNull<FactionExceptionComponent>(uid)), range))
        {
            if (!IsValidTarget(uid, hostile))
                continue;

            if (!TryComp(hostile, out TransformComponent? hostileXform))
                continue;

            var distanceSquared = (_transform.GetWorldPosition(hostileXform) - origin).LengthSquared();
            if (distanceSquared >= bestDistance)
                continue;

            bestDistance = distanceSquared;
            best = hostile;
        }

        return best;
    }

    private void DriveMovement(
        EntityUid uid,
        WH40KServoSkullMobComponent skull,
        EntityCoordinates destination,
        EntityUid movementTarget,
        float speed,
        float stopRange)
    {
        var movement = EnsureComp<MovementSpeedModifierComponent>(uid);
        if (!MathHelper.CloseTo(skull.AppliedBaseSpeed, speed))
        {
            _movementSpeed.ChangeBaseSpeed(uid, speed, speed, movement.BaseAcceleration, movement);
            skull.AppliedBaseSpeed = speed;
        }

        EnsureComp<ActiveNPCComponent>(uid);

        if (!TryComp<NPCSteeringComponent>(uid, out var steering) ||
            steering.Status == SteeringStatus.NoPath ||
            skull.CurrentMovementTarget != movementTarget)
        {
            steering = _steering.Register(uid, destination, steering);
        }
        else
        {
            steering.Coordinates = destination;
        }

        steering.Range = stopRange;
        steering.DirectMove = false;
        steering.ArriveOnLineOfSight = false;
        steering.InRangeMaxSpeed = 0.03f;
        steering.Status = SteeringStatus.Moving;
        skull.CurrentMovementTarget = movementTarget;
    }

    private void ClearMovement(
        EntityUid uid,
        WH40KServoSkullMobComponent skull,
        PhysicsComponent? physics = null)
    {
        if (TryComp<NPCSteeringComponent>(uid, out var steering))
            _steering.Unregister(uid, steering);

        RemComp<ActiveNPCComponent>(uid);
        skull.CurrentMovementTarget = null;

        if (physics == null && !TryComp(uid, out physics))
            return;

        _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
    }

    private void UpdateFacing(
        EntityUid uid,
        TransformComponent xform,
        PhysicsComponent physics,
        Vector2 desiredDirection)
    {
        var direction = physics.LinearVelocity.LengthSquared() > 0.0025f
            ? physics.LinearVelocity
            : desiredDirection;

        if (direction.LengthSquared() <= 0.0001f)
            return;

        _transform.SetWorldRotation(uid, direction.ToWorldAngle());
    }

    private bool IsValidTarget(EntityUid owner, EntityUid? target)
    {
        if (target == null || TerminatingOrDeleted(target.Value))
            return false;

        if (!TryComp(owner, out TransformComponent? ownerXform) ||
            !TryComp(target, out TransformComponent? targetXform) ||
            ownerXform.MapID != targetXform.MapID)
        {
            return false;
        }

        if (TryComp<MobStateComponent>(target, out var mobState) && mobState.CurrentState != MobState.Alive)
            return false;

        return true;
    }

    private bool IsUserAuthorizedForSkull(EntityUid user, WH40KServoSkullMobComponent skull)
    {
        if (string.IsNullOrWhiteSpace(skull.TeamId))
            return false;

        return _teamRule.TryGetTeamIdFromEntity(user, out var teamId) &&
               string.Equals(teamId, skull.TeamId, StringComparison.OrdinalIgnoreCase);
    }

    private void ToggleFollow(Entity<WH40KServoSkullMobComponent> ent, EntityUid user)
    {
        if (ent.Comp.FollowTarget == user)
        {
            ent.Comp.FollowTarget = null;
            ent.Comp.CurrentMovementTarget = null;
            _popup.PopupEntity(_culture.GetPlayerString(user, HoldingLoc), ent.Owner, user, PopupType.Small);
            return;
        }

        ent.Comp.OwnerEntity ??= user;
        ent.Comp.FollowTarget = user;
        _popup.PopupEntity(_culture.GetPlayerString(user, FollowingLoc), ent.Owner, user, PopupType.Small);
    }
}
