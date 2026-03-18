using System.Globalization;
using System.Text.RegularExpressions;
using Robust.Shared.Localization;

namespace Content.Shared.Humanoid;

public static partial class HumanoidNameScriptHelper
{
    private static readonly Regex LatinRegex = new("[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex CyrillicRegex = new("[А-Яа-яЁё]", RegexOptions.Compiled);
    private static readonly Regex UnresolvedNameDatasetRegex = new(@"\bnames-[a-z0-9-]+-dataset-\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool ContainsLatin(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && LatinRegex.IsMatch(value);
    }

    public static bool ContainsCyrillic(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && CyrillicRegex.IsMatch(value);
    }

    public static bool IsMixedScript(string value)
    {
        return ContainsLatin(value) && ContainsCyrillic(value);
    }

    public static bool ContainsUnresolvedDatasetId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && UnresolvedNameDatasetRegex.IsMatch(value);
    }

    public static string ResolveUnresolvedDatasetIds(string value)
    {
        if (!ContainsUnresolvedDatasetId(value))
            return value;

        return UnresolvedNameDatasetRegex.Replace(value, match => Loc.GetString(match.Value));
    }

    public static bool MatchesPreferredScript(string value, CultureInfo? culture)
    {
        var hasLatin = ContainsLatin(value);
        var hasCyrillic = ContainsCyrillic(value);

        if (!hasLatin && !hasCyrillic)
            return false;

        return GetPreferredScript(culture) switch
        {
            HumanoidNameScript.Cyrillic => hasCyrillic && !hasLatin,
            _ => hasLatin && !hasCyrillic,
        };
    }

    public static HumanoidNameScript GetPreferredScript(CultureInfo? culture)
    {
        return string.Equals(culture?.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase)
            ? HumanoidNameScript.Cyrillic
            : HumanoidNameScript.Latin;
    }
}

public enum HumanoidNameScript : byte
{
    Latin,
    Cyrillic,
}
