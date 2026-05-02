using System;
using System.Collections.Generic;
using Robust.Shared.Audio;
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
    public WH40KNotificationCategory Category { get; }
    public WH40KNotificationPriority Priority { get; }
    public WH40KNotificationIcon Icon { get; }
    public string StackKey { get; }
    public bool IgnoreUserPreferences { get; }
    public SoundSpecifier? Sound { get; }

    public WH40KNotificationEvent(
        string title,
        string text,
        Color accentColor,
        float durationSeconds,
        bool marquee,
        WH40KNotificationSize size,
        WH40KNotificationCategory category = WH40KNotificationCategory.Auto,
        WH40KNotificationPriority priority = WH40KNotificationPriority.Auto,
        WH40KNotificationIcon icon = WH40KNotificationIcon.Auto,
        string stackKey = "",
        bool ignoreUserPreferences = false,
        SoundSpecifier? sound = null)
    {
        Title = title;
        Text = text;
        AccentColor = accentColor;
        DurationSeconds = durationSeconds;
        Marquee = marquee;
        Size = size;
        Category = category;
        Priority = priority;
        Icon = icon;
        StackKey = stackKey;
        IgnoreUserPreferences = ignoreUserPreferences;
        Sound = sound;
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
    public WH40KNotificationCategory Category { get; init; } = WH40KNotificationCategory.Auto;
    public WH40KNotificationPriority Priority { get; init; } = WH40KNotificationPriority.Auto;
    public WH40KNotificationIcon Icon { get; init; } = WH40KNotificationIcon.Auto;
    public string StackKey { get; init; } = string.Empty;
    public bool IgnoreUserPreferences { get; init; } = false;
    public SoundSpecifier? Sound { get; init; }
}

[Serializable, NetSerializable]
public enum WH40KNotificationSize : byte
{
    Compact,
    Standard,
    Wide
}

[Serializable, NetSerializable]
public enum WH40KNotificationCategory : byte
{
    Auto,
    Admin,
    Critical,
    Point,
    Weather,
    Event,
    Objective,
    Mission,
    Economy,
    Reinforcement,
    Info
}

[Serializable, NetSerializable]
public enum WH40KNotificationPriority : short
{
    Auto = 0,
    Info = 100,
    Economy = 120,
    Mission = 180,
    Reinforcement = 260,
    Objective = 420,
    Event = 560,
    Weather = 620,
    Critical = 780,
    Point = 860,
    Admin = 1000
}

[Serializable, NetSerializable]
public enum WH40KNotificationIcon : byte
{
    Auto,
    Vox,
    Aquila,
    Chaos,
    Weather,
    Event,
    Objective,
    Mission,
    Point,
    Warning,
    Supply,
    Cog,
    Skull,
    Admin,
    Tau,
    Warp
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
    public static readonly Color Admin = Color.FromHex("#FF3030");

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

public static class WH40KNotificationMetadata
{
    public const string DisplayModeFull = "full";
    public const string DisplayModeCompact = "compact";
    public const string DisplayModeOff = "off";
    private static readonly Color HereticsTeamColor = Color.FromHex("#D62828");

    public static WH40KNotificationPriority DefaultPriority(WH40KNotificationCategory category)
    {
        return category switch
        {
            WH40KNotificationCategory.Admin => WH40KNotificationPriority.Admin,
            WH40KNotificationCategory.Point => WH40KNotificationPriority.Point,
            WH40KNotificationCategory.Critical => WH40KNotificationPriority.Critical,
            WH40KNotificationCategory.Weather => WH40KNotificationPriority.Weather,
            WH40KNotificationCategory.Event => WH40KNotificationPriority.Event,
            WH40KNotificationCategory.Objective => WH40KNotificationPriority.Objective,
            WH40KNotificationCategory.Reinforcement => WH40KNotificationPriority.Reinforcement,
            WH40KNotificationCategory.Mission => WH40KNotificationPriority.Mission,
            WH40KNotificationCategory.Economy => WH40KNotificationPriority.Economy,
            _ => WH40KNotificationPriority.Info
        };
    }

    public static WH40KNotificationIcon DefaultIcon(WH40KNotificationCategory category)
    {
        return category switch
        {
            WH40KNotificationCategory.Admin => WH40KNotificationIcon.Admin,
            WH40KNotificationCategory.Critical => WH40KNotificationIcon.Warning,
            WH40KNotificationCategory.Point => WH40KNotificationIcon.Point,
            WH40KNotificationCategory.Weather => WH40KNotificationIcon.Weather,
            WH40KNotificationCategory.Event => WH40KNotificationIcon.Event,
            WH40KNotificationCategory.Objective => WH40KNotificationIcon.Objective,
            WH40KNotificationCategory.Mission => WH40KNotificationIcon.Mission,
            WH40KNotificationCategory.Economy => WH40KNotificationIcon.Cog,
            WH40KNotificationCategory.Reinforcement => WH40KNotificationIcon.Supply,
            _ => WH40KNotificationIcon.Vox
        };
    }

    public static WH40KNotificationIcon DefaultIcon(WH40KNotificationCategory category, Color accentColor)
    {
        if (category == WH40KNotificationCategory.Info)
        {
            if (IsChaosAccent(accentColor))
                return WH40KNotificationIcon.Chaos;

            if (IsCloseColor(accentColor, WH40KNotificationColors.Tau))
                return WH40KNotificationIcon.Tau;
        }

        return DefaultIcon(category);
    }

    public static string DefaultTitle(WH40KNotificationCategory category)
    {
        return category switch
        {
            WH40KNotificationCategory.Admin => "wh40k-notification-title-admin",
            WH40KNotificationCategory.Critical => "wh40k-notification-title-critical",
            WH40KNotificationCategory.Point => "wh40k-notification-title-point",
            WH40KNotificationCategory.Weather => "wh40k-notification-title-weather",
            WH40KNotificationCategory.Event => "wh40k-notification-title-event",
            WH40KNotificationCategory.Objective => "wh40k-notification-title-objective",
            WH40KNotificationCategory.Mission => "wh40k-notification-title-mission",
            WH40KNotificationCategory.Economy => "wh40k-notification-title-economy",
            WH40KNotificationCategory.Reinforcement => "wh40k-notification-title-reinforcement",
            _ => "wh40k-notification-title-vox"
        };
    }

    public static string DefaultTitle(WH40KNotificationCategory category, Color accentColor)
    {
        if (category == WH40KNotificationCategory.Info)
        {
            if (IsChaosAccent(accentColor))
                return "wh40k-notification-title-chaos";

            if (IsCloseColor(accentColor, WH40KNotificationColors.Tau))
                return "wh40k-notification-title-tau";
        }

        return DefaultTitle(category);
    }

    private static bool IsChaosAccent(Color accentColor)
    {
        return IsCloseColor(accentColor, WH40KNotificationColors.Chaos) ||
               IsCloseColor(accentColor, HereticsTeamColor);
    }

    private static bool IsCloseColor(Color value, Color target)
    {
        const float tolerance = 0.06f;
        return MathF.Abs(value.R - target.R) <= tolerance &&
               MathF.Abs(value.G - target.G) <= tolerance &&
               MathF.Abs(value.B - target.B) <= tolerance;
    }

    public static string CategoryId(WH40KNotificationCategory category)
    {
        return category.ToString().ToLowerInvariant();
    }

    public static bool TryParseCategory(string value, out WH40KNotificationCategory category)
    {
        foreach (WH40KNotificationCategory candidate in Enum.GetValues(typeof(WH40KNotificationCategory)))
        {
            if (candidate == WH40KNotificationCategory.Auto)
                continue;

            if (string.Equals(CategoryId(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                category = candidate;
                return true;
            }
        }

        category = WH40KNotificationCategory.Auto;
        return false;
    }

    public static WH40KNotificationCategory InferCategoryFromLocKey(string locKey)
    {
        var key = locKey.ToLowerInvariant();

        if (key.Contains("weather"))
            return WH40KNotificationCategory.Weather;

        if (key.Contains("influence") || key.Contains("captured") || key.Contains("objective-destroyed"))
            return WH40KNotificationCategory.Point;

        if (key.Contains("winner") || key.Contains("draw") || key.Contains("time-limit") || key.Contains("apocalypse"))
            return WH40KNotificationCategory.Critical;

        if (key.Contains("level-up") || key.Contains("level-buff") || key.Contains("periodic-bonus") || key.Contains("development"))
            return WH40KNotificationCategory.Economy;

        if (key.Contains("reinforcement") || key.Contains("airdrop"))
            return WH40KNotificationCategory.Reinforcement;

        if (key.Contains("mission"))
            return WH40KNotificationCategory.Mission;

        if (key.Contains("round-event") || key.Contains("phase") || key.Contains("orbital") || key.Contains("logistics") || key.Contains("black-front"))
            return WH40KNotificationCategory.Event;

        if (key.Contains("objective"))
            return WH40KNotificationCategory.Objective;

        return WH40KNotificationCategory.Info;
    }
}
