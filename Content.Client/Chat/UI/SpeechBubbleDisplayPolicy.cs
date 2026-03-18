using System;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI
{
    internal static class SpeechBubbleDisplayPolicy
    {
        public const int MaxVisibleTextElements = 100;
        public const int MaxAnimatedTextElements = 72;
        public const int MaxQueuedBubblesPerEntity = 4;
        public const int MaxBlipsPerBubble = 24;
        public const float MaxRevealDurationSeconds = 2.4f;
        public const float MinSecondsBetweenBlips = 0.04f;
        public const string TruncationSuffix = "...";

        public static FormattedMessage LimitMessage(FormattedMessage source)
        {
            var textElements = DialogueRevealTextElementHelper.GetTextElements(source);
            if (textElements.Length <= MaxVisibleTextElements)
                return new FormattedMessage(source);

            var visibleLimit = Math.Max(0, MaxVisibleTextElements - TruncationSuffix.Length);
            var limited = DialogueRevealTextElementHelper.BuildVisibleMessage(source, visibleLimit);
            limited.AddText(TruncationSuffix);
            return limited;
        }

        public static int GetAnimatedTextElementCount(int visibleTextElementCount, float charactersPerSecond)
        {
            if (visibleTextElementCount <= 0 || charactersPerSecond <= 0f)
                return 0;

            var durationBudgetLimit = (int) MathF.Ceiling(MaxRevealDurationSeconds * MathF.Max(1f, charactersPerSecond));
            return Math.Clamp(Math.Min(MaxAnimatedTextElements, durationBudgetLimit), 0, visibleTextElementCount);
        }
    }
}
