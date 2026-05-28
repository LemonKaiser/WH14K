using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WH40K.Overlays;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Overlays;

/// <summary>
/// Draws a grayscale filter inside radial areas defined by WH40KRadialGreyscaleComponent entities.
/// </summary>
public sealed partial class WH40KRadialGreyscaleOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "WH40KRadialGreyscale";

    [Dependency] private  IPrototypeManager _prototype = default!;

    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly Dictionary<EntityUid, ShaderInstance> _entityShaders = new();
    private readonly HashSet<EntityUid> _seenThisFrame = new();
    private readonly List<EntityUid> _staleEntities = new();

    public WH40KRadialGreyscaleOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
        _transform = _entManager.System<SharedTransformSystem>();
        IoCManager.InjectDependencies(this);
        ZIndex = 11;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();
        var vertical = args.Viewport.Size.Y;
        var renderScale = args.Viewport.RenderScale.X;
        var zoom = args.Viewport.Eye?.Zoom ?? Vector2.One;
        var zoomLength = MathF.Max(zoom.X, 0.0001f);

        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
        var query = _entManager.AllEntityQueryEnumerator<WH40KRadialGreyscaleComponent, TransformComponent>();
        _seenThisFrame.Clear();

        var usedShader = false;
        while (query.MoveNext(out var uid, out var radial, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            _seenThisFrame.Add(uid);
            if (!_entityShaders.TryGetValue(uid, out var shader))
            {
                shader = _prototype.Index(ShaderId).InstanceUnique();
                _entityShaders[uid] = shader;
            }

            var worldPosition = _transform.GetWorldPosition(xform, xformQuery);
            var pixelCenter = Vector2.Transform(worldPosition, invMatrix);
            var pixelRadius = MathF.Max(0.01f, radial.Radius * renderScale / zoomLength * EyeManager.PixelsPerMeter);
            var pixelFeather = MathF.Max(0.01f, radial.Feather * renderScale / zoomLength * EyeManager.PixelsPerMeter);

            shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            shader.SetParameter("position", new Vector2(pixelCenter.X, vertical - pixelCenter.Y));
            shader.SetParameter("radius", pixelRadius);
            shader.SetParameter("feather", pixelFeather);

            handle.UseShader(shader);
            handle.DrawRect(args.WorldBounds, Color.White);
            usedShader = true;
        }

        if (usedShader)
            handle.UseShader(null);

        _staleEntities.Clear();
        foreach (var uid in _entityShaders.Keys)
        {
            if (!_seenThisFrame.Contains(uid))
                _staleEntities.Add(uid);
        }

        foreach (var uid in _staleEntities)
        {
            _entityShaders.Remove(uid);
        }
    }
}
