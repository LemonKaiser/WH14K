using Content.Shared.Coordinates.Helpers;
using Content.Shared.Interaction.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Combat;

public sealed partial class SharedWH40KBarbedWirePlacementSystem : EntitySystem
{
    [Dependency] private  SharedMapSystem _maps = default!;
    [Dependency] private  SharedStackSystem _stack = default!;
    [Dependency] private  TurfSystem _turf = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KDeployableBarbedWireComponent, HandheldEntityPlacementAttemptEvent>(OnWirePlacementAttempt);
        SubscribeLocalEvent<WH40KDeployableBarbedWireComponent, HandheldEntityPlacementCompleteEvent>(OnWirePlacementComplete);
    }

    private void OnWirePlacementAttempt(Entity<WH40KDeployableBarbedWireComponent> ent, ref HandheldEntityPlacementAttemptEvent args)
    {
        if (!TryComp(ent, out HandheldEntityPlacementComponent? placement))
            return;

        var placementDirection = NormalizeWireDirection(args.Direction);
        if (!TryGetWirePlacementTile(args.Coordinates, out var gridUid, out var grid, out var tileRef, out _))
        {
            args.Cancel();
            return;
        }

        if (IsWirePlacementSideOccupied(gridUid, grid, tileRef, placement.EntityType, placementDirection))
        {
            args.Cancel();
            return;
        }

        args.Direction = placementDirection;
        args.DeployDelay = ent.Comp.DeployTime;
        args.BreakOnDamage = true;
    }

    private void OnWirePlacementComplete(Entity<WH40KDeployableBarbedWireComponent> ent, ref HandheldEntityPlacementCompleteEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(ent, out HandheldEntityPlacementComponent? placement))
            return;

        if (!TryComp(ent, out StackComponent? stack))
            return;

        var placementDirection = NormalizeWireDirection(args.Direction);

        if (!TryGetWirePlacementTile(args.Coordinates, out var gridUid, out var grid, out var tileRef, out var snappedCoords))
            return;

        if (IsWirePlacementSideOccupied(gridUid, grid, tileRef, placement.EntityType, placementDirection))
            return;

        if (!_stack.TryUse((ent, stack), ent.Comp.StackCost))
            return;

        var wire = Spawn(placement.EntityType, snappedCoords);
        _transform.SetLocalRotation(wire, placementDirection.ToAngle());

        args.Handled = true;
    }

    private bool TryGetWirePlacementTile(
        EntityCoordinates location,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out TileRef tileRef,
        out EntityCoordinates snappedLocation)
    {
        snappedLocation = default;
        var gridEntity = _transform.GetGrid(location);
        if (gridEntity == null)
        {
            gridUid = default;
            grid = default!;
            tileRef = default;
            return false;
        }

        var gridEntityUid = gridEntity.Value;
        if (!TryComp<MapGridComponent>(gridEntityUid, out MapGridComponent? gridComp) || gridComp == null)
        {
            gridUid = default;
            grid = default!;
            tileRef = default;
            return false;
        }

        grid = gridComp;
        snappedLocation = location.SnapToGrid(gridComp);

        if (!_maps.TryGetTileRef(gridEntityUid, grid, snappedLocation, out tileRef))
        {
            gridUid = default;
            return false;
        }

        if (tileRef.Tile.IsEmpty || _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
        {
            gridUid = default;
            return false;
        }

        gridUid = gridEntityUid;
        return true;
    }

    private bool IsWirePlacementSideOccupied(
        EntityUid gridUid,
        MapGridComponent grid,
        TileRef tileRef,
        EntProtoId wirePrototype,
        Direction direction)
    {
        foreach (var anchored in _maps.GetAnchoredEntities((gridUid, grid), tileRef.GridIndices))
        {
            if (!Exists(anchored))
                continue;

            var anchoredPrototypeId = MetaData(anchored).EntityPrototype?.ID;
            if (anchoredPrototypeId == null || anchoredPrototypeId != wirePrototype.Id)
                continue;

            var existingDirection = _transform.GetWorldRotation(anchored).GetCardinalDir();
            if (existingDirection == direction)
                return true;
        }

        return false;
    }

    private static Direction NormalizeWireDirection(Direction direction)
    {
        if (direction == Direction.Invalid)
            return Direction.North;

        return direction.ToAngle().GetCardinalDir();
    }
}
