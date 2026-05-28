using System.Collections.Generic;
using Content.Client._WH40K.TacticalMap.UI;
using Content.Shared.GameTicking;
using Content.Shared._WH40K.TacticalMap;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._WH40K.TacticalMap;

public sealed partial class WH40KTacticalMapStateSystem : EntitySystem
{
    [Dependency] private  UserInterfaceSystem _ui = default!;

    private readonly Dictionary<EntityUid, WH40KTacticalMapBuiState> _cachedStates = new();
    private readonly Dictionary<EntityUid, WH40KTacticalMapLiveRefreshState> _cachedLiveRefreshStates = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WH40KTacticalMapStateEvent>(OnStateEvent);
        SubscribeNetworkEvent<WH40KTacticalMapOverlayEvent>(OnOverlayEvent);
        SubscribeNetworkEvent<WH40KTacticalMapLiveRefreshEvent>(OnLiveRefreshEvent);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public bool TryGetCachedState(EntityUid tacticalMapUid, out WH40KTacticalMapBuiState? state)
    {
        if (_cachedStates.TryGetValue(tacticalMapUid, out var cached))
        {
            state = cached;
            return true;
        }

        state = null;
        return false;
    }

    public bool TryGetCachedLiveRefreshState(EntityUid tacticalMapUid, out WH40KTacticalMapLiveRefreshState? state)
    {
        if (_cachedLiveRefreshStates.TryGetValue(tacticalMapUid, out var cached))
        {
            state = cached;
            return true;
        }

        state = null;
        return false;
    }

    private void OnStateEvent(WH40KTacticalMapStateEvent ev, EntitySessionEventArgs args)
    {
        var tacticalMapUid = GetEntity(ev.TacticalMap);
        if (tacticalMapUid == EntityUid.Invalid)
            return;

        _cachedStates[tacticalMapUid] = ev.State;

        if (_ui.TryGetOpenUi<WH40KTacticalMapBoundUserInterface>(tacticalMapUid, WH40KTacticalMapUiKey.Key, out var bui))
            bui.ApplyTacticalState(ev.State);
    }

    private void OnOverlayEvent(WH40KTacticalMapOverlayEvent ev, EntitySessionEventArgs args)
    {
        var tacticalMapUid = GetEntity(ev.TacticalMap);
        if (tacticalMapUid == EntityUid.Invalid)
            return;

        if (!_cachedStates.TryGetValue(tacticalMapUid, out var cachedState))
            return;

        var mergedState = new WH40KTacticalMapBuiState(
            cachedState.TargetGrid,
            cachedState.GridName,
            cachedState.SnapshotTexturePath,
            cachedState.TrackedEntity,
            cachedState.CanAnnotate,
            cachedState.LiveRefreshEnabled,
            cachedState.TeamId,
            cachedState.FogEnabled,
            cachedState.FogChunkSize,
            cachedState.RevealRevision,
            cachedState.RevealedChunks,
            cachedState.AnnotationRevision,
            cachedState.AnnotationStrokes,
            ev.State.OverlayRevision,
            ev.State.AlliedMarkers,
            ev.State.CapturePoints);

        _cachedStates[tacticalMapUid] = mergedState;

        if (_ui.TryGetOpenUi<WH40KTacticalMapBoundUserInterface>(tacticalMapUid, WH40KTacticalMapUiKey.Key, out var bui))
            bui.ApplyOverlayState(ev.State);
    }

    private void OnLiveRefreshEvent(WH40KTacticalMapLiveRefreshEvent ev, EntitySessionEventArgs args)
    {
        var tacticalMapUid = GetEntity(ev.TacticalMap);
        if (tacticalMapUid == EntityUid.Invalid)
            return;

        _cachedLiveRefreshStates[tacticalMapUid] = ev.State;

        if (_ui.TryGetOpenUi<WH40KTacticalMapBoundUserInterface>(tacticalMapUid, WH40KTacticalMapUiKey.Key, out var bui))
            bui.ApplyLiveRefreshState(ev.State);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev, EntitySessionEventArgs args)
    {
        _cachedStates.Clear();
        _cachedLiveRefreshStates.Clear();
    }
}
