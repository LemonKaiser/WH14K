using Content.Client.Overlays;
using Content.Shared._WH40K.Overlays;
using Content.Shared.Inventory.Events;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Overlays;

public sealed partial class WH40KNightVisionSystem : EquipmentHudSystem<WH40KNightVisionComponent>
{
    [Dependency] private IOverlayManager _overlayManager = default!;

    private WH40KNightVisionOverlay _overlay = default!;
    private WH40KNightVisionLightOverlay _lightOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new WH40KNightVisionOverlay();
        _lightOverlay = new WH40KNightVisionLightOverlay();
    }

    protected override void UpdateInternal(RefreshEquipmentHudEvent<WH40KNightVisionComponent> args)
    {
        base.UpdateInternal(args);

        var strongest = args.Components[0];
        foreach (var component in args.Components)
        {
            if (component.Strength > strongest.Strength)
                strongest = component;
        }

        _overlay.Strength = strongest.Strength;
        _overlay.BrightnessBoost = strongest.BrightnessBoost;
        _overlay.Contrast = strongest.Contrast;
        _overlay.Vignette = strongest.Vignette;
        _overlay.Scanline = strongest.Scanline;
        _overlay.Noise = strongest.Noise;
        _overlay.LightFloor = strongest.LightFloor;
        _lightOverlay.LightFloor = strongest.LightFloor;

        if (!_overlayManager.HasOverlay<WH40KNightVisionOverlay>())
            _overlayManager.AddOverlay(_overlay);

        if (!_overlayManager.HasOverlay<WH40KNightVisionLightOverlay>())
            _overlayManager.AddOverlay(_lightOverlay);
    }

    protected override void DeactivateInternal()
    {
        base.DeactivateInternal();

        if (_overlayManager.HasOverlay<WH40KNightVisionOverlay>())
            _overlayManager.RemoveOverlay(_overlay);

        if (_overlayManager.HasOverlay<WH40KNightVisionLightOverlay>())
            _overlayManager.RemoveOverlay(_lightOverlay);
    }
}

public sealed partial class WH40KNightVisionLightOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override OverlaySpace Space => OverlaySpace.BeforeLighting;
    public float LightFloor = 0.34f;

    public WH40KNightVisionLightOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 80;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;

        return args.Viewport.Eye == eyeComp.Eye && LightFloor > 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var light = Math.Clamp(LightFloor, 0f, 1f);
        args.WorldHandle.DrawRect(args.WorldBounds, new Color(light * 0.42f, light, light * 0.48f, 1f));
    }
}

public sealed partial class WH40KNightVisionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "WH40KNightVision";

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly ShaderInstance _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public float Strength = 0.82f;
    public float BrightnessBoost = 2.45f;
    public float Contrast = 1.18f;
    public float Vignette = 0.32f;
    public float Scanline = 0.14f;
    public float Noise = 0.018f;
    public float LightFloor = 0.34f;

    public WH40KNightVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(Shader).InstanceUnique();
        ZIndex = 64;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;

        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        return ScreenTexture != null && Strength > 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("LIGHT_TEXTURE", args.Viewport.LightRenderTarget.Texture);
        _shader.SetParameter("Strength", Strength);
        _shader.SetParameter("BrightnessBoost", BrightnessBoost);
        _shader.SetParameter("Contrast", Contrast);
        _shader.SetParameter("Vignette", Vignette);
        _shader.SetParameter("Scanline", Scanline);
        _shader.SetParameter("Noise", Noise);
        _shader.SetParameter("LightFloor", LightFloor);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
