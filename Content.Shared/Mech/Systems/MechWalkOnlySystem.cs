using Content.Shared.Mech.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;

namespace Content.Shared.Mech.Systems;

/// <summary>
/// Keeps piloted mechs on a single walking gait so shift cannot turn them into sprinting mobs.
/// </summary>
public sealed class MechWalkOnlySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MechWalkOnlyComponent, MoveInputEvent>(OnMoveInput);
    }

    private void OnMoveInput(Entity<MechWalkOnlyComponent> ent, ref MoveInputEvent args)
    {
        if ((args.Entity.Comp.HeldMoveButtons & MoveButtons.Walk) != 0)
            return;

        args.Entity.Comp.HeldMoveButtons |= MoveButtons.Walk;
        args.Entity.Comp.CurTickWalkMovement += args.Entity.Comp.CurTickSprintMovement;
        args.Entity.Comp.CurTickSprintMovement = System.Numerics.Vector2.Zero;
        Dirty(ent.Owner, args.Entity.Comp);
    }
}
