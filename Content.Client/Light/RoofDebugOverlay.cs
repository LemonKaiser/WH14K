using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Light;

/// <summary>
/// Debug overlay that highlights roofed and unroofed tiles in the current viewport.
/// </summary>
public sealed partial class RoofDebugOverlay : Overlay
{
    private static readonly Color RoofedColor = new(0.15f, 0.70f, 1.0f, 0.33f);
    private static readonly Color UnroofedColor = new(0.22f, 1.0f, 0.35f, 0.12f);

    [Dependency] private  IEntityManager _entityManager = default!;
    [Dependency] private  IMapManager _mapManager = default!;

    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedRoofSystem _roof;
    private readonly SharedTransformSystem _xforms;

    private readonly EntityQuery<ImplicitRoofComponent> _implicitRoofQuery;
    private readonly EntityQuery<RoofComponent> _roofQuery;
    private List<Entity<MapGridComponent>> _grids = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;
    public override bool RequestScreenTexture => false;

    public RoofDebugOverlay()
    {
        IoCManager.InjectDependencies(this);

        _lookup = _entityManager.System<EntityLookupSystem>();
        _mapSystem = _entityManager.System<SharedMapSystem>();
        _roof = _entityManager.System<SharedRoofSystem>();
        _xforms = _entityManager.System<SharedTransformSystem>();

        _implicitRoofQuery = _entityManager.GetEntityQuery<ImplicitRoofComponent>();
        _roofQuery = _entityManager.GetEntityQuery<RoofComponent>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _grids.Clear();
        _mapManager.FindGridsIntersecting(args.MapId, args.WorldBounds, ref _grids, approx: true);

        foreach (var (gridUid, grid) in _grids)
        {
            var hasImplicitRoof = _implicitRoofQuery.HasComp(gridUid);
            var hasRoof = _roofQuery.TryComp(gridUid, out var roofComp);
            var worldMatrix = _xforms.GetWorldMatrix(gridUid);
            args.WorldHandle.SetTransform(worldMatrix);

            var tiles = _mapSystem.GetTilesEnumerator(gridUid, grid, args.WorldBounds);
            while (tiles.MoveNext(out var tileRef))
            {
                var isRoofed = hasImplicitRoof ||
                               (hasRoof && _roof.IsRooved((gridUid, grid, roofComp!), tileRef.GridIndices));

                var bounds = _lookup.GetLocalBounds(tileRef, grid.TileSize);
                args.WorldHandle.DrawRect(bounds, isRoofed ? RoofedColor : UnroofedColor);
            }
        }

        args.WorldHandle.SetTransform(Matrix3x2.Identity);
    }
}
