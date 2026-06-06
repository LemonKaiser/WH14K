using Content.Server.Administration;
using Content.Server.EUI;
using Content.Server._WH40K.Administration.Mute;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class WH40KMutePanelCommand : LocalizedCommands
{
    [Dependency] private EuiManager _euis = default!;
    [Dependency] private IPlayerLocator _locator = default!;

    public override string Command => "mutepanel";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        switch (args.Length)
        {
            case 0:
                _euis.OpenEui(new WH40KMutePanelEui(), player);
                break;
            case 1:
                var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
                if (located == null)
                {
                    shell.WriteError(Loc.GetString("cmd-banpanel-player-err"));
                    return;
                }

                var ui = new WH40KMutePanelEui();
                _euis.OpenEui(ui, player);
                ui.ChangePlayer(located.UserId, located.Username);
                break;
            default:
                shell.WriteError(Help);
                break;
        }
    }
}
