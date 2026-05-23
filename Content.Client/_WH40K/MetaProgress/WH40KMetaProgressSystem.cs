using System;
using System.Collections.Generic;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.MetaProgress;

public sealed class WH40KMetaProgressSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public event Action<WH40KMetaProgressSnapshot>? SnapshotUpdated;
    public event Action<WH40KMetaProgressResetAccountResultEvent>? AccountResetResultReceived;

    private WH40KMetaProgressSnapshot? _snapshot;
    private bool _hasCache;
    private bool _requestInFlight;
    private bool _accountResetRequestInFlight;
    private TimeSpan _lastRequest;
    private TimeSpan _snapshotReceivedAt;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KMetaProgressStateEvent>(OnStateEvent);
        SubscribeNetworkEvent<WH40KMetaProgressResetAccountResultEvent>(OnAccountResetResultEvent);
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

    public bool AccountResetRequestInFlight => _accountResetRequestInFlight;

    public int GetAccountResetCooldownRemainingSeconds()
    {
        if (!_hasCache || _snapshot == null)
            return 0;

        var elapsed = (_timing.CurTime - _snapshotReceivedAt).TotalSeconds;
        var remaining = _snapshot.AccountResetCooldownRemainingSeconds - (int) Math.Floor(elapsed);
        return Math.Max(0, remaining);
    }

    public bool CanRequestAccountReset()
    {
        return _hasCache &&
               _snapshot != null &&
               !_accountResetRequestInFlight &&
               GetAccountResetCooldownRemainingSeconds() <= 0;
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

    public bool RequestAccountReset()
    {
        if (_accountResetRequestInFlight)
            return false;

        _accountResetRequestInFlight = true;
        RaiseNetworkEvent(new WH40KMetaProgressResetAccountRequestEvent());
        return true;
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
        _snapshotReceivedAt = _timing.CurTime;
        SnapshotUpdated?.Invoke(ev.Snapshot);
    }

    private void OnAccountResetResultEvent(WH40KMetaProgressResetAccountResultEvent ev, EntitySessionEventArgs args)
    {
        _accountResetRequestInFlight = false;
        AccountResetResultReceived?.Invoke(ev);
    }
}
