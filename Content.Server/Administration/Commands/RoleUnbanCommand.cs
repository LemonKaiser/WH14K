using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Ban)]
public sealed partial class RoleUnbanCommand : LocalizedCommands
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private IBanManager _banManager = default!;
    [Dependency] private IServerDbManager _dbManager = default!;

    public override string Command => "roleunban";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!int.TryParse(args[0], out var banId))
        {
            shell.WriteLine(Loc.GetString($"cmd-roleunban-unable-to-parse-id", ("id", args[0]), ("help", Help)));
            return;
        }

        var ban = await _dbManager.GetBanAsync(banId);
        if (ban != null
            && await _adminActionGuard.TryDenyProtectedBanAsync(
                shell.Player,
                ban,
                Loc.GetString("admin-hierarchy-action-role-unban"),
                shell.WriteLine))
        {
            return;
        }

        var response = await _banManager.PardonRoleBan(banId, shell.Player?.UserId, DateTimeOffset.Now);
        shell.WriteLine(response);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        // Can't think of good way to do hint options for this
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-roleunban-hint-1")),
            _ => CompletionResult.Empty
        };
    }
}
