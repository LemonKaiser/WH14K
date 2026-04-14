using Content.Client._WH40K.Explosion;
using Content.Client._WH40K.WarpBreach;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client._WH40K.Graphics;

/// <summary>
/// Manages WH40K-specific client post-process overlays and toggles.
/// </summary>
public sealed class WH40KGraphicsShaderSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;

    private WH40KGrimdarkOverlay _grimdarkOverlay = default!;
    private WH40KExplosionShockWaveOverlay _shockWaveOverlay = default!;
    private WH40KWarpBreachOverlay _warpBreachOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _grimdarkOverlay = new WH40KGrimdarkOverlay();
        _shockWaveOverlay = new WH40KExplosionShockWaveOverlay();
        _warpBreachOverlay = new WH40KWarpBreachOverlay();

        if (!_overlayMan.HasOverlay<WH40KExplosionShockWaveOverlay>())
            _overlayMan.AddOverlay(_shockWaveOverlay);

        if (!_overlayMan.HasOverlay<WH40KWarpBreachOverlay>())
            _overlayMan.AddOverlay(_warpBreachOverlay);

        Subs.CVar(_cfg, CCVars.WH40KGrimdarkShaderEnabled, _ => SyncConfig(), true);
    }

    private void SyncConfig()
    {
        SyncOverlay(CCVars.WH40KGrimdarkShaderEnabled, _grimdarkOverlay);
    }

    private void SyncOverlay(CVarDef<bool> cvar, Overlay overlay)
    {
        if (_cfg.GetCVar(cvar))
        {
            if (!_overlayMan.HasOverlay(overlay.GetType()))
                _overlayMan.AddOverlay(overlay);
        }
        else
        {
            _overlayMan.RemoveOverlay(overlay);
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayMan.RemoveOverlay(_grimdarkOverlay);
        _overlayMan.RemoveOverlay(_shockWaveOverlay);
        _overlayMan.RemoveOverlay(_warpBreachOverlay);
    }
}
