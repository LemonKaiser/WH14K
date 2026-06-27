using Content.Server.Administration;
using System.Globalization;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Shuttles.Commands;

/// <summary>
/// Delays the round from ending via the shuttle call. Can still be ended via other means.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class DelayRoundEndCommand : LocalizedEntityCommands
{
    [Dependency] private RoundEndSystem _roundEndSystem = default!;
    [Dependency] private EmergencyShuttleSystem _shuttleSystem = default!;

    public override string Command => "delayroundend";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var delay = TimeSpan.FromSeconds(30);

        if (args.Length > 1)
        {
            shell.WriteLine("Usage: delayroundend [seconds]");
            return;
        }

        if (args.Length == 1)
        {
            if (!double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                seconds <= 0)
            {
                shell.WriteLine("Usage: delayroundend [seconds]");
                return;
            }

            delay = TimeSpan.FromSeconds(seconds);
        }

        if (_roundEndSystem.DelayRoundEnd(delay) || _shuttleSystem.DelayEmergencyRoundEnd())
            shell.WriteLine(Loc.GetString("emergency-shuttle-command-round-yes"));
        else
            shell.WriteLine(Loc.GetString("emergency-shuttle-command-round-no"));
    }
}
