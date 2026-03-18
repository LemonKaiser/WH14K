using System.Linq;
using Content.Server.Administration;
using Content.Server._WH40K.Administration.ScreenCheck;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Moderator)]
internal sealed class WH40KScreenCheckCommand : LocalizedCommands
{
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly ScreenCheckManager _screenChecks = default!;

    public override string Command => "screencheck";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } admin)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-need-exactly-one-argument"));
            shell.WriteLine(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("screen-check-player-not-found", ("player", args[0])));
            return;
        }

        if (!_players.TryGetSessionById(located.UserId, out var target))
        {
            shell.WriteError(Loc.GetString("screen-check-player-offline", ("player", located.Username)));
            return;
        }

        var result = _screenChecks.StartScreenCheck(admin, target);
        switch (result)
        {
            case ScreenCheckStartResult.Success:
                shell.WriteLine(Loc.GetString("screen-check-request-sent", ("player", target.Name)));
                break;

            case ScreenCheckStartResult.AdminAlreadyHasPending:
                shell.WriteError(Loc.GetString("screen-check-request-active-admin"));
                break;

            case ScreenCheckStartResult.TargetAlreadyHasPending:
                shell.WriteError(Loc.GetString("screen-check-request-active-target", ("player", target.Name)));
                break;

            case ScreenCheckStartResult.TooManyPending:
                shell.WriteError(Loc.GetString("screen-check-request-limit-reached"));
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = _players.Sessions.OrderBy(player => player.Name).Select(player => player.Name).ToArray();
            return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-screencheck-hint"));
        }

        return CompletionResult.Empty;
    }
}
