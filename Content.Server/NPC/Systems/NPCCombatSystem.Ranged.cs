using System.Numerics;
using Content.Server._WH40K.Objectives.Components;
using Content.Server.NPC.Components;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly RotateToFaceSystem _rotate = default!;

    private EntityQuery<CombatModeComponent> _combatQuery;
    private EntityQuery<NPCSteeringComponent> _steeringQuery;
    private EntityQuery<RechargeBasicEntityAmmoComponent> _rechargeQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<NpcFactionMemberComponent> _factionQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;

    // TODO: Don't predict for hitscan
    private const float ShootSpeed = 20f;
    private const float FriendlyFireCorridorRadius = 0.75f;
    private const float FriendlyFireCheckPadding = 0.35f;
    private const float FriendlyFireSidestepDistance = 1.4f;

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
        _factionQuery = GetEntityQuery<NpcFactionMemberComponent>();
        _mobStateQuery = GetEntityQuery<MobStateComponent>();

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
    }

    private void UpdateRanged(float frameTime)
    {
        using var benchScope = _bench.Measure("npc.combat.ranged.update");
        var query = EntityQueryEnumerator<NPCRangedCombatComponent, TransformComponent>();
        var processed = 0;

        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            processed++;
            using var entityScope = _bench.Measure("npc.combat.ranged.entity");

            if (comp.Status == CombatStatus.Unspecified)
            {
                _bench.RecordCount("npc.combat.ranged.unspecified_skip", 1);
                continue;
            }

            if (_steeringQuery.TryGetComponent(uid, out var steering) && steering.Status == SteeringStatus.NoPath)
            {
                var objectiveOverride =
                    TryComp(comp.Target, out WH40KObjectiveComponent? objective) &&
                    objective is { Destroyed: false, Destroying: false };

                if (!objectiveOverride)
                {
                    comp.Status = CombatStatus.TargetUnreachable;
                    comp.ShootAccumulator = 0f;
                    _bench.RecordCount("npc.combat.ranged.target_unreachable", 1);
                    continue;
                }

                _bench.RecordCount("npc.combat.ranged.nopath_objective_override", 1);
            }

            if (!_xformQuery.TryGetComponent(comp.Target, out var targetXform) ||
                !_physicsQuery.TryGetComponent(comp.Target, out var targetBody))
            {
                comp.Status = CombatStatus.TargetUnreachable;
                comp.ShootAccumulator = 0f;
                _bench.RecordCount("npc.combat.ranged.target_invalid", 1);
                continue;
            }

            if (targetXform.MapID != xform.MapID)
            {
                comp.Status = CombatStatus.TargetUnreachable;
                comp.ShootAccumulator = 0f;
                _bench.RecordCount("npc.combat.ranged.target_wrong_map", 1);
                continue;
            }

            if (_combatQuery.TryGetComponent(uid, out var combatMode))
            {
                _combat.SetInCombatMode(uid, true, combatMode);
            }

            if (!_gun.TryGetGun(uid, out var gun))
            {
                comp.Status = CombatStatus.NoWeapon;
                comp.ShootAccumulator = 0f;
                _bench.RecordCount("npc.combat.ranged.no_weapon", 1);
                continue;
            }

            var ammoEv = new GetAmmoCountEvent();
            RaiseLocalEvent(gun, ref ammoEv);

            if (ammoEv.Count == 0)
            {
                // Recharging then?
                if (_rechargeQuery.HasComponent(gun))
                {
                    _bench.RecordCount("npc.combat.ranged.recharging", 1);
                    continue;
                }

                comp.Status = CombatStatus.Unspecified;
                comp.ShootAccumulator = 0f;
                _bench.RecordCount("npc.combat.ranged.no_ammo", 1);
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
                using var losScope = _bench.Measure("npc.combat.ranged.los_check");
                comp.LOSAccumulator += UnoccludedCooldown;

                // For consistency with NPC steering.
                var collisionGroup = comp.UseOpaqueForLOSChecks ? CollisionGroup.Opaque : (CollisionGroup.Impassable | CollisionGroup.InteractImpassable);
                comp.TargetInLOS = _interaction.InRangeUnobstructed(uid, comp.Target, distance + 0.1f, collisionGroup);
            }

            if (!comp.TargetInLOS)
            {
                comp.ShootAccumulator = 0f;
                comp.Status = CombatStatus.NotInSight;
                _bench.RecordCount("npc.combat.ranged.not_in_sight", 1);

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

            if (!oldInLos)
            {
                _waveComms.TryEnemySpotted(uid, comp.Target);
            }

            comp.ShootAccumulator += frameTime;

            if (comp.ShootAccumulator < comp.ShootDelay)
            {
                continue;
            }

            var mapVelocity = targetBody.LinearVelocity;
            var targetSpot = targetPos + mapVelocity * distance / ShootSpeed;

            if (IsFriendlyInLineOfFire(uid, comp.Target, xform.MapID, worldPos, targetSpot))
            {
                comp.Status = CombatStatus.NotInSight;
                _bench.RecordCount("npc.combat.ranged.friendly_fire_blocked", 1);

                if (TryComp(uid, out steering))
                {
                    steering.ForceMove = true;
                    TryRegisterFriendlyFireSidestep(uid, xform, worldPos, targetSpot);
                }

                continue;
            }

            // If we have a max rotation speed then do that.
            var goalRotation = (targetSpot - worldPos).ToWorldAngle();
            var rotationSpeed = comp.RotationSpeed;

            if (!_rotate.TryRotateTo(uid, goalRotation, frameTime, comp.AccuracyThreshold, rotationSpeed?.Theta ?? double.MaxValue, xform))
            {
                _bench.RecordCount("npc.combat.ranged.rotate_wait", 1);
                continue;
            }

            // TODO: LOS
            // TODO: Ammo checks
            // TODO: Burst fire
            // TODO: Cycling
            // Max rotation speed

            // TODO: Check if we can face

//            if (!Enabled || !_gun.CanShoot(gun))
//                continue;

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
                _bench.RecordCount("npc.combat.ranged.cooldown_blocked", 1);
                continue;
            }

            _bench.RecordCount("npc.combat.ranged.shoot_attempt", 1);
            var shotSucceeded = _gun.AttemptShoot(uid, gun, targetCordinates, comp.Target);
            if (shotSucceeded)
            {
                _bench.RecordCount("npc.combat.ranged.shoot_performed", 1);
                _waveComms.TryEngagingEnemy(uid, comp.Target);
            }
            else
            {
                _bench.RecordCount("npc.combat.ranged.shoot_failed", 1);
            }
        }

        _bench.RecordCount("npc.combat.ranged.entities", processed);
    }

    private bool IsFriendlyInLineOfFire(EntityUid shooter, EntityUid target, MapId mapId, Vector2 shotStart, Vector2 shotEnd)
    {
        if (!_factionQuery.TryGetComponent(shooter, out var shooterFaction))
            return false;

        var shotVector = shotEnd - shotStart;
        var shotLength = shotVector.Length();
        if (shotLength <= 0.05f)
            return false;

        var direction = shotVector / shotLength;
        var scanRange = shotLength + FriendlyFireCheckPadding;
        var mapCoords = new MapCoordinates(shotStart, mapId);

        foreach (var candidate in _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(mapCoords, scanRange))
        {
            var allyUid = candidate.Owner;
            if (allyUid == shooter || allyUid == target || TerminatingOrDeleted(allyUid))
                continue;

            var allyFaction = (NpcFactionMemberComponent?) candidate.Comp;
            var shooterFriendlyToAlly = _npcFaction.IsEntityFriendly((shooter, shooterFaction), (allyUid, allyFaction));
            var allyFriendlyToShooter = _npcFaction.IsEntityFriendly((allyUid, allyFaction), (shooter, shooterFaction));
            if (!shooterFriendlyToAlly && !allyFriendlyToShooter)
                continue;

            if (!_xformQuery.TryGetComponent(allyUid, out var allyXform) || allyXform.MapID != mapId)
                continue;

            if (_mobStateQuery.TryGetComponent(allyUid, out var allyState) &&
                allyState.CurrentState == MobState.Dead)
            {
                continue;
            }

            var allyPosition = _transform.GetWorldPosition(allyXform);
            if (IsPointInsideFireCorridor(shotStart, direction, shotLength, allyPosition, FriendlyFireCorridorRadius))
                return true;
        }

        return false;
    }

    private void TryRegisterFriendlyFireSidestep(EntityUid uid, TransformComponent xform, Vector2 shotStart, Vector2 shotEnd)
    {
        var shotVector = shotEnd - shotStart;
        var shotLength = shotVector.Length();
        if (shotLength <= 0.05f)
            return;

        var direction = shotVector / shotLength;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var side = (uid.GetHashCode() & 1) == 0 ? 1f : -1f;
        var sideStepWorld = shotStart + perpendicular * (FriendlyFireSidestepDistance * side);

        EntityCoordinates sideStepTarget;
        if (_mapManager.TryFindGridAt(xform.MapID, sideStepWorld, out var gridUid, out var mapGrid))
        {
            sideStepTarget = new EntityCoordinates(gridUid, _map.WorldToLocal(gridUid, mapGrid, sideStepWorld));
        }
        else if (xform.MapUid is { } mapUid)
        {
            sideStepTarget = new EntityCoordinates(mapUid, sideStepWorld);
        }
        else
        {
            return;
        }

        _steering.TryRegister(uid, sideStepTarget);
        _bench.RecordCount("npc.combat.ranged.friendly_fire_reposition", 1);
    }

    private static bool IsPointInsideFireCorridor(
        Vector2 shotStart,
        Vector2 direction,
        float shotLength,
        Vector2 point,
        float corridorRadius)
    {
        var relative = point - shotStart;
        var forward = Vector2.Dot(relative, direction);

        if (forward <= 0.05f || forward >= shotLength - 0.05f)
            return false;

        var closestPoint = shotStart + direction * forward;
        var lateralSq = (point - closestPoint).LengthSquared();
        return lateralSq <= corridorRadius * corridorRadius;
    }
}
