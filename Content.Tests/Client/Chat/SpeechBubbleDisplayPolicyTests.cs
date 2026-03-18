using Content.Client.Chat.UI;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests.Client.Chat;

[TestFixture]
public sealed class SpeechBubbleDisplayPolicyTests
{
    [Test]
    public void LimitMessage_TruncatesLongMarkupMessageWithEllipsis()
    {
        var source = FormattedMessage.FromMarkupOrThrow($"[color=red]{new string('A', 120)}[/color]");

        var limited = SpeechBubbleDisplayPolicy.LimitMessage(source);
        var reparsed = FormattedMessage.FromMarkupOrThrow(limited.ToMarkup());

        Assert.Multiple(() =>
        {
            var expectedPrefixLength = SpeechBubbleDisplayPolicy.MaxVisibleTextElements - SpeechBubbleDisplayPolicy.TruncationSuffix.Length;
            Assert.That(
                limited.ToString(),
                Is.EqualTo($"{new string('A', expectedPrefixLength)}{SpeechBubbleDisplayPolicy.TruncationSuffix}"));
            Assert.That(reparsed, Is.EqualTo(limited));
        });
    }

    [TestCase(100, 30f, 72)]
    [TestCase(100, 10f, 24)]
    [TestCase(12, 30f, 12)]
    [TestCase(100, 1f, 3)]
    [TestCase(100, 0f, 0)]
    public void GetAnimatedTextElementCount_ClampsRevealBudget(int visibleTextElements, float charsPerSecond, int expected)
    {
        var result = SpeechBubbleDisplayPolicy.GetAnimatedTextElementCount(visibleTextElements, charsPerSecond);

        Assert.That(result, Is.EqualTo(expected));
    }
}
