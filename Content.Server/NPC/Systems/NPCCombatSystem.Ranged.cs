using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;

    private EntityQuery<CombatModeComponent> _combatQuery;
    private EntityQuery<NPCSteeringComponent> _steeringQuery;
    private EntityQuery<RechargeBasicEntityAmmoComponent> _rechargeQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    // TODO: Don't predict for hitscan
    private const float ShootSpeed = 20f;

    /// <summary>
    /// Cooldown on raycasting to check LOS.
    /// </summary>
    public const float UnoccludedCooldown = 0.2f;

    private void InitializeRanged()
    {
        _combatQuery = GetEntityQuery<CombatModeComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _rechargeQuery = GetEntityQuery<RechargeBasicEntityAmmoComponent>();
        _steeringQuery = GetEntityQuery<NPCSteeringComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<NPCRangedCombatComponent, ComponentStartup>(OnRangedStartup);
        SubscribeLocalEvent<NPCRangedCombatComponent, ComponentShutdown>(OnRangedShutdown);
    }

    private void OnRangedStartup(EntityUid uid, NPCRangedCombatComponent component, ComponentStartup args)
    {
        if (TryComp<CombatModeComponent>(uid, out var combat))
        {
            _combat.SetInCombatMode(uid, true, combat);
        }
        else
        {
            component.Status = CombatStatus.Unspecified;
        }
    }

    private void OnRangedShutdown(EntityUid uid, NPCRangedCombatComponent component, ComponentShutdown args)
    {
        if (TryComp<CombatModeComponent>(uid, out var combat))
        {
            _combat.SetInCombatMode(uid, false, combat);
        }

        component.FriendlyFireRepositionActive = false;
        component.FriendlyFireRepositionCoordinates = EntityCoordinates.Invalid;
        component.FriendlyFireBlockedBy = EntityUid.Invalid;
        component.FriendlyFireHadSteeringSnapshot = false;
        component.FriendlyFireSnapshotCoordinates = EntityCoordinates.Invalid;
        component.FriendlyFireSnapshotHasInRangeMaxSpeed = false;
    }

    private void UpdateRanged(float frameTime)
    {
        var query = EntityQueryEnumerator<NPCRangedCombatComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Status == CombatStatus.Unspecified)
                continue;

            if (_steeringQuery.TryGetComponent(uid, out var steering) && steering.Status == SteeringStatus.NoPath)
            {
                comp.Status = CombatStatus.TargetUnreachable;
                comp.ShootAccumulator = 0f;
                continue;
            }

            if (!_xformQuery.TryGetComponent(comp.Target, out var targetXform) ||
                !_physicsQuery.TryGetComponent(comp.Target, out var targetBody))
            {
                comp.Status = CombatStatus.TargetUnreachable;
                comp.ShootAccumulator = 0f;
                continue;
            }

            if (targetXform.MapID != xform.MapID)
            {
                comp.Status = CombatStatus.TargetUnreachable;
                comp.ShootAccumulator = 0f;
                continue;
            }

            comp.TargetCoordinates = targetXform.Coordinates;

            if (_combatQuery.TryGetComponent(uid, out var combatMode))
            {
                _combat.SetInCombatMode(uid, true, combatMode);
            }

            if (!_gun.TryGetGun(uid, out var gun))
            {
                comp.Status = CombatStatus.NoWeapon;
                comp.ShootAccumulator = 0f;
                continue;
            }

            var ammoEv = new GetAmmoCountEvent();
            RaiseLocalEvent(gun, ref ammoEv);

            if (ammoEv.Count == 0)
            {
                // Recharging then?
                if (_rechargeQuery.HasComponent(gun))
                {
                    continue;
                }

                comp.Status = CombatStatus.Unspecified;
                comp.ShootAccumulator = 0f;
                continue;
            }

            comp.LOSAccumulator -= frameTime;

            var worldPos = _transform.GetWorldPosition(xform);
            var targetPos = _transform.GetWorldPosition(targetXform);

            // We'll work out the projected spot of the target and shoot there instead of where they are.
            var distance = (targetPos - worldPos).Length();
            var oldInLos = comp.TargetInLOS;

            // TODO: Should be doing these raycasts in parallel
            // Ideally we'd have 2 steps, 1. to go over the normal details for shooting and then 2. to handle beep / rotate / shoot
            if (comp.LOSAccumulator < 0f)
            {
                comp.LOSAccumulator += UnoccludedCooldown;

                // For consistency with NPC steering.
                var collisionGroup = comp.UseOpaqueForLOSChecks ? CollisionGroup.Opaque : (CollisionGroup.Impassable | CollisionGroup.InteractImpassable);
                comp.TargetInLOS = _interaction.InRangeUnobstructed(uid, comp.Target, distance + 0.1f, collisionGroup);
            }

            if (!comp.TargetInLOS)
            {
                comp.ShootAccumulator = 0f;
                comp.Status = CombatStatus.NotInSight;
                RestoreFriendlyFireSteering(uid, comp, steering);

                if (TryComp(uid, out steering))
                {
                    steering.ForceMove = true;
                }

                continue;
            }

            if (!oldInLos && comp.SoundTargetInLOS != null)
            {
                _audio.PlayPvs(comp.SoundTargetInLOS, uid);
            }

            comp.ShootAccumulator += frameTime;

            if (comp.ShootAccumulator < comp.ShootDelay)
            {
                continue;
            }

            var mapVelocity = targetBody.LinearVelocity;
            var targetSpot = targetPos + mapVelocity * distance / ShootSpeed;

            // If we have a max rotation speed then do that.
            var goalRotation = (targetSpot - worldPos).ToWorldAngle();
            var rotationSpeed = comp.RotationSpeed;

            if (!_rotate.TryRotateTo(uid, goalRotation, frameTime, comp.AccuracyThreshold, rotationSpeed?.Theta ?? double.MaxValue, xform))
            {
                continue;
            }

            if (TryComp<NPCFriendlyFireAvoidanceComponent>(uid, out var friendlyFire) &&
                TryResolveFriendlyFireBlocker(uid, comp.Target, xform.MapID, worldPos, targetSpot, out var blocker))
            {
                comp.Status = CombatStatus.FriendlyFireBlocked;
                comp.FriendlyFireBlockedBy = blocker;

                if (TryResolveSafeFirePosition(uid, comp, friendlyFire, xform, worldPos, targetSpot, out var safeFirePosition))
                {
                    ApplyFriendlyFireReposition(uid, comp, friendlyFire, safeFirePosition, steering);
                }
                else
                {
                    ApplyFriendlyFireReposition(uid, comp, friendlyFire, comp.TargetCoordinates, steering);
                }

                continue;
            }

            RestoreFriendlyFireSteering(uid, comp, steering);

            // TODO: LOS
            // TODO: Ammo checks
            // TODO: Burst fire
            // TODO: Cycling
            // Max rotation speed

            // TODO: Check if we can face

            // This fork's gun system does not expose the upstream CanShoot helper.
            // AttemptShoot remains authoritative for final fire gating.
            if (!Enabled)
                continue;

            EntityCoordinates targetCordinates;

            if (_mapManager.TryFindGridAt(xform.MapID, targetPos, out var gridUid, out var mapGrid))
            {
                targetCordinates = new EntityCoordinates(gridUid, _map.WorldToLocal(gridUid, mapGrid, targetSpot));
            }
            else
            {
                targetCordinates = new EntityCoordinates(xform.MapUid!.Value, targetSpot);
            }

            comp.Status = CombatStatus.Normal;

            if (gun.Comp.NextFire > _timing.CurTime)
            {
                continue;
            }

            _gun.AttemptShoot(uid, gun, targetCordinates, comp.Target);
        }
    }

    private bool TryResolveFriendlyFireBlocker(
        EntityUid uid,
        EntityUid target,
        MapId mapId,
        Vector2 origin,
        Vector2 targetSpot,
        out EntityUid blocker)
    {
        blocker = EntityUid.Invalid;

        if (!TryComp<NpcFactionMemberComponent>(uid, out var ownerFaction))
            return false;

        var delta = targetSpot - origin;
        var length = delta.Length();
        if (mapId == MapId.Nullspace || length <= 0.05f)
            return false;

        var ray = new CollisionRay(origin, Vector2.Normalize(delta), (int) CollisionGroup.MobMask);
        foreach (var hit in _physics.IntersectRayWithPredicate(
                     mapId,
                     ray,
                     length,
                     entity => entity == uid || entity == target || Deleted(entity),
                     false))
        {
            if (!IsFriendlyFireBlocker((uid, ownerFaction), hit.HitEntity))
                continue;

            blocker = hit.HitEntity;
            return true;
        }

        return false;
    }

    private bool IsFriendlyFireBlocker(
        Entity<NpcFactionMemberComponent> owner,
        EntityUid candidate)
    {
        if (!TryComp<NpcFactionMemberComponent>(candidate, out var candidateFaction))
            return false;

        if (!_factions.IsEntityFriendly((owner.Owner, owner.Comp), (candidate, candidateFaction)))
            return false;

        if (TryComp<MobStateComponent>(candidate, out var mobState) &&
            mobState.CurrentState == MobState.Dead)
        {
            return false;
        }

        return true;
    }

    private bool TryResolveSafeFirePosition(
        EntityUid uid,
        NPCRangedCombatComponent combat,
        NPCFriendlyFireAvoidanceComponent friendlyFire,
        TransformComponent xform,
        Vector2 origin,
        Vector2 targetSpot,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;

        var delta = targetSpot - origin;
        var length = delta.Length();
        if (xform.MapID == MapId.Nullspace ||
            xform.MapUid == null ||
            length <= 0.05f)
        {
            return false;
        }

        var direction = Vector2.Normalize(delta);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var sidePreference = ResolveFriendlyFireSidePreference(origin, perpendicular, combat.FriendlyFireBlockedBy);
        var lateralOffsets = BuildFriendlyFireLateralOffsets(
            sidePreference,
            friendlyFire.RepositionDistance,
            friendlyFire.ExtendedRepositionDistance);
        var forwardOffsets = new[]
        {
            friendlyFire.ForwardOffset,
            0f,
            -friendlyFire.BackwardOffset,
        };

        foreach (var lateral in lateralOffsets)
        {
            foreach (var forward in forwardOffsets)
            {
                var candidatePosition = origin + perpendicular * lateral + direction * forward;
                if (!TryBuildWorldCoordinates(xform.MapID, xform.MapUid.Value, candidatePosition, out var candidateCoordinates) ||
                    _pathfinding.GetPoly(candidateCoordinates) == null ||
                    !HasHypotheticalLineOfSight(xform.MapID, candidatePosition, targetSpot, combat.UseOpaqueForLOSChecks, uid, combat.Target) ||
                    TryResolveFriendlyFireBlocker(uid, combat.Target, xform.MapID, candidatePosition, targetSpot, out _))
                {
                    continue;
                }

                coordinates = candidateCoordinates;
                return true;
            }
        }

        return false;
    }

    private static float[] BuildFriendlyFireLateralOffsets(float preferredSideSign, float nearDistance, float farDistance)
    {
        if (preferredSideSign > 0f)
        {
            return
            [
                nearDistance,
                farDistance,
                -nearDistance,
                -farDistance,
                nearDistance * 0.6f,
                -nearDistance * 0.6f,
            ];
        }

        if (preferredSideSign < 0f)
        {
            return
            [
                -nearDistance,
                -farDistance,
                nearDistance,
                farDistance,
                -nearDistance * 0.6f,
                nearDistance * 0.6f,
            ];
        }

        return
        [
            nearDistance,
            -nearDistance,
            farDistance,
            -farDistance,
            nearDistance * 0.6f,
            -nearDistance * 0.6f,
        ];
    }

    private float ResolveFriendlyFireSidePreference(Vector2 origin, Vector2 perpendicular, EntityUid blocker)
    {
        if (!blocker.IsValid() ||
            !_xformQuery.TryGetComponent(blocker, out var blockerXform))
        {
            return 0f;
        }

        var blockerWorld = _transform.GetWorldPosition(blockerXform);
        var lateral = Vector2.Dot(blockerWorld - origin, perpendicular);
        if (MathF.Abs(lateral) <= 0.05f)
            return 0f;

        return lateral > 0f ? -1f : 1f;
    }

    private bool HasHypotheticalLineOfSight(
        MapId mapId,
        Vector2 origin,
        Vector2 targetSpot,
        bool useOpaqueForLosChecks,
        EntityUid owner,
        EntityUid target)
    {
        var delta = targetSpot - origin;
        var length = delta.Length();
        if (mapId == MapId.Nullspace || length <= 0.05f)
            return false;

        var collisionGroup = useOpaqueForLosChecks
            ? CollisionGroup.Opaque
            : CollisionGroup.Impassable | CollisionGroup.InteractImpassable;
        var ray = new CollisionRay(origin, Vector2.Normalize(delta), (int) collisionGroup);

        foreach (var _ in _physics.IntersectRayWithPredicate(
                     mapId,
                     ray,
                     length,
                     entity => entity == owner || entity == target || Deleted(entity),
                     false))
        {
            return false;
        }

        return true;
    }

    private bool TryBuildWorldCoordinates(MapId mapId, EntityUid mapUid, Vector2 worldPosition, out EntityCoordinates coordinates)
    {
        if (_mapManager.TryFindGridAt(mapId, worldPosition, out var gridUid, out var mapGrid))
        {
            coordinates = new EntityCoordinates(gridUid, _map.WorldToLocal(gridUid, mapGrid, worldPosition));
            return coordinates.IsValid(EntityManager);
        }

        coordinates = new EntityCoordinates(mapUid, worldPosition);
        return coordinates.IsValid(EntityManager);
    }

    private void ApplyFriendlyFireReposition(
        EntityUid uid,
        NPCRangedCombatComponent combat,
        NPCFriendlyFireAvoidanceComponent friendlyFire,
        EntityCoordinates coordinates,
        NPCSteeringComponent? steering)
    {
        if (!combat.FriendlyFireRepositionActive)
            SnapshotFriendlyFireSteering(combat, steering);

        var resolvedSteering = _steering.Register(uid, coordinates, steering);
        resolvedSteering.Range = friendlyFire.ArrivalRange;
        resolvedSteering.DirectMove = false;
        resolvedSteering.ArriveOnLineOfSight = false;
        resolvedSteering.InRangeMaxSpeed = 0.08f;
        resolvedSteering.ForceMove = true;

        combat.FriendlyFireRepositionActive = true;
        combat.FriendlyFireRepositionCoordinates = coordinates;
    }

    private static void SnapshotFriendlyFireSteering(NPCRangedCombatComponent combat, NPCSteeringComponent? steering)
    {
        if (steering == null)
        {
            combat.FriendlyFireHadSteeringSnapshot = false;
            combat.FriendlyFireSnapshotCoordinates = EntityCoordinates.Invalid;
            combat.FriendlyFireSnapshotHasInRangeMaxSpeed = false;
            return;
        }

        combat.FriendlyFireHadSteeringSnapshot = true;
        combat.FriendlyFireSnapshotCoordinates = steering.Coordinates;
        combat.FriendlyFireSnapshotRange = steering.Range;
        combat.FriendlyFireSnapshotDirectMove = steering.DirectMove;
        combat.FriendlyFireSnapshotArriveOnLineOfSight = steering.ArriveOnLineOfSight;
        combat.FriendlyFireSnapshotHasInRangeMaxSpeed = steering.InRangeMaxSpeed != null;
        combat.FriendlyFireSnapshotInRangeMaxSpeed = steering.InRangeMaxSpeed ?? 0f;
    }

    private void RestoreFriendlyFireSteering(
        EntityUid uid,
        NPCRangedCombatComponent combat,
        NPCSteeringComponent? steering)
    {
        if (!combat.FriendlyFireRepositionActive)
        {
            combat.FriendlyFireBlockedBy = EntityUid.Invalid;
            return;
        }

        if (steering != null)
        {
            if (combat.FriendlyFireHadSteeringSnapshot &&
                combat.FriendlyFireSnapshotCoordinates.IsValid(EntityManager))
            {
                var restoredSteering = _steering.Register(uid, combat.FriendlyFireSnapshotCoordinates, steering);
                restoredSteering.Range = combat.FriendlyFireSnapshotRange;
                restoredSteering.DirectMove = combat.FriendlyFireSnapshotDirectMove;
                restoredSteering.ArriveOnLineOfSight = combat.FriendlyFireSnapshotArriveOnLineOfSight;
                restoredSteering.InRangeMaxSpeed = combat.FriendlyFireSnapshotHasInRangeMaxSpeed
                    ? combat.FriendlyFireSnapshotInRangeMaxSpeed
                    : null;
                restoredSteering.ForceMove = false;
            }
            else
            {
                _steering.Unregister(uid, steering);
            }
        }

        combat.FriendlyFireRepositionActive = false;
        combat.FriendlyFireRepositionCoordinates = EntityCoordinates.Invalid;
        combat.FriendlyFireBlockedBy = EntityUid.Invalid;
        combat.FriendlyFireHadSteeringSnapshot = false;
        combat.FriendlyFireSnapshotCoordinates = EntityCoordinates.Invalid;
        combat.FriendlyFireSnapshotHasInRangeMaxSpeed = false;
    }
}
