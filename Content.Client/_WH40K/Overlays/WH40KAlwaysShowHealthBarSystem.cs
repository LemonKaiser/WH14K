using Robust.Client.Graphics;

namespace Content.Client._WH40K.Overlays;

/// <summary>
/// Keeps the always-show health bar overlay active.
/// </summary>
public sealed class WH40KAlwaysShowHealthBarSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;

    private WH40KAlwaysShowHealthBarOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new WH40KAlwaysShowHealthBarOverlay(EntityManager);
        if (!_overlayMan.HasOverlay<WH40KAlwaysShowHealthBarOverlay>())
            _overlayMan.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlayMan.HasOverlay<WH40KAlwaysShowHealthBarOverlay>())
            _overlayMan.RemoveOverlay(_overlay);
    }
}
