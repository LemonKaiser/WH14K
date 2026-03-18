using System;
using Robust.Shared.Localization;

namespace Content.Client._WH40K.TacticalMap.UI;

internal static class WH40KTacticalMapLoc
{
    public static string LocalizeCaptureLabel(string? callsign, string fallbackLabel)
    {
        if (!string.IsNullOrWhiteSpace(callsign))
        {
            return Loc.GetString(
                "wh40k-tactical-map-capture-label",
                ("callsign", LocalizeCallsign(callsign)));
        }

        return string.IsNullOrWhiteSpace(fallbackLabel)
            ? Loc.GetString("wh40k-tactical-map-capture-fallback")
            : fallbackLabel;
    }

    public static string LocalizeCallsign(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            return string.Empty;

        var trimmed = callsign.Trim();
        var separator = trimmed.IndexOf('-', StringComparison.Ordinal);
        var baseToken = separator >= 0 ? trimmed[..separator] : trimmed;
        var suffix = separator >= 0 ? trimmed[separator..] : string.Empty;
        var key = $"wh40k-tactical-map-callsign-{baseToken.ToLowerInvariant()}";

        var localized = Loc.TryGetString(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : baseToken;

        return $"{localized}{suffix}";
    }

    public static string LocalizeTeamName(string? teamId, string fallbackDisplayName = "")
    {
        if (!string.IsNullOrWhiteSpace(teamId) &&
            TryGetTeamLocKey(teamId, out var key) &&
            Loc.TryGetString(key, out var localized) &&
            !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        if (!string.IsNullOrWhiteSpace(fallbackDisplayName))
            return fallbackDisplayName;

        return teamId ?? string.Empty;
    }

    private static bool TryGetTeamLocKey(string teamId, out string key)
    {
        switch (teamId.Trim().ToLowerInvariant())
        {
            case "imperium":
                key = "wh40k-team-imperium";
                return true;
            case "heretics":
                key = "wh40k-team-heretics";
                return true;
            default:
                key = string.Empty;
                return false;
        }
    }
}
