using System;
using System.Collections.Generic;
using Content.Shared._WH40K.LateJoin;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.LateJoin;

public sealed class WH40KFactionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public event Action<IReadOnlyList<WH40KFactionInfo>>? FactionsUpdated;

    private IReadOnlyList<WH40KFactionInfo> _cachedFactions = Array.Empty<WH40KFactionInfo>();
    private bool _hasCache;
    private bool _requestInFlight;
    private TimeSpan _lastRequest;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KFactionsEvent>(OnFactionsEvent);
    }

    public bool HasCache => _hasCache;

    public bool TryGetCachedFactions(out IReadOnlyList<WH40KFactionInfo> factions)
    {
        if (_hasCache)
        {
            factions = _cachedFactions;
            return true;
        }

        factions = Array.Empty<WH40KFactionInfo>();
        return false;
    }

    public void EnsureCache()
    {
        if (!_hasCache)
            RequestFactions();
    }

    public void RequestFactions(bool force = false)
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
        RaiseNetworkEvent(new WH40KRequestFactionsEvent());
    }

    private void OnFactionsEvent(WH40KFactionsEvent msg, EntitySessionEventArgs args)
    {
        _requestInFlight = false;
        _cachedFactions = msg.Factions;
        _hasCache = true;
        FactionsUpdated?.Invoke(_cachedFactions);
    }
}
