using System.Numerics;
using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.CombatMode;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.NPC;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;
using ClimbableComponent = Content.Shared.Climbing.Components.ClimbableComponent;
using ClimbingComponent = Content.Shared.Climbing.Components.ClimbingComponent;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    /*
     * For any custom path handlers, e.g. destroying walls, opening airlocks, etc.
     * Putting it onto steering seemed easier than trying to make a custom compound task for it.
     * I also considered task interrupts although the problem is handling stuff like pathfinding overlaps
     * Ideally we could do interrupts but that's TODO.
     */

    /*
     * TODO:
     * - Add path cap
     * - Circle cast BFS in LOS to determine targets.
     * - Store last known coordinates of X targets.
     * - Require line of sight for melee
     * - Add new behavior where they move to melee target's last known position (diffing theirs and current)
     *  then do the thing like from dishonored where it gets passed to a search system that opens random stuff.
     *
     * Also need to make sure it picks nearest obstacle path so it starts smashing in front of it.
     */

    [Dependency] private EntityQuery<DoorComponent> _doorQuery = default!;
    [Dependency] private EntityQuery<ClimbableComponent> _climbableQuery = default!;
    [Dependency] private EntityQuery<DestructibleComponent> _destructibleQuery = default!;
    [Dependency] private DestructibleSystem _destructible = default!;

    private static readonly TimeSpan ObstacleClaimDuration = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan GroupObstacleActionDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan GroupClimbActionDuration = TimeSpan.FromSeconds(1.25);

    private readonly Dictionary<ObstacleClaimKey, ObstacleClaim> _obstacleClaims = new();
    private readonly Dictionary<EntityUid, ObstacleClaimKey> _actorObstacleClaims = new();
    private readonly Dictionary<string, List<GroupObstacleAction>> _groupObstacleActions = new();

    private SteeringObstacleStatus TryHandleFlags(EntityUid uid, NPCSteeringComponent component, PathPoly poly)
    {
        DebugTools.Assert(!poly.Data.IsFreeSpace);
        // TODO: Store PathFlags on the steering comp
        // and be able to re-check it.

        var layer = 0;
        var mask = 0;

        if (TryComp<FixturesComponent>(uid, out var manager))
        {
            (layer, mask) = _physics.GetHardCollision(uid, manager);
        }
        else
        {
            return SteeringObstacleStatus.Failed;
        }

        // TODO: Should cache the fact we're doing this somewhere.
        // See https://github.com/space-wizards/space-station-14/issues/11475
        var coordinateObstacles = TryGetCollectiveGroup(uid, out var group) && group.CoordinateObstacles;

        if ((poly.Data.CollisionLayer & mask) != 0x0 ||
            (poly.Data.CollisionMask & layer) != 0x0)
        {
            var id = component.DoAfterId;

            // Still doing what we were doing before.
            var doAfterStatus = _doAfter.GetStatus(id);

            switch (doAfterStatus)
            {
                case DoAfterStatus.Running:
                    RefreshGroupObstacleAction(uid);
                    return SteeringObstacleStatus.Continuing;
                case DoAfterStatus.Cancelled:
                    ReleaseObstacleClaims(uid);
                    ReleaseGroupObstacleAction(uid);
                    return SteeringObstacleStatus.Failed;
            }

            if (coordinateObstacles &&
                group.WaitForGroupObstacle &&
                ShouldWaitForGroupObstacleAction(uid, group))
            {
                return SteeringObstacleStatus.Continuing;
            }

            var obstacleEnts = new List<EntityUid>();
            GetObstacleEntities(poly, mask, layer, component.Radius, obstacleEnts);

            if (obstacleEnts.Count == 0)
                return SteeringObstacleStatus.Completed;

            SortObstacleEntities(uid, obstacleEnts);

            var isDoor = (poly.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0;
            var isClimbable = (poly.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

            if (isDoor)
            {
                var doorStatus = TryHandleDoorObstacles(uid, component, obstacleEnts);
                if (doorStatus != SteeringObstacleStatus.Failed)
                    return doorStatus;
            }

            if (isClimbable && (component.Flags & PathFlags.Climbing) != 0x0)
            {
                var climbStatus = TryClimbObstacles(uid, component, obstacleEnts);
                if (climbStatus != SteeringObstacleStatus.Failed)
                    return climbStatus;
            }

            if ((component.Flags & PathFlags.Smashing) != 0x0)
                return TrySmashObstacles(uid, obstacleEnts, coordinateObstacles ? group : null);

            return SteeringObstacleStatus.Failed;
        }

        return SteeringObstacleStatus.Completed;
    }

    private SteeringObstacleStatus TryHandleDoorObstacles(
        EntityUid uid,
        NPCSteeringComponent component,
        List<EntityUid> obstacleEnts)
    {
        var sawDoor = false;
        var hasNonDoorObstacle = false;
        var blockedDoors = new List<EntityUid>();
        var hasOpeningDoor = false;
        var coordinateObstacles = TryGetCollectiveGroup(uid, out var group) && group.CoordinateObstacles;

        foreach (var ent in obstacleEnts)
        {
            if (!_doorQuery.TryGetComponent(ent, out var door))
            {
                hasNonDoorObstacle = true;
                continue;
            }

            sawDoor = true;

            if (door.State == DoorState.Open)
            {
                if (coordinateObstacles)
                    ReleaseObstacleClaim(uid, ent, group.GroupId);

                continue;
            }

            if (door.State == DoorState.Opening)
            {
                hasOpeningDoor = true;
                continue;
            }

            blockedDoors.Add(ent);
        }

        if (!sawDoor)
            return SteeringObstacleStatus.Failed;

        if (blockedDoors.Count == 0)
        {
            if (hasOpeningDoor)
                return SteeringObstacleStatus.Completed;

            return hasNonDoorObstacle
                ? SteeringObstacleStatus.Failed
                : SteeringObstacleStatus.Completed;
        }

        if ((component.Flags & PathFlags.Interact) != 0x0)
        {
            foreach (var ent in blockedDoors)
            {
                if (!_doorQuery.TryGetComponent(ent, out var door) ||
                    door.State == DoorState.Welded ||
                    door.State is DoorState.Open or DoorState.Opening)
                {
                    continue;
                }

                if (coordinateObstacles &&
                    IsObstacleClaimedByOther(uid, ent, group.GroupId))
                    return SteeringObstacleStatus.Continuing;

                if (coordinateObstacles &&
                    !TryClaimObstacle(uid, ent, group.GroupId))
                    return SteeringObstacleStatus.Continuing;

                if (coordinateObstacles &&
                    !TryStartGroupObstacleAction(uid, ent, group, GroupObstacleActionKind.Door))
                    return SteeringObstacleStatus.Continuing;

                if (_doorSystem.TryOpen(ent, door, uid, predicted: true, quiet: true))
                    return SteeringObstacleStatus.Completed;
            }
        }

        if ((component.Flags & PathFlags.Prying) == 0x0)
        {
            if (coordinateObstacles)
            {
                foreach (var ent in blockedDoors)
                {
                    ReleaseObstacleClaim(uid, ent, group.GroupId);
                }
            }

            return SteeringObstacleStatus.Failed;
        }

        foreach (var ent in blockedDoors)
        {
            if (!_doorQuery.TryGetComponent(ent, out var door) ||
                door.State is DoorState.Open or DoorState.Opening)
            {
                continue;
            }

            if (coordinateObstacles &&
                IsObstacleClaimedByOther(uid, ent, group.GroupId))
                return SteeringObstacleStatus.Continuing;

            if (coordinateObstacles &&
                !TryClaimObstacle(uid, ent, group.GroupId))
                return SteeringObstacleStatus.Continuing;

            if (coordinateObstacles &&
                !TryStartGroupObstacleAction(uid, ent, group, GroupObstacleActionKind.Pry))
                return SteeringObstacleStatus.Continuing;

            if (TryStartPry(uid, ent, component))
                return SteeringObstacleStatus.Continuing;

            if (coordinateObstacles)
            {
                ReleaseObstacleClaim(uid, ent, group.GroupId);
                ReleaseGroupObstacleAction(uid);
            }
        }

        return SteeringObstacleStatus.Failed;
    }

    private bool TryStartPry(EntityUid uid, EntityUid target, NPCSteeringComponent component)
    {
        if (TryGetPryingTool(uid, out var tool) &&
            _pryingSystem.TryPry(target, uid, out var id, tool) &&
            id != null)
        {
            component.DoAfterId = id;
            return true;
        }

        if (_pryingSystem.TryPry(target, uid, out id) && id != null)
        {
            component.DoAfterId = id;
            return true;
        }

        return false;
    }

    private bool TryGetPryingTool(EntityUid uid, out EntityUid tool)
    {
        tool = EntityUid.Invalid;

        if (_hands.TryGetActiveItem(uid, out var activeItem) &&
            activeItem is { } active &&
            _pryingQuery.HasComponent(active))
        {
            tool = active;
            return true;
        }

        if (!_handsQuery.TryGetComponent(uid, out var hands))
        {
            if (_pryingQuery.HasComponent(uid))
            {
                tool = uid;
                return true;
            }

            return false;
        }

        foreach (var item in _inventory.GetHandOrInventoryEntities((uid, hands, null)))
        {
            if (!_pryingQuery.HasComponent(item))
                continue;

            if (_hands.TryPickupAnyHand(uid, item, checkActionBlocker: false, animate: false, handsComp: hands) ||
                IsHeldBy(uid, hands, item))
            {
                tool = item;
                return true;
            }
        }

        if (_pryingQuery.HasComponent(uid))
        {
            tool = uid;
            return true;
        }

        return false;
    }

    private bool IsHeldBy(EntityUid uid, HandsComponent hands, EntityUid item)
    {
        foreach (var hand in _hands.EnumerateHands((uid, hands)))
        {
            if (_hands.TryGetHeldItem((uid, hands), hand, out var held) &&
                held == item)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldWaitForGroupObstacleAction(EntityUid actor, NPCGroupComponent group)
    {
        if (!_groupObstacleActions.TryGetValue(group.GroupId, out var actions))
            return false;

        RemoveExpiredGroupObstacleActions(group.GroupId, actions);

        if (!TryGetActorCoordinates(actor, out var actorCoordinates))
            return false;

        var actorRadius = GetWorkGroupRadius(group);

        foreach (var action in actions)
        {
            if (action.Actor == actor)
                continue;

            if (GroupActionZonesOverlap(actorCoordinates, actorRadius, action.Coordinates, action.Radius))
                return true;
        }

        return false;
    }

    private bool TryStartGroupObstacleAction(
        EntityUid actor,
        EntityUid obstacle,
        NPCGroupComponent group,
        GroupObstacleActionKind kind)
    {
        if (!TryGetGroupObstacleActionCoordinates(actor, obstacle, out var coordinates))
            return false;

        var now = _timing.CurTime;
        var radius = GetWorkGroupRadius(group);
        var duration = kind == GroupObstacleActionKind.Climb
            ? GroupClimbActionDuration
            : GroupObstacleActionDuration;

        if (!_groupObstacleActions.TryGetValue(group.GroupId, out var actions))
        {
            actions = new List<GroupObstacleAction>();
            _groupObstacleActions[group.GroupId] = actions;
        }

        RemoveExpiredGroupObstacleActions(group.GroupId, actions);

        if (!_groupObstacleActions.ContainsKey(group.GroupId))
            _groupObstacleActions[group.GroupId] = actions;

        foreach (var action in actions)
        {
            if (action.Actor == actor)
            {
                action.Obstacle = obstacle;
                action.Kind = kind;
                action.Coordinates = coordinates;
                action.Radius = radius;
                action.Expires = now + duration;
                return true;
            }

            if (GroupActionZonesOverlap(coordinates, radius, action.Coordinates, action.Radius))
                return false;
        }

        actions.Add(new GroupObstacleAction(actor, obstacle, kind, coordinates, radius, now + duration));
        return true;
    }

    private void RefreshGroupObstacleAction(EntityUid actor)
    {
        if (!TryGetCollectiveGroup(actor, out var group) ||
            !_groupObstacleActions.TryGetValue(group.GroupId, out var actions))
        {
            return;
        }

        RemoveExpiredGroupObstacleActions(group.GroupId, actions);

        foreach (var action in actions)
        {
            if (action.Actor != actor)
                continue;

            var duration = action.Kind == GroupObstacleActionKind.Climb
                ? GroupClimbActionDuration
                : GroupObstacleActionDuration;

            if (TryGetGroupObstacleActionCoordinates(actor, action.Obstacle, out var coordinates))
                action.Coordinates = coordinates;

            action.Radius = GetWorkGroupRadius(group);
            action.Expires = _timing.CurTime + duration;
            return;
        }
    }

    private void ReleaseGroupObstacleAction(EntityUid actor)
    {
        if (!TryGetCollectiveGroup(actor, out var group) ||
            !_groupObstacleActions.TryGetValue(group.GroupId, out var actions))
        {
            return;
        }

        for (var i = actions.Count - 1; i >= 0; i--)
        {
            if (actions[i].Actor == actor)
                actions.RemoveAt(i);
        }

        if (actions.Count == 0)
            _groupObstacleActions.Remove(group.GroupId);
    }

    private void RemoveExpiredGroupObstacleActions(string groupId, List<GroupObstacleAction> actions)
    {
        for (var i = actions.Count - 1; i >= 0; i--)
        {
            if (IsGroupObstacleActionDone(actions[i]))
                actions.RemoveAt(i);
        }

        if (actions.Count == 0 &&
            _groupObstacleActions.TryGetValue(groupId, out var current) &&
            ReferenceEquals(current, actions))
        {
            _groupObstacleActions.Remove(groupId);
        }
    }

    private float GetWorkGroupRadius(NPCGroupComponent group)
    {
        return MathF.Max(0.1f, group.WorkGroupRadius);
    }

    private bool TryGetActorCoordinates(EntityUid actor, out MapCoordinates coordinates)
    {
        coordinates = default;

        if (Deleted(actor) ||
            !_xformQuery.TryGetComponent(actor, out var xform))
        {
            return false;
        }

        coordinates = _transform.GetMapCoordinates(actor, xform: xform);
        return true;
    }

    private bool TryGetGroupObstacleActionCoordinates(
        EntityUid actor,
        EntityUid obstacle,
        out MapCoordinates coordinates)
    {
        coordinates = default;

        if (!Deleted(obstacle) &&
            _xformQuery.TryGetComponent(obstacle, out var obstacleXform))
        {
            coordinates = _transform.GetMapCoordinates(obstacle, xform: obstacleXform);
            return true;
        }

        return TryGetActorCoordinates(actor, out coordinates);
    }

    private bool GroupActionZonesOverlap(
        MapCoordinates aCoordinates,
        float aRadius,
        MapCoordinates bCoordinates,
        float bRadius)
    {
        if (aCoordinates.MapId != bCoordinates.MapId)
            return false;

        var radius = aRadius + bRadius;
        return Vector2.DistanceSquared(aCoordinates.Position, bCoordinates.Position) <= radius * radius;
    }

    private bool IsGroupObstacleActionDone(GroupObstacleAction action)
    {
        if (Deleted(action.Actor) ||
            !HasComp<NPCSteeringComponent>(action.Actor) ||
            Deleted(action.Obstacle))
        {
            return true;
        }

        if (action.Expires <= _timing.CurTime)
            return true;

        switch (action.Kind)
        {
            case GroupObstacleActionKind.Door:
            case GroupObstacleActionKind.Pry:
                return !_doorQuery.TryGetComponent(action.Obstacle, out var door) ||
                       door.State == DoorState.Open;
            case GroupObstacleActionKind.Smash:
                return !_destructibleQuery.TryGetComponent(action.Obstacle, out var destructible) ||
                       _destructible.DestroyedAt(action.Obstacle, destructible).Float() <= 0f;
            case GroupObstacleActionKind.Climb:
                return TryComp<ClimbingComponent>(action.Actor, out var climbing) &&
                       climbing.IsClimbing &&
                       climbing.NextTransition == null;
            default:
                return true;
        }
    }

    private bool TryClaimObstacle(EntityUid actor, EntityUid obstacle, string groupId)
    {
        var now = _timing.CurTime;
        var key = new ObstacleClaimKey(groupId, obstacle);

        if (_obstacleClaims.TryGetValue(key, out var claim))
        {
            if (claim.Actor == actor)
            {
                claim.Expires = now + ObstacleClaimDuration;
                return true;
            }

            if (claim.Expires > now &&
                !Deleted(claim.Actor) &&
                HasComp<NPCSteeringComponent>(claim.Actor))
            {
                return false;
            }

            _obstacleClaims.Remove(key);
            _actorObstacleClaims.Remove(claim.Actor);
        }

        if (_actorObstacleClaims.TryGetValue(actor, out var previousKey) &&
            previousKey != key)
        {
            _obstacleClaims.Remove(previousKey);
        }

        _obstacleClaims[key] = new ObstacleClaim(actor, now + ObstacleClaimDuration);
        _actorObstacleClaims[actor] = key;
        return true;
    }

    private bool IsObstacleClaimedByOther(EntityUid actor, EntityUid obstacle, string groupId)
    {
        var key = new ObstacleClaimKey(groupId, obstacle);

        if (!_obstacleClaims.TryGetValue(key, out var claim))
            return false;

        if (claim.Actor == actor)
            return false;

        if (claim.Expires > _timing.CurTime &&
            !Deleted(claim.Actor) &&
            HasComp<NPCSteeringComponent>(claim.Actor))
        {
            return true;
        }

        _obstacleClaims.Remove(key);
        _actorObstacleClaims.Remove(claim.Actor);
        return false;
    }

    private void ReleaseObstacleClaim(EntityUid actor, EntityUid obstacle, string groupId)
    {
        var key = new ObstacleClaimKey(groupId, obstacle);

        if (!_obstacleClaims.TryGetValue(key, out var claim) ||
            claim.Actor != actor)
        {
            return;
        }

        _obstacleClaims.Remove(key);
        _actorObstacleClaims.Remove(actor);
    }

    private void ReleaseObstacleClaims(EntityUid actor)
    {
        if (!_actorObstacleClaims.Remove(actor, out var key))
            return;

        _obstacleClaims.Remove(key);
    }

    private SteeringObstacleStatus TryClimbObstacles(
        EntityUid uid,
        NPCSteeringComponent component,
        List<EntityUid> obstacleEnts)
    {
        var coordinateObstacles = TryGetCollectiveGroup(uid, out var group) && group.CoordinateObstacles;

        if (!TryComp<ClimbingComponent>(uid, out var climbing) || !climbing.CanClimb)
            return SteeringObstacleStatus.Failed;

        if (climbing.IsClimbing)
        {
            if (coordinateObstacles && climbing.NextTransition == null)
                ReleaseGroupObstacleAction(uid);

            return SteeringObstacleStatus.Completed;
        }

        if (climbing.NextTransition != null)
        {
            if (coordinateObstacles)
                RefreshGroupObstacleAction(uid);

            return SteeringObstacleStatus.Continuing;
        }

        if (IsClimbRouteBlocked(uid, component, obstacleEnts))
            return SteeringObstacleStatus.Failed;

        foreach (var ent in obstacleEnts)
        {
            if (!_climbableQuery.TryGetComponent(ent, out var climbable) ||
                !climbable.Vaultable ||
                !_climb.CanVault(climbable, uid, uid, out _))
            {
                continue;
            }

            if (coordinateObstacles &&
                !TryStartGroupObstacleAction(uid, ent, group, GroupObstacleActionKind.Climb))
            {
                return SteeringObstacleStatus.Continuing;
            }

            if (!_climb.TryClimb(uid, uid, ent, out var id, climbable, climbing))
            {
                if (coordinateObstacles)
                    ReleaseGroupObstacleAction(uid);

                continue;
            }

            component.DoAfterId = id;
            return SteeringObstacleStatus.Continuing;
        }

        return SteeringObstacleStatus.Failed;
    }

    private bool IsClimbRouteBlocked(EntityUid uid, NPCSteeringComponent component, List<EntityUid> obstacleEnts)
    {
        var path = component.CurrentPath.ToArray();
        if (path.Length == 0)
            return false;

        var nextCoordinates = path.Length > 1
            ? path[1].Coordinates
            : component.Coordinates;

        var start = _transform.GetMapCoordinates(uid);
        var end = _transform.ToMapCoordinates(nextCoordinates);

        if (start.MapId != end.MapId)
            return true;

        var offset = end.Position - start.Position;
        if (offset.LengthSquared() <= 0.01f)
            return false;

        var mask = GetMovementCollisionMask(uid);
        if (mask == 0)
            return false;

        var filter = new QueryFilter
        {
            MaskBits = mask,
            Flags = QueryFlags.Dynamic | QueryFlags.Static,
            IsIgnored = entity => entity == uid ||
                                HasComp<PathfindingIgnoredComponent>(entity) ||
                                Deleted(entity),
        };

        var shape = new PhysShapeCircle(Math.Clamp(component.Radius + 0.05f, 0.1f, 0.6f));
        var result = _rayCast.CastShape(
            start.MapId,
            shape,
            new Transform(start.Position, Angle.Zero),
            offset,
            filter,
            RayCastSystem.RayCastAllCallback);

        foreach (var hit in result.Results)
        {
            var ent = hit.Entity;

            if (!_physicsQuery.TryGetComponent(ent, out var body) ||
                !body.Hard ||
                !body.CanCollide)
            {
                continue;
            }

            if (obstacleEnts.Contains(ent) &&
                _climbableQuery.TryGetComponent(ent, out var climbable) &&
                climbable.Vaultable)
            {
                continue;
            }

            if (_doorQuery.TryGetComponent(ent, out var door) &&
                door.State is DoorState.Open or DoorState.Opening)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private SteeringObstacleStatus TrySmashObstacles(EntityUid uid, List<EntityUid> obstacleEnts, NPCGroupComponent? group)
    {
        if (!TryGetSmashWeapon(uid, out var weaponUid, out var meleeWeapon) ||
            meleeWeapon == null ||
            !TryComp<CombatModeComponent>(uid, out var combatMode))
        {
            return SteeringObstacleStatus.Failed;
        }

        if (meleeWeapon.NextAttack > _timing.CurTime)
            return SteeringObstacleStatus.Continuing;

        foreach (var ent in obstacleEnts)
        {
            if (!_destructibleQuery.TryGetComponent(ent, out var destructible) ||
                _destructible.DestroyedAt(ent, destructible).Float() <= 0f)
            {
                continue;
            }

            if (group != null &&
                !TryStartGroupObstacleAction(uid, ent, group, GroupObstacleActionKind.Smash))
            {
                return SteeringObstacleStatus.Continuing;
            }

            _combat.SetInCombatMode(uid, true, combatMode);
            var attackResult = _melee.AttemptLightAttack(uid, weaponUid, meleeWeapon, ent);
            _combat.SetInCombatMode(uid, false, combatMode);

            if (!attackResult && group != null)
                ReleaseGroupObstacleAction(uid);

            return attackResult
                ? SteeringObstacleStatus.Continuing
                : SteeringObstacleStatus.Failed;
        }

        return SteeringObstacleStatus.Failed;
    }

    private bool TryGetSmashWeapon(EntityUid uid, out EntityUid weaponUid, out MeleeWeaponComponent? meleeWeapon)
    {
        if (_melee.TryGetWeapon(uid, out weaponUid, out meleeWeapon))
            return true;

        if (TrySelectHeldMeleeWeapon(uid, out weaponUid, out meleeWeapon))
            return true;

        if (TryEquipInventoryMeleeWeapon(uid, out weaponUid, out meleeWeapon))
            return true;

        if (TryComp(uid, out meleeWeapon))
        {
            weaponUid = uid;
            return true;
        }

        weaponUid = default;
        meleeWeapon = null;
        return false;
    }

    private bool TrySelectHeldMeleeWeapon(EntityUid uid, out EntityUid weaponUid, out MeleeWeaponComponent? meleeWeapon)
    {
        weaponUid = default;
        meleeWeapon = null;

        if (!_handsQuery.TryGetComponent(uid, out var hands))
            return false;

        foreach (var held in _hands.EnumerateHeld((uid, hands)))
        {
            if (!TryComp(held, out meleeWeapon) || meleeWeapon.MustBeEquippedToUse)
                continue;

            _hands.TrySelect(uid, held);
            weaponUid = held;
            return true;
        }

        return false;
    }

    private bool TryEquipInventoryMeleeWeapon(EntityUid uid, out EntityUid weaponUid, out MeleeWeaponComponent? meleeWeapon)
    {
        weaponUid = default;
        meleeWeapon = null;

        if (!_handsQuery.TryGetComponent(uid, out var hands) ||
            !TryComp<InventoryComponent>(uid, out var inventory))
        {
            return false;
        }

        foreach (var slot in inventory.Slots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot.Name, out var item, inventory) ||
                !TryComp(item.Value, out meleeWeapon) ||
                meleeWeapon.MustBeEquippedToUse)
            {
                continue;
            }

            _inventory.TryUnequip(uid, uid, slot.Name, out _, silent: true, force: true);

            var pickedUp =
                _hands.TryPickupAnyHand(uid, item.Value, checkActionBlocker: false, animateUser: false, animate: false, handsComp: hands) ||
                _hands.TryForcePickupAnyHand(uid, item.Value, checkActionBlocker: false);

            if (!pickedUp)
                continue;

            _hands.TrySelect(uid, item.Value);
            weaponUid = item.Value;
            return true;
        }

        return false;
    }

    private void SortObstacleEntities(EntityUid uid, List<EntityUid> ents)
    {
        var origin = _transform.GetMapCoordinates(uid);

        ents.Sort((a, b) =>
        {
            var aDistance = GetObstacleDistance(origin, a);
            var bDistance = GetObstacleDistance(origin, b);
            return aDistance.CompareTo(bDistance);
        });
    }

    private float GetObstacleDistance(MapCoordinates origin, EntityUid ent)
    {
        var xform = Transform(ent);
        var coordinates = _transform.GetMapCoordinates(ent, xform: xform);
        if (coordinates.MapId != origin.MapId)
            return float.MaxValue;

        return Vector2.DistanceSquared(origin.Position, coordinates.Position);
    }

    private void GetObstacleEntities(PathPoly poly, int mask, int layer, float radius, List<EntityUid> ents)
    {
        var intersecting = _entSetPool.Get();
        _lookup.GetLocalEntitiesIntersecting(
            poly.GraphUid,
            poly.Box.Enlarged(MathF.Max(radius, 0.1f)),
            intersecting,
            flags: LookupFlags.Static);

        foreach (var ent in intersecting)
        {
            if (HasComp<PathfindingIgnoredComponent>(ent))
                continue;

            if (!_physicsQuery.TryGetComponent(ent, out var body) ||
                !body.Hard ||
                !body.CanCollide ||
                (body.CollisionMask & layer) == 0x0 && (body.CollisionLayer & mask) == 0x0)
            {
                continue;
            }

            ents.Add(ent);
        }

        _entSetPool.Return(intersecting);
    }

    private enum SteeringObstacleStatus : byte
    {
        Completed,
        Failed,
        Continuing
    }

    private enum GroupObstacleActionKind : byte
    {
        Door,
        Pry,
        Smash,
        Climb
    }

    private readonly record struct ObstacleClaimKey(string GroupId, EntityUid Obstacle);

    private sealed class ObstacleClaim(EntityUid actor, TimeSpan expires)
    {
        public readonly EntityUid Actor = actor;
        public TimeSpan Expires = expires;
    }

    private sealed class GroupObstacleAction(
        EntityUid actor,
        EntityUid obstacle,
        GroupObstacleActionKind kind,
        MapCoordinates coordinates,
        float radius,
        TimeSpan expires)
    {
        public readonly EntityUid Actor = actor;
        public EntityUid Obstacle = obstacle;
        public GroupObstacleActionKind Kind = kind;
        public MapCoordinates Coordinates = coordinates;
        public float Radius = radius;
        public TimeSpan Expires = expires;
    }
}
