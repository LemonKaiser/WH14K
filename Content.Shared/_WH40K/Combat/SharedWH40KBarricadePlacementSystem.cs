using Content.Shared.Coordinates.Helpers;
using Content.Shared.Interaction.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics;

namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Handheld deployment flow for WH40K barricade kits:
/// validates tile, waits deploy time, spawns configured barricade entity and consumes one kit item.
/// </summary>
public sealed partial class SharedWH40KBarricadePlacementSystem : EntitySystem
{
    [Dependency] private  EntityLookupSystem _lookup = default!;
    [Dependency] private  SharedMapSystem _maps = default!;
    [Dependency] private  SharedStackSystem _stack = default!;
    [Dependency] private  TurfSystem _turf = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KDeployableBarricadeComponent, HandheldEntityPlacementAttemptEvent>(OnPlacementAttempt);
        SubscribeLocalEvent<WH40KDeployableBarricadeComponent, HandheldEntityPlacementCompleteEvent>(OnPlacementComplete);
    }

    private void OnPlacementAttempt(Entity<WH40KDeployableBarricadeComponent> ent, ref HandheldEntityPlacementAttemptEvent args)
    {
        if (!TryComp(ent, out HandheldEntityPlacementComponent? _))
            return;

        if (!TryGetPlacementTile(args.Coordinates, out _, out _, out var tileRef, out var snappedCoords))
        {
            args.Cancel();
            return;
        }

        var placementDirection = NormalizeDirection(args.Direction);
        if (!CanPlaceBarricade(tileRef, placementDirection))
        {
            args.Cancel();
            return;
        }

        args.Coordinates = snappedCoords;
        args.Direction = placementDirection;
        args.DeployDelay = ent.Comp.DeployTime;
        args.BreakOnDamage = true;
    }

    private void OnPlacementComplete(Entity<WH40KDeployableBarricadeComponent> ent, ref HandheldEntityPlacementCompleteEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(ent, out HandheldEntityPlacementComponent? placement))
            return;

        var placementDirection = NormalizeDirection(args.Direction);

        if (!TryGetPlacementTile(args.Coordinates, out _, out _, out var tileRef, out var snappedCoords))
            return;

        if (!CanPlaceBarricade(tileRef, placementDirection))
            return;

        if (TryComp(ent, out StackComponent? stack))
        {
            if (!_stack.TryUse((ent, stack), ent.Comp.StackCost))
                return;
        }
        else
        {
            QueueDel(ent.Owner);
        }

        var barricade = Spawn(placement.EntityType, snappedCoords);
        _transform.SetLocalRotation(barricade, placementDirection.ToAngle());

        args.Handled = true;
    }

    private bool TryGetPlacementTile(
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

        gridUid = gridEntityUid;
        return true;
    }

    private bool CanPlaceBarricade(TileRef tileRef, Direction direction)
    {
        if (tileRef.Tile.IsEmpty)
            return false;

        if (!_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
            return true;

        return IsOccupiedOnlyByCompatibleBarricades(tileRef, direction);
    }

    private bool IsOccupiedOnlyByCompatibleBarricades(TileRef tileRef, Direction direction)
    {
        var foundBarricade = false;

        foreach (var entity in _lookup.GetEntitiesInTile(tileRef, LookupFlags.Dynamic | LookupFlags.Static))
        {
            if (!TryComp<FixturesComponent>(entity, out var fixtures) || !BlocksMobPlacement(fixtures))
                continue;

            if (!HasComp<WH40KDirectionalBarricadeComponent>(entity))
                return false;

            foundBarricade = true;
            var existingDirection = _transform.GetWorldRotation(entity).GetCardinalDir();
            if (existingDirection == direction)
                return false;
        }

        return foundBarricade;
    }

    private static bool BlocksMobPlacement(FixturesComponent fixtures)
    {
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            if ((fixture.CollisionLayer & (int) CollisionGroup.MobMask) != 0)
                return true;
        }

        return false;
    }

    private static Direction NormalizeDirection(Direction direction)
    {
        if (direction == Direction.Invalid)
            return Direction.North;

        return direction.ToAngle().GetCardinalDir();
    }
}
