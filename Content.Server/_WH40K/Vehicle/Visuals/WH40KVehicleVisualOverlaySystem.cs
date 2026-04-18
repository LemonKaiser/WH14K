using System.Numerics;
using Content.Shared._WH40K.Vehicle.Visuals;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Robust.Shared.Map;

namespace Content.Server._WH40K.Vehicle.Visuals;

public sealed class WH40KVehicleVisualOverlaySystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KVehicleVisualOverlayComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WH40KVehicleVisualOverlayComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KVehicleVisualOverlayComponent, SpriteMoveEvent>(OnSpriteMove);
    }

    private void OnMapInit(Entity<WH40KVehicleVisualOverlayComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.OverlayEntity != null)
            return;

        ent.Comp.OverlayEntity = Spawn(ent.Comp.Overlay, new EntityCoordinates(ent.Owner, Vector2.Zero));
    }

    private void OnSpriteMove(Entity<WH40KVehicleVisualOverlayComponent> ent, ref SpriteMoveEvent args)
    {
        if (ent.Comp.OverlayEntity is not { } overlay ||
            TerminatingOrDeleted(overlay) ||
            !HasComp<SpriteMovementComponent>(overlay))
        {
            return;
        }

        var ev = new SpriteMoveEvent(args.IsMoving);
        RaiseLocalEvent(overlay, ref ev);
    }

    private void OnShutdown(Entity<WH40KVehicleVisualOverlayComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.OverlayEntity is { } overlay && !TerminatingOrDeleted(overlay))
            QueueDel(overlay);

        ent.Comp.OverlayEntity = null;
    }
}
