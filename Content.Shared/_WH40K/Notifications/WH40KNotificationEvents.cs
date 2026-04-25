using System;
using System.Collections.Generic;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Notifications;

[Serializable, NetSerializable]
public sealed class WH40KNotificationEvent : EntityEventArgs
{
    public string Title { get; }
    public string Text { get; }
    public Color AccentColor { get; }
    public float DurationSeconds { get; }
    public bool Marquee { get; }
    public WH40KNotificationSize Size { get; }

    public WH40KNotificationEvent(
        string title,
        string text,
        Color accentColor,
        float durationSeconds,
        bool marquee,
        WH40KNotificationSize size)
    {
        Title = title;
        Text = text;
        AccentColor = accentColor;
        DurationSeconds = durationSeconds;
        Marquee = marquee;
        Size = size;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KLocalizedNotificationEvent : EntityEventArgs
{
    public string Title { get; init; } = "wh40k-notification-title-vox";
    public string LocKey { get; init; } = string.Empty;
    public Dictionary<string, string>? LocArgs { get; init; }
    public bool ResolveArgValues { get; init; }
    public Color AccentColor { get; init; } = WH40KNotificationColors.Event;
    public float DurationSeconds { get; init; } = 8f;
    public bool Marquee { get; init; } = false;
    public WH40KNotificationSize Size { get; init; } = WH40KNotificationSize.Wide;
}

[Serializable, NetSerializable]
public enum WH40KNotificationSize : byte
{
    Compact,
    Standard,
    Wide
}

public static class WH40KNotificationColors
{
    public static readonly Color Neutral = Color.FromHex("#D6D6D6");
    public static readonly Color Imperium = Color.FromHex("#F3C548");
    public static readonly Color Chaos = Color.FromHex("#E03232");
    public static readonly Color Tau = Color.FromHex("#4DA7FF");
    public static readonly Color Weather = Color.FromHex("#66D7FF");
    public static readonly Color Event = Color.FromHex("#FF9B2F");
    public static readonly Color Objective = Color.FromHex("#B86CFF");
    public static readonly Color Warning = Color.FromHex("#FF4D4D");
    public static readonly Color Success = Color.FromHex("#61D66F");

    public static Color ForTeam(string teamId)
    {
        if (string.Equals(teamId, "Imperium", StringComparison.OrdinalIgnoreCase))
            return Imperium;

        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(teamId, "Chaos", StringComparison.OrdinalIgnoreCase))
        {
            return Chaos;
        }

        if (string.Equals(teamId, "Tau", StringComparison.OrdinalIgnoreCase))
            return Tau;

        return Neutral;
    }
}
