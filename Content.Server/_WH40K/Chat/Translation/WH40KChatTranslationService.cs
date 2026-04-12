using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared._WH40K.Chat.Translation;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Chat.Translation;

public interface IWH40KChatTranslationService
{
    bool IsConfiguredForChannel(ChatChannel channel);

    uint AllocateMessageId();

    Task<WH40KChatTranslationDispatch> TranslateWithSoftHoldAsync(
        string text,
        string? fallbackLanguage,
        ChatChannel channel,
        CancellationToken cancel = default);

    Task<WH40KChatTranslationPayload?> TranslateAsync(
        string text,
        string? fallbackLanguage,
        ChatChannel channel,
        CancellationToken cancel = default);
}

public sealed record WH40KChatTranslationDispatch(
    WH40KChatTranslationPayload? ImmediateTranslation,
    Task<WH40KChatTranslationPayload?>? PendingTranslation);

public sealed record WH40KChatTranslationPayload(
    string OriginalText,
    string SourceLanguage,
    IReadOnlyDictionary<string, string> Translations)
{
    public static WH40KChatTranslationPayload CreatePlaceholder(string originalText, string sourceLanguage)
    {
        var normalizedSource = WH40KChatTranslationMarkup.NormalizeLanguageCode(sourceLanguage)
            ?? throw new ArgumentException($"Unsupported source language '{sourceLanguage}'.", nameof(sourceLanguage));

        return new WH40KChatTranslationPayload(
            originalText,
            normalizedSource,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    public string GetVisibleText(string? targetLanguage)
    {
        var normalized = WH40KChatTranslationMarkup.NormalizeLanguageCode(targetLanguage);
        if (normalized == null || normalized == SourceLanguage)
            return OriginalText;

        return Translations.TryGetValue(normalized, out var translated) && !string.IsNullOrWhiteSpace(translated)
            ? translated
            : OriginalText;
    }
}

public sealed class WH40KChatTranslationService : IWH40KChatTranslationService
{
    private const string ServiceAutoDetectSourceCacheKey = "AUTO";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IConfigurationManager _config;
    private readonly HttpClient _http;
    private readonly ISawmill _sawmill;

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    private DateTimeOffset _failureBackoffUntil;
    private int _nextMessageId;

    public WH40KChatTranslationService(
        IConfigurationManager config,
        IHttpClientHolder http,
        ILogManager logManager)
    {
        _config = config;
        _http = http.Client;
        _sawmill = logManager.GetSawmill("wh40k.chat.translation");
    }

    public bool IsConfiguredForChannel(ChatChannel channel)
    {
        if (!_config.GetCVar(CCVars.WH40KChatTranslationEnabled))
            return false;

        if (!IsProviderConfigured(GetProvider()))
            return false;

        return channel switch
        {
            ChatChannel.Local => _config.GetCVar(CCVars.WH40KChatTranslationLocalEnabled),
            ChatChannel.Whisper => _config.GetCVar(CCVars.WH40KChatTranslationWhisperEnabled),
            ChatChannel.Radio => _config.GetCVar(CCVars.WH40KChatTranslationRadioEnabled),
            ChatChannel.LOOC => _config.GetCVar(CCVars.WH40KChatTranslationLoocEnabled),
            ChatChannel.Dead => _config.GetCVar(CCVars.WH40KChatTranslationDeadEnabled),
            ChatChannel.OOC => _config.GetCVar(CCVars.WH40KChatTranslationOocEnabled),
            _ => false,
        };
    }

    public uint AllocateMessageId()
    {
        return unchecked((uint) Interlocked.Increment(ref _nextMessageId));
    }

    public async Task<WH40KChatTranslationDispatch> TranslateWithSoftHoldAsync(
        string text,
        string? fallbackLanguage,
        ChatChannel channel,
        CancellationToken cancel = default)
    {
        var translationTask = TranslateAsync(text, fallbackLanguage, channel, cancel);
        var softHoldMs = Math.Max(0, _config.GetCVar(CCVars.WH40KChatTranslationSoftHoldMs));
        if (softHoldMs <= 0)
            return new WH40KChatTranslationDispatch(null, translationTask);

        if (await Task.WhenAny(translationTask, Task.Delay(softHoldMs, CancellationToken.None)) == translationTask)
            return new WH40KChatTranslationDispatch(await translationTask, null);

        return new WH40KChatTranslationDispatch(null, translationTask);
    }

    public async Task<WH40KChatTranslationPayload?> TranslateAsync(
        string text,
        string? fallbackLanguage,
        ChatChannel channel,
        CancellationToken cancel = default)
    {
        if (!IsConfiguredForChannel(channel))
            return null;

        if (DateTimeOffset.UtcNow < _failureBackoffUntil)
            return null;

        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (text.Length > _config.GetCVar(CCVars.WH40KChatTranslationMaxMessageLength))
            return null;

        var normalizedText = WH40KChatTranslationMarkup.NormalizeTranslationText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
            return null;

        var provider = GetProvider();
        string? sourceLanguage = null;
        string cacheSourceLanguage;
        IReadOnlyList<string> targetLanguages;

        if (provider == WH40KChatTranslationProviderSettings.ServiceProvider)
        {
            cacheSourceLanguage = ServiceAutoDetectSourceCacheKey;
            targetLanguages = BuildServiceTargetLanguages();
        }
        else
        {
            sourceLanguage = WH40KChatTranslationMarkup.ResolveLanguageFromText(text, fallbackLanguage);
            if (!WH40KChatTranslationMarkup.IsSupportedLanguage(sourceLanguage))
                return null;

            cacheSourceLanguage = sourceLanguage!;
            targetLanguages = BuildTargetLanguages(sourceLanguage!);
        }

        var cacheKey = BuildCacheKey(BuildProviderCacheSegment(provider, cacheSourceLanguage, targetLanguages), cacheSourceLanguage, normalizedText);
        if (TryGetCachedTranslation(cacheKey, out var cached))
            return cached;

        var baseUrl = _config.GetCVar(CCVars.WH40KChatTranslationServiceUrl).TrimEnd('/');
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(_config.GetCVar(CCVars.WH40KChatTranslationTimeoutMs)));

        try
        {
            var result = provider switch
            {
                WH40KChatTranslationProviderSettings.DeepLProvider => await TranslateWithDeepLAsync(
                    normalizedText,
                    sourceLanguage!,
                    targetLanguages,
                    channel,
                    linkedCts.Token),
                _ => await TranslateWithServiceAsync(
                    normalizedText,
                    sourceLanguage,
                    targetLanguages,
                    channel,
                    baseUrl,
                    linkedCts.Token),
            };

            if (result == null)
                return null;

            StoreCachedTranslation(cacheKey, result);
            return result;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            RegisterFailure($"Translation timed out for {channel}.");
            return null;
        }
        catch (Exception e)
        {
            RegisterFailure($"Translation request failed: {e.Message}");
            return null;
        }
    }

    private async Task<WH40KChatTranslationPayload?> TranslateWithServiceAsync(
        string normalizedText,
        string? sourceLanguage,
        IReadOnlyList<string> targetLanguages,
        ChatChannel channel,
        string baseUrl,
        CancellationToken cancel)
    {
        var request = new TranslateRequest(
            normalizedText,
            sourceLanguage,
            targetLanguages,
            channel.ToString());

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/translate")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        var apiKey = _config.GetCVar(CCVars.WH40KChatTranslationApiKey);
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpRequest.Headers.Add("X-Api-Key", apiKey);

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(httpRequest, cancel);
        if (!response.IsSuccessStatusCode)
        {
            RegisterFailure($"Translation service returned {(int) response.StatusCode} for {channel}.");
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<TranslateResponse>(JsonOptions, cancel);
        if (payload == null)
        {
            RegisterFailure("Translation service returned an empty payload.");
            return null;
        }

        var normalizedSource = WH40KChatTranslationMarkup.NormalizeLanguageCode(payload.SourceLanguage)
            ?? WH40KChatTranslationMarkup.NormalizeLanguageCode(sourceLanguage);
        if (normalizedSource == null)
            return null;

        var normalizedOriginal = WH40KChatTranslationMarkup.NormalizeTranslationText(payload.OriginalText ?? normalizedText);
        if (string.IsNullOrWhiteSpace(normalizedOriginal))
            normalizedOriginal = normalizedText;

        var translations = NormalizeTranslations(payload.Translations);
        if (translations.Count == 0)
            return null;

        return new WH40KChatTranslationPayload(normalizedOriginal, normalizedSource, translations);
    }

    private async Task<WH40KChatTranslationPayload?> TranslateWithDeepLAsync(
        string normalizedText,
        string sourceLanguage,
        IReadOnlyList<string> targetLanguages,
        ChatChannel channel,
        CancellationToken cancel)
    {
        var authKey = _config.GetCVar(CCVars.WH40KChatTranslationDeepLAuthKey).Trim();
        if (string.IsNullOrWhiteSpace(authKey))
            return null;

        var baseUrl = WH40KChatTranslationProviderSettings.ResolveDeepLBaseUrl(
            _config.GetCVar(CCVars.WH40KChatTranslationDeepLBaseUrl),
            authKey);
        var modelType = WH40KChatTranslationProviderSettings.NormalizeDeepLModelType(
            _config.GetCVar(CCVars.WH40KChatTranslationDeepLModelType));
        var splitSentences = WH40KChatTranslationProviderSettings.NormalizeDeepLSplitSentences(
            _config.GetCVar(CCVars.WH40KChatTranslationDeepLSplitSentences));
        var context = _config.GetCVar(CCVars.WH40KChatTranslationDeepLContext).Trim();
        var preserveFormatting = _config.GetCVar(CCVars.WH40KChatTranslationDeepLPreserveFormatting);
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var targetLanguage in targetLanguages)
        {
            var glossaryId = WH40KChatTranslationProviderSettings.ResolveDeepLGlossaryId(
                sourceLanguage,
                targetLanguage,
                _config.GetCVar(CCVars.WH40KChatTranslationDeepLGlossaryRuToEn),
                _config.GetCVar(CCVars.WH40KChatTranslationDeepLGlossaryEnToRu));

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/translate")
            {
                Content = JsonContent.Create(new DeepLTranslateRequest
                {
                    Text = [normalizedText],
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    ModelType = modelType,
                    PreserveFormatting = preserveFormatting,
                    SplitSentences = splitSentences,
                    Context = string.IsNullOrWhiteSpace(context) ? null : context,
                    GlossaryId = glossaryId,
                }, options: JsonOptions)
            };

            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {authKey}");
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(httpRequest, cancel);
            if (!response.IsSuccessStatusCode)
            {
                var failureBody = await response.Content.ReadAsStringAsync(cancel);
                RegisterFailure($"DeepL returned {(int) response.StatusCode} for {channel}: {TrimFailureBody(failureBody)}");
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<DeepLTranslateResponse>(JsonOptions, cancel);
            var translatedText = payload?.Translations?.FirstOrDefault()?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                RegisterFailure($"DeepL returned an empty translation for {channel}.");
                return null;
            }

            translations[targetLanguage] = translatedText;
        }

        return translations.Count == 0
            ? null
            : new WH40KChatTranslationPayload(normalizedText, sourceLanguage, translations);
    }

    private bool TryGetCachedTranslation(string cacheKey, out WH40KChatTranslationPayload? payload)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(cacheKey, out var entry))
            {
                payload = null;
                return false;
            }

            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _cache.Remove(cacheKey);
                payload = null;
                return false;
            }

            payload = entry.Payload;
            return true;
        }
    }

    private void StoreCachedTranslation(string cacheKey, WH40KChatTranslationPayload payload)
    {
        var ttl = Math.Max(1, _config.GetCVar(CCVars.WH40KChatTranslationCacheTtlSeconds));
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttl);

        lock (_cacheLock)
        {
            _cache[cacheKey] = new CacheEntry(payload, expiresAt, DateTimeOffset.UtcNow);
            PruneCacheLocked();
        }
    }

    private void PruneCacheLocked()
    {
        var now = DateTimeOffset.UtcNow;
        var maxEntries = Math.Max(64, _config.GetCVar(CCVars.WH40KChatTranslationCacheMaxEntries));

        // Only prune when we exceed 10% over max to avoid sorting on every insert
        if (_cache.Count <= maxEntries)
        {
            // Still remove expired entries but only scan when at capacity
            return;
        }

        // Remove expired entries first
        List<string>? expired = null;
        foreach (var (key, entry) in _cache)
        {
            if (entry.ExpiresAt <= now)
            {
                expired ??= new List<string>();
                expired.Add(key);
            }
        }

        if (expired != null)
        {
            foreach (var key in expired)
                _cache.Remove(key);
        }

        if (_cache.Count <= maxEntries)
            return;

        // Evict oldest entries to get back under the limit
        var overflow = _cache.Count - maxEntries;
        var oldest = new List<(string Key, DateTimeOffset StoredAt)>(_cache.Count);
        foreach (var (key, entry) in _cache)
            oldest.Add((key, entry.StoredAt));

        oldest.Sort((a, b) => a.StoredAt.CompareTo(b.StoredAt));

        for (var i = 0; i < overflow && i < oldest.Count; i++)
            _cache.Remove(oldest[i].Key);
    }

    private void RegisterFailure(string reason)
    {
        var backoffSeconds = Math.Max(1, _config.GetCVar(CCVars.WH40KChatTranslationFailureBackoffSeconds));
        _failureBackoffUntil = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds);
        _sawmill.Warning(reason);
    }

    private string GetProvider()
    {
        return WH40KChatTranslationProviderSettings.NormalizeProvider(
            _config.GetCVar(CCVars.WH40KChatTranslationProvider));
    }

    private bool IsProviderConfigured(string provider)
    {
        return provider switch
        {
            WH40KChatTranslationProviderSettings.DeepLProvider =>
                !string.IsNullOrWhiteSpace(_config.GetCVar(CCVars.WH40KChatTranslationDeepLAuthKey)),
            _ => !string.IsNullOrWhiteSpace(_config.GetCVar(CCVars.WH40KChatTranslationServiceUrl)),
        };
    }

    private string BuildProviderCacheSegment(string provider, string sourceLanguage, IReadOnlyList<string> targetLanguages)
    {
        return provider switch
        {
            WH40KChatTranslationProviderSettings.DeepLProvider => BuildDeepLCacheSegment(sourceLanguage, targetLanguages),
            _ => $"{provider}|{_config.GetCVar(CCVars.WH40KChatTranslationServiceUrl).Trim().TrimEnd('/')}",
        };
    }

    private string BuildDeepLCacheSegment(string sourceLanguage, IReadOnlyList<string> targetLanguages)
    {
        var targetLanguage = targetLanguages.FirstOrDefault() ?? string.Empty;
        var glossaryId = WH40KChatTranslationProviderSettings.ResolveDeepLGlossaryId(
                            sourceLanguage,
                            targetLanguage,
                            _config.GetCVar(CCVars.WH40KChatTranslationDeepLGlossaryRuToEn),
                            _config.GetCVar(CCVars.WH40KChatTranslationDeepLGlossaryEnToRu)) ?? "-";
        var baseUrl = WH40KChatTranslationProviderSettings.ResolveDeepLBaseUrl(
            _config.GetCVar(CCVars.WH40KChatTranslationDeepLBaseUrl),
            _config.GetCVar(CCVars.WH40KChatTranslationDeepLAuthKey));
        var modelType = WH40KChatTranslationProviderSettings.NormalizeDeepLModelType(
            _config.GetCVar(CCVars.WH40KChatTranslationDeepLModelType));
        var splitSentences = WH40KChatTranslationProviderSettings.NormalizeDeepLSplitSentences(
            _config.GetCVar(CCVars.WH40KChatTranslationDeepLSplitSentences));
        var preserveFormatting = _config.GetCVar(CCVars.WH40KChatTranslationDeepLPreserveFormatting) ? "pf1" : "pf0";
        var context = _config.GetCVar(CCVars.WH40KChatTranslationDeepLContext).Trim();

        return $"{WH40KChatTranslationProviderSettings.DeepLProvider}|{baseUrl}|{modelType}|{splitSentences}|{preserveFormatting}|{glossaryId}|{targetLanguage}|{context}";
    }

    private static string BuildCacheKey(string providerSegment, string sourceLanguage, string normalizedText)
    {
        return $"{providerSegment}|{sourceLanguage}|{normalizedText}";
    }

    private static string[] BuildTargetLanguages(string sourceLanguage)
    {
        return sourceLanguage == WH40KChatTranslationMarkup.RussianLanguageCode
            ? [WH40KChatTranslationMarkup.EnglishLanguageCode]
            : [WH40KChatTranslationMarkup.RussianLanguageCode];
    }

    private static string[] BuildServiceTargetLanguages()
    {
        return [
            WH40KChatTranslationMarkup.RussianLanguageCode,
            WH40KChatTranslationMarkup.EnglishLanguageCode,
        ];
    }

    private static Dictionary<string, string> NormalizeTranslations(Dictionary<string, string>? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (raw == null)
            return result;

        foreach (var (key, value) in raw)
        {
            var normalizedKey = WH40KChatTranslationMarkup.NormalizeLanguageCode(key);
            if (normalizedKey == null || string.IsNullOrWhiteSpace(value))
                continue;

            result[normalizedKey] = value.Trim();
        }

        return result;
    }

    private static string TrimFailureBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "empty response body";

        var collapsed = string.Join(' ', body
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed.Length <= 160
            ? collapsed
            : $"{collapsed[..160]}...";
    }

    private sealed record CacheEntry(
        WH40KChatTranslationPayload Payload,
        DateTimeOffset ExpiresAt,
        DateTimeOffset StoredAt);

    private sealed record TranslateRequest(
        string Text,
        string? SourceLanguage,
        IReadOnlyList<string> TargetLanguages,
        string Channel);

    private sealed class TranslateResponse
    {
        public string? SourceLanguage { get; set; }
        public string? OriginalText { get; set; }
        public Dictionary<string, string>? Translations { get; set; }
    }

    private sealed class DeepLTranslateRequest
    {
        [JsonPropertyName("text")]
        public required string[] Text { get; init; }

        [JsonPropertyName("source_lang")]
        public required string SourceLanguage { get; init; }

        [JsonPropertyName("target_lang")]
        public required string TargetLanguage { get; init; }

        [JsonPropertyName("model_type")]
        public string? ModelType { get; init; }

        [JsonPropertyName("preserve_formatting")]
        public bool PreserveFormatting { get; init; }

        [JsonPropertyName("split_sentences")]
        public string? SplitSentences { get; init; }

        [JsonPropertyName("context")]
        public string? Context { get; init; }

        [JsonPropertyName("glossary_id")]
        public string? GlossaryId { get; init; }
    }

    private sealed class DeepLTranslateResponse
    {
        [JsonPropertyName("translations")]
        public List<DeepLTranslation>? Translations { get; set; }
    }

    private sealed class DeepLTranslation
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
