using Content.Client._WH40K.Explosion;
using Content.Client._WH40K.WarpBreach;
using Robust.Client.Graphics;

namespace Content.Client._WH40K.Graphics;

/// <summary>
/// Manages WH40K-specific always-on client overlays.
/// </summary>
public sealed partial class WH40KGraphicsShaderSystem : EntitySystem
{
    [Dependency] private  IOverlayManager _overlayMan = default!;

    private WH40KExplosionShockWaveOverlay _shockWaveOverlay = default!;
    private WH40KWarpBreachOverlay _warpBreachOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shockWaveOverlay = new WH40KExplosionShockWaveOverlay();
        _warpBreachOverlay = new WH40KWarpBreachOverlay();

        if (!_overlayMan.HasOverlay<WH40KExplosionShockWaveOverlay>())
            _overlayMan.AddOverlay(_shockWaveOverlay);

        if (!_overlayMan.HasOverlay<WH40KWarpBreachOverlay>())
            _overlayMan.AddOverlay(_warpBreachOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayMan.RemoveOverlay(_shockWaveOverlay);
        _overlayMan.RemoveOverlay(_warpBreachOverlay);
    }
}
