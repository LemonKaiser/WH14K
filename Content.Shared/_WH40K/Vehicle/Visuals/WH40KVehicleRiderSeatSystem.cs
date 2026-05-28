using Content.Shared.Buckle.Components;
using Content.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;

namespace Content.Shared._WH40K.Vehicle.Visuals;

public sealed partial class WH40KVehicleRiderSeatSystem : EntitySystem
{
    private const int RiderSuppressedCollisionMask = (int) (CollisionGroup.Impassable |
                                                            CollisionGroup.MidImpassable |
                                                            CollisionGroup.HighImpassable |
                                                            CollisionGroup.LowImpassable);

    [Dependency] private  SharedPhysicsSystem _physics = default!;
    [Dependency] private  VehicleSystem _vehicle = default!;

    private EntityQuery<BuckleComponent> _buckleQuery;
    private EntityQuery<VehicleComponent> _vehicleQuery;
    private EntityQuery<WH40KVehicleRiderSeatComponent> _riderSeatQuery;

    public override void Initialize()
    {
        _buckleQuery = GetEntityQuery<BuckleComponent>();
        _vehicleQuery = GetEntityQuery<VehicleComponent>();
        _riderSeatQuery = GetEntityQuery<WH40KVehicleRiderSeatComponent>();

        SubscribeLocalEvent<WH40KVehicleRiderSeatComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<WH40KVehicleRiderSeatComponent, StrappedEvent>(OnStrapped, after: [typeof(VehicleSystem)]);
        SubscribeLocalEvent<WH40KVehicleRiderSeatComponent, UnstrappedEvent>(OnUnstrapped, after: [typeof(VehicleSystem)]);
        SubscribeLocalEvent<WH40KVehicleRiderSeatComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStrapAttempt(Entity<WH40KVehicleRiderSeatComponent> ent, ref StrapAttemptEvent args)
    {
        PruneInvalidOccupants(ent);

        if (ent.Comp.SeatOffsets.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        if (ent.Comp.SeatOccupants.Contains(args.Buckle.Owner))
            return;

        if (ent.Comp.SeatOccupants.Count >= ent.Comp.SeatOffsets.Count)
            args.Cancelled = true;
    }

    private void OnStrapped(Entity<WH40KVehicleRiderSeatComponent> ent, ref StrappedEvent args)
    {
        PruneInvalidOccupants(ent);

        if (!ent.Comp.SeatOccupants.Contains(args.Buckle.Owner))
        {
            ent.Comp.SeatOccupants.Add(args.Buckle.Owner);
            Dirty(ent);
        }

        ApplyRiderCollisionSuppression(args.Buckle.Owner);
        RefreshOperator(ent);
    }

    private void OnUnstrapped(Entity<WH40KVehicleRiderSeatComponent> ent, ref UnstrappedEvent args)
    {
        if (ent.Comp.SeatOccupants.Remove(args.Buckle.Owner))
            Dirty(ent);

        RestoreRiderCollisionSuppression(args.Buckle.Owner);
        RefreshOperator(ent);
    }

    private void OnShutdown(Entity<WH40KVehicleRiderSeatComponent> ent, ref ComponentShutdown args)
    {
        foreach (var occupant in ent.Comp.SeatOccupants)
        {
            RestoreRiderCollisionSuppression(occupant);
        }

        ent.Comp.SeatOccupants.Clear();
    }

    private void RefreshOperator(Entity<WH40KVehicleRiderSeatComponent> ent)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        PruneInvalidOccupants(ent);

        if (!_vehicleQuery.TryComp(ent.Owner, out var vehicle))
            return;

        foreach (var occupant in ent.Comp.SeatOccupants)
        {
            if (!IsValidOccupant(ent.Owner, occupant))
                continue;

            _vehicle.TrySetOperator((ent.Owner, vehicle), occupant);
            return;
        }

        _vehicle.TryRemoveOperator((ent.Owner, vehicle));
    }

    private void PruneInvalidOccupants(Entity<WH40KVehicleRiderSeatComponent> ent)
    {
        var removed = false;

        for (var i = ent.Comp.SeatOccupants.Count - 1; i >= 0; i--)
        {
            if (IsValidOccupant(ent.Owner, ent.Comp.SeatOccupants[i]))
                continue;

            RestoreRiderCollisionSuppression(ent.Comp.SeatOccupants[i]);
            ent.Comp.SeatOccupants.RemoveAt(i);
            removed = true;
        }

        if (removed)
            Dirty(ent);
    }

    private bool IsValidOccupant(EntityUid vehicle, EntityUid occupant)
    {
        return !TerminatingOrDeleted(occupant) &&
               _buckleQuery.TryComp(occupant, out var buckle) &&
               buckle.BuckledTo == vehicle;
    }

    private void ApplyRiderCollisionSuppression(EntityUid rider)
    {
        if (!TryComp<FixturesComponent>(rider, out var fixtures) ||
            !TryComp<PhysicsComponent>(rider, out var body))
        {
            return;
        }

        var suppressed = EnsureComp<WH40KVehicleRiderCollisionSuppressedComponent>(rider);
        suppressed.OriginalMasks.Clear();

        foreach (var (fixtureId, fixture) in fixtures.Fixtures)
        {
            suppressed.OriginalMasks[fixtureId] = fixture.CollisionMask;
            _physics.SetCollisionMask(
                rider,
                fixtureId,
                fixture,
                fixture.CollisionMask & ~RiderSuppressedCollisionMask,
                fixtures,
                body);
        }
    }

    private void RestoreRiderCollisionSuppression(EntityUid rider)
    {
        if (!TryComp<WH40KVehicleRiderCollisionSuppressedComponent>(rider, out var suppressed))
            return;

        if (TryComp<FixturesComponent>(rider, out var fixtures) &&
            TryComp<PhysicsComponent>(rider, out var body))
        {
            foreach (var (fixtureId, originalMask) in suppressed.OriginalMasks)
            {
                if (!fixtures.Fixtures.TryGetValue(fixtureId, out var fixture))
                    continue;

                _physics.SetCollisionMask(rider, fixtureId, fixture, originalMask, fixtures, body);
            }
        }

        RemCompDeferred<WH40KVehicleRiderCollisionSuppressedComponent>(rider);
    }
}
