using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Localizations;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Localizations;

/// <summary>
///     Tracks each connected player's client culture (e.g. "ru-RU" / "en-US")
///     and provides helpers to temporarily scope <see cref="ILocalizationManager"/>
///     to that culture when building per-player text (popups, verbs, examine, etc.).
/// </summary>
public sealed class WH40KPlayerCultureTracker : EntitySystem
{
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly Dictionary<NetUserId, string> _cultures = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<RequestLobbyInfoRefreshEvent>(OnLobbyInfoRefresh);
        SubscribeNetworkEvent<ExamineSystemMessages.RequestExamineInfoMessage>(OnExamineRequest);
        SubscribeNetworkEvent<RequestServerVerbsEvent>(OnVerbsRequest);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    // ── Public API ──────────────────────────────────────────────────────

    public string? GetCulture(ICommonSession session)
    {
        return _cultures.TryGetValue(session.UserId, out var culture) ? culture : null;
    }

    public string? GetCulture(EntityUid player)
    {
        return TryComp<ActorComponent>(player, out var actor)
            ? GetCulture(actor.PlayerSession)
            : null;
    }

    public LocalizationCultureScope CreateScope(ICommonSession session)
    {
        return new LocalizationCultureScope(_loc, GetCulture(session));
    }

    public LocalizationCultureScope CreateScope(EntityUid player)
    {
        return new LocalizationCultureScope(_loc, GetCulture(player));
    }

    public string GetPlayerString(EntityUid player, string key)
    {
        using var scope = CreateScope(player);
        return Loc.GetString(key);
    }

    public string GetPlayerString(EntityUid player, string key, params (string, object)[] args)
    {
        using var scope = CreateScope(player);
        return Loc.GetString(key, args);
    }

    public string? ResolveLanguageCode(ICommonSession session)
    {
        return ResolveLanguageCodeFromCulture(GetCulture(session));
    }

    public string? ResolveLanguageCode(EntityUid player)
    {
        return TryComp<ActorComponent>(player, out var actor)
            ? ResolveLanguageCode(actor.PlayerSession)
            : null;
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void OnLobbyInfoRefresh(RequestLobbyInfoRefreshEvent msg, EntitySessionEventArgs args)
    {
        UpdateCulture(args.SenderSession, msg.CultureName);
    }

    private void OnExamineRequest(ExamineSystemMessages.RequestExamineInfoMessage msg, EntitySessionEventArgs args)
    {
        UpdateCulture(args.SenderSession, msg.CultureName);
    }

    private void OnVerbsRequest(RequestServerVerbsEvent msg, EntitySessionEventArgs args)
    {
        UpdateCulture(args.SenderSession, msg.CultureName);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _cultures.Remove(args.Session.UserId);
    }

    // ── Internals ───────────────────────────────────────────────────────

    private void UpdateCulture(ICommonSession? session, string? cultureName)
    {
        if (session == null)
            return;

        var validated = ContentLocalizationManager.ValidateCultureName(cultureName);
        if (validated == null)
        {
            _cultures.Remove(session.UserId);
            return;
        }

        _cultures[session.UserId] = validated;
    }

    private static string? ResolveLanguageCodeFromCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return null;

        if (cultureName.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            return "RU";

        if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return "EN";

        return null;
    }
}
