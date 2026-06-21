using System;
using Content.Client._WH40K.Command;
using Content.Shared._WH40K.TacticalMap;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.Client._WH40K.TacticalMap.UI;

/// <summary>
///     Localization helpers for tactical-map UI labels.
///     All methods are static and use the global <see cref="Loc"/> accessor,
///     which respects the current client-side culture set by the engine.
/// </summary>
internal static class WH40KTacticalMapLoc
{

    public static string LocalizeStrategicLabel(WH40KTacticalMapCapturePointMarker marker)
    {
        if (marker.Kind == WH40KTacticalMapStrategicMarkerKind.CommandNode)
        {
            if (!string.IsNullOrWhiteSpace(marker.Label))
            {
                return Loc.GetString(
                    "wh40k-tactical-map-command-node-ordinal",
                    ("ordinal", marker.Label));
            }

            return Loc.GetString("wh40k-tactical-map-command-node-fallback");
        }

        return LocalizeCaptureLabel(marker.Callsign, marker.Label);
    }

    public static string ResolveStrategicIcon(WH40KTacticalMapCapturePointMarker marker)
    {
        return marker.Kind switch
        {
            WH40KTacticalMapStrategicMarkerKind.CommandNode => "\u25A0",
            _ when marker.Relation == WH40KTacticalMapStrategicRelation.Contested => "x",
            _ when marker.Relation == WH40KTacticalMapStrategicRelation.Neutral => "o",
            _ => "\u25CF"
        };
    }

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

        var localized = IoCManager.Resolve<ILocalizationManager>().TryGetString(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : baseToken;

        return $"{localized}{suffix}";
    }

    public static string LocalizeTeamName(string? teamId, string fallbackDisplayName = "")
    {
        if (!string.IsNullOrWhiteSpace(teamId) &&
            TryGetTeamLocKey(teamId, out var key) &&
            IoCManager.Resolve<ILocalizationManager>().TryGetString(key, out var localized) &&
            !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        if (!string.IsNullOrWhiteSpace(fallbackDisplayName))
            return WH40KCommandUiStyles.ResolveLocalizedOrRaw(fallbackDisplayName);

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
