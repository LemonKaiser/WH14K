using Content.Server.NPC.HTN;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server.Combat;

/// <summary>
/// Resolves the entity ultimately responsible for damage when the immediate origin is a projectile,
/// held item, mech equipment, thrown item, timer-triggered entity, or nested child entity.
/// </summary>
public sealed class CombatAttackerResolverSystem : EntitySystem
{
    private const int MaxResolveDepth = 6;

    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly VehicleSystem _vehicle = default!;

    public bool TryResolveResponsibleEntity(EntityUid origin, out EntityUid responsible)
    {
        return TryResolveResponsibleEntity(origin, out responsible, 0);
    }

    public bool TryResolveAttacker(EntityUid origin, out EntityUid attacker)
    {
        return TryResolveResponsibleEntity(origin, out attacker);
    }

    public bool TryResolveAttacker(EntityUid origin, out EntityUid attacker, out ActorComponent? attackerActor)
    {
        if (!TryResolveResponsibleEntity(origin, out attacker))
        {
            attackerActor = null;
            return false;
        }

        attackerActor = CompOrNull<ActorComponent>(attacker);
        return true;
    }

    private bool TryResolveResponsibleEntity(EntityUid origin, out EntityUid responsible, int depth)
    {
        responsible = default;

        if (depth > MaxResolveDepth)
            return false;

        if (TryComp<ActorComponent>(origin, out _ ) || HasComp<HTNComponent>(origin))
        {
            responsible = origin;
            return true;
        }

        if (TryResolveMechPilot(origin, out responsible))
            return true;

        if (TryComp(origin, out MechEquipmentComponent? mechEquipment) &&
            mechEquipment.EquipmentOwner is { } mechOwner &&
            TryResolveMechPilot(mechOwner, out responsible))
        {
            return true;
        }

        if (TryComp<ProjectileComponent>(origin, out var projectile) &&
            projectile.Shooter is { } shooter &&
            shooter != origin)
        {
            return TryResolveResponsibleEntity(shooter, out responsible, depth + 1);
        }

        if (TryComp<ThrownItemComponent>(origin, out var thrown) &&
            thrown.Thrower is { } thrower &&
            thrower != origin)
        {
            return TryResolveResponsibleEntity(thrower, out responsible, depth + 1);
        }

        if (TryComp<TimerTriggerComponent>(origin, out var timer) &&
            timer.User is { } timerUser &&
            timerUser != origin)
        {
            return TryResolveResponsibleEntity(timerUser, out responsible, depth + 1);
        }

        if (TryResolveResponsibleEntityFromContainer(origin, out responsible, depth + 1))
            return true;

        if (TryResolveResponsibleEntityFromParents(origin, out responsible, depth + 1))
            return true;

        return false;
    }

    private bool TryResolveResponsibleEntityFromContainer(EntityUid origin, out EntityUid responsible, int depth)
    {
        responsible = default;

        var current = origin;
        for (var i = 0; i < MaxResolveDepth; i++)
        {
            if (!_container.TryGetContainingContainer((current, null, null), out var container))
                return false;

            var owner = container.Owner;
            if (!owner.IsValid() || owner == current)
                return false;

            if (TryResolveResponsibleEntity(owner, out responsible, depth + i))
                return true;

            current = owner;
        }

        return false;
    }

    private bool TryResolveResponsibleEntityFromParents(EntityUid origin, out EntityUid responsible, int depth)
    {
        responsible = default;

        var current = origin;
        for (var i = 0; i < MaxResolveDepth; i++)
        {
            if (!TryComp(current, out TransformComponent? xform))
                return false;

            var parent = xform.ParentUid;
            if (!parent.IsValid() || parent == current)
                return false;

            if (TryResolveResponsibleEntity(parent, out responsible, depth + i))
                return true;

            current = parent;
        }

        return false;
    }

    private bool TryResolveMechPilot(EntityUid mech, out EntityUid pilot)
    {
        pilot = default;

        if (!TryComp(mech, out MechComponent? mechComp))
            return false;

        if (_vehicle.GetOperatorOrNull((mech, CompOrNull<VehicleComponent>(mech))) is not { } pilotEntity)
            return false;

        if (!TryComp<ActorComponent>(pilotEntity, out _) && !HasComp<HTNComponent>(pilotEntity))
            return false;

        pilot = pilotEntity;
        return true;
    }
}
