using System.Globalization;
using System.Text;

namespace Content.Shared._WH40K.Chat.Translation;

public static class WH40KChatTranslationMarkup
{
    public const string RussianLanguageCode = "RU";
    public const string EnglishLanguageCode = "EN";
    public const string TagName = "wh40kchatlang";
    private const string AllowedTranslationPunctuation = ".,!?;:'\"()[]{}<>-_=+/\\|@#$%^&*~`";

    public static bool IsSupportedLanguage(string? languageCode)
    {
        return NormalizeLanguageCode(languageCode) != null;
    }

    public static string? NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return null;

        return languageCode.Trim().ToUpperInvariant() switch
        {
            RussianLanguageCode => RussianLanguageCode,
            EnglishLanguageCode => EnglishLanguageCode,
            _ => null,
        };
    }

    public static string BuildTagMarkup(string languageCode, string originalText)
    {
        var normalized = NormalizeLanguageCode(languageCode) ?? EnglishLanguageCode;
        var encoded = EncodeOriginalText(originalText);
        return $"[{TagName} lang=\"{normalized}\" original=\"{encoded}\"][/{TagName}]";
    }

    public static string EncodeOriginalText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecodeOriginalText(string? encoded, out string decoded)
    {
        decoded = string.Empty;

        if (string.IsNullOrWhiteSpace(encoded))
            return false;

        try
        {
            var padded = encoded
                .Replace('-', '+')
                .Replace('_', '/');

            var remainder = padded.Length % 4;
            if (remainder != 0)
                padded = padded.PadRight(padded.Length + (4 - remainder), '=');

            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizeTranslationText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingWhitespace = false;

        foreach (var rawChar in text.Normalize(NormalizationForm.FormKC))
        {
            var mapped = MapTranslationCharacter(rawChar);
            if (mapped == null)
            {
                if (ShouldDropSilently(rawChar))
                    continue;

                if (builder.Length > 0)
                    pendingWhitespace = true;

                continue;
            }

            foreach (var ch in mapped)
            {
                if (char.IsWhiteSpace(ch))
                {
                    pendingWhitespace = true;
                    continue;
                }

                if (pendingWhitespace && builder.Length > 0)
                    builder.Append(' ');

                pendingWhitespace = false;
                builder.Append(ch);
            }
        }

        return builder.ToString().Trim();
    }

    public static string NormalizeCacheText(string text)
    {
        return NormalizeTranslationText(text);
    }

    public static string? ResolveLanguageFromText(string text, string? fallbackLanguage = null)
    {
        var normalizedFallback = NormalizeLanguageCode(fallbackLanguage);

        var normalizedText = NormalizeTranslationText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
            return normalizedFallback;

        var cyrillicCount = 0;
        var latinCount = 0;

        foreach (var ch in normalizedText)
        {
            if (!char.IsLetter(ch))
                continue;

            if (IsCyrillic(ch))
            {
                cyrillicCount++;
                continue;
            }

            if (IsLatin(ch))
                latinCount++;
        }

        if (cyrillicCount == 0 && latinCount == 0)
            return normalizedFallback;

        if (cyrillicCount > 0 && latinCount == 0)
            return RussianLanguageCode;

        if (latinCount > 0 && cyrillicCount == 0)
            return EnglishLanguageCode;

        if (cyrillicCount > latinCount)
            return RussianLanguageCode;

        if (latinCount > cyrillicCount)
            return EnglishLanguageCode;

        return normalizedFallback;
    }

    private static bool IsLatin(char ch)
    {
        return ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    private static bool IsCyrillic(char ch)
    {
        return ch is >= '\u0400' and <= '\u04FF' or >= '\u0500' and <= '\u052F';
    }

    private static string? MapTranslationCharacter(char ch)
    {
        return ch switch
        {
            '\u2018' or '\u2019' or '\u201B' or '\u2032' => "'",
            '\u201C' or '\u201D' or '\u201F' or '\u2033' => "\"",
            '\u2013' or '\u2014' or '\u2015' or '\u2212' => "-",
            '\u2026' => "...",
            _ when char.IsLetterOrDigit(ch) => ch.ToString(),
            _ when char.IsWhiteSpace(ch) => " ",
            _ when AllowedTranslationPunctuation.Contains(ch) => ch.ToString(),
            _ => null,
        };
    }

    private static bool ShouldDropSilently(char ch)
    {
        if (!Rune.TryCreate(ch, out var rune))
            return true;

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.NonSpacingMark => true,
            UnicodeCategory.SpacingCombiningMark => true,
            UnicodeCategory.EnclosingMark => true,
            UnicodeCategory.Control => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.Surrogate => true,
            UnicodeCategory.PrivateUse => true,
            UnicodeCategory.OtherNotAssigned => true,
            _ => false,
        };
    }
}
