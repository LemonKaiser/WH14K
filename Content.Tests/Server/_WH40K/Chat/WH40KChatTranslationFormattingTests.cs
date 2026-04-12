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
}
