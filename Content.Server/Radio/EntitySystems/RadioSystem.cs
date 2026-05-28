using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server._WH40K.Chat.Translation;
using Content.Server._WH40K.Localizations;
using Content.Server.Power.Components;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Robust.Shared.Map;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;
using Content.Shared._WH40K.Chat.Translation;

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     This system handles intrinsic radios and the general process of converting radio messages into chat messages.
/// </summary>
public sealed partial class RadioSystem : EntitySystem
{
    [Dependency] private  INetManager _netMan = default!;
    [Dependency] private  IReplayRecordingManager _replay = default!;
    [Dependency] private  IAdminLogManager _adminLogger = default!;
    [Dependency] private  IPrototypeManager _prototype = default!;
    [Dependency] private  IRobustRandom _random = default!;
    [Dependency] private  ChatSystem _chat = default!;
    [Dependency] private  IWH40KChatTranslationService _wh40kChatTranslation = default!;
    [Dependency] private  WH40KPlayerCultureTracker _wh40kPlayerCulture = default!;
    [Dependency] private  ITaskManager _wh40kTaskManager = default!;

    // set used to prevent radio feedback loops.
    private readonly HashSet<string> _messages = new();

    private EntityQuery<TelecomExemptComponent> _exemptQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IntrinsicRadioReceiverComponent, RadioReceiveEvent>(OnIntrinsicReceive);
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeEvent>(OnIntrinsicSpeak);

        _exemptQuery = GetEntityQuery<TelecomExemptComponent>();
    }

    private void OnIntrinsicSpeak(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioMessage(uid, args.Message, args.Channel, uid);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    private void OnIntrinsicReceive(EntityUid uid, IntrinsicRadioReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (TryComp(uid, out ActorComponent? actor))
            _netMan.ServerSendMessage(args.ChatMsg, actor.PlayerSession.Channel);
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(EntityUid messageSource, string message, ProtoId<RadioChannelPrototype> channel, EntityUid radioSource, bool escapeMarkup = true)
    {
        SendRadioMessage(messageSource, message, _prototype.Index(channel), radioSource, escapeMarkup: escapeMarkup);
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    /// <param name="messageSource">Entity that spoke the message</param>
    /// <param name="radioSource">Entity that picked up the message and will send it, e.g. headset</param>
    public void SendRadioMessage(EntityUid messageSource, string message, RadioChannelPrototype channel, EntityUid radioSource, bool escapeMarkup = true)
    {
        var fallbackLanguage = _wh40kPlayerCulture.ResolveLanguageCode(messageSource);
        var sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(message, fallbackLanguage);
        if (escapeMarkup &&
            _wh40kChatTranslation.IsConfiguredForChannel(ChatChannel.Radio) &&
            WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
        {
            _ = DispatchTranslatedRadioMessageAsync(messageSource, message, channel, radioSource, fallbackLanguage, sourceLanguage!);
            return;
        }

        DispatchRadioMessageCore(messageSource, message, channel, radioSource, escapeMarkup, null);
    }

    private async Task DispatchTranslatedRadioMessageAsync(
        EntityUid messageSource,
        string message,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        string? fallbackLanguage,
        string sourceLanguage)
    {
        var translationDispatch = await _wh40kChatTranslation.TranslateWithSoftHoldAsync(message, fallbackLanguage, ChatChannel.Radio);
        await RunOnMainThreadAsync(() =>
        {
            if (translationDispatch.ImmediateTranslation == null)
            {
                var serverMessageId = translationDispatch.PendingTranslation != null
                    ? (uint?) _wh40kChatTranslation.AllocateMessageId()
                    : null;

                var initialTranslation = translationDispatch.PendingTranslation != null
                    ? WH40KChatTranslationPayload.CreatePlaceholder(message, sourceLanguage)
                    : null;

                DispatchRadioMessageCore(messageSource, message, channel, radioSource, true, initialTranslation, serverMessageId, fallbackLanguage);

                if (translationDispatch.PendingTranslation != null && serverMessageId is { } delayedMessageId)
                {
                    _ = DispatchDelayedRadioMessageUpdateAsync(
                        delayedMessageId,
                        messageSource,
                        message,
                        channel,
                        radioSource,
                        fallbackLanguage,
                        translationDispatch.PendingTranslation);
                }

                return;
            }

            DispatchRadioMessageCore(messageSource, message, channel, radioSource, true, translationDispatch.ImmediateTranslation, null, fallbackLanguage);
        });
    }

    private async Task DispatchDelayedRadioMessageUpdateAsync(
        uint serverMessageId,
        EntityUid messageSource,
        string message,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        string? senderLanguage,
        Task<WH40KChatTranslationPayload?> pendingTranslation)
    {
        var translation = await pendingTranslation;
        if (translation == null)
            return;

        await RunOnMainThreadAsync(() =>
        {
            DispatchRadioMessageUpdate(messageSource, message, channel, radioSource, translation, serverMessageId, senderLanguage);
        });
    }

    private void DispatchRadioMessageCore(
        EntityUid messageSource,
        string message,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        bool escapeMarkup,
        WH40KChatTranslationPayload? translation,
        uint? serverMessageId = null,
        string? senderLanguage = null)
    {
        // TODO if radios ever garble / modify messages, feedback-prevention needs to be handled better than this.
        if (!_messages.Add(message))
            return;

        try
        {
            var evt = new TransformSpeakerNameEvent(messageSource, MetaData(messageSource).EntityName);
            RaiseLocalEvent(messageSource, evt);

            var name = evt.VoiceName;
            name = FormattedMessage.EscapeText(name);

            SpeechVerbPrototype speech;
            if (evt.SpeechVerb != null && _prototype.Resolve(evt.SpeechVerb, out var evntProto))
                speech = evntProto;
            else
                speech = _chat.GetSpeechVerb(messageSource, message);

            var content = escapeMarkup
                ? FormattedMessage.EscapeText(message)
                : message;

            var speechVerbLocKey = _random.Pick(speech.SpeechVerbStrings);
            var wrappedMessage = Loc.GetString(
                speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
                ("color", channel.Color),
                ("fontType", speech.FontId),
                ("fontSize", speech.FontSize),
                ("verb", Loc.GetString(speechVerbLocKey)),
                ("channel", $"\\[{channel.LocalizedName}\\]"),
                ("name", name),
                ("message", content));

            // most radios are relayed to chat, so lets parse the chat message beforehand
            var chat = new ChatMessage(
                ChatChannel.Radio,
                message,
                wrappedMessage,
                NetEntity.Invalid,
                null,
                speechTransport: ChatSpeechTransport.Radio,
                serverMessageId: serverMessageId);
            var chatMsg = new MsgChatMessage { Message = chat };

            var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
            RaiseLocalEvent(ref sendAttemptEv);
            RaiseLocalEvent(radioSource, ref sendAttemptEv);
            var canSend = !sendAttemptEv.Cancelled;

            var sourceMapId = Transform(radioSource).MapID;
            var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
            var sourceServerExempt = _exemptQuery.HasComp(radioSource);

            var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();
            Dictionary<(string?, string?), MsgChatMessage?>? radioCache = translation != null ? new() : null;
            while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
            {
                if (!radio.ReceiveAllChannels)
                {
                    if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                                 !intercom.SupportedChannels.Contains(channel.ID)))
                        continue;
                }

                if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                    continue;

                // don't need telecom server for long range channels or handheld radios and intercoms
                var needServer = !channel.LongRange && !sourceServerExempt;
                if (needServer && !hasActiveServer)
                    continue;

                // check if message can be sent to specific receiver
                var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
                RaiseLocalEvent(ref attemptEv);
                RaiseLocalEvent(receiver, ref attemptEv);
                if (attemptEv.Cancelled)
                    continue;

                var receiverChatMsg = chatMsg;
                if (translation != null &&
                    TryCreateTranslatedChatMsgForReceiver(
                        receiver,
                        message,
                        messageSource,
                        channel,
                        speech,
                        speechVerbLocKey,
                        name,
                        translation,
                        serverMessageId,
                        senderLanguage,
                        radioCache!,
                        out var translatedChatMsg))
                {
                    receiverChatMsg = translatedChatMsg;
                }

                var ev = new RadioReceiveEvent(message, messageSource, channel, radioSource, receiverChatMsg);
                RaiseLocalEvent(receiver, ref ev);
            }

            if (name != Name(messageSource))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} as {name} on {channel.LocalizedName}: {message}");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {message}");

            _replay.RecordServerMessage(chat);
        }
        finally
        {
            _messages.Remove(message);
        }
    }

    private void DispatchRadioMessageUpdate(
        EntityUid messageSource,
        string originalMessage,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        WH40KChatTranslationPayload translation,
        uint serverMessageId,
        string? senderLanguage)
    {
        var evt = new TransformSpeakerNameEvent(messageSource, MetaData(messageSource).EntityName);
        RaiseLocalEvent(messageSource, evt);

        var name = FormattedMessage.EscapeText(evt.VoiceName);

        SpeechVerbPrototype speech;
        if (evt.SpeechVerb != null && _prototype.Resolve(evt.SpeechVerb, out var eventProto))
            speech = eventProto;
        else
            speech = _chat.GetSpeechVerb(messageSource, originalMessage);

        var speechVerbLocKey = _random.Pick(speech.SpeechVerbStrings);

        var sourceMapId = Transform(radioSource).MapID;
        var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
        var sourceServerExempt = _exemptQuery.HasComp(radioSource);

        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();
        var radioCache = new Dictionary<(string?, string?), (string? VisibleText, string? Wrapped)>();
        while (radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                continue;

            var needServer = !channel.LongRange && !sourceServerExempt;
            if (needServer && !hasActiveServer)
                continue;

            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            if (!TryGetRecipientSession(receiver, out var session))
                continue;

            if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
                continue;

            var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
            if (!radioCache.TryGetValue(cacheKey, out var cached))
            {
                var preserveOriginal = IsMessageAuthorSession(messageSource, session);
                var visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
                    translation,
                    originalMessage,
                    senderLanguage,
                    recipientLanguage,
                    preserveOriginal);
                if (string.Equals(visibleText, originalMessage, StringComparison.Ordinal))
                {
                    radioCache[cacheKey] = (null, null);
                    continue;
                }

                var wrappedMessage = WH40KChatTranslationFormatting.BuildRadioWrappedMessage(
                    _wh40kPlayerCulture,
                    session,
                    channel,
                    name,
                    speech,
                    speechVerbLocKey,
                    visibleText,
                    translation.SourceLanguage,
                    WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                        translation,
                        originalMessage,
                        senderLanguage,
                        recipientLanguage,
                        preserveOriginal));
                cached = (visibleText, wrappedMessage);
                radioCache[cacheKey] = cached;
            }

            if (cached.Wrapped == null)
                continue;

            var update = new MsgUpdateChatMessage
            {
                Message = new ChatMessage(
                    ChatChannel.Radio,
                    cached.VisibleText!,
                    cached.Wrapped,
                    NetEntity.Invalid,
                    null,
                    speechTransport: ChatSpeechTransport.Radio,
                    serverMessageId: serverMessageId)
            };

            _netMan.ServerSendMessage(update, session.Channel);
        }
    }

    private bool TryCreateTranslatedChatMsgForReceiver(
        EntityUid receiver,
        string originalMessage,
        EntityUid messageSource,
        RadioChannelPrototype channel,
        SpeechVerbPrototype speech,
        string speechVerbLocKey,
        string escapedName,
        WH40KChatTranslationPayload translation,
        uint? serverMessageId,
        string? senderLanguage,
        Dictionary<(string?, string?), MsgChatMessage?> radioCache,
        out MsgChatMessage chatMsg)
    {
        chatMsg = default!;

        if (!TryGetRecipientSession(receiver, out var session))
            return false;

        if (!_wh40kPlayerCulture.TryResolveChatLanguageCode(session, out var recipientLanguage))
            return false;

        var cacheKey = (_wh40kPlayerCulture.GetCulture(session), recipientLanguage);
        if (radioCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached == null)
                return false;

            chatMsg = cached;
            return true;
        }

        var preserveOriginal = IsMessageAuthorSession(messageSource, session);
        var visibleText = WH40KChatTranslationFormatting.ResolveVisibleText(
            translation,
            originalMessage,
            senderLanguage,
            recipientLanguage,
            preserveOriginal);
        var wrappedMessage = WH40KChatTranslationFormatting.BuildRadioWrappedMessage(
            _wh40kPlayerCulture,
            session,
            channel,
            escapedName,
            speech,
            speechVerbLocKey,
            visibleText,
            translation.SourceLanguage,
            WH40KChatTranslationFormatting.ResolveOriginalTextForTag(
                translation,
                originalMessage,
                senderLanguage,
                recipientLanguage,
                preserveOriginal));

        chatMsg = new MsgChatMessage
        {
            Message = new ChatMessage(
                ChatChannel.Radio,
                originalMessage,
                wrappedMessage,
                NetEntity.Invalid,
                null,
                speechTransport: ChatSpeechTransport.Radio,
                serverMessageId: serverMessageId)
        };

        radioCache[cacheKey] = chatMsg;
        return true;
    }

    private bool IsMessageAuthorSession(EntityUid messageSource, ICommonSession session)
    {
        return TryComp(messageSource, out ActorComponent? actor) &&
               actor.PlayerSession.UserId == session.UserId;
    }

    private bool TryGetRecipientSession(EntityUid receiver, out ICommonSession session)
    {
        session = default!;

        if (TryComp(receiver, out ActorComponent? actor))
        {
            session = actor.PlayerSession;
            return true;
        }

        var parent = Transform(receiver).ParentUid;
        if (!parent.IsValid() || !TryComp(parent, out ActorComponent? parentActor))
            return false;

        session = parentActor.PlayerSession;
        return true;
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

    /// <inheritdoc cref="TelecomServerComponent"/>
    private bool HasActiveServer(MapId mapId, string channelId)
    {
        var servers = EntityQuery<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        foreach (var (_, keys, power, transform) in servers)
        {
            if (transform.MapID == mapId &&
                power.Powered &&
                keys.Channels.Contains(channelId))
            {
                return true;
            }
        }
        return false;
    }
}
