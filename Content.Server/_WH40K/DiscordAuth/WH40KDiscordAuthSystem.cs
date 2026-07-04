using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server._WH40K.Localizations;
using Content.Server._WH40K.MetaProgress;
using Content.Shared.CCVar;
using Content.Shared.Popups;
using Content.Shared._WH40K.DiscordAuth;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.DiscordAuth;

public sealed partial class WH40KDiscordAuthSystem : EntitySystem
{
    private const string DefaultScope = "identify guilds.members.read";
    private const string DefaultCallbackPath = "/wh40k/discord-auth/callback";
    private const int MaxRelayBodyBytes = 4096;
    private const int EndpointRateLimitPerSecond = 10;
    private const int EndpointRateLimitBurst = 20;

    private static readonly JsonSerializerOptions RoleCacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Dependency] private  IWH40KDiscordAuthApi _api = default!;
    [Dependency] private  IConfigurationManager _config = default!;
    [Dependency] private  IServerDbManager _db = default!;
    [Dependency] private  ILogManager _logManager = default!;
    [Dependency] private  IPlayerManager _players = default!;
    [Dependency] private  SharedPopupSystem _popup = default!;
    [Dependency] private  WH40KPlayerCultureTracker _culture = default!;
    [Dependency] private  IStatusHost _statusHost = default!;
    [Dependency] private  ITaskManager _task = default!;
    [Dependency] private  UserDbDataManager _userDb = default!;
    [Dependency] private  WH40KMetaProgressSystem _metaProgress = default!;

    private readonly Dictionary<NetUserId, RuntimeState> _states = new();
    private readonly Dictionary<NetUserId, DateTimeOffset> _connectRefreshAttempts = new();
    private readonly Dictionary<string, PendingLinkRequest> _pendingRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<NetUserId, string> _pendingRequestByUser = new();
    private readonly Dictionary<NetUserId, Task<RefreshResult>> _activeRefreshes = new();
    private readonly object _stateLock = new();
    private readonly object _pendingRequestLock = new();
    private readonly object _refreshLock = new();

    // Token-bucket rate limiter for callback + relay endpoints.
    private readonly object _rateLimitLock = new();
    private double _rateLimitTokens = EndpointRateLimitBurst;
    private DateTimeOffset _rateLimitLastRefill = DateTimeOffset.UtcNow;

    private ISawmill _sawmill = default!;
    private DateTimeOffset _lastCleanupAt;

    private bool _enabled;
    private bool _gateOnConnect;
    private bool _requireGuildMember;
    private bool _requireLink;
    private string _clientId = string.Empty;
    private string _clientSecret = string.Empty;
    private string _guildId = string.Empty;
    private string _redirectUri = string.Empty;
    private HashSet<string> _requiredRoleIds = new(StringComparer.Ordinal);
    private string _relaySecret = string.Empty;
    private TimeSpan _cacheTtl = TimeSpan.FromHours(2);
    private TimeSpan _connectRefreshCooldown = TimeSpan.FromSeconds(15);
    private TimeSpan _linkRequestTtl = TimeSpan.FromMinutes(10);
    private TimeSpan _refreshCooldown = TimeSpan.FromSeconds(30);

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("wh40k.discord_auth");

        Subs.CVar(_config, CCVars.WH40KDiscordAuthEnabled, value =>
        {
            _enabled = value;
            PushSnapshotToAllOnline();
            RefreshMetaProgressForAllOnline();
        }, true);

        Subs.CVar(_config, CCVars.WH40KDiscordAuthClientId, value => _clientId = value.Trim(), true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthClientSecret, value => _clientSecret = value.Trim(), true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthRedirectUri, value =>
        {
            _redirectUri = value.Trim();
            PushSnapshotToAllOnline();
        }, true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthGuildId, value =>
        {
            _guildId = value.Trim();
            PushSnapshotToAllOnline();
        }, true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthRequireLink, value =>
        {
            _requireLink = value;
            PushSnapshotToAllOnline();
        }, true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthGateOnConnect, value => _gateOnConnect = value, true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthRequireGuildMember, value =>
        {
            _requireGuildMember = value;
            PushSnapshotToAllOnline();
        }, true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthRequiredRoleIds, value =>
        {
            _requiredRoleIds = ParseCsvIds(value);
            PushSnapshotToAllOnline();
        }, true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthLinkRequestTtlSeconds, value => _linkRequestTtl = TimeSpan.FromSeconds(Math.Max(30, value)), true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthRefreshCooldownSeconds, value => _refreshCooldown = TimeSpan.FromSeconds(Math.Max(0, value)), true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthConnectRefreshCooldownSeconds, value => _connectRefreshCooldown = TimeSpan.FromSeconds(Math.Max(0, value)), true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthCacheTtlMinutes, value =>
        {
            _cacheTtl = TimeSpan.FromMinutes(Math.Max(1, value));
            PushSnapshotToAllOnline();
        }, true);
        Subs.CVar(_config, CCVars.WH40KDiscordAuthRelaySecret, value => _relaySecret = value.Trim(), true);
        _userDb.AddOnLoadPlayer(LoadPlayerDataAsync);
        _userDb.AddOnFinishLoad(FinishPlayerLoad);
        _userDb.AddOnPlayerDisconnect(OnPlayerDisconnected);

        _statusHost.AddHandler(HandleCallbackRequestAsync);
        _statusHost.AddHandler(HandleRelayRequestAsync);

        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeNetworkEvent<WH40KDiscordAuthRequestStateEvent>(OnRequestState);
        SubscribeNetworkEvent<WH40KDiscordAuthStartLinkEvent>(OnStartLink);
        SubscribeNetworkEvent<WH40KDiscordAuthRefreshProfileEvent>(OnRefreshProfile);
        SubscribeNetworkEvent<WH40KDiscordAuthUnlinkEvent>(OnUnlink);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = DateTimeOffset.UtcNow;
        if (now - _lastCleanupAt < TimeSpan.FromSeconds(30))
            return;

        _lastCleanupAt = now;
        CleanupExpiredPendingRequests();
    }

    private async Task LoadPlayerDataAsync(ICommonSession session, CancellationToken cancel)
    {
        var link = await _db.GetWH40KDiscordLink(session.UserId, cancel);
        cancel.ThrowIfCancellationRequested();
        link = await TryAutoRefreshStaleLinkAsync(session.UserId, link, cancel);
        cancel.ThrowIfCancellationRequested();
        SetRuntimeLinkData(session.UserId, link, markLoadComplete: true);
    }

    private void FinishPlayerLoad(ICommonSession session)
    {
        RunOnMainThreadSafe(() =>
        {
            if (_players.TryGetSessionById(session.UserId, out var current))
            {
                SendSnapshot(current);
                TryRefreshMetaProgressForUser(session.UserId);
            }
        }, $"finish player load for {session.UserId}");
    }

    private void OnPlayerDisconnected(ICommonSession session)
    {
        lock (_stateLock)
        {
            _states.Remove(session.UserId);
        }

        lock (_pendingRequestLock)
        {
            if (_pendingRequestByUser.Remove(session.UserId, out var requestId))
                _pendingRequests.Remove(requestId);
        }
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        if (_userDb.IsLoadComplete(ev.PlayerSession))
            SendSnapshot(ev.PlayerSession);
    }

    private void OnRequestState(WH40KDiscordAuthRequestStateEvent ev, EntitySessionEventArgs args)
    {
        SendSnapshot(args.SenderSession);
    }

    private void OnStartLink(WH40KDiscordAuthStartLinkEvent ev, EntitySessionEventArgs args)
    {
        if (!TryStartLinkFlow(args.SenderSession))
            SendSnapshot(args.SenderSession);
    }

    private async void OnRefreshProfile(WH40KDiscordAuthRefreshProfileEvent ev, EntitySessionEventArgs args)
    {
        try
        {
            await OnRefreshProfileCore(args);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Discord refresh error for {args.SenderSession.UserId}: {e}");
        }
    }

    private async Task OnRefreshProfileCore(EntitySessionEventArgs args)
    {
        if (!_enabled)
        {
            Popup(args.SenderSession, "wh40k-discord-auth-popup-disabled");
            SendSnapshot(args.SenderSession);
            return;
        }

        if (!TryGetLinkedData(args.SenderSession.UserId, out var current))
        {
            Popup(args.SenderSession, "wh40k-discord-auth-popup-link-required");
            SendSnapshot(args.SenderSession);
            return;
        }

        if (!TryMarkRefreshAttempt(args.SenderSession.UserId))
        {
            Popup(args.SenderSession, "wh40k-discord-auth-popup-refresh-cooldown");
            SendSnapshot(args.SenderSession);
            return;
        }

        var refreshResult = await DeduplicatedRefreshAsync(args.SenderSession.UserId, current, CancellationToken.None);
        if (!refreshResult.Success || refreshResult.Data == null)
        {
            if (refreshResult.RequiresReauth)
            {
                if (!TryStartLinkFlow(args.SenderSession, "wh40k-discord-auth-popup-reauth-opening"))
                    Popup(args.SenderSession, refreshResult.UserErrorKey ?? "wh40k-discord-auth-popup-reauth-required");

                SendSnapshot(args.SenderSession);
                return;
            }

            _sawmill.Warning($"Discord refresh failed for {args.SenderSession.UserId}: {refreshResult.Error}");
            Popup(args.SenderSession, refreshResult.UserErrorKey ?? "wh40k-discord-auth-popup-refresh-failed");
            SendSnapshot(args.SenderSession);
            return;
        }

        await _db.SetWH40KDiscordLink(args.SenderSession.UserId, refreshResult.Data);
        ApplyLinkedState(args.SenderSession.UserId, refreshResult.Data);
        Popup(args.SenderSession, "wh40k-discord-auth-popup-refresh-success");
        SendSnapshotIfOnline(args.SenderSession.UserId);
        TryRefreshMetaProgressForUser(args.SenderSession.UserId);
    }

    private async void OnUnlink(WH40KDiscordAuthUnlinkEvent ev, EntitySessionEventArgs args)
    {
        try
        {
            if (!TryGetLinkedData(args.SenderSession.UserId, out var linkToRevoke))
            {
                Popup(args.SenderSession, "wh40k-discord-auth-popup-link-required");
                SendSnapshot(args.SenderSession);
                return;
            }

            await ClearLinkAsync(args.SenderSession.UserId);
            Popup(args.SenderSession, "wh40k-discord-auth-popup-unlink-success");
            _sawmill.Info($"Discord unlinked for {args.SenderSession.UserId}.");

            _ = Task.Run(() => TryRevokeTokenAsync(linkToRevoke.AccessToken));
        }
        catch (Exception e)
        {
            _sawmill.Error($"Discord unlink error for {args.SenderSession.UserId}: {e}");
        }
    }

    private async Task<bool> HandleCallbackRequestAsync(IStatusHandlerContext context)
    {
        if (context.RequestMethod != HttpMethod.Get || context.Url.AbsolutePath != GetCallbackPath())
            return false;

        if (!TryConsumeRateLimitToken())
        {
            await RespondHtmlAsync(context, HttpStatusCode.TooManyRequests, false, "Слишком много запросов. Попробуйте позже.");
            return true;
        }

        var query = ParseQuery(context.Url.Query);

        var error = GetQueryValue(query, "error");
        if (!string.IsNullOrWhiteSpace(error))
        {
            var errorStateId = GetQueryValue(query, "state");
            if (!string.IsNullOrWhiteSpace(errorStateId) && TryConsumePendingRequest(errorStateId, out var errorPending))
                await NotifyCallbackFailureAsync(errorPending.UserId, "wh40k-discord-auth-popup-access-denied");

            await RespondHtmlAsync(context, HttpStatusCode.BadRequest, false, "Авторизация Discord была отклонена.");
            return true;
        }

        var code = GetQueryValue(query, "code");
        var stateId = GetQueryValue(query, "state");

        var result = await ProcessCallbackAsync(code ?? string.Empty, stateId ?? string.Empty, CancellationToken.None);
        await RespondHtmlAsync(context, result.HttpStatus, result.Ok, result.Message);
        return true;
    }

    private async Task<bool> HandleRelayRequestAsync(IStatusHandlerContext context)
    {
        if (context.RequestMethod != HttpMethod.Post || context.Url.AbsolutePath != "/wh40k/discord-auth/relay")
            return false;

        if (!TryConsumeRateLimitToken())
        {
            await RespondJsonAsync(context, HttpStatusCode.TooManyRequests, false, "Rate limit exceeded.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(_relaySecret))
        {
            await RespondJsonAsync(context, HttpStatusCode.ServiceUnavailable, false, "Relay not configured.");
            return true;
        }

        string? headerSecret = null;
            if (context.RequestHeaders.TryGetValue("X-WH40K-Relay-Secret", out var secretValues))
        {
            foreach (var v in secretValues)
            {
                headerSecret = v;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(headerSecret)
            || !FixedTimeSecretEquals(headerSecret.Trim(), _relaySecret))
        {
            await RespondJsonAsync(context, HttpStatusCode.Forbidden, false, "Invalid relay secret.");
            return true;
        }

        RelayPayload? payload;
        try
        {
            using var limited = new LimitedReadStream(context.RequestBody, MaxRelayBodyBytes);
            payload = await JsonSerializer.DeserializeAsync<RelayPayload>(limited, JsonOptions);
        }
        catch
        {
            payload = null;
        }

        if (payload == null
            || string.IsNullOrWhiteSpace(payload.Code)
            || string.IsNullOrWhiteSpace(payload.State))
        {
            await RespondJsonAsync(context, HttpStatusCode.BadRequest, false, "Missing code or state.");
            return true;
        }

        var result = await ProcessCallbackAsync(payload.Code!, payload.State!, CancellationToken.None);
        await RespondJsonAsync(context, result.HttpStatus, result.Ok, result.Message);
        return true;
    }

    private async Task<CallbackResult> ProcessCallbackAsync(string code, string stateId, CancellationToken cancel)
    {
        if (!_enabled || !IsOAuthConfigured())
            return new CallbackResult(HttpStatusCode.ServiceUnavailable, false, "Discord OAuth2 не настроен на сервере.");

        if (string.IsNullOrWhiteSpace(stateId) || !TryConsumePendingRequest(stateId, out var pending))
            return new CallbackResult(HttpStatusCode.Gone, false, "Запрос привязки истёк или уже был использован.");

        if (string.IsNullOrWhiteSpace(code))
        {
            await NotifyCallbackFailureAsync(pending.UserId, "wh40k-discord-auth-popup-callback-invalid");
            return new CallbackResult(HttpStatusCode.BadRequest, false, "Discord не вернул код авторизации.");
        }

        var tokenResult = await ExchangeAuthorizationCodeAsync(code, cancel);
        if (!tokenResult.Success || tokenResult.Token == null)
        {
            await NotifyCallbackFailureAsync(pending.UserId, tokenResult.UserErrorKey ?? "wh40k-discord-auth-popup-token-failed");
            return new CallbackResult(HttpStatusCode.BadGateway, false, "Не удалось обменять код Discord на токен.");
        }

        var resolved = await ResolveLinkDataAsync(tokenResult.Token, cancel);
        if (!resolved.Success || resolved.Data == null)
        {
            await NotifyCallbackFailureAsync(pending.UserId, resolved.UserErrorKey ?? "wh40k-discord-auth-popup-fetch-failed");
            return new CallbackResult(HttpStatusCode.BadGateway, false, "Не удалось получить профиль Discord.");
        }

        try
        {
            await _db.SetWH40KDiscordLink(pending.UserId, resolved.Data);
        }
        catch (InvalidOperationException)
        {
            await NotifyCallbackFailureAsync(pending.UserId, "wh40k-discord-auth-popup-duplicate-link");
            return new CallbackResult(HttpStatusCode.Conflict, false, "Этот Discord уже привязан к другому игровому аккаунту.");
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"Database error during Discord link for {pending.UserId}: {ex}");
            await NotifyCallbackFailureAsync(pending.UserId, "wh40k-discord-auth-popup-link-failed");
            return new CallbackResult(HttpStatusCode.InternalServerError, false, "Ошибка сохранения привязки.");
        }

        _sawmill.Info($"Discord linked: {pending.UserId} -> {resolved.Data.DiscordUserId} ({resolved.Data.Username}).");

        await RunOnMainThreadAsync(() =>
        {
            ApplyLinkedState(pending.UserId, resolved.Data);
            if (_players.TryGetSessionById(pending.UserId, out var session))
            {
                Popup(session, "wh40k-discord-auth-popup-link-success");
                SendSnapshot(session);
            }

            TryRefreshMetaProgressForUser(pending.UserId);
        });

        return new CallbackResult(HttpStatusCode.OK, true, "Discord успешно привязан.");
    }

    private static async Task RespondJsonAsync(IStatusHandlerContext context, HttpStatusCode code, bool ok, string message)
    {
        context.ResponseHeaders["Cache-Control"] = "no-store";
        var json = JsonSerializer.Serialize(new { ok, message });
        await context.RespondAsync(json, code, "application/json; charset=utf-8");
    }

    private WH40KDiscordAuthSnapshot BuildSnapshot(NetUserId userId)
    {
        var link = default(WH40KDiscordAuthDbData);
        var loadComplete = false;
        var lastManualRefreshAt = DateTimeOffset.MinValue;

        lock (_stateLock)
        {
            if (_states.TryGetValue(userId, out var state))
            {
                link = state.Link;
                loadComplete = state.LoadComplete;
                lastManualRefreshAt = state.LastManualRefreshAt;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var evaluation = WH40KDiscordAuthPolicyEvaluator.Evaluate(BuildPolicyConfig(), link, loadComplete, now);
        var displayName = link == null ? string.Empty : GetDisplayName(link);
        var username = link?.Username ?? string.Empty;
        var discordUserId = link?.DiscordUserId ?? string.Empty;
        var refreshCooldownRemaining = GetRefreshCooldownRemaining(lastManualRefreshAt, now);

        return new WH40KDiscordAuthSnapshot(
            _enabled,
            link != null,
            displayName,
            username,
            discordUserId,
            evaluation.GuildConfigured,
            evaluation.GuildMemberKnown,
            evaluation.IsGuildMember,
            evaluation.RoleConfigured,
            evaluation.RoleGatePassed,
            ParseRoleCacheIds(link?.RoleCacheJson),
            evaluation.CacheStale,
            refreshCooldownRemaining,
            evaluation.BlockReason);
    }

    public bool TryGetSharedSnapshot(NetUserId userId, out WH40KDiscordAuthSnapshot snapshot)
    {
        lock (_stateLock)
        {
            if (!_states.TryGetValue(userId, out var state) || !state.LoadComplete)
            {
                snapshot = default!;
                return false;
            }
        }

        snapshot = BuildSnapshot(userId);
        return true;
    }

    public async Task<WH40KDiscordAuthGateBlockReason> GetConnectionBlockReasonAsync(NetUserId userId, CancellationToken cancel = default)
    {
        var config = BuildPolicyConfig();
        if (!_gateOnConnect || !WH40KDiscordAuthPolicyEvaluator.IsPolicyActive(config))
        {
            SetConnectGateRequiresReauth(userId, false);
            return WH40KDiscordAuthGateBlockReason.None;
        }

        if (GetConnectGateRequiresReauth(userId))
            return WH40KDiscordAuthGateBlockReason.CacheStale;

        var link = await _db.GetWH40KDiscordLink(userId, cancel);
        cancel.ThrowIfCancellationRequested();
        SetRuntimeLinkData(userId, link);

        if (ShouldAttemptConnectRefreshOnConnect(config, link))
        {
            var now = DateTimeOffset.UtcNow;
            if (TryMarkConnectRefreshAttempt(userId, now, out _))
            {
                var refreshResult = await DeduplicatedRefreshAsync(userId, link!, cancel);
                cancel.ThrowIfCancellationRequested();

                if (refreshResult.Success && refreshResult.Data != null)
                {
                    await _db.SetWH40KDiscordLink(userId, refreshResult.Data);
                    ApplyLinkedState(userId, refreshResult.Data);
                    RunOnMainThreadSafe(() => TryRefreshMetaProgressForUser(userId), $"connect refresh for {userId}");
                    link = refreshResult.Data;
                }
                else if (refreshResult.RequiresReauth)
                {
                    SetConnectGateRequiresReauth(userId, true);
                    return WH40KDiscordAuthGateBlockReason.CacheStale;
                }
                else
                {
                    _sawmill.Warning($"Discord connect refresh failed for {userId}: {refreshResult.Error}");
                }
            }
        }

        var evaluation = WH40KDiscordAuthPolicyEvaluator.Evaluate(config, link, true, DateTimeOffset.UtcNow);
        SetConnectGateRequiresReauth(userId, false);
        return evaluation.BlockReason;
    }

    public string GetConnectionDenyMessage(NetUserId userId, WH40KDiscordAuthGateBlockReason reason)
    {
        var key = reason switch
        {
            WH40KDiscordAuthGateBlockReason.LinkRequired => "wh40k-discord-auth-connect-deny-link-required",
            WH40KDiscordAuthGateBlockReason.GuildMembershipRequired => "wh40k-discord-auth-connect-deny-guild-required",
            WH40KDiscordAuthGateBlockReason.RoleRequired => "wh40k-discord-auth-connect-deny-role-required",
            WH40KDiscordAuthGateBlockReason.Misconfigured => "wh40k-discord-auth-connect-deny-misconfigured",
            WH40KDiscordAuthGateBlockReason.CacheStale => "wh40k-discord-auth-connect-deny-cache-stale",
            _ => "wh40k-discord-auth-connect-deny-generic",
        };

        var lines = new List<string>
        {
            Loc.GetString(key),
        };

        if (TryBuildLinkedAccountSummary(userId, out var linkedName, out var linkedId))
        {
            lines.Add(Loc.GetString(
                "wh40k-discord-auth-connect-deny-linked-account",
                ("name", linkedName),
                ("id", string.IsNullOrWhiteSpace(linkedId) ? "-" : linkedId)));

            if (reason is WH40KDiscordAuthGateBlockReason.GuildMembershipRequired
                or WH40KDiscordAuthGateBlockReason.RoleRequired
                or WH40KDiscordAuthGateBlockReason.CacheStale)
            {
                lines.Add(Loc.GetString("wh40k-discord-auth-connect-deny-change-hint"));
            }
        }

        lines.Add(GetDefaultSupportMessage());
        return string.Join("\n", lines);
    }

    public NetDenyReason BuildConnectionDenyReason(NetUserId userId, WH40KDiscordAuthGateBlockReason reason)
    {
        var properties = new Dictionary<string, object>();
        var requiresReauth = GetConnectGateRequiresReauth(userId);
        var effectiveReason = requiresReauth && reason != WH40KDiscordAuthGateBlockReason.LinkRequired
            ? WH40KDiscordAuthGateBlockReason.CacheStale
            : reason;
        var hasLinkedAccount = TryGetLinkedData(userId, out _);
        var action = requiresReauth
            ? WH40KDiscordAuthConstants.ConnectDenyAuthActionLink
            : GetConnectAuthAction(effectiveReason);
        var linkMode = GetConnectAuthLinkMode(effectiveReason, hasLinkedAccount, requiresReauth);

        if (ShouldOfferExternalConnectAuth(effectiveReason)
            && TryCreatePendingLinkUrl(userId, out var url))
        {
            properties[WH40KDiscordAuthConstants.ConnectDenyAuthUrlKey] = url;
        }

        if (ShouldOfferExternalConnectAuth(effectiveReason))
        {
            properties[WH40KDiscordAuthConstants.ConnectDenyAuthActionKey] = action;
            properties[WH40KDiscordAuthConstants.ConnectDenyAuthLinkModeKey] = linkMode;
        }

        if (action == WH40KDiscordAuthConstants.ConnectDenyAuthActionRefresh)
        {
            var cooldownRemaining = GetConnectRefreshCooldownRemaining(userId, DateTimeOffset.UtcNow);
            if (cooldownRemaining > TimeSpan.Zero)
            {
                properties[WH40KDiscordAuthConstants.ConnectDenyRefreshCooldownKey] =
                    Math.Max(1, (int) Math.Ceiling(cooldownRemaining.TotalSeconds));
            }
        }

        return new NetDenyReason(GetConnectionDenyMessage(userId, effectiveReason), properties);
    }

    private void SendSnapshot(ICommonSession session)
    {
        RaiseNetworkEvent(new WH40KDiscordAuthStateEvent(BuildSnapshot(session.UserId)), session);
    }

    private void SendSnapshotIfOnline(NetUserId userId)
    {
        if (_players.TryGetSessionById(userId, out var session) && session.Status != SessionStatus.Disconnected)
            SendSnapshot(session);
    }

    private void PushSnapshotToAllOnline()
    {
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.Disconnected)
                SendSnapshot(session);
        }
    }

    private void RefreshMetaProgressForAllOnline()
    {
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.Disconnected)
                TryRefreshMetaProgressForUser(session.UserId);
        }
    }

    private bool TryGetLinkedData(NetUserId userId, out WH40KDiscordAuthDbData data)
    {
        lock (_stateLock)
        {
            if (_states.TryGetValue(userId, out var state) && state.Link != null)
            {
                data = state.Link;
                return true;
            }
        }

        data = default!;
        return false;
    }

    private bool GetConnectGateRequiresReauth(NetUserId userId)
    {
        lock (_stateLock)
        {
            return _states.TryGetValue(userId, out var state) && state.ConnectGateRequiresReauth;
        }
    }

    private void SetConnectGateRequiresReauth(NetUserId userId, bool value)
    {
        lock (_stateLock)
        {
            if (!_states.TryGetValue(userId, out var state))
            {
                if (!value)
                    return;

                state = new RuntimeState();
                _states[userId] = state;
            }

            state.ConnectGateRequiresReauth = value;
        }
    }

    private async Task<WH40KDiscordAuthDbData?> TryAutoRefreshStaleLinkAsync(
        NetUserId userId,
        WH40KDiscordAuthDbData? link,
        CancellationToken cancel = default)
    {
        if (!ShouldAutoRefreshStaleLink(link, DateTimeOffset.UtcNow))
            return link;

        cancel.ThrowIfCancellationRequested();

        var refreshResult = await RefreshFromDiscordAsync(link!, cancel);
        cancel.ThrowIfCancellationRequested();

        if (!refreshResult.Success || refreshResult.Data == null)
        {
            var reason = refreshResult.RequiresReauth ? "reauth-required" : "refresh-failed";
            _sawmill.Warning($"Discord auth auto-refresh {reason} for {userId}: {refreshResult.Error}");
            return link;
        }

        await _db.SetWH40KDiscordLink(userId, refreshResult.Data);
        cancel.ThrowIfCancellationRequested();
        SetRuntimeLinkData(userId, refreshResult.Data);

        RunOnMainThreadSafe(() => TryRefreshMetaProgressForUser(userId), $"stale link refresh for {userId}");

        return refreshResult.Data;
    }

    private bool ShouldAutoRefreshStaleLink(WH40KDiscordAuthDbData? link, DateTimeOffset now)
    {
        if (!_enabled || link == null || !IsOAuthConfigured() || string.IsNullOrWhiteSpace(_guildId))
            return false;

        if (!string.Equals(link.GuildIdCached, _guildId, StringComparison.Ordinal))
            return true;

        if (link.LastGuildRefreshAt == null)
            return true;

        return now - link.LastGuildRefreshAt.Value > _cacheTtl;
    }

    private bool TryMarkRefreshAttempt(NetUserId userId)
    {
        lock (_stateLock)
        {
            if (!_states.TryGetValue(userId, out var state))
            {
                state = new RuntimeState();
                _states[userId] = state;
            }

            var now = DateTimeOffset.UtcNow;
            if (_refreshCooldown > TimeSpan.Zero && now - state.LastManualRefreshAt < _refreshCooldown)
                return false;

            state.LastManualRefreshAt = now;
            return true;
        }
    }

    private bool TryMarkConnectRefreshAttempt(NetUserId userId, DateTimeOffset now, out TimeSpan cooldownRemaining)
    {
        lock (_stateLock)
        {
            CleanupExpiredConnectRefreshAttempts(now);

            if (_connectRefreshCooldown <= TimeSpan.Zero)
            {
                _connectRefreshAttempts[userId] = now;
                cooldownRemaining = TimeSpan.Zero;
                return true;
            }

            if (_connectRefreshAttempts.TryGetValue(userId, out var lastAttempt))
            {
                cooldownRemaining = GetCooldownRemaining(lastAttempt, now, _connectRefreshCooldown);
                if (cooldownRemaining > TimeSpan.Zero)
                    return false;
            }

            _connectRefreshAttempts[userId] = now;
            cooldownRemaining = TimeSpan.Zero;
            return true;
        }
    }

    private TimeSpan GetRefreshCooldownRemaining(DateTimeOffset lastManualRefreshAt, DateTimeOffset now)
    {
        return GetCooldownRemaining(lastManualRefreshAt, now, _refreshCooldown);
    }

    private TimeSpan GetConnectRefreshCooldownRemaining(NetUserId userId, DateTimeOffset now)
    {
        lock (_stateLock)
        {
            if (!_connectRefreshAttempts.TryGetValue(userId, out var lastAttempt))
                return TimeSpan.Zero;

            var remaining = GetCooldownRemaining(lastAttempt, now, _connectRefreshCooldown);
            if (remaining <= TimeSpan.Zero)
                _connectRefreshAttempts.Remove(userId);

            return remaining;
        }
    }

    private static TimeSpan GetCooldownRemaining(DateTimeOffset lastAttemptAt, DateTimeOffset now, TimeSpan cooldown)
    {
        if (cooldown <= TimeSpan.Zero || lastAttemptAt == DateTimeOffset.MinValue)
            return TimeSpan.Zero;

        var nextAllowedAt = lastAttemptAt + cooldown;
        if (nextAllowedAt <= now)
            return TimeSpan.Zero;

        return nextAllowedAt - now;
    }

    private void CleanupExpiredConnectRefreshAttempts(DateTimeOffset now)
    {
        if (_connectRefreshAttempts.Count == 0)
            return;

        // Avoid LINQ allocation inside lock — collect keys directly.
        List<NetUserId>? expired = null;
        foreach (var (key, value) in _connectRefreshAttempts)
        {
            if (GetCooldownRemaining(value, now, _connectRefreshCooldown) <= TimeSpan.Zero)
                (expired ??= new List<NetUserId>()).Add(key);
        }

        if (expired != null)
        {
            foreach (var key in expired)
                _connectRefreshAttempts.Remove(key);
        }
    }

    public async Task ClearLinkAsync(NetUserId userId)
    {
        await _db.ClearWH40KDiscordLink(userId);
        ApplyLinkedState(userId, null);
        SendSnapshotIfOnline(userId);
        TryRefreshMetaProgressForUser(userId);
    }

    private void ApplyLinkedState(NetUserId userId, WH40KDiscordAuthDbData? data)
    {
        SetRuntimeLinkData(userId, data, markLoadComplete: true, clearConnectGateRequiresReauth: true);
    }

    private void Popup(ICommonSession session, string locKey)
    {
        using var scope = _culture.CreateScope(session);
        var message = Loc.GetString(locKey);
        if (locKey == "wh40k-discord-auth-popup-misconfigured")
        {
            message = $"{message} {GetDefaultSupportMessage()}";
        }

        _popup.PopupCursor(message, session, PopupType.Medium);
    }

    private async Task NotifyCallbackFailureAsync(NetUserId userId, string locKey)
    {
        await RunOnMainThreadAsync(() =>
        {
            if (_players.TryGetSessionById(userId, out var session))
            {
                Popup(session, locKey);
                SendSnapshot(session);
            }
        });
    }

    private string BuildAuthorizeUrl(string requestId)
    {
        return _api.BuildAuthorizeUrl(_clientId, _redirectUri, requestId, DefaultScope);
    }

    private bool TryCreatePendingLinkUrl(NetUserId userId, out string url)
    {
        url = string.Empty;

        if (!_enabled || !IsOAuthConfigured())
            return false;

        var now = DateTimeOffset.UtcNow;

        lock (_pendingRequestLock)
        {
            if (_pendingRequestByUser.TryGetValue(userId, out var existingRequestId))
            {
                if (_pendingRequests.TryGetValue(existingRequestId, out var existingRequest)
                    && existingRequest.ExpiresAt > now)
                {
                    url = BuildAuthorizeUrl(existingRequestId);
                    return true;
                }

                _pendingRequestByUser.Remove(userId);
                _pendingRequests.Remove(existingRequestId);
            }

            var requestId = CreateRequestToken();
            _pendingRequests[requestId] = new PendingLinkRequest(userId, now + _linkRequestTtl);
            _pendingRequestByUser[userId] = requestId;
            url = BuildAuthorizeUrl(requestId);
        }
        return true;
    }

    private bool TryStartLinkFlow(ICommonSession session, string openedPopupKey = "wh40k-discord-auth-popup-browser-opened")
    {
        if (!_enabled)
        {
            Popup(session, "wh40k-discord-auth-popup-disabled");
            return false;
        }

        if (!IsOAuthConfigured())
        {
            Popup(session, "wh40k-discord-auth-popup-misconfigured");
            return false;
        }

        if (!TryCreatePendingLinkUrl(session.UserId, out var url))
        {
            Popup(session, "wh40k-discord-auth-popup-misconfigured");
            return false;
        }

        RaiseNetworkEvent(new WH40KDiscordAuthOpenUrlEvent(url), session);
        Popup(session, openedPopupKey);
        return true;
    }

    private async Task<TokenExchangeResult> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancel = default)
    {
        var result = await _api.ExchangeAuthorizationCodeAsync(_clientId, _clientSecret, _redirectUri, code, cancel);
        if (!result.Success || result.Value == null)
        {
            if (result.StatusCode != null)
                _sawmill.Warning($"Discord token exchange failed (authorization_code) with status {(int) result.StatusCode.Value}.");

            return new TokenExchangeResult(null, false, result.Error, "wh40k-discord-auth-popup-token-failed");
        }

        var payload = result.Value;
        var token = new DiscordTokenPayload(
            payload.AccessToken,
            payload.RefreshToken,
            string.IsNullOrWhiteSpace(payload.TokenType) ? "Bearer" : payload.TokenType,
            string.IsNullOrWhiteSpace(payload.Scope) ? DefaultScope : payload.Scope,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, payload.ExpiresIn)));

        return new TokenExchangeResult(token, true, null, null);
    }

    private async Task<TokenExchangeResult> RefreshAccessTokenAsync(WH40KDiscordAuthDbData link, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(link.RefreshToken))
            return new TokenExchangeResult(null, false, "missing_refresh_token", "wh40k-discord-auth-popup-reauth-required", true);

        var result = await _api.RefreshAccessTokenAsync(_clientId, _clientSecret, link.RefreshToken!, cancel);
        if (!result.Success || result.Value == null)
        {
            if (result.StatusCode != null)
                _sawmill.Warning($"Discord token exchange failed (refresh_token) with status {(int) result.StatusCode.Value}.");

            var requiresReauth = WH40KDiscordAuthRefreshFailureClassifier.RequiresReauthAfterRefreshTokenFailure(result.StatusCode);
            return new TokenExchangeResult(
                null,
                false,
                result.Error,
                requiresReauth ? "wh40k-discord-auth-popup-reauth-required" : "wh40k-discord-auth-popup-refresh-failed",
                requiresReauth);
        }

        var payload = result.Value;
        var token = new DiscordTokenPayload(
            payload.AccessToken,
            payload.RefreshToken,
            string.IsNullOrWhiteSpace(payload.TokenType) ? "Bearer" : payload.TokenType,
            string.IsNullOrWhiteSpace(payload.Scope) ? DefaultScope : payload.Scope,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, payload.ExpiresIn)));

        return new TokenExchangeResult(token, true, null, null);
    }

    private async Task<RefreshResult> RefreshFromDiscordAsync(WH40KDiscordAuthDbData current, CancellationToken cancel = default)
    {
        var token = new DiscordTokenPayload(
            current.AccessToken,
            current.RefreshToken,
            current.TokenType,
            current.Scope,
            current.TokenExpiresAt);
        var tokenWasRefreshed = false;

        if (DateTimeOffset.UtcNow >= current.TokenExpiresAt - TimeSpan.FromMinutes(1))
        {
            var refresh = await RefreshAccessTokenAsync(current, cancel);
            if (!refresh.Success || refresh.Token == null)
                return new RefreshResult(null, false, refresh.Error, refresh.UserErrorKey, refresh.RequiresReauth);

            token = refresh.Token;
            tokenWasRefreshed = true;
        }

        var resolved = await ResolveLinkDataAsync(token, cancel);
        if (resolved.Success && resolved.Data != null)
            return new RefreshResult(resolved.Data, true, null, null, false);

        if (!tokenWasRefreshed && resolved.RequiresReauth)
        {
            var refresh = await RefreshAccessTokenAsync(current, cancel);
            if (!refresh.Success || refresh.Token == null)
                return new RefreshResult(null, false, refresh.Error, refresh.UserErrorKey, refresh.RequiresReauth);

            resolved = await ResolveLinkDataAsync(refresh.Token, cancel);
            if (resolved.Success && resolved.Data != null)
                return new RefreshResult(resolved.Data, true, null, null, false);
        }

        return new RefreshResult(resolved.Data, resolved.Success, resolved.Error, resolved.UserErrorKey, resolved.RequiresReauth);
    }

    private async Task<ResolveResult> ResolveLinkDataAsync(DiscordTokenPayload token, CancellationToken cancel = default)
    {
        try
        {
            var userResult = await _api.GetCurrentUserAsync(token.AccessToken, cancel);
            if (!userResult.Success || userResult.Value == null)
            {
                var requiresReauth = WH40KDiscordAuthRefreshFailureClassifier.RequiresReauthAfterResolveFailure(userResult.StatusCode);
                return new ResolveResult(
                    null,
                    false,
                    "user_fetch_failed",
                    requiresReauth ? "wh40k-discord-auth-popup-reauth-required" : "wh40k-discord-auth-popup-fetch-failed",
                    requiresReauth);
            }

            var user = userResult.Value;
            var now = DateTimeOffset.UtcNow;
            string? guildIdCached = null;
            var guildMember = false;
            DateTimeOffset? lastGuildRefreshAt = null;
            string? guildNickname = null;
            var roles = new List<string>();

            if (!string.IsNullOrWhiteSpace(_guildId))
            {
                guildIdCached = _guildId;
                var memberResult = await _api.GetGuildMemberAsync(token.AccessToken, _guildId, cancel);
                if (!memberResult.Success)
                {
                    var requiresReauth = WH40KDiscordAuthRefreshFailureClassifier.RequiresReauthAfterResolveFailure(memberResult.StatusCode);
                    return new ResolveResult(
                        null,
                        false,
                        memberResult.Error,
                        requiresReauth ? "wh40k-discord-auth-popup-reauth-required" : "wh40k-discord-auth-popup-fetch-failed",
                        requiresReauth);
                }

                lastGuildRefreshAt = now;

                var member = memberResult.Value;
                if (member != null)
                {
                    guildMember = true;
                    guildNickname = string.IsNullOrWhiteSpace(member.Nick) ? null : member.Nick.Trim();
                    roles.AddRange(member.Roles.Where(role => !string.IsNullOrWhiteSpace(role)).Select(role => role.Trim()));
                }
            }

            var data = new WH40KDiscordAuthDbData(
                user.Id.Trim(),
                user.Username.Trim(),
                string.IsNullOrWhiteSpace(user.GlobalName) ? null : user.GlobalName.Trim(),
                string.IsNullOrWhiteSpace(user.Avatar) ? null : user.Avatar.Trim(),
                token.AccessToken,
                token.RefreshToken,
                token.TokenType,
                token.Scope,
                now,
                token.ExpiresAt,
                now,
                guildIdCached,
                lastGuildRefreshAt,
                guildMember,
                guildNickname,
                JsonSerializer.Serialize(roles, JsonOptions));

            return new ResolveResult(data, true, null, null);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Discord profile resolve failed: {e}");
            return new ResolveResult(null, false, e.Message, "wh40k-discord-auth-popup-fetch-failed", false);
        }
    }

    private bool IsOAuthConfigured()
    {
        return WH40KDiscordAuthPolicyEvaluator.IsOAuthConfigured(_clientId, _clientSecret, _redirectUri);
    }

    private bool CanUseServerRefresh()
    {
        return _enabled
               && !string.IsNullOrWhiteSpace(_clientId)
               && !string.IsNullOrWhiteSpace(_clientSecret);
    }

    private bool ShouldAttemptConnectRefreshOnConnect(WH40KDiscordAuthPolicyConfig config, WH40KDiscordAuthDbData? link)
    {
        if (link == null || !CanUseServerRefresh())
            return false;

        return config.RequireGuildMember || config.RequiredRoleIds.Count > 0;
    }

    private static bool ShouldOfferExternalConnectAuth(WH40KDiscordAuthGateBlockReason reason)
    {
        return reason == WH40KDiscordAuthGateBlockReason.LinkRequired
               || reason == WH40KDiscordAuthGateBlockReason.CacheStale
               || reason == WH40KDiscordAuthGateBlockReason.GuildMembershipRequired
               || reason == WH40KDiscordAuthGateBlockReason.RoleRequired;
    }

    private static string GetConnectAuthAction(WH40KDiscordAuthGateBlockReason reason)
    {
        return reason == WH40KDiscordAuthGateBlockReason.LinkRequired
            ? WH40KDiscordAuthConstants.ConnectDenyAuthActionLink
            : WH40KDiscordAuthConstants.ConnectDenyAuthActionRefresh;
    }

    private static string GetConnectAuthLinkMode(
        WH40KDiscordAuthGateBlockReason reason,
        bool hasLinkedAccount,
        bool requiresReauth)
    {
        if (requiresReauth)
            return WH40KDiscordAuthConstants.ConnectDenyAuthLinkModeReauth;

        if (reason == WH40KDiscordAuthGateBlockReason.LinkRequired || !hasLinkedAccount)
            return WH40KDiscordAuthConstants.ConnectDenyAuthLinkModeLink;

        return WH40KDiscordAuthConstants.ConnectDenyAuthLinkModeChange;
    }

    private static List<string> ParseRoleCacheIds(string? roleCacheJson)
    {
        if (string.IsNullOrWhiteSpace(roleCacheJson))
            return new List<string>();

        try
        {
            var roles = JsonSerializer.Deserialize<List<string>>(roleCacheJson, RoleCacheJsonOptions);
            return WH40KDiscordAuthRequirementEvaluator.NormalizeRoleIds(roles);
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private WH40KDiscordAuthPolicyConfig BuildPolicyConfig()
    {
        return new WH40KDiscordAuthPolicyConfig(
            _enabled,
            _requireLink,
            _requireGuildMember,
            _clientId,
            _clientSecret,
            _redirectUri,
            _guildId,
            _requiredRoleIds,
            _cacheTtl);
    }

    private string GetCallbackPath()
    {
        if (Uri.TryCreate(_redirectUri, UriKind.Absolute, out var uri))
            return string.IsNullOrWhiteSpace(uri.AbsolutePath) ? DefaultCallbackPath : uri.AbsolutePath;

        return DefaultCallbackPath;
    }

    private static HashSet<string> ParseCsvIds(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        var raw = query.Length > 0 && query[0] == '?' ? query.Substring(1) : query;

        foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            var key = WebUtility.UrlDecode(split[0]);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var value = split.Length > 1 ? WebUtility.UrlDecode(split[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static string? GetQueryValue(IReadOnlyDictionary<string, string> query, string key)
    {
        return query.TryGetValue(key, out var value) ? value : null;
    }

    private void CleanupExpiredPendingRequests()
    {
        lock (_pendingRequestLock)
        {
            if (_pendingRequests.Count == 0)
                return;

            var now = DateTimeOffset.UtcNow;
            List<string>? expired = null;
            foreach (var (key, value) in _pendingRequests)
            {
                if (value.ExpiresAt <= now)
                    (expired ??= new List<string>()).Add(key);
            }

            if (expired == null)
                return;

            foreach (var key in expired)
            {
                var userId = _pendingRequests[key].UserId;
                _pendingRequests.Remove(key);
                if (_pendingRequestByUser.TryGetValue(userId, out var pending) && pending == key)
                    _pendingRequestByUser.Remove(userId);
            }
        }
    }

    private bool TryConsumePendingRequest(string requestId, out PendingLinkRequest request)
    {
        lock (_pendingRequestLock)
        {
            request = default!;
            if (!_pendingRequests.Remove(requestId, out var removed))
                return false;

            request = removed;

            if (_pendingRequestByUser.TryGetValue(request.UserId, out var current) && current == requestId)
                _pendingRequestByUser.Remove(request.UserId);

            return request.ExpiresAt > DateTimeOffset.UtcNow;
        }
    }

    private static string CreateRequestToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    }

    private async Task TryRevokeTokenAsync(string accessToken)
    {
        try
        {
            await _api.RevokeTokenAsync(_clientId, _clientSecret, accessToken);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Discord token revocation failed (non-critical): {e.Message}");
        }
    }

    private static bool FixedTimeSecretEquals(string a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a);
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }

    private static string GetDisplayName(WH40KDiscordAuthDbData link)
    {
        if (!string.IsNullOrWhiteSpace(link.GlobalName))
            return link.GlobalName!;

        return link.Username;
    }

    private bool TryBuildLinkedAccountSummary(NetUserId userId, out string displayName, out string discordUserId)
    {
        if (!TryGetLinkedData(userId, out var link))
        {
            displayName = string.Empty;
            discordUserId = string.Empty;
            return false;
        }

        var resolvedName = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(GetDisplayName(link));
        if (string.IsNullOrWhiteSpace(resolvedName))
            resolvedName = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(link.Username);
        if (string.IsNullOrWhiteSpace(resolvedName))
            resolvedName = link.DiscordUserId;

        displayName = WH40KDiscordAuthDisplayNameSanitizer.Ellipsize(resolvedName, 48);
        discordUserId = link.DiscordUserId.Trim();
        return !string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(discordUserId);
    }

    private void SetRuntimeLinkData(
        NetUserId userId,
        WH40KDiscordAuthDbData? data,
        bool markLoadComplete = false,
        bool clearConnectGateRequiresReauth = false)
    {
        lock (_stateLock)
        {
            if (!_states.TryGetValue(userId, out var state))
            {
                state = new RuntimeState();
                _states[userId] = state;
            }

            state.Link = data;
            if (markLoadComplete)
                state.LoadComplete = true;

            if (clearConnectGateRequiresReauth)
                state.ConnectGateRequiresReauth = false;
        }
    }

    private string GetDefaultSupportMessage()
    {
        return Loc.GetString("wh40k-discord-auth-support-default");
    }

    private void TryRefreshMetaProgressForUser(NetUserId userId)
    {
        try
        {
            _metaProgress.RefreshDiscordRequirementsForUser(userId);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Discord auth meta-progress refresh failed for {userId}: {e}");
        }
    }

    private void RunOnMainThreadSafe(Action action, string context)
    {
        _task.RunOnMainThread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                _sawmill.Error($"Discord auth main-thread callback failed during {context}: {e}");
            }
        });
    }

    private async Task RunOnMainThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        _task.RunOnMainThread(() =>
        {
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        });

        await tcs.Task;
    }

    private static async Task RespondHtmlAsync(IStatusHandlerContext context, HttpStatusCode code, bool success, string message)
    {
        context.ResponseHeaders["Cache-Control"] = "no-store";
        var title = success ? "Discord linked" : "Discord link failed";
        var color = success ? "#6ab04c" : "#d35454";
        var badgeText = success ? "OK" : "ERROR";
        var html = $@"
<!doctype html>
<html lang=""ru"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>{WebUtility.HtmlEncode(title)}</title>
    <style>
        body {{ background:#12151c; color:#f1f3f5; font-family:Segoe UI, sans-serif; margin:0; padding:32px; }}
        .card {{ max-width:560px; margin:0 auto; background:#1b2230; border:1px solid #2f3a4d; border-radius:14px; padding:24px; }}
        .badge {{ display:inline-block; padding:6px 10px; border-radius:999px; background:{color}; color:#fff; font-weight:700; margin-bottom:16px; }}
        h1 {{ margin:0 0 12px 0; font-size:24px; }}
        p {{ margin:0; line-height:1.5; color:#d9dde5; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""badge"">{WebUtility.HtmlEncode(badgeText)}</div>
        <h1>{WebUtility.HtmlEncode(title)}</h1>
        <p>{WebUtility.HtmlEncode(message)}</p>
    </div>
</body>
</html>";

        await context.RespondAsync(html, code, "text/html; charset=utf-8");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class RuntimeState
    {
        public bool LoadComplete;
        public bool ConnectGateRequiresReauth;
        public DateTimeOffset LastManualRefreshAt;
        public WH40KDiscordAuthDbData? Link;
    }

    private sealed record PendingLinkRequest(NetUserId UserId, DateTimeOffset ExpiresAt);
    private sealed record DiscordTokenPayload(string AccessToken, string? RefreshToken, string TokenType, string Scope, DateTimeOffset ExpiresAt);
    private sealed record TokenExchangeResult(DiscordTokenPayload? Token, bool Success, string? Error, string? UserErrorKey, bool RequiresReauth = false);
    private sealed record ResolveResult(WH40KDiscordAuthDbData? Data, bool Success, string? Error, string? UserErrorKey, bool RequiresReauth = false);
    private sealed record RefreshResult(WH40KDiscordAuthDbData? Data, bool Success, string? Error, string? UserErrorKey, bool RequiresReauth = false);
    private sealed record CallbackResult(HttpStatusCode HttpStatus, bool Ok, string Message);

    private sealed class RelayPayload
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }
    }

    // ── Rate limiter (token bucket) ──────────────────────────────────────

    private bool TryConsumeRateLimitToken()
    {
        lock (_rateLimitLock)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - _rateLimitLastRefill).TotalSeconds;
            if (elapsed > 0)
            {
                _rateLimitTokens = Math.Min(
                    EndpointRateLimitBurst,
                    _rateLimitTokens + elapsed * EndpointRateLimitPerSecond);
                _rateLimitLastRefill = now;
            }

            if (_rateLimitTokens < 1)
                return false;

            _rateLimitTokens -= 1;
            return true;
        }
    }

    // ── Concurrent refresh deduplication ─────────────────────────────────

    private Task<RefreshResult> DeduplicatedRefreshAsync(
        NetUserId userId,
        WH40KDiscordAuthDbData current,
        CancellationToken cancel = default)
    {
        lock (_refreshLock)
        {
            if (_activeRefreshes.TryGetValue(userId, out var existing))
                return existing;

            var task = RunRefreshCoreAsync(userId, current, cancel);
            _activeRefreshes[userId] = task;
            return task;
        }
    }

    private async Task<RefreshResult> RunRefreshCoreAsync(
        NetUserId userId,
        WH40KDiscordAuthDbData current,
        CancellationToken cancel)
    {
        try
        {
            return await RefreshFromDiscordAsync(current, cancel);
        }
        finally
        {
            lock (_refreshLock)
            {
                _activeRefreshes.Remove(userId);
            }
        }
    }

    // ── Body size limiter stream ─────────────────────────────────────────

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private long _totalRead;

        public LimitedReadStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _maxBytes = maxBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var allowed = (int) Math.Min(count, _maxBytes - _totalRead);
            if (allowed <= 0)
                throw new InvalidOperationException("Request body exceeded maximum allowed size.");

            var read = _inner.Read(buffer, offset, allowed);
            _totalRead += read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var allowed = (int) Math.Min(count, _maxBytes - _totalRead);
            if (allowed <= 0)
                throw new InvalidOperationException("Request body exceeded maximum allowed size.");

            var read = await _inner.ReadAsync(buffer, offset, allowed, cancellationToken);
            _totalRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var allowed = (int) Math.Min(buffer.Length, _maxBytes - _totalRead);
            if (allowed <= 0)
                throw new InvalidOperationException("Request body exceeded maximum allowed size.");

            var read = await _inner.ReadAsync(buffer[..allowed], cancellationToken);
            _totalRead += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
