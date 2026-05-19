using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Climbing;
using Content.Shared.CombatMode;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.NPC;
using Robust.Shared.Physics;
using Robust.Shared.Utility;
using ClimbableComponent = Content.Shared.Climbing.Components.ClimbableComponent;
using ClimbingComponent = Content.Shared.Climbing.Components.ClimbingComponent;
using Robust.Shared.Random;

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
        if ((poly.Data.CollisionLayer & mask) == 0x0 &&
            (poly.Data.CollisionMask & layer) == 0x0)
        {
            ClearActionableObstacle(component);
            return SteeringObstacleStatus.Completed;
        }

        var id = component.DoAfterId;

        // Still doing what we were doing before.
        var doAfterStatus = _doAfter.GetStatus(id);

        switch (doAfterStatus)
        {
            case DoAfterStatus.Running:
                MarkActionableObstacle(component, component.ActiveObstacle, component.ActiveObstacleMode, progress: true);
                return SteeringObstacleStatus.Continuing;
            case DoAfterStatus.Cancelled:
                ClearActionableObstacle(component);
                return SteeringObstacleStatus.Failed;
        }

        var obstacleEnts = _entSetPool.Get();

        try
        {
            _lookup.GetLocalEntitiesIntersecting(poly.GraphUid, poly.Box, obstacleEnts, flags: LookupFlags.Dynamic | LookupFlags.Static);
            FilterObstacleEntities((uid, component), mask, layer, obstacleEnts);

            if (obstacleEnts.Count == 0)
            {
                ClearActionableObstacle(component);
                return SteeringObstacleStatus.Completed;
            }

            var isDoor = (poly.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0;
            var isClimbable = (poly.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

            // Just walk into it stupid
            if (isDoor)
            {
                foreach (var ent in obstacleEnts)
                {
                    if (!CanHandleDoor((uid, component), poly.Data.Flags, ent, allowPrying: false))
                        continue;

                    _interaction.InteractionActivate(uid, ent);
                    MarkActionableObstacle(component, ent, "InteractDoor", progress: true);
                    return SteeringObstacleStatus.Continuing;
                }

                if ((component.Flags & PathFlags.Prying) != 0x0)
                {
                    foreach (var ent in obstacleEnts)
                    {
                        if (!CanHandleDoor((uid, component), poly.Data.Flags, ent))
                            continue;

                        if (_pryingSystem.TryPry(ent, uid, out id, uid) && id != null)
                        {
                            component.DoAfterId = id;
                            MarkActionableObstacle(component, ent, "PryDoor", progress: true);
                            return SteeringObstacleStatus.Continuing;
                        }
                    }
                }

                if ((component.Flags & PathFlags.Smashing) != 0x0)
                    return TrySmashObstacle(uid, component, obstacleEnts, "SmashDoor");

                ClearActionableObstacle(component);
                return SteeringObstacleStatus.Failed;
            }
            // Try climbing obstacles
            else if ((component.Flags & PathFlags.Climbing) != 0x0 && isClimbable)
            {
                if (!TryComp<ClimbingComponent>(uid, out var climbing))
                    return SteeringObstacleStatus.Failed;

                if (climbing.IsClimbing)
                {
                    MarkActionableObstacle(component, component.ActiveObstacle, "Climb", progress: true);
                    return SteeringObstacleStatus.Completed;
                }

                if (climbing.NextTransition != null)
                {
                    MarkActionableObstacle(component, component.ActiveObstacle, "Climb", progress: true);
                    return SteeringObstacleStatus.Continuing;
                }

                foreach (var ent in obstacleEnts)
                {
                    if (CanHandleClimb((uid, climbing), ent, out var climbable) &&
                        _climb.TryClimb(uid, uid, ent, out id, climbable, climbing))
                    {
                        component.DoAfterId = id;
                        MarkActionableObstacle(component, ent, "Climb", progress: true);
                        return SteeringObstacleStatus.Continuing;
                    }
                }
            }
            // Try smashing obstacles.
            else if ((component.Flags & PathFlags.Smashing) != 0x0)
            {
                return TrySmashObstacle(uid, component, obstacleEnts, "SmashObstacle");
            }

            ClearActionableObstacle(component);
            return SteeringObstacleStatus.Failed;
        }
        finally
        {
            _entSetPool.Return(obstacleEnts);
        }
    }

    private SteeringObstacleStatus TrySmashObstacle(
        EntityUid uid,
        NPCSteeringComponent component,
        IEnumerable<EntityUid> obstacleEnts,
        string mode)
    {
        if (!_melee.TryGetWeapon(uid, out var weaponUid, out var meleeWeapon) ||
            !TryComp<CombatModeComponent>(uid, out var combatMode))
        {
            ClearActionableObstacle(component);
            return SteeringObstacleStatus.Failed;
        }

        var shuffledEnts = obstacleEnts.ToList();
        _random.Shuffle(shuffledEnts);
        var smashTarget = shuffledEnts.FirstOrDefault(ent => _destructibleQuery.HasComponent(ent));
        if (smashTarget == EntityUid.Invalid)
        {
            ClearActionableObstacle(component);
            return SteeringObstacleStatus.Failed;
        }

        MarkActionableObstacle(component, smashTarget, mode, progress: meleeWeapon.NextAttack <= _timing.CurTime);

        if (meleeWeapon.NextAttack > _timing.CurTime)
            return SteeringObstacleStatus.Continuing;

        _combat.SetInCombatMode(uid, true, combatMode);
        var attackResult = _melee.AttemptLightAttack(uid, weaponUid, meleeWeapon, smashTarget);
        _combat.SetInCombatMode(uid, false, combatMode);

        if (attackResult)
        {
            MarkActionableObstacle(component, smashTarget, mode, progress: true);
            return SteeringObstacleStatus.Continuing;
        }

        return SteeringObstacleStatus.Continuing;
    }

    private void MarkActionableObstacle(
        NPCSteeringComponent component,
        EntityUid obstacle,
        string mode,
        bool progress = false)
    {
        component.ActionableObstacle = true;
        if (obstacle.IsValid())
            component.ActiveObstacle = obstacle;

        component.ActiveObstacleMode = mode;
        component.LastObstacleSeenAt = _timing.CurTime;

        if (progress)
            component.LastObstacleProgressAt = _timing.CurTime;
    }

    private static void ClearActionableObstacle(NPCSteeringComponent component)
    {
        component.ActionableObstacle = false;
        component.ActiveObstacle = EntityUid.Invalid;
        component.ActiveObstacleMode = string.Empty;
        component.LastObstacleSeenAt = TimeSpan.Zero;
        component.LastObstacleProgressAt = TimeSpan.Zero;
    }

    private enum SteeringObstacleStatus : byte
    {
        Completed,
        Failed,
        Continuing
    }
}
