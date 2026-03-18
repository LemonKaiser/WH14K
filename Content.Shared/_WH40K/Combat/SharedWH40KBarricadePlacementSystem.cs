using Content.Shared.Coordinates.Helpers;
using Content.Shared.Interaction.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Handheld deployment flow for WH40K barricade kits:
/// validates tile, waits deploy time, spawns configured barricade entity and consumes one kit item.
/// </summary>
public sealed class SharedWH40KBarricadePlacementSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

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

        if (!IsTileClear(tileRef))
        {
            args.Cancel();
            return;
        }

        args.Coordinates = snappedCoords;
        args.Direction = NormalizeDirection(args.Direction);
        args.DeployDelay = ent.Comp.DeployTime;
        args.BreakOnDamage = true;
    }

    private void OnPlacementComplete(Entity<WH40KDeployableBarricadeComponent> ent, ref HandheldEntityPlacementCompleteEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(ent, out HandheldEntityPlacementComponent? placement))
            return;

        if (!TryGetPlacementTile(args.Coordinates, out _, out _, out var tileRef, out var snappedCoords))
            return;

        if (!IsTileClear(tileRef))
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
        _transform.SetLocalRotation(barricade, NormalizeDirection(args.Direction).ToAngle());

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

    private bool IsTileClear(TileRef tileRef)
    {
        return !tileRef.Tile.IsEmpty && !_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask);
    }

    private static Direction NormalizeDirection(Direction direction)
    {
        if (direction == Direction.Invalid)
            return Direction.North;

        return direction.ToAngle().GetCardinalDir();
    }
}
