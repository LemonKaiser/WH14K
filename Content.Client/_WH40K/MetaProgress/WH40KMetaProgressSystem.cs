using System;
using System.Collections.Generic;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.MetaProgress;

public sealed class WH40KMetaProgressSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public event Action<WH40KMetaProgressSnapshot>? SnapshotUpdated;

    private WH40KMetaProgressSnapshot? _snapshot;
    private bool _hasCache;
    private bool _requestInFlight;
    private TimeSpan _lastRequest;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KMetaProgressStateEvent>(OnStateEvent);
    }

    public bool HasCache => _hasCache;

    public bool TryGetCachedSnapshot(out WH40KMetaProgressSnapshot snapshot)
    {
        if (_hasCache && _snapshot != null)
        {
            snapshot = _snapshot;
            return true;
        }

        snapshot = default!;
        return false;
    }

    public void EnsureSnapshot()
    {
        if (!_hasCache)
            RequestSnapshot();
    }

    public void RequestSnapshot(bool force = false)
    {
        var now = _timing.CurTime;

        if (!force)
        {
            if (_requestInFlight)
                return;

            if (now - _lastRequest < TimeSpan.FromSeconds(2))
                return;
        }

        _requestInFlight = true;
        _lastRequest = now;
        RaiseNetworkEvent(new WH40KMetaProgressRequestStateEvent());
    }

    public void SetDecorationSelection(WH40KMetaDecorationCategory category, string decorationId)
    {
        var normalized = string.IsNullOrWhiteSpace(decorationId)
            ? string.Empty
            : decorationId.Trim();

        RaiseNetworkEvent(new WH40KMetaProgressSetDecorationSelectionEvent(category, normalized));
    }

    public void ConfirmDevelopmentPlan(IReadOnlyCollection<string> nodeIds)
    {
        if (nodeIds.Count == 0)
            return;

        var normalized = new List<string>(nodeIds.Count);

        foreach (var nodeId in nodeIds)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                continue;

            var trimmed = nodeId.Trim();
            if (!normalized.Contains(trimmed))
                normalized.Add(trimmed);
        }

        if (normalized.Count == 0)
            return;

        RaiseNetworkEvent(new WH40KMetaProgressConfirmDevelopmentPlanEvent(normalized));
    }

    private void OnStateEvent(WH40KMetaProgressStateEvent ev, EntitySessionEventArgs args)
    {
        _requestInFlight = false;
        _snapshot = ev.Snapshot;
        _hasCache = true;
        SnapshotUpdated?.Invoke(ev.Snapshot);
    }
}
