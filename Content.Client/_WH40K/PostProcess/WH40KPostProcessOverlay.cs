using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.PostProcess;

/// <summary>
/// Basic WH40K fullscreen post-process pass for additive lighting and light falloff shaping.
/// </summary>
public sealed class WH40KPostProcessOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KPostProcess";

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly ILightManager _lightManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly ShaderInstance _shader;

    public WH40KPostProcessOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index<ShaderPrototype>(Shader).InstanceUnique();
        // Keep the base lighting pass late in the world-space chain so other default-Z overlays
        // do not randomly overdraw it.
        ZIndex = 8;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entMan.TryGetComponent(_player.LocalSession?.AttachedEntity, out EyeComponent? eyeComp))
            return false;

        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        if (!_lightManager.Enabled || !eyeComp.Eye.DrawLight || !eyeComp.Eye.DrawFov)
            return false;

        return _player.LocalSession?.AttachedEntity != null;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || args.Viewport.Eye == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("LIGHT_TEXTURE", args.Viewport.LightRenderTarget.Texture);
        _shader.SetParameter("Zoom", args.Viewport.Eye.Zoom.X);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}

public sealed class WH40KPostProcessSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        if (_cfg.GetCVar(CCVars.WH40KPostProcessEnabled) && !_overlay.HasOverlay<WH40KPostProcessOverlay>())
        {
            _overlay.AddOverlay(new WH40KPostProcessOverlay());
        }

        Subs.CVar(_cfg, CCVars.WH40KPostProcessEnabled, OnCVarUpdate, true);
    }

    private void OnCVarUpdate(bool enabled)
    {
        if (enabled && !_overlay.HasOverlay<WH40KPostProcessOverlay>())
        {
            _overlay.AddOverlay(new WH40KPostProcessOverlay());
        }
        else if (!enabled && _overlay.HasOverlay<WH40KPostProcessOverlay>())
        {
            _overlay.RemoveOverlay<WH40KPostProcessOverlay>();
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<WH40KPostProcessOverlay>();
    }
}
