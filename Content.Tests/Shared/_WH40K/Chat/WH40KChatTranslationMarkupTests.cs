using Content.Shared._WH40K.Chat.Translation;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.Chat;

[TestFixture]
public sealed class WH40KChatTranslationMarkupTests
{
    [Test]
    public void EncodeAndDecodeOriginalTextRoundTripsUnicode()
    {
        const string original = "Привет, guardsman #42!";

        var encoded = WH40KChatTranslationMarkup.EncodeOriginalText(original);

        Assert.That(WH40KChatTranslationMarkup.TryDecodeOriginalText(encoded, out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(original));
    }

    [Test]
    public void BuildTagMarkupEmbedsNormalizedLanguage()
    {
        var markup = WH40KChatTranslationMarkup.BuildTagMarkup("ru", "Привет");

        Assert.That(markup, Does.StartWith($"[{WH40KChatTranslationMarkup.TagName} lang=\"RU\""));
        Assert.That(markup, Does.Contain("original=\""));
        Assert.That(markup, Does.EndWith($"[/{WH40KChatTranslationMarkup.TagName}]"));
    }

    [Test]
    public void ResolveLanguageFromTextDetectsCyrillic()
    {
        var detected = WH40KChatTranslationMarkup.ResolveLanguageFromText("Привет, мир!");

        Assert.That(detected, Is.EqualTo(WH40KChatTranslationMarkup.RussianLanguageCode));
    }

    [Test]
    public void ResolveLanguageFromTextDetectsLatin()
    {
        var detected = WH40KChatTranslationMarkup.ResolveLanguageFromText("Hello world!");

        Assert.That(detected, Is.EqualTo(WH40KChatTranslationMarkup.EnglishLanguageCode));
    }

    [Test]
    public void ResolveLanguageFromTextUsesFallbackForNonLetterText()
    {
        var detected = WH40KChatTranslationMarkup.ResolveLanguageFromText("12345 ???", "en");

        Assert.That(detected, Is.EqualTo(WH40KChatTranslationMarkup.EnglishLanguageCode));
    }

    [Test]
    public void NormalizeCacheTextCollapsesWhitespace()
    {
        var normalized = WH40KChatTranslationMarkup.NormalizeCacheText("  hello\t\tworld \n  again  ");

        Assert.That(normalized, Is.EqualTo("hello world again"));
    }

    [Test]
    public void NormalizeTranslationTextStripsZalgoEmojiAndKeepsBasicPunctuation()
    {
        var normalized = WH40KChatTranslationMarkup.NormalizeTranslationText("H̴e̷l̶l̸o̴ 🙂 #42 — go!!!");

        Assert.That(normalized, Is.EqualTo("Hello #42 - go!!!"));
    }

    [Test]
    public void ResolveLanguageFromTextIgnoresCombiningNoise()
    {
        var detected = WH40KChatTranslationMarkup.ResolveLanguageFromText("П̴р̷и̶в̵е̶т̷!!!");

        Assert.That(detected, Is.EqualTo(WH40KChatTranslationMarkup.RussianLanguageCode));
    }
}
