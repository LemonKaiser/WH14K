using System.Numerics;
using Content.Shared._WH40K.WarpBreach;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.WarpBreach;

public sealed partial class WH40KWarpBreachOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShaderProto = "WH40KWarpBreach";

    [Dependency] private  IEntityManager _entMan = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;
    [Dependency] private  IConfigurationManager _cfg = default!;
    [Dependency] private  IResourceCache _resCache = default!;
    [Dependency] private  IGameTiming _timing = default!;

    private SharedTransformSystem? _xform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;
    private readonly Texture _warpTexture;

    public const int MaxCount = 5;
    private const float MaxDistance = 30f;

    private readonly Vector2[] _positions = new Vector2[MaxCount];
    private readonly float[] _intensity = new float[MaxCount];
    private readonly float[] _radius = new float[MaxCount];
    private readonly float[] _falloff = new float[MaxCount];
    private readonly float[] _progress = new float[MaxCount];
    private int _count;

    public WH40KWarpBreachOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototype.Index(ShaderProto).Instance().Duplicate();
        _shader.SetParameter("maxDistance", MaxDistance * EyeManager.PixelsPerMeter);
        _warpTexture = _resCache.GetResource<TextureResource>("/Textures/_WH40K/Parallaxes/red.png").Texture;
        ZIndex = 100;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_cfg.GetCVar(CCVars.ReducedMotion))
            return false;

        if (args.Viewport.Eye == null)
            return false;

        if (_xform == null && !_entMan.TrySystem(out _xform))
            return false;

        var now = _timing.CurTime;

        _count = 0;
        var query = _entMan.EntityQueryEnumerator<WH40KWarpBreachComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var breach, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var worldPos = _xform.GetWorldPosition(uid);

            if ((worldPos - args.WorldAABB.ClosestPoint(worldPos)).LengthSquared() > 30f * 30f)
                continue;

            // Compute progress from server-side creation time: 0 → 1 over OpenDuration seconds
            var elapsed = (float) (now - breach.CreatedAt).TotalSeconds;
            var prog = Math.Clamp(elapsed / Math.Max(breach.OpenDuration, 0.1f), 0f, 1f);

            // World → viewport local → fragment-space (Y-flipped)
            var coords = args.Viewport.WorldToLocal(worldPos);
            coords.Y = args.Viewport.Size.Y - coords.Y;

            _positions[_count] = coords;
            _intensity[_count] = breach.Intensity;
            _radius[_count] = breach.Radius * EyeManager.PixelsPerMeter * args.Viewport.RenderScale.X;
            _falloff[_count] = breach.Falloff;
            _progress[_count] = prog;
            _count++;

            if (_count == MaxCount)
                break;
        }

        return _count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || args.Viewport.Eye == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("warpTexture", _warpTexture);
        _shader.SetParameter("renderScale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
        _shader.SetParameter("count", _count);
        _shader.SetParameter("position", _positions);
        _shader.SetParameter("intensity", _intensity);
        _shader.SetParameter("radius", _radius);
        _shader.SetParameter("falloff", _falloff);
        _shader.SetParameter("progress", _progress);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldAABB, Color.White);
        handle.UseShader(null);
    }
}
