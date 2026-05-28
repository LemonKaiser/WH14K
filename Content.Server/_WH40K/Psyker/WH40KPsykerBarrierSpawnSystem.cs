using System.Collections.Generic;
using Content.Shared.Magic;
using Content.Shared.Magic.Events;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Handles directional shield placement for WH40K psyker barrier actions.
/// The vanilla force wall uses square walls and does not need rotation,
/// but shield barricades must face the caster to form proper cover lines.
/// </summary>
public sealed partial class WH40KPsykerBarrierSpawnSystem : EntitySystem
{
    private const string ImperialAegisAction = "ActionWH40KPsykerAegisWall";
    private const string ChaosTzeentchBarrierAction = "ActionWH40KChaosTzeentchBarrier";
    private const string ChaosUndividedAegisAction = "ActionWH40KChaosUndividedAegis";

    [Dependency] private  SharedMapSystem _mapSystem = default!;
    [Dependency] private  TurfSystem _turf = default!;
    [Dependency] private  SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<InstantSpawnSpellEvent>(
            OnInstantBarrierSpawn,
            after: [typeof(WH40KChaosTzeentchGiftAbilitySystem)],
            before: [typeof(SharedMagicSystem)]);
    }

    private void OnInstantBarrierSpawn(InstantSpawnSpellEvent args)
    {
        if (args.Handled)
            return;

        var actionPrototype = MetaData(args.Action.Owner).EntityPrototype?.ID;
        if (actionPrototype != ImperialAegisAction &&
            actionPrototype != ChaosTzeentchBarrierAction &&
            actionPrototype != ChaosUndividedAegisAction)
        {
            return;
        }

        if (!TryGetBarrierLineSpawns(args.Performer, out var positions, out var barrierDirection))
            return;

        foreach (var position in positions)
        {
            var barrier = Spawn(args.Prototype, position);
            _transform.SetLocalRotation(barrier, barrierDirection.ToAngle());

            if (!args.PreventCollideWithCaster)
                continue;

            var preventCollide = EnsureComp<PreventCollideComponent>(barrier);
            preventCollide.Uid = args.Performer;
        }

        args.Handled = true;
    }

    private bool TryGetBarrierLineSpawns(EntityUid performer, out List<EntityCoordinates> positions, out Direction barrierDirection)
    {
        positions = new List<EntityCoordinates>();
        barrierDirection = Direction.Invalid;

        var casterXform = Transform(performer);
        if (casterXform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var mapGrid))
        {
            return false;
        }

        var directionPos = casterXform.Coordinates.Offset(casterXform.LocalRotation.ToWorldVec().Normalized());
        if (!_turf.TryGetTileRef(directionPos, out var tileReference))
            return false;

        var casterDirection = casterXform.LocalRotation.GetCardinalDir();
        barrierDirection = casterDirection.GetOpposite();

        var tileIndex = tileReference.Value.GridIndices;
        positions.Add(_mapSystem.GridTileToLocal(gridUid, mapGrid, tileIndex));

        switch (casterDirection)
        {
            case Direction.North:
            case Direction.South:
                positions.Add(_mapSystem.GridTileToLocal(gridUid, mapGrid, tileIndex + (1, 0)));
                positions.Add(_mapSystem.GridTileToLocal(gridUid, mapGrid, tileIndex + (-1, 0)));
                return true;

            case Direction.East:
            case Direction.West:
                positions.Add(_mapSystem.GridTileToLocal(gridUid, mapGrid, tileIndex + (0, 1)));
                positions.Add(_mapSystem.GridTileToLocal(gridUid, mapGrid, tileIndex + (0, -1)));
                return true;

            default:
                return false;
        }
    }
}
