using System.Globalization;
using System.Text;

namespace Content.Shared.Chat;

public static class ChatTextElementUtility
{
    private const int ZeroWidthJoiner = 0x200D;
    private const int VariationSelectorStart = 0xFE00;
    private const int VariationSelectorEnd = 0xFE0F;
    private const int VariationSelectorSupplementStart = 0xE0100;
    private const int VariationSelectorSupplementEnd = 0xE01EF;
    private const int EmojiModifierStart = 0x1F3FB;
    private const int EmojiModifierEnd = 0x1F3FF;
    private const int RegionalIndicatorStart = 0x1F1E6;
    private const int RegionalIndicatorEnd = 0x1F1FF;

    public static string CapitalizeLeadingTextElement(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        var firstElementLength = GetFirstTextElementLength(message);
        if (firstElementLength <= 0)
            return message;

        var firstElement = message[..firstElementLength];
        var remaining = firstElementLength >= message.Length
            ? string.Empty
            : message[firstElementLength..];

        return firstElement.ToUpper(CultureInfo.CurrentCulture) + remaining;
    }

    public static bool EndsWithLetterTextElement(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        var lastElement = GetLastTextElement(message);
        foreach (var rune in lastElement.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
                return true;
        }

        return false;
    }

    public static string GetLastTextElement(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        var currentStart = 0;
        var currentLength = 0;
        var utf16Index = 0;
        TextElementState? state = null;

        foreach (var rune in message.EnumerateRunes())
        {
            var runeLength = rune.Utf16SequenceLength;
            if (currentLength == 0)
            {
                currentStart = utf16Index;
                currentLength = runeLength;
                state = TextElementState.Start(rune);
                utf16Index += runeLength;
                continue;
            }

            if (state != null && ShouldContinueTextElement(state.Value, rune))
            {
                currentLength += runeLength;
                state = state.Value.Append(rune);
                utf16Index += runeLength;
                continue;
            }

            currentStart = utf16Index;
            currentLength = runeLength;
            state = TextElementState.Start(rune);
            utf16Index += runeLength;
        }

        if (currentLength <= 0)
            return string.Empty;

        return message.Substring(currentStart, currentLength);
    }

    private static int GetFirstTextElementLength(string message)
    {
        var length = 0;
        TextElementState? state = null;

        foreach (var rune in message.EnumerateRunes())
        {
            var runeLength = rune.Utf16SequenceLength;
            if (length == 0)
            {
                length = runeLength;
                state = TextElementState.Start(rune);
                continue;
            }

            if (state != null && ShouldContinueTextElement(state.Value, rune))
            {
                length += runeLength;
                state = state.Value.Append(rune);
                continue;
            }

            break;
        }

        return length;
    }

    private static bool ShouldContinueTextElement(TextElementState state, Rune currentRune)
    {
        if (IsCombiningMark(currentRune) ||
            IsVariationSelector(currentRune) ||
            IsEmojiModifier(currentRune))
        {
            return true;
        }

        if (IsZeroWidthJoiner(currentRune) || IsZeroWidthJoiner(state.PreviousRune))
            return true;

        return state.RegionalIndicatorCount == 1 &&
               IsRegionalIndicator(state.PreviousRune) &&
               IsRegionalIndicator(currentRune);
    }

    private static bool IsCombiningMark(Rune rune)
    {
        return Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark;
    }

    private static bool IsVariationSelector(Rune rune)
    {
        return rune.Value is >= VariationSelectorStart and <= VariationSelectorEnd or
            >= VariationSelectorSupplementStart and <= VariationSelectorSupplementEnd;
    }

    private static bool IsEmojiModifier(Rune rune)
    {
        return rune.Value is >= EmojiModifierStart and <= EmojiModifierEnd;
    }

    private static bool IsRegionalIndicator(Rune rune)
    {
        return rune.Value is >= RegionalIndicatorStart and <= RegionalIndicatorEnd;
    }

    private static bool IsZeroWidthJoiner(Rune rune)
    {
        return rune.Value == ZeroWidthJoiner;
    }

    private readonly record struct TextElementState(Rune PreviousRune, int RegionalIndicatorCount)
    {
        public static TextElementState Start(Rune rune)
        {
            return new TextElementState(rune, IsRegionalIndicator(rune) ? 1 : 0);
        }

        public TextElementState Append(Rune rune)
        {
            var regionalIndicatorCount = IsRegionalIndicator(rune)
                ? (IsRegionalIndicator(PreviousRune) ? RegionalIndicatorCount + 1 : 1)
                : 0;

            return new TextElementState(rune, regionalIndicatorCount);
        }
    }
}
