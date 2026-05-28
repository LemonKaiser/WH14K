using Content.Shared._WH40K.Interface;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;

namespace Content.Client._WH40K.Command;

public sealed partial class WH40KMissionObjectiveOverlaySystem : EntitySystem
{
    [Dependency] private  IOverlayManager _overlayManager = default!;
    [Dependency] private  IPlayerManager _playerManager = default!;
    [Dependency] private  IResourceCache _resourceCache = default!;

    private WH40KMissionObjectiveOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new WH40KMissionObjectiveOverlay(EntityManager, _resourceCache, _playerManager);
        if (!_overlayManager.HasOverlay<WH40KMissionObjectiveOverlay>())
            _overlayManager.AddOverlay(_overlay);

        SubscribeNetworkEvent<WH40KTeamThemeAssignedEvent>(OnTeamThemeAssigned);
        SubscribeNetworkEvent<WH40KTeamColorsAssignedEvent>(OnTeamColorsAssigned);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlayManager.HasOverlay<WH40KMissionObjectiveOverlay>())
            _overlayManager.RemoveOverlay(_overlay);
    }

    private void OnTeamThemeAssigned(WH40KTeamThemeAssignedEvent ev, EntitySessionEventArgs args)
    {
        _overlay.SetLocalTeamId(ev.TeamId);
    }

    private void OnTeamColorsAssigned(WH40KTeamColorsAssignedEvent ev, EntitySessionEventArgs args)
    {
        _overlay.ApplyTeamColors(ev.TeamColors);
    }
}
