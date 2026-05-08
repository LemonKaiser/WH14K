using System.Linq;
using Content.Client.Construction;
using Content.Shared._WH40K.StrategicPoints;
using Content.Shared._WH40K.StrategicPoints.Construction;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.Client._WH40K.StrategicPoints;

/// <summary>
/// Snaps the strategic point preview onto the point's actual build tile
/// while the cursor is near a free T0 anchor.
/// </summary>
public sealed class WH40KStrategicPointPlacement : SnapgridCenter
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private readonly SharedTransformSystem _transform;

    private const float PreviewAlpha = 0.5f;
    private const float OccupiedAnchorTolerance = 0.2f;

    private bool _showPreview;
    private EntityUid _previewAnchorUid = EntityUid.Invalid;
    public EntityUid PreviewAnchorUid => _previewAnchorUid;

    public override bool HasLineMode => false;
    public override bool HasGridMode => false;

    public WH40KStrategicPointPlacement(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);
        _transform = _entityManager.System<SharedTransformSystem>();

        ValidPlaceColor = ValidPlaceColor.WithAlpha(PreviewAlpha);
        InvalidPlaceColor = InvalidPlaceColor.WithAlpha(PreviewAlpha);
    }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        base.AlignPlacementMode(mouseScreen);

        _showPreview = false;
        _previewAnchorUid = EntityUid.Invalid;

        if (!TryGetAnchorCondition(out var anchorCondition))
            return;

        var cursorCoordinates = ScreenToCursorGrid(mouseScreen);
        if (!TryFindClosestAnchor(cursorCoordinates, anchorCondition.PointType, out var anchorUid, out var anchor))
            return;

        MouseCoords = _entityManager.GetComponent<TransformComponent>(anchorUid).Coordinates.Offset(anchor.BuiltOffset);
        _showPreview = true;
        _previewAnchorUid = anchorUid;
    }

    public override void Render(in OverlayDrawArgs args)
    {
        if (!_showPreview)
            return;

        base.Render(args);
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        if (!_showPreview || !RangeCheck(position))
            return false;

        if (_previewAnchorUid == EntityUid.Invalid ||
            !_entityManager.TryGetComponent<WH40KStrategicPointAnchorComponent>(_previewAnchorUid, out var anchor))
        {
            return false;
        }

        return !IsAnchorOccupied(_previewAnchorUid, anchor);
    }

    private bool TryGetAnchorCondition(out WH40KStrategicPointAnchorCondition condition)
    {
        condition = default!;
        if ((pManager.Hijack as ConstructionPlacementHijack)?.CurrentPrototype is not { } prototype ||
            prototype.Conditions.OfType<WH40KStrategicPointAnchorCondition>().FirstOrDefault() is not { } anchorCondition)
        {
            return false;
        }

        condition = anchorCondition;
        return true;
    }

    private bool TryFindClosestAnchor(
        EntityCoordinates cursorCoordinates,
        WH40KStrategicPointType pointType,
        out EntityUid anchorUid,
        out WH40KStrategicPointAnchorComponent anchor)
    {
        anchorUid = EntityUid.Invalid;
        anchor = default!;

        var cursorMapCoordinates = _transform.ToMapCoordinates(cursorCoordinates);
        var bestDistanceSquared = float.MaxValue;

        var anchors = _entityManager.EntityQueryEnumerator<WH40KStrategicPointAnchorComponent, TransformComponent>();
        while (anchors.MoveNext(out var uid, out var candidate, out var xform))
        {
            if (candidate.PointType != pointType)
                continue;

            var anchorMapCoordinates = _transform.GetMapCoordinates(uid, xform: xform);
            if (anchorMapCoordinates.MapId != cursorMapCoordinates.MapId)
                continue;

            // Strategic points are selected by hovering near the anchor itself,
            // while the preview snaps onto the actual build tile via BuiltOffset.
            var distanceSquared = (anchorMapCoordinates.Position - cursorMapCoordinates.Position).LengthSquared();
            if (distanceSquared > candidate.BuildRadius * candidate.BuildRadius)
                continue;

            if (IsAnchorOccupied(uid, candidate))
                continue;

            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            anchorUid = uid;
            anchor = candidate;
        }

        return anchorUid != EntityUid.Invalid;
    }

    private bool IsAnchorOccupied(EntityUid anchorUid, WH40KStrategicPointAnchorComponent anchor)
    {
        if (anchor.BuiltPoint is { Valid: true } builtPoint && _entityManager.EntityExists(builtPoint))
            return true;

        var anchorCoordinates = _entityManager.GetComponent<TransformComponent>(anchorUid).Coordinates.Offset(anchor.BuiltOffset);
        var anchorMapCoordinates = _transform.ToMapCoordinates(anchorCoordinates);
        var toleranceSquared = OccupiedAnchorTolerance * OccupiedAnchorTolerance;

        var points = _entityManager.EntityQueryEnumerator<WH40KStrategicPointComponent, TransformComponent>();
        while (points.MoveNext(out var uid, out var point, out var xform))
        {
            if (point.PointType != anchor.PointType)
                continue;

            var pointMapCoordinates = _transform.GetMapCoordinates(uid, xform: xform);
            if (pointMapCoordinates.MapId != anchorMapCoordinates.MapId)
                continue;

            if ((pointMapCoordinates.Position - anchorMapCoordinates.Position).LengthSquared() <= toleranceSquared)
                return true;
        }

        return false;
    }
}
