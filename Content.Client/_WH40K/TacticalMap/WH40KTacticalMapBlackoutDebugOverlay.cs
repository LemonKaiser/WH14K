using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WH40K.TacticalMap;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WH40K.TacticalMap;

/// <summary>
/// Debug overlay that highlights mapper-authored tactical blackout tiles.
/// </summary>
public sealed partial class WH40KTacticalMapBlackoutDebugOverlay : Overlay
{
    private static readonly Color BlackoutColor = new(1.0f, 0.18f, 0.18f, 0.38f);

    [Dependency] private  IEntityManager _entityManager = default!;
    [Dependency] private  IMapManager _mapManager = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xforms;
    private readonly SharedWH40KTacticalMapBlackoutSystem _blackout;
    private readonly EntityQuery<WH40KTacticalMapBlackoutComponent> _blackoutQuery;
    private List<Entity<MapGridComponent>> _grids = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;
    public override bool RequestScreenTexture => false;

    public WH40KTacticalMapBlackoutDebugOverlay()
    {
        IoCManager.InjectDependencies(this);

        _lookup = _entityManager.System<EntityLookupSystem>();
        _mapSystem = _entityManager.System<SharedMapSystem>();
        _xforms = _entityManager.System<SharedTransformSystem>();
        _blackout = _entityManager.System<SharedWH40KTacticalMapBlackoutSystem>();
        _blackoutQuery = _entityManager.GetEntityQuery<WH40KTacticalMapBlackoutComponent>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _grids.Clear();
        _mapManager.FindGridsIntersecting(args.MapId, args.WorldBounds, ref _grids, approx: true);

        foreach (var (gridUid, grid) in _grids)
        {
            if (!_blackoutQuery.TryComp(gridUid, out var blackout))
                continue;

            args.WorldHandle.SetTransform(_xforms.GetWorldMatrix(gridUid));

            var tiles = _mapSystem.GetTilesEnumerator(gridUid, grid, args.WorldBounds);
            while (tiles.MoveNext(out var tileRef))
            {
                if (!_blackout.IsBlackedOut((gridUid, grid, blackout), tileRef.GridIndices))
                    continue;

                var bounds = _lookup.GetLocalBounds(tileRef, grid.TileSize);
                args.WorldHandle.DrawRect(bounds, BlackoutColor);
            }
        }

        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }
}
