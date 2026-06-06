using System;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Shared.Localization;

namespace Content.Client._WH40K.Administration.Mute;

internal static class WH40KMuteDisplayHelper
{
    public static bool NeedsLiveRefresh(WH40KActiveMuteInfo? muteInfo)
    {
        return muteInfo?.ExpiresAtUtc != null;
    }

    public static string BuildChatPlaceholder(WH40KActiveMuteInfo? muteInfo)
    {
        if (muteInfo?.ExpiresAtUtc is not { } expiresAtUtc)
            return Loc.GetString("wh40k-chat-mute-placeholder-permanent");

        return BuildTemporaryPlaceholder(
            expiresAtUtc,
            "wh40k-chat-mute-placeholder-duration",
            "wh40k-chat-mute-placeholder-until");
    }

    public static string BuildAHelpPlaceholder(WH40KActiveMuteInfo? muteInfo)
    {
        if (muteInfo?.ExpiresAtUtc is not { } expiresAtUtc)
            return Loc.GetString("wh40k-ahelp-mute-placeholder-permanent");

        return BuildTemporaryPlaceholder(
            expiresAtUtc,
            "wh40k-ahelp-mute-placeholder-duration",
            "wh40k-ahelp-mute-placeholder-until");
    }

    public static string BuildMuteTooltip(WH40KActiveMuteInfo? muteInfo)
    {
        return muteInfo?.ExpiresAtUtc is { } expiresAtUtc
            ? Loc.GetString(
                "wh40k-mute-tooltip-temporary",
                ("reason", muteInfo.Reason),
                ("time", FormatAbsoluteDate(expiresAtUtc)))
            : Loc.GetString("wh40k-mute-tooltip-permanent", ("reason", muteInfo?.Reason ?? string.Empty));
    }

    private static string BuildTemporaryPlaceholder(
        DateTime expiresAtUtc,
        string durationKey,
        string untilKey)
    {
        return ShouldUseAbsoluteDate(expiresAtUtc)
            ? Loc.GetString(untilKey, ("time", FormatAbsoluteDate(expiresAtUtc)))
            : Loc.GetString(durationKey, ("time", FormatRelativeDuration(expiresAtUtc)));
    }

    private static bool ShouldUseAbsoluteDate(DateTime expiresAtUtc)
    {
        return expiresAtUtc - DateTime.UtcNow > TimeSpan.FromHours(24);
    }

    private static string FormatRelativeDuration(DateTime expiresAtUtc)
    {
        var remaining = expiresAtUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return Loc.GetString("wh40k-mute-time-seconds", ("count", 0));

        if (remaining.TotalMinutes < 1)
            return Loc.GetString("wh40k-mute-time-seconds", ("count", GetSeconds(remaining)));

        if (remaining.TotalHours < 1)
            return Loc.GetString("wh40k-mute-time-minutes", ("count", Math.Max(1, (int) Math.Ceiling(remaining.TotalMinutes))));

        var wholeHours = Math.Max(1, (int) remaining.TotalHours);
        var minuteRemainder = remaining - TimeSpan.FromHours(wholeHours);
        var wholeMinutes = (int) Math.Ceiling(Math.Max(0, minuteRemainder.TotalMinutes));

        if (wholeMinutes >= 60)
        {
            wholeHours += wholeMinutes / 60;
            wholeMinutes %= 60;
        }

        if (wholeMinutes <= 0)
            return Loc.GetString("wh40k-mute-time-hours", ("count", wholeHours));

        return Loc.GetString(
            "wh40k-mute-time-hours-minutes",
            ("hours", wholeHours),
            ("minutes", wholeMinutes));
    }

    private static int GetSeconds(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return 0;

        if (remaining.TotalSeconds < 1)
            return 1;

        return (int) Math.Floor(remaining.TotalSeconds);
    }

    private static string FormatAbsoluteDate(DateTime expiresAtUtc)
    {
        return expiresAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }
}
