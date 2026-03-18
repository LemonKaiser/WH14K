using Robust.Client.Graphics;

namespace Content.Client._WH40K.Overlays;

/// <summary>
/// Keeps WH40K radial grayscale overlay active.
/// </summary>
public sealed class WH40KRadialGreyscaleOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private WH40KRadialGreyscaleOverlay _radialOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _radialOverlay = new WH40KRadialGreyscaleOverlay(EntityManager);
        if (!_overlay.HasOverlay<WH40KRadialGreyscaleOverlay>())
            _overlay.AddOverlay(_radialOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlay.HasOverlay<WH40KRadialGreyscaleOverlay>())
            _overlay.RemoveOverlay(_radialOverlay);
    }
}
