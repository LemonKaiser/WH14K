using Content.Client._WH40K.TacticalMap;
using Content.Shared._WH40K.TacticalMap;
using Robust.Client.UserInterface;

namespace Content.Client._WH40K.TacticalMap.UI;

public sealed class WH40KTacticalMapBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WH40KTacticalMapWindow? _window;
    private WH40KTacticalMapBuiState? _latestState;
    private WH40KTacticalMapLiveRefreshState? _latestLiveRefreshState;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WH40KTacticalMapWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.SaveAnnotationsPressed += OnSaveAnnotationsPressed;
        _window.Set(string.Empty, null, Owner, null);

        if (EntMan.System<WH40KTacticalMapStateSystem>().TryGetCachedState(Owner, out var cachedState) &&
            cachedState != null)
        {
            ApplyTacticalState(cachedState);
        }

        if (EntMan.System<WH40KTacticalMapStateSystem>().TryGetCachedLiveRefreshState(Owner, out var cachedLiveRefreshState) &&
            cachedLiveRefreshState != null)
        {
            ApplyLiveRefreshState(cachedLiveRefreshState);
        }

        _window.OnClose += OnWindowClosed;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not WH40KTacticalMapBuiState cast)
            return;

        ApplyTacticalState(cast);
    }

    public void ApplyTacticalState(WH40KTacticalMapBuiState state)
    {
        _latestState = state;
        _window?.ApplyState(state);
    }

    public void ApplyOverlayState(WH40KTacticalMapOverlayState state)
    {
        if (_latestState == null)
            return;

        _latestState = new WH40KTacticalMapBuiState(
            _latestState.TargetGrid,
            _latestState.GridName,
            _latestState.SnapshotTexturePath,
            _latestState.TrackedEntity,
            _latestState.CanAnnotate,
            _latestState.LiveRefreshEnabled,
            _latestState.TeamId,
            _latestState.FogEnabled,
            _latestState.FogChunkSize,
            _latestState.RevealRevision,
            _latestState.RevealedChunks,
            _latestState.AnnotationRevision,
            _latestState.AnnotationStrokes,
            state.OverlayRevision,
            state.AlliedMarkers,
            state.CapturePoints);

        _window?.ApplyOverlayState(state);
    }

    public void ApplyLiveRefreshState(WH40KTacticalMapLiveRefreshState state)
    {
        _latestLiveRefreshState = state;
        _window?.ApplyLiveRefreshState(state);
    }

    private void OnSaveAnnotationsPressed(WH40KTacticalMapSaveAnnotationsMessage message)
    {
        SendMessage(message);
    }

    private void OnWindowClosed()
    {
        if (_window == null)
            return;

        _window.SaveAnnotationsPressed -= OnSaveAnnotationsPressed;
        _window.OnClose -= OnWindowClosed;
    }
}
