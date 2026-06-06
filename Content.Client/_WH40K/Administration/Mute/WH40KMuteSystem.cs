using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Administration.Mute;

public sealed partial class WH40KMuteSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public event Action<WH40KMuteSnapshot>? SnapshotUpdated;

    private WH40KMuteSnapshot _snapshot = new(WH40KMuteType.None, null, null);
    private bool _hasCache;
    private bool _requestInFlight;
    private TimeSpan _lastRequest;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KMuteStateEvent>(OnStateEvent);
    }

    public bool HasCache => _hasCache;

    public WH40KMuteSnapshot Snapshot => _snapshot;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        RefreshExpiredSnapshotState();
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
        RaiseNetworkEvent(new WH40KMuteRequestStateEvent());
    }

    public bool IsChatMuted(out WH40KActiveMuteInfo? info)
    {
        RefreshExpiredSnapshotState();
        info = _snapshot.ChatMute;
        return info != null;
    }

    public bool IsAHelpMuted(out WH40KActiveMuteInfo? info)
    {
        RefreshExpiredSnapshotState();
        info = _snapshot.AHelpMute;
        return info != null;
    }

    private void OnStateEvent(WH40KMuteStateEvent ev, EntitySessionEventArgs args)
    {
        _requestInFlight = false;
        _snapshot = ev.Snapshot;
        _hasCache = true;
        SnapshotUpdated?.Invoke(ev.Snapshot);
    }

    private void RefreshExpiredSnapshotState()
    {
        if (!_hasCache)
            return;

        var pruned = PruneExpired(_snapshot, DateTime.UtcNow);
        if (Equals(pruned, _snapshot))
            return;

        _snapshot = pruned;
        SnapshotUpdated?.Invoke(_snapshot);
        RequestSnapshot(force: true);
    }

    private static WH40KMuteSnapshot PruneExpired(WH40KMuteSnapshot snapshot, DateTime now)
    {
        var chat = IsExpired(snapshot.ChatMute, now) ? null : snapshot.ChatMute;
        var ahelp = IsExpired(snapshot.AHelpMute, now) ? null : snapshot.AHelpMute;
        var scopes = (chat == null ? WH40KMuteType.None : WH40KMuteType.Chat) |
                     (ahelp == null ? WH40KMuteType.None : WH40KMuteType.AHelp);
        return new WH40KMuteSnapshot(scopes, chat, ahelp);
    }

    private static bool IsExpired(WH40KActiveMuteInfo? mute, DateTime now)
    {
        return mute?.ExpiresAtUtc is { } expiresAtUtc && expiresAtUtc <= now;
    }
}
