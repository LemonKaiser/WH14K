using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WH40K.Chat.Translation;
using Content.Server._WH40K.Localizations;
using Content.Shared.Chat;
using Content.Shared.Ghost;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Content.Shared._WH40K.Chat.Translation;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Chat.Systems;

public sealed partial class ChatSystem
{
    [Dependency] private  IWH40KChatTranslationService _wh40kChatTranslation = default!;
    [Dependency] private  WH40KPlayerCultureTracker _wh40kPlayerCulture = default!;
    [Dependency] private  ITaskManager _wh40kTaskManager = default!;

    private bool TryDispatchTranslatedEntitySpeak(
        EntityUid source,
        string message,
        string wrappedMessage,
        SpeechVerbPrototype speech,
        string speechVerbLocKey,
        string escapedName,
        ChatTransmitRange range)
    {
        if (!_wh40kChatTranslation.IsConfiguredForChannel(ChatChannel.Local))
            return false;

        var fallbackLanguage = _wh40kPlayerCulture.ResolveLanguageCode(source);
        var sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
        {
            return false;
        }

        ObserveTranslationTask(DispatchTranslatedEntitySpeakAsync(
            source,
            message,
            wrappedMessage,
            speech,
            speechVerbLocKey,
            escapedName,
            range,
            fallbackLanguage,
            sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedEntitySpeakAsync(
        EntityUid source,
        string message,
        string wrappedMessage,
        SpeechVerbPrototype speech,
        string speechVerbLocKey,
        string escapedName,
        ChatTransmitRange range,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _wh40kChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.Local);
        await RunOnMainThreadAsync(() =>
        {
            if (!CanDispatchFromSource(source))
                return;

            if (translationDispatch.ImmediateTranslation == null)
            {
                var serverMessageId = translationDispatch.PendingTranslation != null
                    ? (uint?) _wh40kChatTranslation.AllocateMessageId()
                    : null;

                if (translationDispatch.PendingTranslation != null)
                {
                    var placeholderTranslation = WH40KChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                    var placeholderWrappedCache = new Dictionary<(string?, string?), string>();

                    foreach (var (session, data) in GetRecipients(source, VoiceRange))
                    {
                        var entRange = MessageRangeCheck(session, data, range);
                        if (entRange == MessageRangeCheckResult.Disallowed)
                            continue;

                        if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                        {
                            _chatManager.ChatMessageToOne(
                                ChatChannel.Local,
                                message,
                                wrappedMessage,
                                source,
                                entRange == MessageRangeCheckResult.HideChat,
                                session.Channel,
                                serverMessageId: serverMessageId);
                            continue;
                        }

                        var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                        if (!placeholderWrappedCache.TryGetValue(cacheKey, out var initialWrapped))
                        {
                            var preserveOriginal = IsSourceAuthorSession(source, session);
                            initialWrapped = WH40KChatTranslationFormatting.BuildEntitySayWrappedMessage(
                                _wh40kPlayerCulture,
                                session,
                                escapedName,
                                speech,
                                speechVerbLocKey,
                                placeholderTranslation.OriginalText,
                                placeholderTranslation.SourceLanguage,
                                WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                                    placeholderTranslation,
                                    message,
                                    fallbackLanguage,
                                    recipientLanguage,
                                    preserveOriginal));
                            placeholderWrappedCache[cacheKey] = initialWrapped;
                        }

                        _chatManager.ChatMessageToOne(
                            ChatChannel.Local,
                            message,
                            initialWrapped,
                            source,
                            entRange == MessageRangeCheckResult.HideChat,
                            session.Channel,
                            serverMessageId: serverMessageId);
                    }

                    _replay.RecordServerMessage(new ChatMessage(
                        ChatChannel.Local,
                        message,
                        wrappedMessage,
                        GetNetEntity(source),
                        null,
                        MessageRangeHideChatForReplay(range),
                        serverMessageId: serverMessageId));

                    if (serverMessageId is { } pendingUpdateMessageId)
                    {
                        ObserveTranslationTask(DispatchDelayedEntitySpeakUpdateAsync(
                            pendingUpdateMessageId,
                            source,
                            message,
                            speech,
                            speechVerbLocKey,
                            escapedName,
                            range,
                            fallbackLanguage,
                            translationDispatch.PendingTranslation));
                    }

                    return;
                }

                SendInVoiceRange(ChatChannel.Local, message, wrappedMessage, source, range, serverMessageId: serverMessageId);

                if (translationDispatch.PendingTranslation != null && serverMessageId is { } delayedMessageId)
                {
                    ObserveTranslationTask(DispatchDelayedEntitySpeakUpdateAsync(
                        delayedMessageId,
                        source,
                        message,
                        speech,
                        speechVerbLocKey,
                        escapedName,
                        range,
                        fallbackLanguage,
                        translationDispatch.PendingTranslation));
                }

                return;
            }

            var wrappedCache = new Dictionary<(string?, string?), string>();
            foreach (var (session, data) in GetRecipients(source, VoiceRange))
            {
                var entRange = MessageRangeCheck(session, data, range);
                if (entRange == MessageRangeCheckResult.Disallowed)
                    continue;

                if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                {
                    _chatManager.ChatMessageToOne(
                        ChatChannel.Local,
                        message,
                        wrappedMessage,
                        source,
                        entRange == MessageRangeCheckResult.HideChat,
                        session.Channel);
                    continue;
                }

                var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var translatedWrapped))
                {
                    var preserveOriginal = IsSourceAuthorSession(source, session);
                    var visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
                        translationDispatch.ImmediateTranslation,
                        message,
                        fallbackLanguage,
                        recipientLanguage,
                        preserveOriginal);
                    translatedWrapped = WH40KChatTranslationFormatting.BuildEntitySayWrappedMessage(
                        _wh40kPlayerCulture,
                        session,
                        escapedName,
                        speech,
                        speechVerbLocKey,
                        visibleText,
                        translationDispatch.ImmediateTranslation.SourceLanguage,
                        WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                            translationDispatch.ImmediateTranslation,
                            message,
                            fallbackLanguage,
                            recipientLanguage,
                            preserveOriginal));
                    wrappedCache[cacheKey] = translatedWrapped;
                }

                _chatManager.ChatMessageToOne(
                    ChatChannel.Local,
                    message,
                    translatedWrapped,
                    source,
                    entRange == MessageRangeCheckResult.HideChat,
                    session.Channel);
            }

            _replay.RecordServerMessage(new ChatMessage(
                ChatChannel.Local,
                message,
                wrappedMessage,
                GetNetEntity(source),
                null,
                MessageRangeHideChatForReplay(range)));
        });
    }

    private async Task DispatchDelayedEntitySpeakUpdateAsync(
        uint serverMessageId,
        EntityUid source,
        string message,
        SpeechVerbPrototype speech,
        string speechVerbLocKey,
        string escapedName,
        ChatTransmitRange range,
        string? fallbackLanguage,
        Task<WH40KChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunOnMainThreadAsync(() =>
        {
            if (!CanDispatchFromSource(source))
                return;

            var wrappedCache = new Dictionary<(string?, string?), (string? VisibleText, string? Wrapped)>();
            foreach (var (session, data) in GetRecipients(source, VoiceRange))
            {
                var entRange = MessageRangeCheck(session, data, range);
                if (entRange == MessageRangeCheckResult.Disallowed)
                    continue;

                if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var cached))
                {
                    var preserveOriginal = IsSourceAuthorSession(source, session);
                    var visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
                        translation,
                        message,
                        fallbackLanguage,
                        recipientLanguage,
                        preserveOriginal);
                    if (!ShouldSendLateTranslationUpdate(message, visibleText))
                    {
                        wrappedCache[cacheKey] = (null, null);
                        continue;
                    }

                    var translatedWrapped = WH40KChatTranslationFormatting.BuildEntitySayWrappedMessage(
                        _wh40kPlayerCulture,
                        session,
                        escapedName,
                        speech,
                        speechVerbLocKey,
                        visibleText,
                        translation.SourceLanguage,
                        WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                            translation,
                            message,
                            fallbackLanguage,
                            recipientLanguage,
                            preserveOriginal));
                    cached = (visibleText, translatedWrapped);
                    wrappedCache[cacheKey] = cached;
                }

                if (cached.Wrapped == null)
                    continue;

                _chatManager.UpdateChatMessageToOne(
                    ChatChannel.Local,
                    cached.VisibleText!,
                    cached.Wrapped,
                    source,
                    entRange == MessageRangeCheckResult.HideChat,
                    session.Channel,
                    serverMessageId);
            }
        });
    }

    private bool TryDispatchTranslatedEntityWhisper(
        EntityUid source,
        string message,
        string obfuscatedMessage,
        string wrappedMessage,
        string wrappedObfuscatedMessage,
        string wrappedUnknownMessage,
        ChatTransmitRange range,
        RadioChannelPrototype? channel,
        string escapedName,
        string escapedIdentityName)
    {
        if (!_wh40kChatTranslation.IsConfiguredForChannel(ChatChannel.Whisper))
            return false;

        var fallbackLanguage = _wh40kPlayerCulture.ResolveLanguageCode(source);
        var sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
        {
            return false;
        }

        ObserveTranslationTask(DispatchTranslatedEntityWhisperAsync(
            source,
            message,
            obfuscatedMessage,
            wrappedMessage,
            wrappedObfuscatedMessage,
            wrappedUnknownMessage,
            range,
            channel,
            escapedName,
            escapedIdentityName,
            fallbackLanguage,
            sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedEntityWhisperAsync(
        EntityUid source,
        string message,
        string obfuscatedMessage,
        string wrappedMessage,
        string wrappedObfuscatedMessage,
        string wrappedUnknownMessage,
        ChatTransmitRange range,
        RadioChannelPrototype? channel,
        string escapedName,
        string escapedIdentityName,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _wh40kChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.Whisper);
        await RunOnMainThreadAsync(() =>
        {
            if (!CanDispatchFromSource(source))
                return;

            uint? serverMessageId = null;
            if (translationDispatch.ImmediateTranslation == null && translationDispatch.PendingTranslation != null)
                serverMessageId = _wh40kChatTranslation.AllocateMessageId();

            var initialTranslation = translationDispatch.ImmediateTranslation;
            if (initialTranslation == null && translationDispatch.PendingTranslation != null)
                initialTranslation = WH40KChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);

            DispatchWhisperRecipients(
                source,
                message,
                obfuscatedMessage,
                wrappedMessage,
                wrappedObfuscatedMessage,
                wrappedUnknownMessage,
                range,
                channel,
                initialTranslation,
                escapedName,
                escapedIdentityName,
                fallbackLanguage,
                serverMessageId);

            if (translationDispatch.ImmediateTranslation == null && translationDispatch.PendingTranslation != null && serverMessageId is { } delayedMessageId)
            {
                ObserveTranslationTask(DispatchDelayedEntityWhisperUpdateAsync(
                    delayedMessageId,
                    source,
                    message,
                    range,
                    channel,
                    escapedName,
                    escapedIdentityName,
                    fallbackLanguage,
                    translationDispatch.PendingTranslation));
            }

            _replay.RecordServerMessage(new ChatMessage(
                ChatChannel.Whisper,
                message,
                wrappedMessage,
                GetNetEntity(source),
                null,
                MessageRangeHideChatForReplay(range),
                speechTransport: channel == null ? ChatSpeechTransport.Direct : ChatSpeechTransport.Radio,
                serverMessageId: serverMessageId));
        });
    }

    private async Task DispatchDelayedEntityWhisperUpdateAsync(
        uint serverMessageId,
        EntityUid source,
        string message,
        ChatTransmitRange range,
        RadioChannelPrototype? channel,
        string escapedName,
        string escapedIdentityName,
        string? fallbackLanguage,
        Task<WH40KChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunOnMainThreadAsync(() =>
        {
            DispatchWhisperRecipients(
                source,
                message,
                message,
                string.Empty,
                string.Empty,
                string.Empty,
                range,
                channel,
                translation,
                escapedName,
                escapedIdentityName,
                fallbackLanguage,
                serverMessageId,
                update: true);
        });
    }

    private void DispatchWhisperRecipients(
        EntityUid source,
        string message,
        string obfuscatedMessage,
        string wrappedMessage,
        string wrappedObfuscatedMessage,
        string wrappedUnknownMessage,
        ChatTransmitRange range,
        RadioChannelPrototype? channel,
        WH40KChatTranslationPayload? translation,
        string escapedName,
        string escapedIdentityName,
        string? fallbackLanguage,
        uint? serverMessageId = null,
        bool update = false)
    {
        if (!CanDispatchFromSource(source))
            return;

        Dictionary<(string?, string?), (string? VisibleText, string? Wrapped)>? whisperCache = translation != null
            ? new()
            : null;

        foreach (var (session, data) in GetRecipients(source, WhisperMuffledRange))
        {
            if (session.AttachedEntity is not { Valid: true } listener)
                continue;

            if (MessageRangeCheck(session, data, range) != MessageRangeCheckResult.Full)
                continue;

            if (data.Range <= WhisperClearRange || data.Observer)
            {
                var visibleText = message;
                var finalWrapped = wrappedMessage;
                if (translation != null)
                {
                    if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    {
                        if (update)
                            continue;
                    }
                    else
                    {
                        var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                        if (!whisperCache!.TryGetValue(cacheKey, out var cached))
                        {
                            var preserveOriginal = IsSourceAuthorSession(source, session);
                            visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
                                translation,
                                message,
                                fallbackLanguage,
                                recipientLanguage,
                                preserveOriginal);
                            if (update && !ShouldSendLateTranslationUpdate(message, visibleText))
                            {
                                whisperCache[cacheKey] = (null, null);
                                continue;
                            }

                            finalWrapped = WH40KChatTranslationFormatting.BuildEntityWhisperWrappedMessage(
                                _wh40kPlayerCulture,
                                session,
                                escapedName,
                                visibleText,
                                translation.SourceLanguage,
                                WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                                    translation,
                                    message,
                                    fallbackLanguage,
                                    recipientLanguage,
                                    preserveOriginal));
                            whisperCache[cacheKey] = (visibleText, finalWrapped);
                        }
                        else if (cached.Wrapped == null)
                        {
                            continue;
                        }
                        else
                        {
                            visibleText = cached.VisibleText!;
                            finalWrapped = cached.Wrapped;
                        }
                    }
                }

                if (update && serverMessageId is { } delayedMessageId && translation != null)
                {
                    _chatManager.UpdateChatMessageToOne(
                        ChatChannel.Whisper,
                        visibleText,
                        finalWrapped,
                        source,
                        false,
                        session.Channel,
                        delayedMessageId,
                        speechTransport: channel == null ? ChatSpeechTransport.Direct : ChatSpeechTransport.Radio);
                }
                else
                {
                    _chatManager.ChatMessageToOne(
                        ChatChannel.Whisper,
                        message,
                        finalWrapped,
                        source,
                        false,
                        session.Channel,
                        speechTransport: channel == null ? ChatSpeechTransport.Direct : ChatSpeechTransport.Radio,
                        serverMessageId: serverMessageId);
                }
            }
            else if (update)
            {
                continue;
            }
            else if (_examineSystem.InRangeUnOccluded(source, listener, WhisperMuffledRange))
            {
                _chatManager.ChatMessageToOne(
                    ChatChannel.Whisper,
                    obfuscatedMessage,
                    wrappedObfuscatedMessage,
                    source,
                    false,
                    session.Channel,
                    speechTransport: channel == null ? ChatSpeechTransport.Direct : ChatSpeechTransport.Radio,
                    serverMessageId: serverMessageId);
            }
            else
            {
                _chatManager.ChatMessageToOne(
                    ChatChannel.Whisper,
                    obfuscatedMessage,
                    wrappedUnknownMessage,
                    source,
                    false,
                    session.Channel,
                    speechTransport: channel == null ? ChatSpeechTransport.Direct : ChatSpeechTransport.Radio,
                    serverMessageId: serverMessageId);
            }
        }
    }

    private bool TryDispatchTranslatedLooc(
        EntityUid source,
        string message,
        string wrappedMessage,
        ChatTransmitRange range,
        NetUserId author,
        string escapedName)
    {
        if (!_wh40kChatTranslation.IsConfiguredForChannel(ChatChannel.LOOC))
            return false;

        var fallbackLanguage = _wh40kPlayerCulture.ResolveLanguageCode(source);
        var sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
        {
            return false;
        }

        ObserveTranslationTask(DispatchTranslatedLoocAsync(
            source,
            message,
            wrappedMessage,
            range,
            author,
            escapedName,
            fallbackLanguage,
            sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedLoocAsync(
        EntityUid source,
        string message,
        string wrappedMessage,
        ChatTransmitRange range,
        NetUserId author,
        string escapedName,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _wh40kChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.LOOC);
        await RunOnMainThreadAsync(() =>
        {
            if (!CanDispatchFromSource(source))
                return;

            if (translationDispatch.ImmediateTranslation == null)
            {
                var serverMessageId = translationDispatch.PendingTranslation != null
                    ? (uint?) _wh40kChatTranslation.AllocateMessageId()
                    : null;

                if (translationDispatch.PendingTranslation != null)
                {
                    var placeholderTranslation = WH40KChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                    var placeholderWrappedCache = new Dictionary<(string?, string?), string>();

                    foreach (var (session, data) in GetRecipients(source, VoiceRange))
                    {
                        var entRange = MessageRangeCheck(session, data, range);
                        if (entRange == MessageRangeCheckResult.Disallowed)
                            continue;

                        if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                        {
                            _chatManager.ChatMessageToOne(
                                ChatChannel.LOOC,
                                message,
                                wrappedMessage,
                                source,
                                entRange == MessageRangeCheckResult.HideChat,
                                session.Channel,
                                author: author,
                                serverMessageId: serverMessageId);
                            continue;
                        }

                        var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                        if (!placeholderWrappedCache.TryGetValue(cacheKey, out var initialWrapped))
                        {
                            var preserveOriginal = session.UserId == author;
                            initialWrapped = WH40KChatTranslationFormatting.BuildLoocWrappedMessage(
                                _wh40kPlayerCulture,
                                session,
                                escapedName,
                                placeholderTranslation.OriginalText,
                                placeholderTranslation.SourceLanguage,
                                WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                                    placeholderTranslation,
                                    message,
                                    fallbackLanguage,
                                    recipientLanguage,
                                    preserveOriginal));
                            placeholderWrappedCache[cacheKey] = initialWrapped;
                        }

                        _chatManager.ChatMessageToOne(
                            ChatChannel.LOOC,
                            message,
                            initialWrapped,
                            source,
                            entRange == MessageRangeCheckResult.HideChat,
                            session.Channel,
                            author: author,
                            serverMessageId: serverMessageId);
                    }

                    _replay.RecordServerMessage(new ChatMessage(
                        ChatChannel.LOOC,
                        message,
                        wrappedMessage,
                        GetNetEntity(source),
                        null,
                        MessageRangeHideChatForReplay(range),
                        serverMessageId: serverMessageId));

                    if (serverMessageId is { } pendingUpdateMessageId)
                    {
                        ObserveTranslationTask(DispatchDelayedLoocUpdateAsync(
                            pendingUpdateMessageId,
                            source,
                            message,
                            range,
                            author,
                            escapedName,
                            fallbackLanguage,
                            translationDispatch.PendingTranslation));
                    }

                    return;
                }

                SendInVoiceRange(ChatChannel.LOOC, message, wrappedMessage, source, range, author, serverMessageId);

                if (translationDispatch.PendingTranslation != null && serverMessageId is { } delayedMessageId)
                {
                    ObserveTranslationTask(DispatchDelayedLoocUpdateAsync(
                        delayedMessageId,
                        source,
                        message,
                        range,
                        author,
                        escapedName,
                        fallbackLanguage,
                        translationDispatch.PendingTranslation));
                }

                return;
            }

            var wrappedCache = new Dictionary<(string?, string?), string>();
            foreach (var (session, data) in GetRecipients(source, VoiceRange))
            {
                var entRange = MessageRangeCheck(session, data, range);
                if (entRange == MessageRangeCheckResult.Disallowed)
                    continue;

                if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                {
                    _chatManager.ChatMessageToOne(
                        ChatChannel.LOOC,
                        message,
                        wrappedMessage,
                        source,
                        entRange == MessageRangeCheckResult.HideChat,
                        session.Channel,
                        author: author);
                    continue;
                }

                var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var translatedWrapped))
                {
                    var preserveOriginal = session.UserId == author;
                    var visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
                        translationDispatch.ImmediateTranslation,
                        message,
                        fallbackLanguage,
                        recipientLanguage,
                        preserveOriginal);
                    translatedWrapped = WH40KChatTranslationFormatting.BuildLoocWrappedMessage(
                        _wh40kPlayerCulture,
                        session,
                        escapedName,
                        visibleText,
                        translationDispatch.ImmediateTranslation.SourceLanguage,
                        WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                            translationDispatch.ImmediateTranslation,
                            message,
                            fallbackLanguage,
                            recipientLanguage,
                            preserveOriginal));
                    wrappedCache[cacheKey] = translatedWrapped;
                }

                _chatManager.ChatMessageToOne(
                    ChatChannel.LOOC,
                    message,
                    translatedWrapped,
                    source,
                    entRange == MessageRangeCheckResult.HideChat,
                    session.Channel,
                    author: author);
            }

            _replay.RecordServerMessage(new ChatMessage(
                ChatChannel.LOOC,
                message,
                wrappedMessage,
                GetNetEntity(source),
                null,
                MessageRangeHideChatForReplay(range)));
        });
    }

    private async Task DispatchDelayedLoocUpdateAsync(
        uint serverMessageId,
        EntityUid source,
        string message,
        ChatTransmitRange range,
        NetUserId author,
        string escapedName,
        string? fallbackLanguage,
        Task<WH40KChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunOnMainThreadAsync(() =>
        {
            if (!CanDispatchFromSource(source))
                return;

            var wrappedCache = new Dictionary<(string?, string?), (string? VisibleText, string? Wrapped)>();
            foreach (var (session, data) in GetRecipients(source, VoiceRange))
            {
                var entRange = MessageRangeCheck(session, data, range);
                if (entRange == MessageRangeCheckResult.Disallowed)
                    continue;

                if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var cached))
                {
                    var preserveOriginal = session.UserId == author;
                    var visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
                        translation,
                        message,
                        fallbackLanguage,
                        recipientLanguage,
                        preserveOriginal);
                    if (!ShouldSendLateTranslationUpdate(message, visibleText))
                    {
                        wrappedCache[cacheKey] = (null, null);
                        continue;
                    }

                    var translatedWrapped = WH40KChatTranslationFormatting.BuildLoocWrappedMessage(
                        _wh40kPlayerCulture,
                        session,
                        escapedName,
                        visibleText,
                        translation.SourceLanguage,
                        WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                            translation,
                            message,
                            fallbackLanguage,
                            recipientLanguage,
                            preserveOriginal));
                    cached = (visibleText, translatedWrapped);
                    wrappedCache[cacheKey] = cached;
                }

                if (cached.Wrapped == null)
                    continue;

                _chatManager.UpdateChatMessageToOne(
                    ChatChannel.LOOC,
                    cached.VisibleText!,
                    cached.Wrapped,
                    source,
                    entRange == MessageRangeCheckResult.HideChat,
                    session.Channel,
                    serverMessageId,
                    author: author);
            }
        });
    }

    private bool TryDispatchTranslatedDeadChat(
        EntityUid source,
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool hideChat,
        bool fromAdmin,
        string playerName)
    {
        if (!_wh40kChatTranslation.IsConfiguredForChannel(ChatChannel.Dead))
            return false;

        var fallbackLanguage = _wh40kPlayerCulture.ResolveLanguageCode(player);
        var sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (!WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
        {
            return false;
        }

        ObserveTranslationTask(DispatchTranslatedDeadChatAsync(
            source,
            player,
            message,
            wrappedMessage,
            hideChat,
            fromAdmin,
            playerName,
            fallbackLanguage,
            sourceLanguage!));
        return true;
    }

    private async Task DispatchTranslatedDeadChatAsync(
        EntityUid source,
        ICommonSession player,
        string message,
        string wrappedMessage,
        bool hideChat,
        bool fromAdmin,
        string playerName,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _wh40kChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.Dead);
        await RunOnMainThreadAsync(() =>
        {
            if (translationDispatch.ImmediateTranslation == null)
            {
                var clients = GetDeadChatClients().ToList();
                var serverMessageId = translationDispatch.PendingTranslation != null
                    ? (uint?) _wh40kChatTranslation.AllocateMessageId()
                    : null;

                if (translationDispatch.PendingTranslation != null)
                {
                    var placeholderTranslation = WH40KChatTranslationPayload.CreatePlaceholder(message, sourceLanguage);
                    var placeholderWrappedCache = new Dictionary<(string?, string?), string>();

                    foreach (var session in GetDeadChatSessions())
                    {
                        if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                        {
                            _chatManager.ChatMessageToOne(
                                ChatChannel.Dead,
                                message,
                                wrappedMessage,
                                source,
                                hideChat,
                                session.Channel,
                                author: player.UserId,
                                serverMessageId: serverMessageId);
                            continue;
                        }

                        var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                        if (!placeholderWrappedCache.TryGetValue(cacheKey, out var initialWrapped))
                        {
                            var preserveOriginal = session.UserId == player.UserId;
                            initialWrapped = WH40KChatTranslationFormatting.BuildDeadWrappedMessage(
                                _wh40kPlayerCulture,
                                session,
                                fromAdmin,
                                playerName,
                                player.Channel.UserName,
                                placeholderTranslation.OriginalText,
                                placeholderTranslation.SourceLanguage,
                                WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                                    placeholderTranslation,
                                    message,
                                    fallbackLanguage,
                                    recipientLanguage,
                                    preserveOriginal));
                            placeholderWrappedCache[cacheKey] = initialWrapped;
                        }

                        _chatManager.ChatMessageToOne(
                            ChatChannel.Dead,
                            message,
                            initialWrapped,
                            source,
                            hideChat,
                            session.Channel,
                            author: player.UserId,
                            serverMessageId: serverMessageId);
                    }

                    _replay.RecordServerMessage(new ChatMessage(
                        ChatChannel.Dead,
                        message,
                        wrappedMessage,
                        GetNetEntity(source),
                        null,
                        hideChat,
                        serverMessageId: serverMessageId));

                    if (serverMessageId is { } pendingUpdateMessageId)
                    {
                        ObserveTranslationTask(DispatchDelayedDeadChatUpdateAsync(
                            pendingUpdateMessageId,
                            source,
                            player,
                            message,
                            hideChat,
                            fromAdmin,
                            playerName,
                            fallbackLanguage,
                            translationDispatch.PendingTranslation));
                    }

                    return;
                }

                _chatManager.ChatMessageToMany(ChatChannel.Dead, message, wrappedMessage, source, hideChat, true, clients, author: player.UserId, serverMessageId: serverMessageId);

                if (translationDispatch.PendingTranslation != null && serverMessageId is { } delayedMessageId)
                {
                    ObserveTranslationTask(DispatchDelayedDeadChatUpdateAsync(
                        delayedMessageId,
                        source,
                        player,
                        message,
                        hideChat,
                        fromAdmin,
                        playerName,
                        fallbackLanguage,
                        translationDispatch.PendingTranslation));
                }

                return;
            }

            var wrappedCache = new Dictionary<(string?, string?), string>();
            foreach (var session in GetDeadChatSessions())
            {
                if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                {
                    _chatManager.ChatMessageToOne(
                        ChatChannel.Dead,
                        message,
                        wrappedMessage,
                        source,
                        hideChat,
                        session.Channel,
                        author: player.UserId);
                    continue;
                }

                var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var translatedWrapped))
                {
                    var preserveOriginal = session.UserId == player.UserId;
                    var visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
                        translationDispatch.ImmediateTranslation,
                        message,
                        fallbackLanguage,
                        recipientLanguage,
                        preserveOriginal);
                    translatedWrapped = WH40KChatTranslationFormatting.BuildDeadWrappedMessage(
                        _wh40kPlayerCulture,
                        session,
                        fromAdmin,
                        playerName,
                        player.Channel.UserName,
                        visibleText,
                        translationDispatch.ImmediateTranslation.SourceLanguage,
                        WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                            translationDispatch.ImmediateTranslation,
                            message,
                            fallbackLanguage,
                            recipientLanguage,
                            preserveOriginal));
                    wrappedCache[cacheKey] = translatedWrapped;
                }

                _chatManager.ChatMessageToOne(
                    ChatChannel.Dead,
                    message,
                    translatedWrapped,
                    source,
                    hideChat,
                    session.Channel,
                    author: player.UserId);
            }

            _replay.RecordServerMessage(new ChatMessage(
                ChatChannel.Dead,
                message,
                wrappedMessage,
                GetNetEntity(source),
                null,
                hideChat));
        });
    }

    private async Task DispatchDelayedDeadChatUpdateAsync(
        uint serverMessageId,
        EntityUid source,
        ICommonSession player,
        string message,
        bool hideChat,
        bool fromAdmin,
        string playerName,
        string? fallbackLanguage,
        Task<WH40KChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunOnMainThreadAsync(() =>
        {
            var wrappedCache = new Dictionary<(string?, string?), (string? VisibleText, string? Wrapped)>();
            foreach (var session in GetDeadChatSessions())
            {
                if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                    continue;

                var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
                if (!wrappedCache.TryGetValue(cacheKey, out var cached))
                {
                    var preserveOriginal = session.UserId == player.UserId;
                    var visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
                        translation,
                        message,
                        fallbackLanguage,
                        recipientLanguage,
                        preserveOriginal);
                    if (!ShouldSendLateTranslationUpdate(message, visibleText))
                    {
                        wrappedCache[cacheKey] = (null, null);
                        continue;
                    }

                    var translatedWrapped = WH40KChatTranslationFormatting.BuildDeadWrappedMessage(
                        _wh40kPlayerCulture,
                        session,
                        fromAdmin,
                        playerName,
                        player.Channel.UserName,
                        visibleText,
                        translation.SourceLanguage,
                        WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                            translation,
                            message,
                            fallbackLanguage,
                            recipientLanguage,
                            preserveOriginal));
                    cached = (visibleText, translatedWrapped);
                    wrappedCache[cacheKey] = cached;
                }

                if (cached.Wrapped == null)
                    continue;

                _chatManager.UpdateChatMessageToOne(
                    ChatChannel.Dead,
                    cached.VisibleText!,
                    cached.Wrapped,
                    source,
                    hideChat,
                    session.Channel,
                    serverMessageId,
                    author: player.UserId);
            }
        });
    }

    private List<ICommonSession> GetDeadChatSessions()
    {
        return _playerManager.Sessions
            .Where(session =>
            {
                if (_adminManager.IsAdmin(session))
                    return true;

                return session.AttachedEntity is { Valid: true } entity && HasComp<GhostComponent>(entity);
            })
            .Cast<ICommonSession>()
            .ToList();
    }

    private static bool ShouldSendLateTranslationUpdate(string originalMessage, string translatedMessage)
    {
        return !string.Equals(originalMessage, translatedMessage, StringComparison.Ordinal);
    }

    private bool IsSourceAuthorSession(EntityUid source, ICommonSession session)
    {
        return TryComp(source, out ActorComponent? actor) &&
               actor.PlayerSession.UserId == session.UserId;
    }

    private bool CanDispatchFromSource(EntityUid source)
    {
        return source.Valid && TryComp(source, out TransformComponent? _);
    }

    private void ObserveTranslationTask(Task task)
    {
        _ = ObserveTranslationTaskAsync(task);
    }

    private async Task ObserveTranslationTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Log.Error($"WH40K chat translation task failed: {e}");
        }
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
