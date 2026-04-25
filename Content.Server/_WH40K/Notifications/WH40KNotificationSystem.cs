using System;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared._WH40K.Notifications;
using Robust.Server.Player;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Notifications;

public sealed class WH40KNotificationSystem : EntitySystem
{
    private const float MaxDurationSeconds = 120f;
    private const int MaxTitleLength = 96;
    private const int MaxTextLength = 512;

    [Dependency] private readonly IPlayerManager _players = default!;

    public int SendGlobal(
        string title,
        string text,
        Color accentColor,
        float durationSeconds = 8f,
        bool marquee = true,
        WH40KNotificationSize size = WH40KNotificationSize.Standard)
    {
        var ev = BuildEvent(title, text, accentColor, durationSeconds, marquee, size);
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
        WH40KNotificationSize size = WH40KNotificationSize.Standard)
    {
        var ev = BuildEvent(title, text, accentColor, durationSeconds, marquee, size);
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

    public void SendToSession(
        ICommonSession session,
        string title,
        string text,
        Color accentColor,
        float durationSeconds = 8f,
        bool marquee = true,
        WH40KNotificationSize size = WH40KNotificationSize.Standard)
    {
        RaiseNetworkEvent(BuildEvent(title, text, accentColor, durationSeconds, marquee, size), session);
    }

    private static WH40KNotificationEvent BuildEvent(
        string title,
        string text,
        Color accentColor,
        float durationSeconds,
        bool marquee,
        WH40KNotificationSize size)
    {
        var normalizedTitle = ClampText(title, MaxTitleLength);
        var normalizedText = ClampText(text, MaxTextLength);
        var normalizedDuration = durationSeconds <= 0f
            ? 0f
            : Math.Clamp(durationSeconds, 1f, MaxDurationSeconds);

        return new WH40KNotificationEvent(
            normalizedTitle,
            normalizedText,
            accentColor,
            normalizedDuration,
            marquee,
            size);
    }

    private static string ClampText(string value, int maxLength)
    {
        value = value.Trim();
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
