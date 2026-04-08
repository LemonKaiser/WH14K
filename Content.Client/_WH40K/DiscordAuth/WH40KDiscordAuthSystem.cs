using System;
using Content.Shared._WH40K.DiscordAuth;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.DiscordAuth;

public sealed class WH40KDiscordAuthSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IUriOpener _uri = default!;

    public event Action<WH40KDiscordAuthSnapshot>? SnapshotUpdated;

    private WH40KDiscordAuthSnapshot? _snapshot;
    private bool _hasCache;
    private bool _requestInFlight;
    private TimeSpan _lastRequest;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KDiscordAuthStateEvent>(OnStateEvent);
        SubscribeNetworkEvent<WH40KDiscordAuthOpenUrlEvent>(OnOpenUrlEvent);
    }

    public bool HasCache => _hasCache;

    public bool TryGetCachedSnapshot(out WH40KDiscordAuthSnapshot snapshot)
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
        RaiseNetworkEvent(new WH40KDiscordAuthRequestStateEvent());
    }

    public void StartLinkFlow()
    {
        RaiseNetworkEvent(new WH40KDiscordAuthStartLinkEvent());
    }

    public void RefreshProfile()
    {
        RaiseNetworkEvent(new WH40KDiscordAuthRefreshProfileEvent());
    }

    public void Unlink()
    {
        RaiseNetworkEvent(new WH40KDiscordAuthUnlinkEvent());
    }

    private void OnStateEvent(WH40KDiscordAuthStateEvent ev, EntitySessionEventArgs args)
    {
        _requestInFlight = false;
        _snapshot = ev.Snapshot;
        _hasCache = true;
        SnapshotUpdated?.Invoke(ev.Snapshot);
    }

    private void OnOpenUrlEvent(WH40KDiscordAuthOpenUrlEvent ev, EntitySessionEventArgs args)
    {
        if (IsAllowedDiscordUrl(ev.Url))
        {
            _uri.OpenUri(ev.Url);
        }
    }

    private static bool IsAllowedDiscordUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        var authorityStart = "https://".Length;
        var authorityEnd = url.Length;

        var slashIndex = url.IndexOf('/', authorityStart);
        if (slashIndex != -1)
            authorityEnd = Math.Min(authorityEnd, slashIndex);

        var queryIndex = url.IndexOf('?', authorityStart);
        if (queryIndex != -1)
            authorityEnd = Math.Min(authorityEnd, queryIndex);

        var fragmentIndex = url.IndexOf('#', authorityStart);
        if (fragmentIndex != -1)
            authorityEnd = Math.Min(authorityEnd, fragmentIndex);

        if (authorityEnd <= authorityStart)
            return false;

        var authority = url.Substring(authorityStart, authorityEnd - authorityStart);
        if (authority.Contains('@') || authority.Contains(':'))
            return false;

        return authority.Equals("discord.com", StringComparison.OrdinalIgnoreCase)
            || authority.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase);
    }
}
