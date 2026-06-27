using Robust.Client.Graphics;

namespace Content.Client._WH40K.PropHunt;

public sealed partial class WH40KPropHuntRevealOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;

    private WH40KPropHuntRevealOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new WH40KPropHuntRevealOverlay(EntityManager);
        if (!_overlayMan.HasOverlay<WH40KPropHuntRevealOverlay>())
            _overlayMan.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlayMan.HasOverlay<WH40KPropHuntRevealOverlay>())
            _overlayMan.RemoveOverlay(_overlay);
    }
}
