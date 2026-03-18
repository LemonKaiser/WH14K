using Content.Client.Chat.UI;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests.Client.Chat;

[TestFixture]
public sealed class DialogueRevealTextElementHelperTests
{
    private const string CombiningAccent = "e\u0301";
    private const string FamilyEmoji = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
    private const string ThumbsUpMediumSkinTone = "\U0001F44D\U0001F3FD";
    private const string UnitedStatesFlag = "\U0001F1FA\U0001F1F8";
    private const string CjkWord = "\u4E16\u754C";
    private const string HelloRu = "\u041F\u0440\u0438\u0432\u0435\u0442";

    [Test]
    public void GetTextElements_PreservesMixedLanguageGraphemeClusters()
    {
        var message = FormattedMessage.FromMarkupOrThrow($"A {CombiningAccent} {FamilyEmoji} {CjkWord} {HelloRu}");

        var elements = DialogueRevealTextElementHelper.GetTextElements(message);

        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(elements), Is.EqualTo(message.ToString()));
            Assert.That(elements, Does.Contain(CombiningAccent));
            Assert.That(elements, Does.Contain(FamilyEmoji));
            Assert.That(elements, Does.Contain("\u4E16"));
            Assert.That(elements, Does.Contain("\u041F"));
        });
    }

    [Test]
    public void BuildVisibleMessage_RevealsWholeTextElementsWithoutSplittingCombinedGlyphs()
    {
        var message = FormattedMessage.FromMarkupOrThrow($"A {CombiningAccent} {FamilyEmoji} \u0411");

        var firstSlice = DialogueRevealTextElementHelper.BuildVisibleMessage(message, 3);
        var secondSlice = DialogueRevealTextElementHelper.BuildVisibleMessage(message, 5);

        Assert.Multiple(() =>
        {
            Assert.That(firstSlice.ToString(), Is.EqualTo($"A {CombiningAccent}"));
            Assert.That(secondSlice.ToString(), Is.EqualTo($"A {CombiningAccent} {FamilyEmoji}"));
        });
    }

    [Test]
    public void BuildVisibleMessage_PreservesMarkupWhenTruncatingMultilingualText()
    {
        var message = FormattedMessage.FromMarkupOrThrow($"[color=red]{HelloRu}[/color] [bold]{CjkWord}[/bold]");

        var slice = DialogueRevealTextElementHelper.BuildVisibleMessage(message, 8);
        var reparsed = FormattedMessage.FromMarkupOrThrow(slice.ToMarkup());

        Assert.Multiple(() =>
        {
            Assert.That(slice.ToString(), Is.EqualTo($"{HelloRu} \u4E16"));
            Assert.That(reparsed, Is.EqualTo(slice));
        });
    }

    [Test]
    public void GetTextElements_KeepsEmojiModifiersAndFlagsTogether()
    {
        var message = FormattedMessage.FromMarkupOrThrow($"{ThumbsUpMediumSkinTone} {UnitedStatesFlag}");

        var elements = DialogueRevealTextElementHelper.GetTextElements(message);

        Assert.Multiple(() =>
        {
            Assert.That(elements, Does.Contain(ThumbsUpMediumSkinTone));
            Assert.That(elements, Does.Contain(UnitedStatesFlag));
            Assert.That(string.Concat(elements), Is.EqualTo(message.ToString()));
        });
    }
}
