using System.Globalization;
using System.Text;
using Content.Shared._WH40K.DiscordAuth;
using NUnit.Framework;

namespace Content.Tests.Shared._WH40K.DiscordAuth;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class WH40KDiscordAuthDisplayNameSanitizerTests
{
    [Test]
    public void Sanitize_CollapsesWhitespaceAndDropsControls()
    {
        var input = "  Gados\r\n\tDiscord\u0007User  ";

        var result = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(input);

        Assert.That(result, Is.EqualTo("Gados DiscordUser"));
    }

    [Test]
    public void Sanitize_LimitsCombiningMarksPerBase()
    {
        var input = "x\u035F\u035F\u035F\u035F";

        var result = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(input, maxCombiningMarksPerBase: 2);

        var markCount = 0;
        foreach (var rune in result.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                markCount++;
        }

        Assert.That(markCount, Is.EqualTo(2));
    }

    [Test]
    public void Ellipsize_TrimsByTextElements()
    {
        var result = WH40KDiscordAuthDisplayNameSanitizer.Ellipsize("abcdef", 5);

        Assert.That(result, Is.EqualTo("ab..."));
    }

    [Test]
    public void Ellipsize_KeepsWholeEmojiRunes()
    {
        var result = WH40KDiscordAuthDisplayNameSanitizer.Ellipsize("😀😀😀😀😀😀", 5);

        Assert.That(result, Is.EqualTo("😀😀..."));
    }

    [Test]
    public void SanitizeAndEllipsize_HandlesLongDirtyInput()
    {
        var input = "VeryLong\nDiscord\tNickname\u035F\u035F\u035F\u035F";

        var result = WH40KDiscordAuthDisplayNameSanitizer.SanitizeAndEllipsize(input, 12);

        Assert.That(result, Does.StartWith("VeryLong"));
        Assert.That(result, Does.EndWith("..."));
        Assert.That(result, Does.Not.Contain('\n'));
    }
}
