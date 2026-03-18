using System.Globalization;
using System.Text;

namespace Content.Shared._WH40K.DiscordAuth;

public static class WH40KDiscordAuthDisplayNameSanitizer
{
    public static string Sanitize(string? value, int maxCombiningMarksPerBase = 2)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        var previousWasWhitespace = false;
        var sawBaseRune = false;
        var combiningMarksForBase = 0;

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);

            if (IsWhitespace(rune, category))
            {
                if (!previousWasWhitespace && builder.Length > 0)
                    builder.Append(' ');

                previousWasWhitespace = true;
                sawBaseRune = false;
                combiningMarksForBase = 0;
                continue;
            }

            if (IsCombiningMark(category))
            {
                if (!sawBaseRune || combiningMarksForBase >= maxCombiningMarksPerBase)
                    continue;

                builder.Append(rune.ToString());
                previousWasWhitespace = false;
                combiningMarksForBase++;
                continue;
            }

            if (ShouldDrop(category))
                continue;

            builder.Append(rune.ToString());
            previousWasWhitespace = false;
            sawBaseRune = true;
            combiningMarksForBase = 0;
        }

        return builder.ToString().Trim();
    }

    public static string Ellipsize(string value, int maxTextElements)
    {
        if (string.IsNullOrEmpty(value) || maxTextElements <= 0)
            return string.Empty;

        var textElementCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (!IsCombiningMark(Rune.GetUnicodeCategory(rune)))
                textElementCount++;
        }

        if (textElementCount <= maxTextElements)
            return value;

        var keepTextElements = Math.Max(1, maxTextElements - 3);
        var builder = new StringBuilder(value.Length);
        var keptTextElements = 0;

        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var isCombiningMark = IsCombiningMark(category);

            if (!isCombiningMark)
            {
                if (keptTextElements >= keepTextElements)
                    break;

                keptTextElements++;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString().TrimEnd() + "...";
    }

    public static string SanitizeAndEllipsize(string? value, int maxTextElements, int maxCombiningMarksPerBase = 2)
    {
        return Ellipsize(Sanitize(value, maxCombiningMarksPerBase), maxTextElements);
    }

    private static bool IsWhitespace(Rune rune, UnicodeCategory category)
    {
        return category is UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            || rune.Value is '\t' or '\n' or '\r' or '\f' or '\v';
    }

    private static bool IsCombiningMark(UnicodeCategory category)
    {
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    private static bool ShouldDrop(UnicodeCategory category)
    {
        return category is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.Surrogate
            or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.PrivateUse;
    }
}
