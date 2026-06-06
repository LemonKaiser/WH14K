using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server._WH40K.Administration.Mute;
using Content.Shared.Administration;
using Content.Shared._WH40K.Administration.Mute;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class WH40KUnmuteCommand : LocalizedCommands
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override string Command => "unmute";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Help);
            return;
        }

        var target = args[0];
        var type = WH40KMuteType.Chat | WH40KMuteType.AHelp;
        if (args.Length == 2 && !WH40KMuteCommand.TryParseMuteType(args[1], out type))
        {
            shell.WriteError(Loc.GetString("wh40k-mute-command-invalid-type", ("type", args[1])));
            shell.WriteError(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(target);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("cmd-ban-player"));
            return;
        }

        if (await _adminActionGuard.TryDenyProtectedTargetAsync(
                shell.Player,
                located.UserId,
                Loc.GetString("wh40k-admin-hierarchy-action-unmute"),
                located.Username,
                shell.WriteLine))
        {
            return;
        }

        var muteSystem = _entities.EntitySysManager.GetEntitySystem<WH40KMuteSystem>();
        if (!await muteSystem.CanRemoveMuteAsync(located.UserId, type, shell.Player, shell.WriteError))
            return;

        var removed = await muteSystem.RemoveMuteAsync(located.UserId, type, shell.Player);
        if (removed == 0)
        {
            shell.WriteLine(Loc.GetString("wh40k-unmute-command-none-active", ("player", located.Username)));
            return;
        }

        shell.WriteLine(Loc.GetString("wh40k-unmute-command-success", ("player", located.Username), ("count", removed)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = _playerManager.Sessions.Select(c => c.Name).OrderBy(c => c).ToArray();
            return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-ban-hint"));
        }

        if (args.Length == 2)
        {
            return CompletionResult.FromHintOptions(
                [
                    new CompletionOption("all", Loc.GetString("wh40k-mute-scope-all")),
                    new CompletionOption("chat", Loc.GetString("wh40k-mute-scope-chat")),
                    new CompletionOption("ahelp", Loc.GetString("wh40k-mute-scope-ahelp")),
                ],
                Loc.GetString("wh40k-mute-command-hint-scope"));
        }

        return CompletionResult.Empty;
    }
}
