using System.Numerics;
using Content.Shared.Polymorph.Components;
using Content.Shared._WH40K.PropHunt;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using static Robust.Shared.Maths.Color;

namespace Content.Client._WH40K.PropHunt;

public sealed class WH40KPropHuntRevealOverlay : Overlay
{
    private static readonly Color BorderColor = Color.FromHex("#FFD65C").WithAlpha(230);
    private static readonly Color FillColor = Color.FromHex("#FF9438").WithAlpha(32);

    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;
    private readonly EntityQuery<ChameleonDisguisedComponent> _disguisedQuery;
    private readonly EntityQuery<SpriteComponent> _spriteQuery;
    private readonly EntityQuery<TransformComponent> _xformQuery;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WH40KPropHuntRevealOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
        _transform = entManager.System<SharedTransformSystem>();
        _sprite = entManager.System<SpriteSystem>();
        _disguisedQuery = entManager.GetEntityQuery<ChameleonDisguisedComponent>();
        _spriteQuery = entManager.GetEntityQuery<SpriteComponent>();
        _xformQuery = entManager.GetEntityQuery<TransformComponent>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = _entManager.AllEntityQueryEnumerator<WH40KPropHuntRevealComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            if (!TryResolveVisualTarget(uid, out var target, out var sprite, out var xform) ||
                xform.MapID != args.MapId)
            {
                continue;
            }

            var worldPos = _transform.GetWorldPosition(xform, _xformQuery);
            var bounds = _sprite.GetLocalBounds((target, sprite)).Translated(worldPos);
            if (!bounds.Intersects(args.WorldAABB))
                continue;

            handle.DrawRect(bounds, FillColor);
            DrawOutline(handle, bounds, 0.05f);
        }
    }

    private bool TryResolveVisualTarget(
        EntityUid uid,
        out EntityUid target,
        out SpriteComponent sprite,
        out TransformComponent xform)
    {
        if (_disguisedQuery.TryComp(uid, out var disguised) &&
            disguised.Disguise.Valid &&
            _spriteQuery.TryComp(disguised.Disguise, out var disguisedSprite) &&
            _xformQuery.TryComp(disguised.Disguise, out var disguisedXform) &&
            disguisedSprite != null &&
            disguisedXform != null)
        {
            target = disguised.Disguise;
            sprite = disguisedSprite;
            xform = disguisedXform;
            return true;
        }

        if (_spriteQuery.TryComp(uid, out var ownSprite) &&
            _xformQuery.TryComp(uid, out var ownXform) &&
            ownSprite != null &&
            ownXform != null)
        {
            target = uid;
            sprite = ownSprite;
            xform = ownXform;
            return true;
        }

        target = default;
        sprite = default!;
        xform = default!;
        return false;
    }

    private static void DrawOutline(DrawingHandleWorld handle, Box2 box, float thickness)
    {
        var top = new Box2(box.Left, box.Top - thickness, box.Right, box.Top);
        var bottom = new Box2(box.Left, box.Bottom, box.Right, box.Bottom + thickness);
        var left = new Box2(box.Left - thickness, box.Top, box.Left, box.Bottom);
        var right = new Box2(box.Right, box.Top, box.Right + thickness, box.Bottom);

        handle.DrawRect(top, BorderColor);
        handle.DrawRect(bottom, BorderColor);
        handle.DrawRect(left, BorderColor);
        handle.DrawRect(right, BorderColor);
    }
}
