using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Content.Shared.Chat;
using Content.Shared.Speech;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;

namespace Content.Client.Chat.UI;

internal static class DialogueBlipModulation
{
    private const float Tau = 6.2831855f;
    private const float MinPitchScale = 0.72f;
    private const float MaxPitchScale = 1.45f;
    private const float MaxVariation = 0.22f;
    private const int NeighborScanLimit = 5;
    private const char EllipsisRune = '\u2026';
    private const char EmDashRune = '\u2014';
    private const char EnDashRune = '\u2013';

    public static DialogueBlipMessageContext BuildContext(
        IReadOnlyList<string> textElements,
        EntityUid senderEntity,
        SpeechBubble.SpeechType speechType,
        ChatSpeechTransport speechTransport)
    {
        var voicedOrdinalsByIndex = new int[textElements.Count];
        for (var i = 0; i < voicedOrdinalsByIndex.Length; i++)
        {
            voicedOrdinalsByIndex[i] = -1;
        }

        var voicedCount = 0;
        var casedLetterCount = 0;
        var uppercaseLetterCount = 0;

        for (var i = 0; i < textElements.Count; i++)
        {
            if (!DialogueRevealTextElementHelper.IsSilentTextElementForDialogueBlip(textElements[i]))
            {
                voicedOrdinalsByIndex[i] = voicedCount;
                voicedCount++;
            }

            CountCasedLetters(textElements[i], ref casedLetterCount, ref uppercaseLetterCount);
        }

        var capsRatio = casedLetterCount <= 0
            ? 0f
            : uppercaseLetterCount / (float) casedLetterCount;
        var expressiveness = Math.Clamp(1.16f - MathF.Max(0f, voicedCount - 8) * 0.0125f, 0.8f, 1.16f);
        var pitchBias = Lerp(-0.045f, 0.045f, Hash01(senderEntity.Id * 173 + voicedCount * 37));
        var contourPhase = Hash01(senderEntity.Id * 719 + casedLetterCount * 11 + voicedCount * 53) * Tau;

        return new DialogueBlipMessageContext(
            textElements,
            voicedOrdinalsByIndex,
            voicedCount,
            capsRatio,
            expressiveness,
            pitchBias,
            contourPhase,
            speechType,
            speechTransport);
    }

    public static DialogueBlipTextElementModulation GetTextElementModulation(
        in DialogueBlipMessageContext context,
        int textElementIndex,
        float speedScale,
        in DialogueBlipProfile profile)
    {
        var textElement = context.TextElements[textElementIndex];
        var voicedOrdinal = context.VoicedOrdinalsByIndex[textElementIndex];
        var voicedCount = Math.Max(1, context.VoicedCount);
        var progress = voicedOrdinal < 0 || voicedCount <= 1
            ? 0f
            : voicedOrdinal / (float) (voicedCount - 1);

        var pitchScale = profile.Pitch * (1f + context.SpeakerPitchBias);
        pitchScale *= 1f + MathF.Sin(context.ContourPhase + voicedOrdinal * 0.82f) * 0.018f * context.Expressiveness;

        var variation = Math.Clamp(profile.Variation, 0f, MaxVariation);
        var volumeOffset = profile.VolumeOffset;
        var charsPerBlip = Math.Max(1, profile.CharactersPerBlip);
        var delayMultiplier = 1f;
        var audioFlags = AudioFlags.None;
        var occlusion = 0f;

        pitchScale *= profile.VoiceTone.GetPitchScale();
        variation *= profile.VoiceTone.GetVariationScale();
        volumeOffset += profile.VoiceTone.GetVolumeOffsetDb();
        charsPerBlip += profile.VoiceTone.GetCharactersPerBlipDelta();
        delayMultiplier *= profile.VoiceTone.GetCadenceScale();

        var globalCapsBoost = MathF.Max(0f, context.CapsRatio - 0.24f);
        if (globalCapsBoost > 0f)
        {
            pitchScale *= 1f + globalCapsBoost * 0.09f;
            variation += globalCapsBoost * 0.03f;
            volumeOffset += globalCapsBoost * 1.35f;
        }

        var shortLineExpressiveness = (context.Expressiveness - 1f) * (0.55f + (1f - progress) * 0.45f);
        if (shortLineExpressiveness > 0f)
            pitchScale *= 1f + shortLineExpressiveness * 0.05f;

        if (IsMostlyUppercase(textElement))
        {
            pitchScale *= 1.03f;
            variation += 0.012f;
            volumeOffset += 0.3f;
            charsPerBlip = Math.Max(1, charsPerBlip - 1);
        }

        var speedDelta = speedScale - 1f;
        if (MathF.Abs(speedDelta) > 0.001f)
        {
            pitchScale *= 1f + speedDelta * 0.02f;
            variation += MathF.Abs(speedDelta) * 0.008f;
            delayMultiplier *= 1f - speedDelta * 0.04f;
            if (speedDelta >= 0.3f)
                charsPerBlip = Math.Max(1, charsPerBlip - 1);
            else if (speedDelta <= -0.3f)
                charsPerBlip++;
        }

        if (context.SpeechType == SpeechBubble.SpeechType.Whisper)
        {
            pitchScale *= 0.965f;
            variation = MathF.Max(0.01f, variation - 0.01f);
            volumeOffset -= 1.1f;
            charsPerBlip++;
            delayMultiplier *= 1.07f;
        }

        ApplyPunctuationModulation(context.TextElements, textElementIndex, ref pitchScale, ref variation, ref volumeOffset, ref charsPerBlip, ref delayMultiplier, progress);

        if (context.SpeechTransport == ChatSpeechTransport.Radio)
        {
            pitchScale *= 1.01f;
            variation = Math.Max(0.012f, variation * 0.82f + 0.004f);
            volumeOffset -= 1.55f;
            delayMultiplier *= 0.96f;
            audioFlags |= AudioFlags.NoOcclusion;
            occlusion = 0.38f;
        }

        return new DialogueBlipTextElementModulation(
            Math.Clamp(pitchScale, MinPitchScale, MaxPitchScale),
            Math.Clamp(variation, 0f, MaxVariation),
            volumeOffset,
            Math.Clamp(charsPerBlip, 1, 5),
            Math.Clamp(delayMultiplier, 0.78f, 1.35f),
            audioFlags,
            occlusion);
    }

    private static void ApplyPunctuationModulation(
        IReadOnlyList<string> textElements,
        int textElementIndex,
        ref float pitchScale,
        ref float variation,
        ref float volumeOffset,
        ref int charsPerBlip,
        ref float delayMultiplier,
        float progress)
    {
        var current = NormalizePunctuation(textElements[textElementIndex]);
        var previous = NormalizePunctuation(CollectAdjacentSilentText(textElements, textElementIndex, -1));
        var next = NormalizePunctuation(CollectAdjacentSilentText(textElements, textElementIndex, 1));

        if (ContainsEllipsis(current) || ContainsEllipsis(next))
        {
            pitchScale *= 0.925f;
            variation = MathF.Max(0.01f, variation - 0.014f);
            volumeOffset -= 0.4f;
            charsPerBlip++;
            delayMultiplier *= 1.18f;
        }

        if (ContainsUrgentQuestion(next))
        {
            pitchScale *= 1.13f + progress * 0.03f;
            variation += 0.022f;
            volumeOffset += 0.8f;
            charsPerBlip = Math.Max(1, charsPerBlip - 1);
            delayMultiplier *= 0.89f;
        }
        else if (next.Contains('?'))
        {
            pitchScale *= 1.08f + progress * 0.03f;
            variation += 0.016f;
            delayMultiplier *= 0.96f;
        }
        else if (next.Contains('!'))
        {
            pitchScale *= 1.06f + (1f - progress) * 0.015f;
            variation += 0.018f;
            volumeOffset += 0.55f;
            charsPerBlip = Math.Max(1, charsPerBlip - 1);
            delayMultiplier *= 0.92f;
        }
        else if (current.Contains('.') || next.Contains('.'))
        {
            pitchScale *= 0.975f;
            delayMultiplier *= 1.04f;
        }

        if (current.Contains(',') || next.Contains(','))
        {
            pitchScale *= 0.988f;
            delayMultiplier *= 1.08f;
        }

        if (current.Contains(';') || next.Contains(';'))
        {
            pitchScale *= 0.982f;
            delayMultiplier *= 1.12f;
        }

        if (current.Contains(':') || next.Contains(':'))
        {
            delayMultiplier *= 1.1f;
        }

        if (ContainsDash(current) || ContainsDash(next))
        {
            pitchScale *= 0.982f;
            delayMultiplier *= 1.11f;
        }

        if (previous.Contains('!'))
        {
            pitchScale *= 1.02f;
            volumeOffset += 0.15f;
        }
        else if (previous.Contains('?'))
        {
            pitchScale *= 1.015f;
        }
        else if (ContainsEllipsis(previous))
        {
            pitchScale *= 0.97f;
            delayMultiplier *= 1.04f;
        }
    }

    private static void CountCasedLetters(string textElement, ref int casedLetterCount, ref int uppercaseLetterCount)
    {
        foreach (var rune in textElement.EnumerateRunes())
        {
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                    casedLetterCount++;
                    uppercaseLetterCount++;
                    break;
                case UnicodeCategory.LowercaseLetter:
                    casedLetterCount++;
                    break;
            }
        }
    }

    private static bool IsMostlyUppercase(string textElement)
    {
        var uppercase = 0;
        var lowercase = 0;

        foreach (var rune in textElement.EnumerateRunes())
        {
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                    uppercase++;
                    break;
                case UnicodeCategory.LowercaseLetter:
                    lowercase++;
                    break;
            }
        }

        return uppercase > 0 && lowercase == 0;
    }

    private static string CollectAdjacentSilentText(IReadOnlyList<string> textElements, int textElementIndex, int direction)
    {
        var builder = new StringBuilder();

        for (var step = 1; step <= NeighborScanLimit; step++)
        {
            var index = textElementIndex + direction * step;
            if (index < 0 || index >= textElements.Count)
                break;

            var textElement = textElements[index];
            if (DialogueRevealTextElementHelper.IsWhitespace(textElement))
                continue;

            if (!DialogueRevealTextElementHelper.IsSilentTextElementForDialogueBlip(textElement))
                break;

            if (direction < 0)
                builder.Insert(0, textElement);
            else
                builder.Append(textElement);
        }

        return builder.ToString();
    }

    private static string NormalizePunctuation(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
    }

    private static bool ContainsUrgentQuestion(string value)
    {
        return value.Contains("?!", StringComparison.Ordinal) || value.Contains("!?", StringComparison.Ordinal);
    }

    private static bool ContainsEllipsis(string value)
    {
        return value.Contains("...", StringComparison.Ordinal) || value.Contains(EllipsisRune);
    }

    private static bool ContainsDash(string value)
    {
        return value.Contains('-') || value.Contains(EmDashRune) || value.Contains(EnDashRune);
    }

    private static float Hash01(int seed)
    {
        unchecked
        {
            var x = (uint) seed;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777215f;
        }
    }

    private static float Lerp(float from, float to, float amount)
    {
        return from + (to - from) * amount;
    }
}

internal readonly record struct DialogueBlipMessageContext(
    IReadOnlyList<string> TextElements,
    int[] VoicedOrdinalsByIndex,
    int VoicedCount,
    float CapsRatio,
    float Expressiveness,
    float SpeakerPitchBias,
    float ContourPhase,
    SpeechBubble.SpeechType SpeechType,
    ChatSpeechTransport SpeechTransport);

internal readonly record struct DialogueBlipTextElementModulation(
    float PitchScale,
    float Variation,
    float VolumeOffsetDb,
    int CharactersPerBlip,
    float DelayMultiplier,
    AudioFlags AudioFlags,
    float Occlusion);
