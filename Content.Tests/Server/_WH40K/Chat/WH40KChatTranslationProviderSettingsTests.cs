#nullable enable
using Content.Server._WH40K.Chat.Translation;
using NUnit.Framework;

namespace Content.Tests.Server._WH40K.Chat;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class WH40KChatTranslationProviderSettingsTests
{
    [TestCase(null, WH40KChatTranslationProviderSettings.ServiceProvider)]
    [TestCase("service", WH40KChatTranslationProviderSettings.ServiceProvider)]
    [TestCase("deepl", WH40KChatTranslationProviderSettings.DeepLProvider)]
    [TestCase("DeepL", WH40KChatTranslationProviderSettings.DeepLProvider)]
    [TestCase("unknown", WH40KChatTranslationProviderSettings.ServiceProvider)]
    public void NormalizeProvider_MapsKnownValues(string? rawProvider, string expected)
    {
        Assert.That(
            WH40KChatTranslationProviderSettings.NormalizeProvider(rawProvider),
            Is.EqualTo(expected));
    }

    [Test]
    public void ResolveDeepLBaseUrl_UsesFreeEndpointForFxKeys()
    {
        Assert.That(
            WH40KChatTranslationProviderSettings.ResolveDeepLBaseUrl(null, "example-key:fx"),
            Is.EqualTo(WH40KChatTranslationProviderSettings.DeepLFreeBaseUrl));
    }

    [Test]
    public void ResolveDeepLBaseUrl_UsesProEndpointForRegularKeys()
    {
        Assert.That(
            WH40KChatTranslationProviderSettings.ResolveDeepLBaseUrl(null, "example-key"),
            Is.EqualTo(WH40KChatTranslationProviderSettings.DeepLProBaseUrl));
    }

    [Test]
    public void ResolveDeepLBaseUrl_PrefersConfiguredOverride()
    {
        Assert.That(
            WH40KChatTranslationProviderSettings.ResolveDeepLBaseUrl(" https://regional.deepl.test/ ", "example-key:fx"),
            Is.EqualTo("https://regional.deepl.test"));
    }

    [TestCase(null, WH40KChatTranslationProviderSettings.DeepLLatencyOptimizedModel)]
    [TestCase("quality_optimized", WH40KChatTranslationProviderSettings.DeepLQualityOptimizedModel)]
    [TestCase("prefer_quality_optimized", WH40KChatTranslationProviderSettings.DeepLPreferQualityOptimizedModel)]
    [TestCase("bad-value", WH40KChatTranslationProviderSettings.DeepLLatencyOptimizedModel)]
    public void NormalizeDeepLModelType_ConstrainsToSupportedValues(string? modelType, string expected)
    {
        Assert.That(
            WH40KChatTranslationProviderSettings.NormalizeDeepLModelType(modelType),
            Is.EqualTo(expected));
    }

    [TestCase(null, "0")]
    [TestCase("0", "0")]
    [TestCase("1", "1")]
    [TestCase("nonewlines", "nonewlines")]
    [TestCase("invalid", "0")]
    public void NormalizeDeepLSplitSentences_ConstrainsToSupportedValues(string? splitSentences, string expected)
    {
        Assert.That(
            WH40KChatTranslationProviderSettings.NormalizeDeepLSplitSentences(splitSentences),
            Is.EqualTo(expected));
    }

    [Test]
    public void ResolveDeepLGlossaryId_ReturnsPairSpecificGlossary()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                WH40KChatTranslationProviderSettings.ResolveDeepLGlossaryId("RU", "EN", "ru-en-id", "en-ru-id"),
                Is.EqualTo("ru-en-id"));

            Assert.That(
                WH40KChatTranslationProviderSettings.ResolveDeepLGlossaryId("EN", "RU", "ru-en-id", "en-ru-id"),
                Is.EqualTo("en-ru-id"));

            Assert.That(
                WH40KChatTranslationProviderSettings.ResolveDeepLGlossaryId("RU", "RU", "ru-en-id", "en-ru-id"),
                Is.Null);
        });
    }
}
