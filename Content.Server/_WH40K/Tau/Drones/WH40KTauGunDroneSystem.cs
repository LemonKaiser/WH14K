using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Popups;
using Content.Server.Weapons.Ranged.Systems;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.Tau.Drones.Components;
using Content.Server._WH40K.Weapons.ServoSkulls.Components;
using Content.Shared.Actions;
using Content.Shared.Database;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Toggleable;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Tau.Drones;

public sealed partial class WH40KTauGunDroneSystem : EntitySystem
{
    private const float TwoPi = MathF.PI * 2f;
    private const float FriendlyFireLaneRadius = 0.55f;
    private const string PopupFireEnabledLoc = "wh40k-tau-gun-drone-popup-fire-enabled";
    private const string PopupFireDisabledLoc = "wh40k-tau-gun-drone-popup-fire-disabled";
    private const string PopupAggressionEnabledLoc = "wh40k-tau-gun-drone-popup-aggression-enabled";
    private const string PopupAggressionDisabledLoc = "wh40k-tau-gun-drone-popup-aggression-disabled";
    private const string VerbEnableFireLoc = "wh40k-tau-gun-drone-verb-enable-fire";
    private const string VerbDisableFireLoc = "wh40k-tau-gun-drone-verb-disable-fire";
    private static readonly float[] CombatAngleOffsets = { 0f, MathF.PI / 5f, -MathF.PI / 5f, MathF.PI / 2.8f, -MathF.PI / 2.8f, MathF.PI * 0.75f, -MathF.PI * 0.75f };
    private static readonly float[] StrafeAngleOffsets = { MathF.PI / 5f, -MathF.PI / 5f, MathF.PI / 2.8f, -MathF.PI / 2.8f, 0f, MathF.PI * 0.75f, -MathF.PI * 0.75f };

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private NpcFactionSystem _npcFactions = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WH40KTeamBattleRuleSystem _teamRule = default!;

    private readonly HashSet<EntityUid> _laneCheck = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KTauDroneControllerComponent, ComponentStartup>(OnControllerStartup);
        SubscribeLocalEvent<WH40KTauDroneControllerComponent, ComponentShutdown>(OnControllerShutdown);
        SubscribeLocalEvent<WH40KTauDroneControllerComponent, ToggleActionEvent>(OnToggleAggression);
        SubscribeLocalEvent<WH40KTauGunDroneComponent, ComponentShutdown>(OnDroneShutdown);
        SubscribeLocalEvent<WH40KTauGunDroneComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
    }

    public override void Update(float frameTime)
    {
        var ownerGroups = BuildOwnerGroups();
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KTauGunDroneComponent, WH40KServoSkullMobComponent, GunComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var drone, out var skull, out var gun, out var xform))
        {
            EnsureOwnerController(skull.OwnerEntity);

            if (now < drone.NextScanTime)
                continue;

            if (!TryComp(uid, out PhysicsComponent? physics))
                continue;

            drone.NextScanTime = now + TimeSpan.FromSeconds(drone.ScanInterval);
            var slot = GetFormationSlot(ownerGroups, skull.OwnerEntity, uid, out var slotCount);
            UpdateDrone(uid, drone, skull, gun, xform, physics, now, slot, slotCount);
        }
    }

    private Dictionary<EntityUid, List<EntityUid>> BuildOwnerGroups()
    {
        var groups = new Dictionary<EntityUid, List<EntityUid>>();
        var query = EntityQueryEnumerator<WH40KTauGunDroneComponent, WH40KServoSkullMobComponent>();

        while (query.MoveNext(out var uid, out _, out var skull))
        {
            if (skull.OwnerEntity is not { } owner || TerminatingOrDeleted(owner))
                continue;

            if (!groups.TryGetValue(owner, out var drones))
            {
                drones = new List<EntityUid>();
                groups[owner] = drones;
            }

            drones.Add(uid);
        }

        foreach (var drones in groups.Values)
        {
            drones.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        }

        return groups;
    }

    private static int GetFormationSlot(
        Dictionary<EntityUid, List<EntityUid>> ownerGroups,
        EntityUid? owner,
        EntityUid drone,
        out int slotCount)
    {
        if (owner is not { } ownerUid ||
            !ownerGroups.TryGetValue(ownerUid, out var drones) ||
            drones.Count == 0)
        {
            slotCount = 1;
            return 0;
        }

        slotCount = drones.Count;
        var index = drones.IndexOf(drone);
        return index >= 0 ? index : 0;
    }

    private void OnGetAlternativeVerbs(Entity<WH40KTauGunDroneComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryComp<WH40KServoSkullMobComponent>(ent, out var skull) || !IsUserAuthorized(args.User, skull))
            return;

        var user = args.User;
        using var scope = _culture.CreateScope(user);
        args.Verbs.Add(new AlternativeVerb
        {
            Priority = 90,
            Text = ent.Comp.FireEnabled
                ? Loc.GetString(VerbDisableFireLoc)
                : Loc.GetString(VerbEnableFireLoc),
            Act = () => ToggleFire(ent, user),
            Impact = LogImpact.Low,
        });
    }

    private void OnControllerStartup(Entity<WH40KTauDroneControllerComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction, ent.Owner);
        _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.AggressionEnabled);
    }

    private void OnControllerShutdown(Entity<WH40KTauDroneControllerComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ToggleActionEntity);
    }

    private void OnToggleAggression(Entity<WH40KTauDroneControllerComponent> ent, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.AggressionEnabled = !ent.Comp.AggressionEnabled;
        _actions.SetToggled(ent.Comp.ToggleActionEntity, ent.Comp.AggressionEnabled);

        using var scope = _culture.CreateScope(args.Performer);
        var loc = ent.Comp.AggressionEnabled ? PopupAggressionEnabledLoc : PopupAggressionDisabledLoc;
        _popup.PopupEntity(Loc.GetString(loc), ent.Owner, args.Performer, PopupType.Small);
        args.Handled = true;
    }

    private void OnDroneShutdown(Entity<WH40KTauGunDroneComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<WH40KServoSkullMobComponent>(ent, out var skull) ||
            skull.OwnerEntity is not { } owner ||
            TerminatingOrDeleted(owner))
        {
            return;
        }

        RefreshOwnerController(owner, ent.Owner);
    }

    private void UpdateDrone(
        EntityUid uid,
        WH40KTauGunDroneComponent drone,
        WH40KServoSkullMobComponent skull,
        GunComponent gun,
        TransformComponent xform,
        PhysicsComponent physics,
        TimeSpan now,
        int slot,
        int slotCount)
    {
        if (TryComp<MobStateComponent>(uid, out var state) && state.CurrentState != MobState.Alive)
        {
            drone.CurrentTarget = null;
            ClearMovement(uid, skull, physics);
            return;
        }

        var aggressionEnabled = IsAggressionEnabled(skull.OwnerEntity);
        if (!drone.FireEnabled || !aggressionEnabled)
            drone.CurrentTarget = null;
        else if (!IsValidTarget(uid, xform, drone.CurrentTarget, Math.Max(drone.FireRange, drone.AcquisitionRange)) ||
                 !IsTargetWithinOwnerLeash(drone.CurrentTarget, skull.OwnerEntity, drone.OwnerLeashRange))
            drone.CurrentTarget = FindNearestHostile(uid, skull, xform, Math.Max(drone.FireRange, drone.AcquisitionRange), skull.OwnerEntity, drone.OwnerLeashRange);

        if (drone.CurrentTarget is { } hostile &&
            TryComp(hostile, out TransformComponent? hostileXform))
        {
            var canShoot = HasCleanShot(uid, drone, skull, xform, hostile, hostileXform);
            UpdateCombatMovement(uid, drone, skull, xform, physics, hostile, hostileXform, slot, slotCount, canShoot);
            if (canShoot)
                TryShootAtTarget(uid, drone, gun, xform, hostile, hostileXform, now);
            return;
        }

        if (IsValidTarget(skull.FollowTarget))
        {
            UpdateFollowMovement(uid, drone, skull, xform, physics, skull.FollowTarget!.Value, slot, slotCount);
            return;
        }

        ClearMovement(uid, skull, physics);
    }

    private static bool GunTooEarly(GunComponent gun, TimeSpan now)
    {
        return gun.NextFire > now;
    }

    private void UpdateCombatMovement(
        EntityUid uid,
        WH40KTauGunDroneComponent drone,
        WH40KServoSkullMobComponent skull,
        TransformComponent droneXform,
        PhysicsComponent physics,
        EntityUid target,
        TransformComponent targetXform,
        int slot,
        int slotCount,
        bool canShoot)
    {
        var dronePos = _transform.GetWorldPosition(droneXform);
        var targetPos = _transform.GetWorldPosition(targetXform);
        var toTarget = targetPos - dronePos;
        var distance = toTarget.Length();

        UpdateFacing(uid, droneXform, physics, toTarget);

        if (distance >= drone.MinimumCombatRange && distance <= drone.FireRange && canShoot)
        {
            ClearMovement(uid, skull, physics);
            return;
        }

        if (!TryResolveCombatCoordinate(
                uid,
                drone,
                skull,
                droneXform,
                target,
                targetXform,
                dronePos,
                targetPos,
                slot,
                slotCount,
                !canShoot,
                out var destination))
        {
            ClearMovement(uid, skull, physics);
            return;
        }

        DriveMovement(uid, skull, destination, target, skull.ChargeSpeed, 0.45f);
    }

    private void UpdateFollowMovement(
        EntityUid uid,
        WH40KTauGunDroneComponent drone,
        WH40KServoSkullMobComponent skull,
        TransformComponent droneXform,
        PhysicsComponent physics,
        EntityUid followTarget,
        int slot,
        int slotCount)
    {
        if (!TryComp(followTarget, out TransformComponent? followXform))
        {
            ClearMovement(uid, skull, physics);
            return;
        }

        var dronePos = _transform.GetWorldPosition(droneXform);
        var followPos = _transform.GetWorldPosition(followXform);

        if (!TryResolveRingCoordinate(
                followXform.Coordinates,
                followTarget,
                dronePos,
                followPos,
                drone.FollowFormationRadius,
                slot,
                slotCount,
                out var destination,
                out var desiredPosition))
        {
            ClearMovement(uid, skull, physics);
            return;
        }

        var delta = desiredPosition - dronePos;
        UpdateFacing(uid, droneXform, physics, delta);

        if (delta.LengthSquared() <= 0.25f)
        {
            ClearMovement(uid, skull, physics);
            return;
        }

        DriveMovement(uid, skull, destination, followTarget, skull.FollowSpeed, 0.35f);
    }

    private void TryShootAtTarget(
        EntityUid uid,
        WH40KTauGunDroneComponent drone,
        GunComponent gun,
        TransformComponent xform,
        EntityUid target,
        TransformComponent targetXform,
        TimeSpan now)
    {
        if (GunTooEarly(gun, now))
            return;

        var delta = _transform.GetWorldPosition(targetXform) - _transform.GetWorldPosition(xform);
        if (delta.LengthSquared() <= 0.0001f || delta.LengthSquared() > drone.FireRange * drone.FireRange)
            return;

        _transform.SetWorldRotation(uid, delta.ToWorldAngle());
        _gun.AttemptShoot(uid, (uid, gun), targetXform.Coordinates, target);
    }

    private bool TryResolveRingCoordinate(
        EntityCoordinates anchor,
        EntityUid? referenceEntity,
        Vector2 dronePosition,
        Vector2 anchorPosition,
        float radius,
        int slot,
        int slotCount,
        out EntityCoordinates coordinates,
        out Vector2 desiredPosition,
        float angleOffset = 0f)
    {
        coordinates = default;
        desiredPosition = Vector2.Zero;

        var anchorMap = _transform.ToMapCoordinates(anchor);
        if (anchorMap.MapId == MapId.Nullspace)
            return false;

        var reference = referenceEntity != null &&
                        TryComp(referenceEntity, out TransformComponent? referenceXform)
            ? _transform.GetWorldPosition(referenceXform) - anchorPosition
            : dronePosition - anchorPosition;

        if (reference.LengthSquared() <= 0.001f)
            reference = -Vector2.UnitY;
        else
            reference = Vector2.Normalize(reference);

        var baseAngle = MathF.Atan2(reference.Y, reference.X);
        var spread = TwoPi / Math.Max(4, slotCount);
        var angle = baseAngle + spread * slot + angleOffset;
        var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        desiredPosition = anchorMap.Position + offset;
        var desired = new MapCoordinates(desiredPosition, anchorMap.MapId);
        coordinates = _transform.ToCoordinates(anchor.EntityId, desired);
        return true;
    }

    private EntityUid? FindNearestHostile(
        EntityUid uid,
        WH40KServoSkullMobComponent skull,
        TransformComponent xform,
        float range,
        EntityUid? owner,
        float leashRange)
    {
        if (!TryComp<NpcFactionMemberComponent>(uid, out var factions))
            return null;

        var origin = _transform.GetWorldPosition(xform);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;

        foreach (var hostile in _npcFactions.GetNearbyHostiles((uid, factions, CompOrNull<FactionExceptionComponent>(uid)), range))
        {
            if (!IsValidTarget(uid, xform, hostile, range))
                continue;

            if (!IsTargetWithinOwnerLeash(hostile, owner, leashRange))
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

    private bool IsValidTarget(
        EntityUid owner,
        TransformComponent ownerXform,
        EntityUid? target,
        float range)
    {
        if (target == null || TerminatingOrDeleted(target.Value))
            return false;

        if (!TryComp(target, out TransformComponent? targetXform) || ownerXform.MapID != targetXform.MapID)
            return false;

        if (TryComp<MobStateComponent>(target, out var state) && state.CurrentState != MobState.Alive)
            return false;

        var distanceSquared = (_transform.GetWorldPosition(targetXform) - _transform.GetWorldPosition(ownerXform)).LengthSquared();
        return distanceSquared <= range * range;
    }

    private bool IsValidTarget(EntityUid? target)
    {
        if (target == null || TerminatingOrDeleted(target.Value))
            return false;

        if (TryComp<MobStateComponent>(target, out var state) && state.CurrentState != MobState.Alive)
            return false;

        return TryComp<TransformComponent>(target, out _);
    }

    private bool IsTargetWithinOwnerLeash(EntityUid? target, EntityUid? owner, float leashRange)
    {
        if (target == null || owner == null || TerminatingOrDeleted(target.Value) || TerminatingOrDeleted(owner.Value))
            return true;

        if (!TryComp(target, out TransformComponent? targetXform) ||
            !TryComp(owner, out TransformComponent? ownerXform) ||
            targetXform.MapID != ownerXform.MapID)
        {
            return false;
        }

        var distanceSquared = (_transform.GetWorldPosition(targetXform) - _transform.GetWorldPosition(ownerXform)).LengthSquared();
        return distanceSquared <= leashRange * leashRange;
    }

    private bool IsAggressionEnabled(EntityUid? owner)
    {
        return owner == null ||
               !TryComp(owner, out WH40KTauDroneControllerComponent? controller) ||
               controller.AggressionEnabled;
    }

    private bool HasCleanShot(
        EntityUid uid,
        WH40KTauGunDroneComponent drone,
        WH40KServoSkullMobComponent skull,
        TransformComponent droneXform,
        EntityUid target,
        TransformComponent targetXform)
    {
        if (droneXform.MapID != targetXform.MapID)
            return false;

        var origin = _transform.GetWorldPosition(droneXform);
        var targetPos = _transform.GetWorldPosition(targetXform);
        var delta = targetPos - origin;
        var distanceSquared = delta.LengthSquared();
        if (distanceSquared <= 0.0001f || distanceSquared > drone.FireRange * drone.FireRange)
            return false;

        var distance = MathF.Sqrt(distanceSquared);
        return !HasFriendlyInFireLane(uid, skull, target, droneXform, origin, targetPos, distance);
    }

    private bool TryResolveCombatCoordinate(
        EntityUid uid,
        WH40KTauGunDroneComponent drone,
        WH40KServoSkullMobComponent skull,
        TransformComponent droneXform,
        EntityUid target,
        TransformComponent targetXform,
        Vector2 dronePosition,
        Vector2 targetPosition,
        int slot,
        int slotCount,
        bool preferSideStep,
        out EntityCoordinates coordinates)
    {
        coordinates = default;
        EntityCoordinates? fallback = null;
        var offsets = preferSideStep ? StrafeAngleOffsets : CombatAngleOffsets;

        foreach (var offset in offsets)
        {
            if (!TryResolveRingCoordinate(
                    targetXform.Coordinates,
                    skull.OwnerEntity,
                    dronePosition,
                    targetPosition,
                    drone.PreferredCombatRange,
                    slot,
                    slotCount,
                    out var candidate,
                    out var desiredPosition,
                    offset))
            {
                continue;
            }

            fallback ??= candidate;
            var distance = MathF.Max(0.1f, (targetPosition - desiredPosition).Length());
            if (!HasFriendlyInFireLane(uid, skull, target, droneXform, desiredPosition, targetPosition, distance))
            {
                coordinates = candidate;
                return true;
            }
        }

        if (fallback is { } destination)
        {
            coordinates = destination;
            return true;
        }

        return false;
    }

    private bool HasFriendlyInFireLane(
        EntityUid uid,
        WH40KServoSkullMobComponent skull,
        EntityUid target,
        TransformComponent droneXform,
        Vector2 origin,
        Vector2 targetPosition,
        float distance)
    {
        var lane = targetPosition - origin;
        var laneLengthSquared = lane.LengthSquared();
        if (laneLengthSquared <= 0.01f)
            return false;

        _laneCheck.Clear();
        _lookup.GetEntitiesInRange(
            droneXform.Coordinates,
            MathF.Max(distance + 2f, 6f),
            _laneCheck,
            LookupFlags.Dynamic | LookupFlags.Uncontained | LookupFlags.Approximate);

        foreach (var candidate in _laneCheck)
        {
            if (!IsFriendlyObstacle(uid, skull, target, candidate, droneXform.MapID) ||
                !TryComp(candidate, out TransformComponent? candidateXform))
            {
                continue;
            }

            var candidatePos = _transform.GetWorldPosition(candidateXform);
            var projection = Vector2.Dot(candidatePos - origin, lane) / laneLengthSquared;
            if (projection <= 0.05f || projection >= 0.95f)
                continue;

            var closest = origin + lane * projection;
            if (Vector2.DistanceSquared(candidatePos, closest) <= FriendlyFireLaneRadius * FriendlyFireLaneRadius)
                return true;
        }

        return false;
    }

    private bool IsFriendlyObstacle(
        EntityUid uid,
        WH40KServoSkullMobComponent skull,
        EntityUid target,
        EntityUid candidate,
        MapId mapId)
    {
        if (candidate == uid || candidate == target || TerminatingOrDeleted(candidate))
            return false;

        if (!TryComp(candidate, out TransformComponent? candidateXform) || candidateXform.MapID != mapId)
            return false;

        if (skull.OwnerEntity is { } owner && candidate == owner)
            return true;

        if (TryComp(candidate, out WH40KServoSkullMobComponent? otherSkull) &&
            skull.OwnerEntity != null &&
            otherSkull.OwnerEntity == skull.OwnerEntity)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(skull.TeamId) &&
            _teamRule.TryGetTeamIdFromEntity(candidate, out var teamId) &&
            string.Equals(teamId, skull.TeamId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryComp<NpcFactionMemberComponent>(uid, out var selfFaction) &&
               TryComp<NpcFactionMemberComponent>(candidate, out var otherFaction) &&
               _npcFactions.IsEntityFriendly(
                   (uid, selfFaction),
                   (candidate, otherFaction));
    }

    private bool IsUserAuthorized(EntityUid user, WH40KServoSkullMobComponent skull)
    {
        if (string.IsNullOrWhiteSpace(skull.TeamId))
            return false;

        return _teamRule.TryGetTeamIdFromEntity(user, out var teamId) &&
               string.Equals(teamId, skull.TeamId, StringComparison.OrdinalIgnoreCase);
    }

    private void ToggleFire(Entity<WH40KTauGunDroneComponent> ent, EntityUid user)
    {
        if (!TryComp<WH40KServoSkullMobComponent>(ent, out var skull) || !IsUserAuthorized(user, skull))
            return;

        ent.Comp.FireEnabled = !ent.Comp.FireEnabled;
        if (!ent.Comp.FireEnabled)
            ent.Comp.CurrentTarget = null;

        var loc = ent.Comp.FireEnabled ? PopupFireEnabledLoc : PopupFireDisabledLoc;
        _popup.PopupEntity(_culture.GetPlayerString(user, loc), ent.Owner, user, PopupType.Small);
    }

    private void EnsureOwnerController(EntityUid? owner)
    {
        if (owner == null || TerminatingOrDeleted(owner.Value))
            return;

        EnsureComp<WH40KTauDroneControllerComponent>(owner.Value);
    }

    private void RefreshOwnerController(EntityUid owner, EntityUid? excludingDrone = null)
    {
        if (TerminatingOrDeleted(owner))
            return;

        var query = EntityQueryEnumerator<WH40KTauGunDroneComponent, WH40KServoSkullMobComponent>();
        while (query.MoveNext(out var uid, out _, out var skull))
        {
            if (uid == excludingDrone)
                continue;

            if (skull.OwnerEntity == owner)
            {
                EnsureOwnerController(owner);
                return;
            }
        }

        if (HasComp<WH40KTauDroneControllerComponent>(owner))
            RemCompDeferred<WH40KTauDroneControllerComponent>(owner);
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
        steering.ArriveOnLineOfSight = false;
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
}
