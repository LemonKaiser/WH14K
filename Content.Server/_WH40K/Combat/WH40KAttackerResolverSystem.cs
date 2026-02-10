using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Combat;

/// <summary>
/// Resolves a damage origin entity to the player attacker when possible.
/// </summary>
public sealed class WH40KAttackerResolverSystem : EntitySystem
{
    private const int MaxResolveDepth = 6;

    [Dependency] private readonly SharedContainerSystem _container = default!;

    public bool TryResolveAttacker(EntityUid origin, out EntityUid attacker)
    {
        return TryResolveAttacker(origin, out attacker, out _);
    }

    public bool TryResolveAttacker(EntityUid origin, out EntityUid attacker, out ActorComponent attackerActor)
    {
        return TryResolveAttacker(origin, out attacker, out attackerActor, 0);
    }

    private bool TryResolveAttacker(EntityUid origin, out EntityUid attacker, out ActorComponent attackerActor, int depth)
    {
        attacker = default;
        attackerActor = default!;

        if (depth > MaxResolveDepth)
            return false;

        if (TryComp(origin, out ActorComponent? actor))
        {
            attacker = origin;
            attackerActor = actor;
            return true;
        }

        if (TryResolveMechPilot(origin, out attacker, out attackerActor))
            return true;

        if (TryComp(origin, out MechEquipmentComponent? mechEquipment) &&
            mechEquipment.EquipmentOwner is { } mechOwner &&
            TryResolveMechPilot(mechOwner, out attacker, out attackerActor))
        {
            return true;
        }

        if (TryComp<ProjectileComponent>(origin, out var projectile) &&
            projectile.Shooter is { } shooter)
        {
            if (shooter == origin)
                return false;

            return TryResolveAttacker(shooter, out attacker, out attackerActor, depth + 1);
        }

        if (TryComp<ThrownItemComponent>(origin, out var thrown) &&
            thrown.Thrower is { } thrower)
        {
            if (thrower == origin)
                return false;

            return TryResolveAttacker(thrower, out attacker, out attackerActor, depth + 1);
        }

        if (TryComp<TimerTriggerComponent>(origin, out var timer) &&
            timer.User is { } timerUser)
        {
            if (timerUser == origin)
                return false;

            return TryResolveAttacker(timerUser, out attacker, out attackerActor, depth + 1);
        }

        if (TryResolveAttackerFromContainer(origin, out attacker, out attackerActor))
            return true;

        if (TryResolveAttackerFromParents(origin, out attacker, out attackerActor))
            return true;

        return false;
    }

    private bool TryResolveAttackerFromContainer(EntityUid origin, out EntityUid attacker, out ActorComponent attackerActor)
    {
        attacker = default;
        attackerActor = default!;

        var current = origin;
        for (var i = 0; i < MaxResolveDepth; i++)
        {
            if (!_container.TryGetContainingContainer((current, null, null), out var container))
                return false;

            var owner = container.Owner;
            if (!owner.IsValid() || owner == current)
                return false;

            if (TryComp(owner, out ActorComponent? ownerActor))
            {
                attacker = owner;
                attackerActor = ownerActor;
                return true;
            }

            if (TryResolveMechPilot(owner, out attacker, out attackerActor))
                return true;

            current = owner;
        }

        return false;
    }

    private bool TryResolveAttackerFromParents(EntityUid origin, out EntityUid attacker, out ActorComponent attackerActor)
    {
        attacker = default;
        attackerActor = default!;

        var current = origin;
        for (var i = 0; i < MaxResolveDepth; i++)
        {
            if (!TryComp(current, out TransformComponent? xform))
                return false;

            var parent = xform.ParentUid;
            if (!parent.IsValid() || parent == current)
                return false;

            if (TryComp(parent, out ActorComponent? actor))
            {
                attacker = parent;
                attackerActor = actor;
                return true;
            }

            if (TryResolveMechPilot(parent, out attacker, out attackerActor))
                return true;

            current = parent;
        }

        return false;
    }

    private bool TryResolveMechPilot(EntityUid mech, out EntityUid pilot, out ActorComponent pilotActor)
    {
        pilot = default;
        pilotActor = default!;

        if (!TryComp(mech, out MechComponent? mechComp))
            return false;

        if (mechComp.PilotSlot.ContainedEntity is not { } pilotEntity)
            return false;

        if (!TryComp(pilotEntity, out ActorComponent? actor))
            return false;

        pilot = pilotEntity;
        pilotActor = actor;
        return true;
    }
}
