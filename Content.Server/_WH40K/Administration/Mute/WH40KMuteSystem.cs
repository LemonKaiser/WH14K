using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.Chat.V2.Repository;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server._WH40K.Administration;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Emoting;
using Content.Shared.GameTicking;
using Content.Shared.Speech;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Server.Player;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Administration.Mute;

public sealed partial class WH40KMuteSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogs = default!;
    [Dependency] private IAdminHierarchyManager _adminHierarchy = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private UserDbDataManager _userDb = default!;

    private static readonly WH40KMuteSnapshot EmptySnapshot =
        new(WH40KMuteType.None, null, null);

    private readonly Dictionary<NetUserId, WH40KMuteSnapshot> _snapshots = new();
    private readonly HashSet<NetUserId> _refreshQueued = new();
    private TimeSpan _nextExpirySweep;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpeakAttemptEvent>(OnSpeakAttempt);
        SubscribeLocalEvent<EmoteAttemptEvent>(OnEmoteAttempt);
        SubscribeLocalEvent<InGameOocMessageAttemptEvent>(OnInGameOocAttempt);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeNetworkEvent<WH40KMuteRequestStateEvent>(OnRequestState);

        _userDb.AddOnLoadPlayer(LoadPlayerDataAsync);
        _userDb.AddOnFinishLoad(FinishPlayerLoad);
        _userDb.AddOnPlayerDisconnect(OnPlayerDisconnected);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextExpirySweep)
            return;

        _nextExpirySweep = _timing.CurTime + TimeSpan.FromSeconds(1);
        var now = DateTime.UtcNow;

        foreach (var (userId, snapshot) in _snapshots.ToArray())
        {
            if (!SnapshotMayNeedRefresh(snapshot, now))
                continue;

            QueueRefresh(userId);
        }
    }

    public bool IsChatMuted(ICommonSession session, out WH40KActiveMuteInfo? info)
    {
        return IsMuted(session, WH40KMuteType.Chat, out info);
    }

    public bool IsAHelpMuted(ICommonSession session, out WH40KActiveMuteInfo? info)
    {
        return IsMuted(session, WH40KMuteType.AHelp, out info);
    }

    public async Task ApplyMuteAsync(
        NetUserId targetUserId,
        string targetName,
        WH40KMuteType typeMask,
        string reason,
        TimeSpan? duration,
        NetUserId? adminUserId,
        bool eraseMessages)
    {
        if (typeMask == WH40KMuteType.None)
            throw new ArgumentException("Mute type must not be None.", nameof(typeMask));

        var sanitizedReason = reason.Trim();
        if (string.IsNullOrWhiteSpace(sanitizedReason))
            throw new ArgumentException("Mute reason must not be empty.", nameof(reason));

        if (await HasHostBypassAsync(targetUserId))
            return;

        if (sanitizedReason.Length > 4096)
            sanitizedReason = sanitizedReason[..4096];

        var now = DateTimeOffset.UtcNow;
        foreach (var type in EnumerateScopes(typeMask))
        {
            var activeMutes = await _db.GetMutesAsync(targetUserId, includeUnmuted: false, type);
            foreach (var activeMute in activeMutes)
            {
                if (activeMute.Id is not { } muteId)
                    continue;

                await _db.AddUnmuteAsync(new WH40KUnmuteDef(muteId, adminUserId, now));
            }

            DateTimeOffset? expiresAt = duration == null ? null : now + duration.Value;
            var mute = new WH40KMuteDef(
                null,
                targetUserId,
                type,
                sanitizedReason,
                adminUserId,
                now,
                expiresAt,
                null);

            await _db.AddMuteAsync(mute);
        }

        if (eraseMessages)
            ErasePlayerMessages(targetUserId);

        _adminLogs.Add(
            LogType.Action,
            LogImpact.Medium,
            $"{adminUserId} muted {targetName} ({targetUserId}) for {FormatScopes(typeMask)}. Reason: {sanitizedReason}");

        await RefreshSnapshotIfOnlineAsync(targetUserId);
    }

    public async Task<bool> CanRemoveMuteAsync(
        NetUserId targetUserId,
        WH40KMuteType typeMask,
        ICommonSession? actor,
        Action<string>? notify = null,
        CancellationToken cancel = default)
    {
        if (actor == null)
            return true;

        if (typeMask == WH40KMuteType.None)
            typeMask = WH40KMuteType.Chat | WH40KMuteType.AHelp;

        var actorHierarchy = _adminHierarchy.GetAdminHierarchy(actor, includeDeAdmin: true);
        foreach (var type in EnumerateScopes(typeMask))
        {
            var activeMutes = await _db.GetMutesAsync(targetUserId, includeUnmuted: false, type);
            foreach (var activeMute in activeMutes)
            {
                if (activeMute.MutingAdmin is not { } mutingAdminId || mutingAdminId == actor.UserId)
                    continue;

                var decision = await _adminHierarchy.CanManageAdminAsync(actor, mutingAdminId, cancel);
                if (decision.Allowed)
                    continue;

                var sourceHierarchy = await TryGetHierarchyAsync(mutingAdminId, cancel);
                if (!WH40KStaffProtection.CanOverrideStaffAction(actorHierarchy, sourceHierarchy))
                {
                    notify?.Invoke(Loc.GetString("wh40k-mute-unmute-denied-protected"));
                    return false;
                }
            }
        }

        return true;
    }

    public async Task<int> RemoveMuteAsync(NetUserId targetUserId, WH40KMuteType typeMask, ICommonSession? actor)
    {
        if (typeMask == WH40KMuteType.None)
            typeMask = WH40KMuteType.Chat | WH40KMuteType.AHelp;

        if (!await CanRemoveMuteAsync(targetUserId, typeMask, actor))
            return 0;

        var adminUserId = actor?.UserId;
        var now = DateTimeOffset.UtcNow;
        var removed = 0;

        foreach (var type in EnumerateScopes(typeMask))
        {
            var activeMutes = await _db.GetMutesAsync(targetUserId, includeUnmuted: false, type);
            foreach (var activeMute in activeMutes)
            {
                if (activeMute.Id is not { } muteId)
                    continue;

                await _db.AddUnmuteAsync(new WH40KUnmuteDef(muteId, adminUserId, now));
                removed++;
            }
        }

        if (removed > 0)
        {
            _adminLogs.Add(
                LogType.Action,
                LogImpact.Medium,
                $"{adminUserId} removed mute from {targetUserId} for {FormatScopes(typeMask)}");
        }

        await RefreshSnapshotIfOnlineAsync(targetUserId);
        return removed;
    }

    private bool IsMuted(ICommonSession session, WH40KMuteType type, out WH40KActiveMuteInfo? info)
    {
        if (ShouldIgnoreMute(session))
        {
            info = null;
            return false;
        }

        if (!_snapshots.TryGetValue(session.UserId, out var snapshot))
        {
            if (!_userDb.IsLoadComplete(session))
            {
                info = null;
                return true;
            }

            snapshot = EmptySnapshot;
            _snapshots[session.UserId] = snapshot;
        }

        if (SnapshotMayNeedRefresh(snapshot, DateTime.UtcNow))
        {
            QueueRefresh(session.UserId);
            snapshot = PruneExpired(snapshot, DateTime.UtcNow);
            _snapshots[session.UserId] = snapshot;
        }

        info = type switch
        {
            WH40KMuteType.Chat => snapshot.ChatMute,
            WH40KMuteType.AHelp => snapshot.AHelpMute,
            _ => null
        };

        return info != null;
    }

    private async Task LoadPlayerDataAsync(ICommonSession session, CancellationToken cancel)
    {
        if (ShouldIgnoreMute(session))
        {
            _snapshots[session.UserId] = EmptySnapshot;
            return;
        }

        var snapshot = await LoadSnapshotAsync(session.UserId);
        cancel.ThrowIfCancellationRequested();
        _snapshots[session.UserId] = snapshot;
    }

    private void FinishPlayerLoad(ICommonSession session)
    {
        if (_players.TryGetSessionById(session.UserId, out var current))
            PushSnapshot(current);
    }

    private void OnPlayerDisconnected(ICommonSession session)
    {
        _snapshots.Remove(session.UserId);
        _refreshQueued.Remove(session.UserId);
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        if (_userDb.IsLoadComplete(ev.PlayerSession))
            PushSnapshot(ev.PlayerSession);
    }

    private void OnRequestState(WH40KMuteRequestStateEvent ev, EntitySessionEventArgs args)
    {
        PushSnapshot(args.SenderSession);
    }

    private void OnSpeakAttempt(SpeakAttemptEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Uid, out var session))
            return;

        if (IsChatMuted(session, out _))
            args.Cancel();
    }

    private void OnEmoteAttempt(EmoteAttemptEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Uid, out var session))
            return;

        if (IsChatMuted(session, out _))
            args.Cancel();
    }

    private void OnInGameOocAttempt(ref InGameOocMessageAttemptEvent args)
    {
        if (IsChatMuted(args.Session, out _))
            args.Cancelled = true;
    }

    private void ErasePlayerMessages(NetUserId userId)
    {
        _chat.DeleteMessagesBy(userId);

        if (EntityManager.System<ChatRepositorySystem>().NukeForUserId(userId, out _))
            return;
    }

    private async Task<WH40KMuteSnapshot> LoadSnapshotAsync(NetUserId userId)
    {
        if (await HasHostBypassAsync(userId))
            return EmptySnapshot;

        var activeMutes = await _db.GetMutesAsync(userId, includeUnmuted: false);
        var chatMute = activeMutes
            .Where(m => m.Type == WH40KMuteType.Chat)
            .OrderByDescending(m => m.MuteTime)
            .FirstOrDefault();
        var ahelpMute = activeMutes
            .Where(m => m.Type == WH40KMuteType.AHelp)
            .OrderByDescending(m => m.MuteTime)
            .FirstOrDefault();

        return new WH40KMuteSnapshot(
            (chatMute == null ? WH40KMuteType.None : WH40KMuteType.Chat) |
            (ahelpMute == null ? WH40KMuteType.None : WH40KMuteType.AHelp),
            ToActiveInfo(chatMute),
            ToActiveInfo(ahelpMute));
    }

    private async Task RefreshSnapshotIfOnlineAsync(NetUserId userId)
    {
        try
        {
            var snapshot = await LoadSnapshotAsync(userId);
            _snapshots[userId] = snapshot;

            if (_players.TryGetSessionById(userId, out var session))
                PushSnapshot(session, snapshot);
        }
        finally
        {
            _refreshQueued.Remove(userId);
        }
    }

    private void QueueRefresh(NetUserId userId)
    {
        if (!_refreshQueued.Add(userId))
            return;

        _ = RefreshSnapshotIfOnlineAsync(userId);
    }

    private void PushSnapshot(ICommonSession session)
    {
        var snapshot = _snapshots.GetValueOrDefault(session.UserId, EmptySnapshot);
        PushSnapshot(session, snapshot);
    }

    private void PushSnapshot(ICommonSession session, WH40KMuteSnapshot snapshot)
    {
        RaiseNetworkEvent(new WH40KMuteStateEvent(snapshot), session.Channel);
    }

    private static WH40KActiveMuteInfo? ToActiveInfo(WH40KMuteDef? mute)
    {
        if (mute == null)
            return null;

        return new WH40KActiveMuteInfo(mute.Type, mute.Reason, mute.ExpirationTime?.UtcDateTime);
    }

    private static bool SnapshotMayNeedRefresh(WH40KMuteSnapshot snapshot, DateTime now)
    {
        return IsExpired(snapshot.ChatMute, now) || IsExpired(snapshot.AHelpMute, now);
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
        return mute?.ExpiresAtUtc is { } expiresAt && expiresAt <= now;
    }

    private static IEnumerable<WH40KMuteType> EnumerateScopes(WH40KMuteType typeMask)
    {
        if ((typeMask & WH40KMuteType.Chat) != 0)
            yield return WH40KMuteType.Chat;

        if ((typeMask & WH40KMuteType.AHelp) != 0)
            yield return WH40KMuteType.AHelp;
    }

    private static string FormatScopes(WH40KMuteType typeMask)
    {
        return typeMask switch
        {
            WH40KMuteType.Chat => "chat",
            WH40KMuteType.AHelp => "ahelp",
            WH40KMuteType.Chat | WH40KMuteType.AHelp => "chat+ahelp",
            _ => typeMask.ToString()
        };
    }

    private bool ShouldIgnoreMute(ICommonSession session)
    {
        var adminData = _adminManager.GetAdminData(session, includeDeAdmin: true);
        return WH40KStaffProtection.HasHostBypass(adminData, _adminManager.IsPromotedHost(session.UserId));
    }

    private async ValueTask<bool> HasHostBypassAsync(NetUserId userId, CancellationToken cancel = default)
    {
        if (_adminManager.IsPromotedHost(userId))
            return true;

        if (_players.TryGetSessionById(userId, out var session))
        {
            var adminData = _adminManager.GetAdminData(session, includeDeAdmin: true);
            return WH40KStaffProtection.HasHostBypass(adminData, isPromotedHost: false);
        }

        var admin = await _db.GetAdminDataForAsync(userId, cancel);
        return admin != null && _adminHierarchy.GetAdminHierarchy(admin).IsHost;
    }

    private async ValueTask<AdminHierarchyInfo> TryGetHierarchyAsync(NetUserId userId, CancellationToken cancel)
    {
        if (_adminManager.IsPromotedHost(userId))
            return new AdminHierarchyInfo(true, true, 0, 0);

        if (_players.TryGetSessionById(userId, out var session))
            return _adminHierarchy.GetAdminHierarchy(session, includeDeAdmin: true);

        var admin = await _db.GetAdminDataForAsync(userId, cancel);
        return admin == null ? AdminHierarchyInfo.Missing : _adminHierarchy.GetAdminHierarchy(admin);
    }
}
