using Robust.Client.Graphics;
using Content.Shared._WH40K.Interface;
using Robust.Shared.Player;

namespace Content.Client._WH40K.Influence;

/// <summary>
/// Keeps the WH40K influence capture zone overlay active.
/// </summary>
public sealed class WH40KInfluenceOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;

    private WH40KInfluenceOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new WH40KInfluenceOverlay(EntityManager);
        if (!_overlayMan.HasOverlay<WH40KInfluenceOverlay>())
            _overlayMan.AddOverlay(_overlay);

        SubscribeNetworkEvent<WH40KTeamColorsAssignedEvent>(OnTeamColorsAssigned);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlayMan.HasOverlay<WH40KInfluenceOverlay>())
            _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnTeamColorsAssigned(WH40KTeamColorsAssignedEvent ev, EntitySessionEventArgs args)
    {
        _overlay.ApplyTeamColors(ev.TeamColors);
    }
}
