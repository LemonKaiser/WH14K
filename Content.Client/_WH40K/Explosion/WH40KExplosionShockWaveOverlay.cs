using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared._WH40K.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Explosion;

public sealed partial class WH40KExplosionShockWaveOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KShockWave";

    [Dependency] private  IEntityManager _entMan = default!;
    [Dependency] private  IConfigurationManager _cfg = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;

    private SharedTransformSystem? _xform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    /// <summary>
    ///     Maximum simultaneous waves rendered in one frame.
    /// </summary>
    public const int MaxCount = 10;

    private readonly Vector2[] _positions = new Vector2[MaxCount];
    private readonly float[] _falloffPower = new float[MaxCount];
    private readonly float[] _sharpness = new float[MaxCount];
    private readonly float[] _width = new float[MaxCount];
    private int _count;

    public WH40KExplosionShockWaveOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototype.Index(Shader).Instance().Duplicate();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_cfg.GetCVar(CCVars.ReducedMotion))
            return false;

        if (args.Viewport.Eye == null)
            return false;

        if (_xform == null && !_entMan.TrySystem(out _xform))
            return false;

        _count = 0;
        var query = _entMan.EntityQueryEnumerator<WH40KExplosionShockWaveComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var wave, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var worldPos = _xform.GetWorldPosition(uid);
            var coords = args.Viewport.WorldToLocal(worldPos);

            // Convert to normalized 0..1 coordinates and flip Y for fragment-space sampling.
            coords.Y = 1f - (coords.Y / args.Viewport.Size.Y);
            coords.X /= args.Viewport.Size.X;

            _positions[_count] = coords;
            _falloffPower[_count] = wave.FalloffPower;
            _sharpness[_count] = wave.Sharpness;
            _width[_count] = wave.Width;
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

        _shader.SetParameter("renderScale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
        _shader.SetParameter("count", _count);
        _shader.SetParameter("position", _positions);
        _shader.SetParameter("falloffPower", _falloffPower);
        _shader.SetParameter("sharpness", _sharpness);
        _shader.SetParameter("width", _width);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
