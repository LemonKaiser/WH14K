using System;
using System.Collections.Generic;
using Content.Server.Administration;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared._WH40K.Notifications;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Maths;

namespace Content.Server._WH40K.Notifications.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KNotificationAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "wh40knotify";
    public string Description => "Show a WH40K top-center HUD notification to everyone or one team.";
    public string Help =>
        "Usage:\n" +
        "wh40knotify <all|teamId> <title> <text> [#RRGGBB] [durationSeconds] [compact|standard|wide] [marquee]\n" +
        "Examples:\n" +
        "wh40knotify all \"Vox report\" \"Orbital storm is approaching\" #D64A4A 9 wide true\n" +
        "wh40knotify Imperium \"Order\" \"Hold the line\" #F3C548 0 standard true";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError(Help);
            return;
        }

        var target = args[0];
        var title = args[1];
        var text = args[2];
        var color = Color.FromHex("#D6D6D6");
        var duration = 8f;
        var size = WH40KNotificationSize.Standard;
        var marquee = true;

        if (args.Length >= 4)
        {
            var parsed = Color.TryFromHex(args[3]);
            if (parsed == null)
            {
                shell.WriteError("Color must be a hex value, for example #D64A4A.");
                return;
            }

            color = parsed.Value;
        }

        if (args.Length >= 5 && !float.TryParse(args[4], out duration))
        {
            shell.WriteError("durationSeconds must be a number. Use 0 to keep the notification until clicked.");
            return;
        }

        if (args.Length >= 6 && !TryParseSize(args[5], out size))
        {
            shell.WriteError("Size must be one of: compact, standard, wide.");
            return;
        }

        if (args.Length >= 7 && !bool.TryParse(args[6], out marquee))
        {
            shell.WriteError("marquee must be true or false.");
            return;
        }

        var notifications = _entityManager.EntitySysManager.GetEntitySystem<WH40KNotificationSystem>();
        int delivered;

        if (string.Equals(target, "all", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, "*", StringComparison.Ordinal))
        {
            delivered = notifications.SendGlobal(title, text, color, duration, marquee, size);
            shell.WriteLine($"WH40K notification sent to all sessions ({delivered}).");
            return;
        }

        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        if (!TryResolveCanonicalTeamId(rule, target, out var teamId))
        {
            var ids = rule.GetTeamIds();
            shell.WriteError(ids.Count == 0
                ? "Active WH40K team-battle rule not found. Use target 'all' or start a WH40K team round."
                : $"Unknown team id '{target}'. Available: {string.Join(", ", ids)}");
            return;
        }

        delivered = notifications.SendTeam(teamId, title, text, color, duration, marquee, size);
        shell.WriteLine($"WH40K notification sent to team '{teamId}' ({delivered}).");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = new List<string> { "all" };
            var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
            options.AddRange(rule.GetTeamIds());
            return CompletionResult.FromHintOptions(options, "<all|teamId>");
        }

        return args.Length switch
        {
            2 => CompletionResult.FromHint("<title>"),
            3 => CompletionResult.FromHint("<text>"),
            4 => CompletionResult.FromHint("[#RRGGBB]"),
            5 => CompletionResult.FromHint("[durationSeconds]"),
            6 => CompletionResult.FromHintOptions(new[] { "compact", "standard", "wide" }, "[size]"),
            7 => CompletionResult.FromHintOptions(new[] { "true", "false" }, "[marquee]"),
            _ => CompletionResult.Empty,
        };
    }

    private static bool TryParseSize(string value, out WH40KNotificationSize size)
    {
        switch (value.ToLowerInvariant())
        {
            case "compact":
            case "small":
                size = WH40KNotificationSize.Compact;
                return true;

            case "standard":
            case "normal":
            case "medium":
                size = WH40KNotificationSize.Standard;
                return true;

            case "wide":
            case "large":
                size = WH40KNotificationSize.Wide;
                return true;

            default:
                size = WH40KNotificationSize.Standard;
                return false;
        }
    }

    private static bool TryResolveCanonicalTeamId(
        WH40KTeamBattleRuleSystem rule,
        string inputTeamId,
        out string resolvedTeamId)
    {
        resolvedTeamId = string.Empty;
        foreach (var teamId in rule.GetTeamIds())
        {
            if (!string.Equals(teamId, inputTeamId, StringComparison.OrdinalIgnoreCase))
                continue;

            resolvedTeamId = teamId;
            return true;
        }

        return false;
    }
}
