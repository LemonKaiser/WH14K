using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Chat.Translation;
using Content.Server._WH40K.Localizations;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared._WH40K.Chat.Translation;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Asynchronous;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Managers;

internal sealed partial class ChatManager
{
    [Dependency] private readonly IWH40KChatTranslationService _wh40kChatTranslation = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;
    [Dependency] private readonly ITaskManager _wh40kTaskManager = default!;

    private WH40KPlayerCultureTracker WH40KPlayerCulture => _entitySystems.GetEntitySystem<WH40KPlayerCultureTracker>();

    private readonly record struct WH40KOocFormatContext(
        string RawPlayerName,
        string DisplayPlayerName,
        Color? ColorOverride,
        string? PatronColor,
        string? MetaNameColorHex,
        string? MetaNameMarkup,
        string TitlePrefix,
        WH40KMetaDecorationSnapshotEntry? TitleEntry,
        WH40KMetaDecorationSnapshotEntry? ColorEntry,
        bool AdminDecorationPriority,
        bool SenderIsAdmin);

    private bool TryDispatchTranslatedHookOoc(string sender, string message, string wrappedMessage)
    {
        if (!_wh40kChatTranslation.IsConfiguredForChannel(ChatChannel.OOC))
            return false;

        var sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(message);
        if (!WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
        {
            return false;
        }

        _ = DispatchTranslatedHookOocAsync(sender, message, wrappedMessage, sourceLanguage!);
        return true;
    }

    private async Task DispatchTranslatedHookOocAsync(string sender, string message, string wrappedMessage, string sourceLanguage)
    {
        var translationDispatch = await _wh40kChatTranslation.TranslateWithSoftHoldAsync(message, null, ChatChannel.OOC);
        await RunOnMainThreadAsync(() =>
        {
            if (translationDispatch.ImmediateTranslation == null)
            {
                var serverMessageId = translationDispatch.PendingTranslation != null
                    ? (uint?) _wh40kChatTranslation.AllocateMessageId()
                    : null;

                if (translationDispatch.PendingTranslation != null)
                {
                    var placeholderTranslation = WH40KChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                    var placeholderWrappedCache = new Dictionary<(string?, string?), string>();

                    foreach (var session in _player.Sessions)
                    {
                        if (!WH40KPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                        {
                            ChatMessageToOne(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, false, session.Channel, serverMessageId: serverMessageId);
                            continue;
                        }

                        var cacheKey = (WH40KPlayerCulture.GetCulture(session), recipientLanguage);
                        if (!placeholderWrappedCache.TryGetValue(cacheKey, out var initialWrapped))
                        {
                            initialWrapped = WH40KChatTranslationFormatting.PrefixWithLanguageTag(
                                BuildHookOocWrappedMessage(sender, placeholderTranslation.OriginalText, session),
                                recipientLanguage,
                                placeholderTranslation.SourceLanguage,
                                placeholderTranslation.OriginalText);
                            placeholderWrappedCache[cacheKey] = initialWrapped;
                        }

                        ChatMessageToOne(ChatChannel.OOC, message, initialWrapped, EntityUid.Invalid, false, session.Channel, serverMessageId: serverMessageId);
                    }

                    if (serverMessageId is { } pendingUpdateMessageId)
                        _ = DispatchDelayedHookOocUpdateAsync(sender, message, pendingUpdateMessageId, translationDispatch.PendingTranslation);

                    RecordReplayMessage(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, false);
                    return;
                }

                ChatMessageToAll(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, hideChat: false, recordReplay: true, serverMessageId: serverMessageId);

                if (translationDispatch.PendingTranslation != null && serverMessageId is { } delayedMessageId)
                    _ = DispatchDelayedHookOocUpdateAsync(sender, message, delayedMessageId, translationDispatch.PendingTranslation);

                return;
            }

            var wrappedCache = new Dictionary<(string?, string?), string>();
            foreach (var session in _player.Sessions)
            {
                if (!WH40KPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                {
                    ChatMessageToOne(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, false, session.Channel);
                    continue;
                }

                var cacheKey = (WH40KPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var translatedWrapped))
                {
                    var visibleText = translationDispatch.ImmediateTranslation.GetVisibleText(recipientLanguage);
                    translatedWrapped = WH40KChatTranslationFormatting.PrefixWithLanguageTag(
                        BuildHookOocWrappedMessage(sender, visibleText, session),
                        recipientLanguage,
                        translationDispatch.ImmediateTranslation.SourceLanguage,
                        translationDispatch.ImmediateTranslation.OriginalText);
                    wrappedCache[cacheKey] = translatedWrapped;
                }

                ChatMessageToOne(ChatChannel.OOC, message, translatedWrapped, EntityUid.Invalid, false, session.Channel);
            }

            RecordReplayMessage(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, false);
        });
    }

    private async Task DispatchDelayedHookOocUpdateAsync(
        string sender,
        string message,
        uint serverMessageId,
        Task<WH40KChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunOnMainThreadAsync(() =>
        {
            var wrappedCache = new Dictionary<(string?, string?), (string? VisibleText, string? Wrapped)>();
            foreach (var session in _player.Sessions)
            {
                if (!WH40KPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var cacheKey = (WH40KPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var cached))
                {
                    var visibleText = translation.GetVisibleText(recipientLanguage);
                    if (!ShouldSendLateTranslationUpdate(message, visibleText))
                    {
                        wrappedCache[cacheKey] = (null, null);
                        continue;
                    }

                    var translatedWrapped = WH40KChatTranslationFormatting.PrefixWithLanguageTag(
                        BuildHookOocWrappedMessage(sender, visibleText, session),
                        recipientLanguage,
                        translation.SourceLanguage,
                        translation.OriginalText);
                    cached = (visibleText, translatedWrapped);
                    wrappedCache[cacheKey] = cached;
                }

                if (cached.Wrapped == null)
                    continue;

                UpdateChatMessageToOne(
                    ChatChannel.OOC,
                    cached.VisibleText!,
                    cached.Wrapped,
                    EntityUid.Invalid,
                    false,
                    session.Channel,
                    serverMessageId);
            }
        });
    }

    private bool TryDispatchTranslatedOoc(
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool lobbyIsolationMode,
        WH40KOocFormatContext formatContext)
    {
        if (!_wh40kChatTranslation.IsConfiguredForChannel(ChatChannel.OOC))
            return false;

        var fallbackLanguage = WH40KPlayerCulture.ResolveLanguageCode(player);
        var sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
        {
            return false;
        }

        _ = DispatchTranslatedOocAsync(player, message, wrappedMessage, lobbyIsolationMode, formatContext, fallbackLanguage, sourceLanguage!);
        return true;
    }

    private async Task DispatchTranslatedOocAsync(
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool lobbyIsolationMode,
        WH40KOocFormatContext formatContext,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _wh40kChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.OOC);
        await RunOnMainThreadAsync(() =>
        {
            if (translationDispatch.ImmediateTranslation == null)
            {
                var serverMessageId = translationDispatch.PendingTranslation != null
                    ? (uint?) _wh40kChatTranslation.AllocateMessageId()
                    : null;

                if (translationDispatch.PendingTranslation != null)
                {
                    var placeholderTranslation = WH40KChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                    var placeholderWrappedCache = new Dictionary<(string?, string?), string>();

                    foreach (var session in GetOocRecipients(lobbyIsolationMode, formatContext.SenderIsAdmin))
                    {
                        if (!WH40KPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                        {
                            ChatMessageToOne(
                                ChatChannel.OOC,
                                message,
                                wrappedMessage,
                                EntityUid.Invalid,
                                false,
                                session.Channel,
                                colorOverride: formatContext.ColorOverride,
                                author: player.UserId,
                                serverMessageId: serverMessageId);
                            continue;
                        }

                        var cacheKey = (WH40KPlayerCulture.GetCulture(session), recipientLanguage);
                        if (!placeholderWrappedCache.TryGetValue(cacheKey, out var initialWrapped))
                        {
                            initialWrapped = WH40KChatTranslationFormatting.PrefixWithLanguageTag(
                                BuildOocWrappedMessage(formatContext, placeholderTranslation.OriginalText, session),
                                recipientLanguage,
                                placeholderTranslation.SourceLanguage,
                                placeholderTranslation.OriginalText);
                            placeholderWrappedCache[cacheKey] = initialWrapped;
                        }

                        ChatMessageToOne(
                            ChatChannel.OOC,
                            message,
                            initialWrapped,
                            EntityUid.Invalid,
                            false,
                            session.Channel,
                            colorOverride: formatContext.ColorOverride,
                            author: player.UserId,
                            serverMessageId: serverMessageId);
                    }

                        if (serverMessageId is { } pendingUpdateMessageId)
                    {
                        _ = DispatchDelayedOocUpdateAsync(
                            player,
                            message,
                            lobbyIsolationMode,
                            formatContext,
                            pendingUpdateMessageId,
                            translationDispatch.PendingTranslation);
                    }

                    RecordReplayMessage(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, false, formatContext.ColorOverride, player.UserId);
                    return;
                }

                DispatchOocFallback(player, message, wrappedMessage, lobbyIsolationMode, formatContext, serverMessageId);

                if (translationDispatch.PendingTranslation != null && serverMessageId is { } delayedMessageId)
                {
                    _ = DispatchDelayedOocUpdateAsync(
                        player,
                        message,
                        lobbyIsolationMode,
                        formatContext,
                        delayedMessageId,
                        translationDispatch.PendingTranslation);
                }

                return;
            }

            var wrappedCache = new Dictionary<(string?, string?), string>();
            foreach (var session in GetOocRecipients(lobbyIsolationMode, formatContext.SenderIsAdmin))
            {
                if (!WH40KPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                {
                    ChatMessageToOne(
                        ChatChannel.OOC,
                        message,
                        wrappedMessage,
                        EntityUid.Invalid,
                        false,
                        session.Channel,
                        colorOverride: formatContext.ColorOverride,
                        author: player.UserId);
                    continue;
                }

                var cacheKey = (WH40KPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var translatedWrapped))
                {
                    var visibleText = translationDispatch.ImmediateTranslation.GetVisibleText(recipientLanguage);
                    translatedWrapped = WH40KChatTranslationFormatting.PrefixWithLanguageTag(
                        BuildOocWrappedMessage(formatContext, visibleText, session),
                        recipientLanguage,
                        translationDispatch.ImmediateTranslation.SourceLanguage,
                        translationDispatch.ImmediateTranslation.OriginalText);
                    wrappedCache[cacheKey] = translatedWrapped;
                }

                ChatMessageToOne(
                    ChatChannel.OOC,
                    message,
                    translatedWrapped,
                    EntityUid.Invalid,
                    false,
                    session.Channel,
                    colorOverride: formatContext.ColorOverride,
                    author: player.UserId);
            }

            RecordReplayMessage(ChatChannel.OOC, message, wrappedMessage, EntityUid.Invalid, false, formatContext.ColorOverride, player.UserId);
        });
    }

    private async Task DispatchDelayedOocUpdateAsync(
        ICommonSession player,
        string message,
        bool lobbyIsolationMode,
        WH40KOocFormatContext formatContext,
        uint serverMessageId,
        Task<WH40KChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunOnMainThreadAsync(() =>
        {
            var wrappedCache = new Dictionary<(string?, string?), (string? VisibleText, string? Wrapped)>();
            foreach (var session in GetOocRecipients(lobbyIsolationMode, formatContext.SenderIsAdmin))
            {
                if (!WH40KPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var cacheKey = (WH40KPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var cached))
                {
                    var visibleText = translation.GetVisibleText(recipientLanguage);
                    if (!ShouldSendLateTranslationUpdate(message, visibleText))
                    {
                        wrappedCache[cacheKey] = (null, null);
                        continue;
                    }

                    var translatedWrapped = WH40KChatTranslationFormatting.PrefixWithLanguageTag(
                        BuildOocWrappedMessage(formatContext, visibleText, session),
                        recipientLanguage,
                        translation.SourceLanguage,
                        translation.OriginalText);
                    cached = (visibleText, translatedWrapped);
                    wrappedCache[cacheKey] = cached;
                }

                if (cached.Wrapped == null)
                    continue;

                UpdateChatMessageToOne(
                    ChatChannel.OOC,
                    cached.VisibleText!,
                    cached.Wrapped,
                    EntityUid.Invalid,
                    false,
                    session.Channel,
                    serverMessageId,
                    colorOverride: formatContext.ColorOverride,
                    author: player.UserId);
            }
        });
    }

    private void DispatchOocFallback(
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool lobbyIsolationMode,
        WH40KOocFormatContext formatContext,
        uint? serverMessageId = null)
    {
        if (lobbyIsolationMode && !formatContext.SenderIsAdmin)
        {
            var lobbyClients = GetOocRecipients(lobbyIsolationMode, formatContext.SenderIsAdmin)
                .Select(session => session.Channel)
                .ToList();

            ChatMessageToMany(
                ChatChannel.OOC,
                message,
                wrappedMessage,
                EntityUid.Invalid,
                hideChat: false,
                recordReplay: true,
                clients: lobbyClients,
                colorOverride: formatContext.ColorOverride,
                author: player.UserId,
                serverMessageId: serverMessageId);
            return;
        }

        ChatMessageToAll(
            ChatChannel.OOC,
            message,
            wrappedMessage,
            EntityUid.Invalid,
            hideChat: false,
            recordReplay: true,
            colorOverride: formatContext.ColorOverride,
                author: player.UserId,
                serverMessageId: serverMessageId);
    }

    private List<ICommonSession> GetOocRecipients(bool lobbyIsolationMode, bool senderIsAdmin)
    {
        if (!lobbyIsolationMode || senderIsAdmin)
            return _player.Sessions.Cast<ICommonSession>().ToList();

        return _player.Sessions
            .Where(session => !IsSessionInRoundGameplay(session))
            .Cast<ICommonSession>()
            .ToList();
    }

    private string BuildHookOocWrappedMessage(string sender, string visibleMessage, ICommonSession? recipient)
    {
        if (recipient != null)
        {
            using var scope = WH40KPlayerCulture.CreateChatScope(recipient);
            return Loc.GetString(
                "chat-manager-send-hook-ooc-wrap-message",
                ("senderName", sender),
                ("message", FormattedMessage.EscapeText(visibleMessage)));
        }

        return Loc.GetString(
            "chat-manager-send-hook-ooc-wrap-message",
            ("senderName", sender),
            ("message", FormattedMessage.EscapeText(visibleMessage)));
    }

    private string BuildOocWrappedMessage(WH40KOocFormatContext formatContext, string visibleMessage, ICommonSession? recipient)
    {
        if (recipient != null)
        {
            using var scope = WH40KPlayerCulture.CreateChatScope(recipient);
            return BuildOocWrappedMessageCore(formatContext, visibleMessage);
        }

        return BuildOocWrappedMessageCore(formatContext, visibleMessage);
    }

    private string BuildOocWrappedMessageCore(WH40KOocFormatContext formatContext, string visibleMessage)
    {
        var escapedMessage = FormattedMessage.EscapeText(visibleMessage);

        if (!string.IsNullOrWhiteSpace(formatContext.PatronColor))
        {
            return Loc.GetString(
                "chat-manager-send-ooc-patron-wrap-message",
                ("patronColor", (object) formatContext.PatronColor!),
                ("playerName", formatContext.DisplayPlayerName),
                ("message", escapedMessage));
        }

        if (formatContext.ColorOverride == null &&
            ShouldDecorateFullLine(NormalizeDecorationLineMode(_configurationManager.GetCVar(CCVars.WH40KMetaOocDecorationLineMode)),
                formatContext.SenderIsAdmin,
                formatContext.AdminDecorationPriority) &&
            TryBuildDecoratedOocLineMarkup(
                formatContext.ColorEntry,
                formatContext.TitleEntry,
                formatContext.RawPlayerName,
                formatContext.TitlePrefix,
                visibleMessage,
                out var fullLineMarkup))
        {
            return fullLineMarkup;
        }

        if (formatContext.ColorOverride == null && !string.IsNullOrWhiteSpace(formatContext.MetaNameMarkup))
        {
            return Loc.GetString(
                "chat-manager-send-ooc-decoration-markup-wrap-message",
                ("playerNameMarkup", (object) formatContext.MetaNameMarkup!),
                ("message", escapedMessage));
        }

        if (formatContext.ColorOverride == null && !string.IsNullOrWhiteSpace(formatContext.MetaNameColorHex))
        {
            return Loc.GetString(
                "chat-manager-send-ooc-decoration-wrap-message",
                ("nameColor", (object) formatContext.MetaNameColorHex!),
                ("playerName", formatContext.DisplayPlayerName),
                ("message", escapedMessage));
        }

        return Loc.GetString(
            "chat-manager-send-ooc-wrap-message",
            ("playerName", formatContext.DisplayPlayerName),
            ("message", escapedMessage));
    }

    private static bool ShouldSendLateTranslationUpdate(string originalMessage, string translatedMessage)
    {
        return !string.Equals(originalMessage, translatedMessage, StringComparison.Ordinal);
    }

    private void RecordReplayMessage(
        ChatChannel channel,
        string message,
        string wrappedMessage,
        EntityUid source,
        bool hideChat,
        Color? colorOverride = null,
        NetUserId? author = null,
        ChatSpeechTransport speechTransport = ChatSpeechTransport.Direct)
    {
        var user = author == null ? null : EnsurePlayer(author);
        var netSource = _entityManager.GetNetEntity(source);
        user?.AddEntity(netSource);
        var (senderJobIconId, senderNameIsProperNoun) = ResolveSenderChatVisuals(source, author);

        _replay.RecordServerMessage(new ChatMessage(
            channel,
            message,
            wrappedMessage,
            netSource,
            user?.Key,
            hideChat,
            colorOverride,
            senderJobIconId: senderJobIconId,
            senderNameIsProperNoun: senderNameIsProperNoun,
            speechTransport: speechTransport));
    }

    private Task RunOnMainThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        _wh40kTaskManager.RunOnMainThread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        return tcs.Task;
    }
}
