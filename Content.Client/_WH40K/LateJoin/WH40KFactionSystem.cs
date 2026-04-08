using System;
using System.Collections.Generic;
using Content.Shared._WH40K.LateJoin;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.LateJoin;

public sealed class WH40KFactionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public event Action<WH40KFactionsEvent>? FactionsUpdated;
    public event Action<WH40KFactionSelectionResultEvent>? FactionSelectionResultReceived;

    private readonly Dictionary<WH40KFactionSelectionPurpose, IReadOnlyList<WH40KFactionInfo>> _cachedFactions = new();
    private readonly HashSet<WH40KFactionSelectionPurpose> _requestsInFlight = new();
    private readonly Dictionary<WH40KFactionSelectionPurpose, TimeSpan> _lastRequest = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KFactionsEvent>(OnFactionsEvent);
        SubscribeNetworkEvent<WH40KFactionSelectionResultEvent>(OnSelectionResult);
    }

    public bool TryGetCachedFactions(WH40KFactionSelectionPurpose purpose, out IReadOnlyList<WH40KFactionInfo> factions)
    {
        if (_cachedFactions.ContainsKey(purpose))
        {
            factions = _cachedFactions[purpose];
            return true;
        }

        factions = Array.Empty<WH40KFactionInfo>();
        return false;
    }

    public void RequestFactions(WH40KFactionSelectionPurpose purpose = WH40KFactionSelectionPurpose.Preview, bool force = false)
    {
        var now = _timing.CurTime;

        if (!force)
        {
            if (_requestsInFlight.Contains(purpose))
                return;

            if (_lastRequest.TryGetValue(purpose, out var lastRequest) && now - lastRequest < TimeSpan.FromSeconds(2))
                return;
        }

        _requestsInFlight.Add(purpose);
        _lastRequest[purpose] = now;
        RaiseNetworkEvent(new WH40KRequestFactionsEvent(purpose));
    }

    public void SelectFaction(string factionId, WH40KFactionSelectionPurpose purpose)
    {
        RaiseNetworkEvent(new WH40KSelectFactionEvent(factionId, purpose));
    }

    public void CancelSelection(WH40KFactionSelectionPurpose purpose)
    {
        RaiseNetworkEvent(new WH40KCancelFactionSelectionEvent(purpose));
    }

    private void OnFactionsEvent(WH40KFactionsEvent msg, EntitySessionEventArgs args)
    {
        _requestsInFlight.Remove(msg.Purpose);
        _cachedFactions[msg.Purpose] = msg.Factions;
        FactionsUpdated?.Invoke(msg);
    }

    private void OnSelectionResult(WH40KFactionSelectionResultEvent msg, EntitySessionEventArgs args)
    {
        _requestsInFlight.Remove(msg.Purpose);
        _cachedFactions[msg.Purpose] = msg.Factions;
        FactionsUpdated?.Invoke(new WH40KFactionsEvent(msg.Purpose, msg.Factions));
        FactionSelectionResultReceived?.Invoke(msg);
    }
}
