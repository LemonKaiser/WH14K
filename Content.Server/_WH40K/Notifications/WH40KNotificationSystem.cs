using System;
using System.Collections.Generic;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.Notifications;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Notifications;

public sealed class WH40KNotificationSystem : EntitySystem
{
    private const float MaxDurationSeconds = 120f;
    private const float DefaultSpamCooldownSeconds = 1.0f;
    private const int MaxTitleLength = 96;
    private const int MaxTextLength = 512;

    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<string, TimeSpan> _lastSentByStackKey = new(StringComparer.OrdinalIgnoreCase);

    public int SendGlobal(
        string title,
        string text,
        Color accentColor,
        float durationSeconds = 8f,
        bool marquee = true,
        WH40KNotificationSize size = WH40KNotificationSize.Standard,
        WH40KNotificationCategory category = WH40KNotificationCategory.Auto,
        WH40KNotificationPriority priority = WH40KNotificationPriority.Auto,
        WH40KNotificationIcon icon = WH40KNotificationIcon.Auto,
        string stackKey = "",
        bool ignoreUserPreferences = false,
        float spamCooldownSeconds = DefaultSpamCooldownSeconds,
        SoundSpecifier? sound = null)
    {
        var ev = BuildEvent(title, text, accentColor, durationSeconds, marquee, size, category, priority, icon, stackKey, ignoreUserPreferences, sound);
        if (IsThrottled(ev, spamCooldownSeconds))
            return 0;

        RaiseNetworkEvent(ev);
        return _players.Sessions.Length;
    }

    public int SendTeam(
        string teamId,
        string title,
        string text,
        Color accentColor,
        float durationSeconds = 8f,
        bool marquee = true,
        WH40KNotificationSize size = WH40KNotificationSize.Standard,
        WH40KNotificationCategory category = WH40KNotificationCategory.Auto,
        WH40KNotificationPriority priority = WH40KNotificationPriority.Auto,
        WH40KNotificationIcon icon = WH40KNotificationIcon.Auto,
        string stackKey = "",
        bool ignoreUserPreferences = false,
        float spamCooldownSeconds = DefaultSpamCooldownSeconds,
        SoundSpecifier? sound = null)
    {
        var ev = BuildEvent(title, text, accentColor, durationSeconds, marquee, size, category, priority, icon, stackKey, ignoreUserPreferences, sound);
        if (IsThrottled(ev, spamCooldownSeconds, teamId))
            return 0;

        var delivered = 0;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } entity)
                continue;

            if (!TryComp<WH40KTeamMemberComponent>(entity, out var team) ||
                !string.Equals(team.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            RaiseNetworkEvent(ev, session);
            delivered++;
        }

        return delivered;
    }

    public int SendFiltered(
        Filter filter,
        string title,
        string text,
        Color accentColor,
        float durationSeconds = 8f,
        bool marquee = true,
        WH40KNotificationSize size = WH40KNotificationSize.Standard,
        WH40KNotificationCategory category = WH40KNotificationCategory.Auto,
        WH40KNotificationPriority priority = WH40KNotificationPriority.Auto,
        WH40KNotificationIcon icon = WH40KNotificationIcon.Auto,
        string stackKey = "",
        bool ignoreUserPreferences = false,
        float spamCooldownSeconds = DefaultSpamCooldownSeconds,
        SoundSpecifier? sound = null)
    {
        var ev = BuildEvent(title, text, accentColor, durationSeconds, marquee, size, category, priority, icon, stackKey, ignoreUserPreferences, sound);
        if (IsThrottled(ev, spamCooldownSeconds))
            return 0;

        var delivered = 0;
        foreach (var session in filter.Recipients)
        {
            RaiseNetworkEvent(ev, session);
            delivered++;
        }

        return delivered;
    }

    public void SendToSession(
        ICommonSession session,
        string title,
        string text,
        Color accentColor,
        float durationSeconds = 8f,
        bool marquee = true,
        WH40KNotificationSize size = WH40KNotificationSize.Standard,
        WH40KNotificationCategory category = WH40KNotificationCategory.Auto,
        WH40KNotificationPriority priority = WH40KNotificationPriority.Auto,
        WH40KNotificationIcon icon = WH40KNotificationIcon.Auto,
        string stackKey = "",
        bool ignoreUserPreferences = false,
        float spamCooldownSeconds = DefaultSpamCooldownSeconds,
        SoundSpecifier? sound = null)
    {
        var ev = BuildEvent(title, text, accentColor, durationSeconds, marquee, size, category, priority, icon, stackKey, ignoreUserPreferences, sound);
        if (IsThrottled(ev, spamCooldownSeconds, session.UserId.ToString()))
            return;

        RaiseNetworkEvent(ev, session);
    }

    public int SendFilteredLocalized(
        Filter filter,
        string locKey,
        string title = "wh40k-notification-title-vox",
        Dictionary<string, string>? locArgs = null,
        bool resolveArgValues = false,
        Color? accentColor = null,
        float durationSeconds = 8f,
        bool marquee = false,
        WH40KNotificationSize size = WH40KNotificationSize.Wide,
        WH40KNotificationCategory category = WH40KNotificationCategory.Auto,
        WH40KNotificationPriority priority = WH40KNotificationPriority.Auto,
        WH40KNotificationIcon icon = WH40KNotificationIcon.Auto,
        string stackKey = "",
        bool ignoreUserPreferences = false,
        float spamCooldownSeconds = DefaultSpamCooldownSeconds,
        SoundSpecifier? sound = null)
    {
        var ev = BuildLocalizedEvent(
            title,
            locKey,
            locArgs,
            resolveArgValues,
            accentColor ?? WH40KNotificationColors.Event,
            durationSeconds,
            marquee,
            size,
            category,
            priority,
            icon,
            stackKey,
            ignoreUserPreferences,
            sound);

        if (IsThrottled(ev, spamCooldownSeconds))
            return 0;

        var delivered = 0;
        foreach (var session in filter.Recipients)
        {
            RaiseNetworkEvent(ev, session);
            delivered++;
        }

        return delivered;
    }

    public void SendLocalizedToSession(
        ICommonSession session,
        string locKey,
        string title = "wh40k-notification-title-vox",
        Dictionary<string, string>? locArgs = null,
        bool resolveArgValues = false,
        Color? accentColor = null,
        float durationSeconds = 8f,
        bool marquee = false,
        WH40KNotificationSize size = WH40KNotificationSize.Wide,
        WH40KNotificationCategory category = WH40KNotificationCategory.Auto,
        WH40KNotificationPriority priority = WH40KNotificationPriority.Auto,
        WH40KNotificationIcon icon = WH40KNotificationIcon.Auto,
        string stackKey = "",
        bool ignoreUserPreferences = false,
        float spamCooldownSeconds = DefaultSpamCooldownSeconds,
        SoundSpecifier? sound = null)
    {
        var ev = BuildLocalizedEvent(
            title,
            locKey,
            locArgs,
            resolveArgValues,
            accentColor ?? WH40KNotificationColors.Event,
            durationSeconds,
            marquee,
            size,
            category,
            priority,
            icon,
            stackKey,
            ignoreUserPreferences,
            sound);

        if (IsThrottled(ev, spamCooldownSeconds, session.UserId.ToString()))
            return;

        RaiseNetworkEvent(ev, session);
    }

    private static WH40KNotificationEvent BuildEvent(
        string title,
        string text,
        Color accentColor,
        float durationSeconds,
        bool marquee,
        WH40KNotificationSize size,
        WH40KNotificationCategory category,
        WH40KNotificationPriority priority,
        WH40KNotificationIcon icon,
        string stackKey,
        bool ignoreUserPreferences,
        SoundSpecifier? sound)
    {
        var normalizedTitle = ClampText(title, MaxTitleLength);
        var normalizedText = ClampText(text, MaxTextLength);
        var normalizedDuration = durationSeconds <= 0f
            ? 0f
            : Math.Clamp(durationSeconds, 1f, MaxDurationSeconds);
        var normalizedCategory = category == WH40KNotificationCategory.Auto
            ? WH40KNotificationCategory.Info
            : category;
        var normalizedPriority = priority == WH40KNotificationPriority.Auto
            ? WH40KNotificationMetadata.DefaultPriority(normalizedCategory)
            : priority;
        var normalizedIcon = icon == WH40KNotificationIcon.Auto
            ? WH40KNotificationMetadata.DefaultIcon(normalizedCategory, accentColor)
            : icon;

        return new WH40KNotificationEvent(
            normalizedTitle,
            normalizedText,
            accentColor,
            normalizedDuration,
            marquee,
            size,
            normalizedCategory,
            normalizedPriority,
            normalizedIcon,
            stackKey,
            ignoreUserPreferences,
            sound);
    }

    private static WH40KLocalizedNotificationEvent BuildLocalizedEvent(
        string title,
        string locKey,
        Dictionary<string, string>? locArgs,
        bool resolveArgValues,
        Color accentColor,
        float durationSeconds,
        bool marquee,
        WH40KNotificationSize size,
        WH40KNotificationCategory category,
        WH40KNotificationPriority priority,
        WH40KNotificationIcon icon,
        string stackKey,
        bool ignoreUserPreferences,
        SoundSpecifier? sound)
    {
        var normalizedDuration = durationSeconds <= 0f
            ? 0f
            : Math.Clamp(durationSeconds, 1f, MaxDurationSeconds);

        return new WH40KLocalizedNotificationEvent
        {
            Title = ClampText(title, MaxTitleLength),
            LocKey = ClampText(locKey, MaxTextLength),
            LocArgs = locArgs,
            ResolveArgValues = resolveArgValues,
            AccentColor = accentColor,
            DurationSeconds = normalizedDuration,
            Marquee = marquee,
            Size = size,
            Category = category,
            Priority = priority,
            Icon = icon,
            StackKey = stackKey.Trim(),
            IgnoreUserPreferences = ignoreUserPreferences,
            Sound = sound
        };
    }

    private bool IsThrottled(WH40KNotificationEvent ev, float cooldownSeconds, string? scope = null)
    {
        if (ev.IgnoreUserPreferences ||
            ev.Category == WH40KNotificationCategory.Admin ||
            cooldownSeconds <= 0f ||
            string.IsNullOrWhiteSpace(ev.StackKey))
        {
            return false;
        }

        var key = string.IsNullOrWhiteSpace(scope)
            ? ev.StackKey
            : $"{scope}:{ev.StackKey}";
        var now = _timing.CurTime;
        var cooldown = TimeSpan.FromSeconds(Math.Max(0f, cooldownSeconds));

        if (_lastSentByStackKey.TryGetValue(key, out var last) && now - last < cooldown)
            return true;

        _lastSentByStackKey[key] = now;
        return false;
    }

    private bool IsThrottled(WH40KLocalizedNotificationEvent ev, float cooldownSeconds, string? scope = null)
    {
        if (ev.IgnoreUserPreferences ||
            ev.Category == WH40KNotificationCategory.Admin ||
            cooldownSeconds <= 0f ||
            string.IsNullOrWhiteSpace(ev.StackKey))
        {
            return false;
        }

        var key = string.IsNullOrWhiteSpace(scope)
            ? ev.StackKey
            : $"{scope}:{ev.StackKey}";
        var now = _timing.CurTime;
        var cooldown = TimeSpan.FromSeconds(Math.Max(0f, cooldownSeconds));

        if (_lastSentByStackKey.TryGetValue(key, out var last) && now - last < cooldown)
            return true;

        _lastSentByStackKey[key] = now;
        return false;
    }

    private static string ClampText(string value, int maxLength)
    {
        value = value.Trim();
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
