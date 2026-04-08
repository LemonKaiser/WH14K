using System;

namespace Content.Shared._WH40K.Interface;

public static class WH40KInterfaceThemes
{
    public const string Auto = "WH40KAutoTheme";
    public const string Default = "SS14DefaultTheme";
    public const string Imperium = "WH40KImperiumTheme";
    public const string Heretics = "WH40KChaosTheme";

    public static bool IsAuto(string? themeId)
    {
        return string.Equals(themeId, Auto, StringComparison.Ordinal);
    }

    public static bool IsFactionTheme(string? themeId)
    {
        return string.Equals(themeId, Imperium, StringComparison.Ordinal) ||
               string.Equals(themeId, Heretics, StringComparison.Ordinal);
    }
}
