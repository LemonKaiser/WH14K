using Robust.Client.Graphics;

namespace Content.Client._WH40K.Intel;

public sealed partial class WH40KIntelDetectorOverlaySystem : EntitySystem
{
    [Dependency] private  IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        if (!_overlay.HasOverlay<WH40KIntelDetectorOverlay>())
            _overlay.AddOverlay(new WH40KIntelDetectorOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<WH40KIntelDetectorOverlay>();
    }
}
