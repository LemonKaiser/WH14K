#nullable enable
using Content.Server._WH40K.Chat.Translation;
using NUnit.Framework;

namespace Content.Tests.Server._WH40K.Chat;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class WH40KChatTranslationFormattingTests
{
    [TestCase("RU", "RU", false)]
    [TestCase("EN", "EN", false)]
    [TestCase("RU", "EN", true)]
    [TestCase("EN", "RU", true)]
    [TestCase(null, "RU", true)]
    public void ShouldShowLanguageTag_MatchesRecipientLanguage(string? recipientLanguage, string sourceLanguage, bool expected)
    {
        Assert.That(
            WH40KChatTranslationFormatting.ShouldShowLanguageTag(recipientLanguage, sourceLanguage),
            Is.EqualTo(expected));
    }

    [Test]
    public void BuildAHelpWrappedMessage_PrefixesLanguageTagForTranslatedRecipient()
    {
        var wrapped = WH40KChatTranslationFormatting.BuildAHelpWrappedMessage(
            "[color=red]Admin[/color]",
            "Hello there",
            "RU",
            "EN",
            "Hello there",
            "(S)");

        Assert.That(wrapped, Does.StartWith($"[{Content.Shared._WH40K.Chat.Translation.WH40KChatTranslationMarkup.TagName} "));
        Assert.That(wrapped, Does.Contain("(S) [color=red]Admin[/color]: Hello there"));
    }

    [Test]
    public void BuildAHelpWrappedMessage_SkipsLanguageTagForSameLanguage()
    {
        var wrapped = WH40KChatTranslationFormatting.BuildAHelpWrappedMessage(
            "Guardsman",
            "Привет",
            "RU",
            "RU",
            "Привет");

        Assert.That(wrapped, Is.EqualTo("Guardsman: Привет"));
    }
}
