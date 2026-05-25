using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables translated chat for this client when the server-side WH40K translation pipeline is active.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationPreferenceEnabled =
        CVarDef.Create("wh40k.chat_translation.preference.enabled", true, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Preferred incoming chat translation language for this client. Empty follows the game language.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationPreferenceLanguage =
        CVarDef.Create("wh40k.chat_translation.preference.language", string.Empty, CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Enables the WH40K automatic RU/EN chat translation pipeline.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationEnabled =
        CVarDef.Create("wh40k.chat_translation.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Translation backend provider. Supported values: service, deepl.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationProvider =
        CVarDef.Create("wh40k.chat_translation.provider", "service", CVar.SERVERONLY);

    /// <summary>
    ///     Base URL of the external WH40K translation service when provider=service.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationServiceUrl =
        CVarDef.Create("wh40k.chat_translation.service_url", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional API key forwarded as X-Api-Key to the translation service when provider=service.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationApiKey =
        CVarDef.Create("wh40k.chat_translation.api_key", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL authentication key used when provider=deepl.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLAuthKey =
        CVarDef.Create("wh40k.chat_translation.deepl.auth_key", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL API base URL override. When empty, `:fx` keys use api-free.deepl.com and others use api.deepl.com.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLBaseUrl =
        CVarDef.Create("wh40k.chat_translation.deepl.base_url", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL model preference. Supported values: latency_optimized, quality_optimized, prefer_quality_optimized.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLModelType =
        CVarDef.Create("wh40k.chat_translation.deepl.model_type", "latency_optimized", CVar.SERVERONLY);

    /// <summary>
    ///     If true, asks DeepL to preserve the source formatting as much as possible.
    /// </summary>
    public static readonly CVarDef<bool> WH40KChatTranslationDeepLPreserveFormatting =
        CVarDef.Create("wh40k.chat_translation.deepl.preserve_formatting", true, CVar.SERVERONLY);

    /// <summary>
    ///     DeepL sentence splitting mode. Supported values: 0, 1, nonewlines.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLSplitSentences =
        CVarDef.Create("wh40k.chat_translation.deepl.split_sentences", "0", CVar.SERVERONLY);

    /// <summary>
    ///     Optional unbilled context string forwarded to DeepL to improve short chat translations.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLContext =
        CVarDef.Create("wh40k.chat_translation.deepl.context", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL glossary id for RU -> EN requests.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLGlossaryRuToEn =
        CVarDef.Create("wh40k.chat_translation.deepl.glossary_id.ru_en", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Optional DeepL glossary id for EN -> RU requests.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationDeepLGlossaryEnToRu =
        CVarDef.Create("wh40k.chat_translation.deepl.glossary_id.en_ru", string.Empty, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum time spent waiting for a translation before falling back to original text.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationTimeoutMs =
        CVarDef.Create("wh40k.chat_translation.timeout_ms", 1000, CVar.SERVERONLY);

    /// <summary>
    ///     Soft wait window before the original message is sent and translation continues in the background.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationSoftHoldMs =
        CVarDef.Create("wh40k.chat_translation.soft_hold_ms", 100, CVar.SERVERONLY);

    /// <summary>
    ///     Backoff window after a translation failure to avoid stalling every chat line.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationFailureBackoffSeconds =
        CVarDef.Create("wh40k.chat_translation.failure_backoff_seconds", 5, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum message length eligible for translation.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationMaxMessageLength =
        CVarDef.Create("wh40k.chat_translation.max_message_length", 256, CVar.SERVERONLY);

    /// <summary>
    ///     Local translation cache entry lifetime in seconds.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationCacheTtlSeconds =
        CVarDef.Create("wh40k.chat_translation.cache_ttl_seconds", 1800, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum amount of cached translation entries stored locally on the game server.
    /// </summary>
    public static readonly CVarDef<int> WH40KChatTranslationCacheMaxEntries =
        CVarDef.Create("wh40k.chat_translation.cache_max_entries", 4096, CVar.SERVERONLY);

    /// <summary>
    ///     Comma-separated chat channel ids translated by the WH40K server-side pipeline.
    ///     Supported values: local, whisper, radio, looc, dead, ooc, ahelp. Use '*' or 'all' to enable every listed channel.
    /// </summary>
    public static readonly CVarDef<string> WH40KChatTranslationChannels =
        CVarDef.Create("wh40k.chat_translation.channels", "local,whisper,radio,looc,dead,ooc,ahelp", CVar.SERVERONLY);
}
