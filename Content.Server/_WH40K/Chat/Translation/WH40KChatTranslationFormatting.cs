using Content.Server._WH40K.Localizations;
using Content.Shared._WH40K.Chat.Translation;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Chat.Translation;

public static class WH40KChatTranslationFormatting
{
    public static bool ShouldPreserveOriginalText(string? senderLanguage, string? recipientLanguage)
    {
        var normalizedSender = WH40KChatTranslationMarkup.NormalizeLanguageCode(senderLanguage);
        var normalizedRecipient = WH40KChatTranslationMarkup.NormalizeLanguageCode(recipientLanguage);

        return normalizedSender != null &&
               normalizedRecipient != null &&
               normalizedSender == normalizedRecipient;
    }

    public static string ResolveVisibleText(
        WH40KChatTranslationPayload translation,
        string originalMessage,
        string? senderLanguage,
        string? recipientLanguage,
        bool preserveOriginal = false)
    {
        return preserveOriginal || ShouldPreserveOriginalText(senderLanguage, recipientLanguage)
            ? originalMessage
            : translation.GetVisibleText(recipientLanguage);
    }

    public static string ResolveOriginalTextForTag(
        WH40KChatTranslationPayload translation,
        string originalMessage,
        string? senderLanguage,
        string? recipientLanguage,
        bool preserveOriginal = false)
    {
        return preserveOriginal || ShouldPreserveOriginalText(senderLanguage, recipientLanguage)
            ? string.Empty
            : translation.OriginalText;
    }

    public static bool ShouldShowLanguageTag(string? recipientLanguage, string sourceLanguage)
    {
        var normalizedSource = WH40KChatTranslationMarkup.NormalizeLanguageCode(sourceLanguage);
        if (normalizedSource == null)
            return false;

        var normalizedRecipient = WH40KChatTranslationMarkup.NormalizeLanguageCode(recipientLanguage);
        return normalizedRecipient == null || normalizedRecipient != normalizedSource;
    }

    public static string PrefixWithLanguageTag(string wrappedMessage, string? recipientLanguage, string sourceLanguage, string originalText)
    {
        if (!ShouldShowLanguageTag(recipientLanguage, sourceLanguage) || string.IsNullOrWhiteSpace(originalText))
            return wrappedMessage;

        return $"{WH40KChatTranslationMarkup.BuildTagMarkup(sourceLanguage, originalText)} {wrappedMessage}";
    }

    public static string BuildEntitySayWrappedMessage(
        WH40KPlayerCultureTracker culture,
        ICommonSession recipient,
        string escapedName,
        SpeechVerbPrototype speech,
        string speechVerbLocKey,
        string visibleText,
        string sourceLanguage,
        string originalText)
    {
        using var scope = culture.CreateChatScope(recipient);
        var wrappedMessage = Loc.GetString(
            speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message",
            ("entityName", escapedName),
            ("verb", Loc.GetString(speechVerbLocKey)),
            ("fontType", speech.FontId),
            ("fontSize", speech.FontSize),
            ("message", FormattedMessage.EscapeText(visibleText)));

        return PrefixWithLanguageTag(wrappedMessage, culture.ResolveChatLanguageCode(recipient), sourceLanguage, originalText);
    }

    public static string BuildEntityWhisperWrappedMessage(
        WH40KPlayerCultureTracker culture,
        ICommonSession recipient,
        string escapedName,
        string visibleText,
        string sourceLanguage,
        string originalText)
    {
        using var scope = culture.CreateChatScope(recipient);
        var wrappedMessage = Loc.GetString(
            "chat-manager-entity-whisper-wrap-message",
            ("entityName", escapedName),
            ("message", FormattedMessage.EscapeText(visibleText)));

        return PrefixWithLanguageTag(wrappedMessage, culture.ResolveChatLanguageCode(recipient), sourceLanguage, originalText);
    }

    public static string BuildLoocWrappedMessage(
        WH40KPlayerCultureTracker culture,
        ICommonSession recipient,
        string escapedName,
        string visibleText,
        string sourceLanguage,
        string originalText)
    {
        using var scope = culture.CreateChatScope(recipient);
        var wrappedMessage = Loc.GetString(
            "chat-manager-entity-looc-wrap-message",
            ("entityName", escapedName),
            ("message", FormattedMessage.EscapeText(visibleText)));

        return PrefixWithLanguageTag(wrappedMessage, culture.ResolveChatLanguageCode(recipient), sourceLanguage, originalText);
    }

    public static string BuildDeadWrappedMessage(
        WH40KPlayerCultureTracker culture,
        ICommonSession recipient,
        bool fromAdmin,
        string playerName,
        string userName,
        string visibleText,
        string sourceLanguage,
        string originalText)
    {
        using var scope = culture.CreateChatScope(recipient);
        var wrappedMessage = fromAdmin
            ? Loc.GetString(
                "chat-manager-send-admin-dead-chat-wrap-message",
                ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                ("userName", FormattedMessage.EscapeText(userName)),
                ("message", FormattedMessage.EscapeText(visibleText)))
            : Loc.GetString(
                "chat-manager-send-dead-chat-wrap-message",
                ("deadChannelName", Loc.GetString("chat-manager-dead-channel-name")),
                ("playerName", FormattedMessage.EscapeText(playerName)),
                ("message", FormattedMessage.EscapeText(visibleText)));

        return PrefixWithLanguageTag(wrappedMessage, culture.ResolveChatLanguageCode(recipient), sourceLanguage, originalText);
    }

    public static string BuildAHelpWrappedMessage(
        string senderMarkup,
        string visibleText,
        string? recipientLanguage,
        string sourceLanguage,
        string originalText,
        string? statusPrefix = null)
    {
        var escapedMessage = FormattedMessage.EscapeText(visibleText);
        var wrappedMessage = string.IsNullOrWhiteSpace(statusPrefix)
            ? $"{senderMarkup}: {escapedMessage}"
            : $"{statusPrefix} {senderMarkup}: {escapedMessage}";

        return PrefixWithLanguageTag(wrappedMessage, recipientLanguage, sourceLanguage, originalText);
    }

    public static string BuildRadioWrappedMessage(
        WH40KPlayerCultureTracker culture,
        ICommonSession recipient,
        RadioChannelPrototype channel,
        string escapedName,
        SpeechVerbPrototype speech,
        string speechVerbLocKey,
        string visibleText,
        string sourceLanguage,
        string originalText)
    {
        using var scope = culture.CreateChatScope(recipient);
        var wrappedMessage = Loc.GetString(
            speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
            ("color", channel.Color),
            ("fontType", speech.FontId),
            ("fontSize", speech.FontSize),
            ("verb", Loc.GetString(speechVerbLocKey)),
            ("channel", $"\\[{channel.LocalizedName}\\]"),
            ("name", escapedName),
            ("message", FormattedMessage.EscapeText(visibleText)));

        return PrefixWithLanguageTag(wrappedMessage, culture.ResolveChatLanguageCode(recipient), sourceLanguage, originalText);
    }
}
