using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI
{
    internal static class DialogueRevealTextElementHelper
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

        public static string[] GetTextElements(FormattedMessage message)
        {
            var elements = new List<string>(message.ToString().Length);

            foreach (var node in message)
            {
                if (node.Name != null ||
                    !node.Value.TryGetString(out var text) ||
                    string.IsNullOrEmpty(text))
                {
                    continue;
                }

                AppendTextElements(elements, text);
            }

            return elements.ToArray();
        }

        public static FormattedMessage BuildVisibleMessage(FormattedMessage source, int visibleTextElements)
        {
            if (visibleTextElements <= 0)
                return FormattedMessage.Empty;

            var builder = new StringBuilder();
            var openTags = new Stack<MarkupNode>();
            var remaining = visibleTextElements;

            foreach (var node in source)
            {
                if (remaining <= 0)
                    break;

                if (node.Name == null)
                {
                    if (!node.Value.TryGetString(out var text) || string.IsNullOrEmpty(text))
                        continue;

                    remaining -= AppendTextElementPrefix(builder, text, remaining);
                    continue;
                }

                builder.Append(node.ToString());

                if (node.Closing)
                {
                    if (openTags.Count > 0)
                        openTags.Pop();
                }
                else
                {
                    openTags.Push(node);
                }
            }

            while (openTags.Count > 0)
            {
                builder.Append(new MarkupNode(openTags.Pop().Name, null, null, true));
            }

            return builder.Length == 0
                ? FormattedMessage.Empty
                : FormattedMessage.FromMarkupOrThrow(builder.ToString());
        }

        public static bool IsWhitespace(string textElement)
        {
            if (string.IsNullOrEmpty(textElement))
                return true;

            foreach (var rune in textElement.EnumerateRunes())
            {
                if (!Rune.IsWhiteSpace(rune))
                    return false;
            }

            return true;
        }

        public static bool IsSilentTextElementForDialogueBlip(string textElement)
        {
            if (string.IsNullOrEmpty(textElement))
                return true;

            foreach (var rune in textElement.EnumerateRunes())
            {
                if (Rune.IsWhiteSpace(rune))
                    continue;

                switch (Rune.GetUnicodeCategory(rune))
                {
                    case UnicodeCategory.ClosePunctuation:
                    case UnicodeCategory.ConnectorPunctuation:
                    case UnicodeCategory.CurrencySymbol:
                    case UnicodeCategory.DashPunctuation:
                    case UnicodeCategory.FinalQuotePunctuation:
                    case UnicodeCategory.InitialQuotePunctuation:
                    case UnicodeCategory.MathSymbol:
                    case UnicodeCategory.ModifierSymbol:
                    case UnicodeCategory.OpenPunctuation:
                    case UnicodeCategory.OtherPunctuation:
                    case UnicodeCategory.OtherSymbol:
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }

        public static Rune GetLeadingRune(string textElement)
        {
            foreach (var rune in textElement.EnumerateRunes())
            {
                return rune;
            }

            return Rune.ReplacementChar;
        }

        private static int AppendTextElementPrefix(StringBuilder builder, string text, int maxTextElements)
        {
            if (maxTextElements <= 0 || string.IsNullOrEmpty(text))
                return 0;

            var written = 0;
            var current = new StringBuilder();
            TextElementState? state = null;

            foreach (var rune in text.EnumerateRunes())
            {
                if (current.Length == 0)
                {
                    AppendRune(current, rune);
                    state = TextElementState.Start(rune);
                    continue;
                }

                if (state != null && ShouldContinueTextElement(state.Value, rune))
                {
                    AppendRune(current, rune);
                    state = state.Value.Append(rune);
                    continue;
                }

                builder.Append(FormattedMessage.EscapeText(current.ToString()));
                written++;
                if (written >= maxTextElements)
                    return written;

                current.Clear();
                AppendRune(current, rune);
                state = TextElementState.Start(rune);
            }

            if (current.Length > 0 && written < maxTextElements)
            {
                builder.Append(FormattedMessage.EscapeText(current.ToString()));
                written++;
            }

            return written;
        }

        private static void AppendTextElements(List<string> destination, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var current = new StringBuilder();
            TextElementState? state = null;

            foreach (var rune in text.EnumerateRunes())
            {
                if (current.Length == 0)
                {
                    AppendRune(current, rune);
                    state = TextElementState.Start(rune);
                    continue;
                }

                if (state != null && ShouldContinueTextElement(state.Value, rune))
                {
                    AppendRune(current, rune);
                    state = state.Value.Append(rune);
                    continue;
                }

                destination.Add(current.ToString());
                current.Clear();
                AppendRune(current, rune);
                state = TextElementState.Start(rune);
            }

            if (current.Length > 0)
                destination.Add(current.ToString());
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

        private static void AppendRune(StringBuilder builder, Rune rune)
        {
            builder.Append(rune.ToString());
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
}
