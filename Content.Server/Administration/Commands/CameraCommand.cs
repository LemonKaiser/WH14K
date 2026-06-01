using Content.Server.Administration.UI;
using Content.Server.EUI;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class CameraCommand : LocalizedCommands
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override string Command => "camera";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } user)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var targetNetId) || !_entManager.TryGetEntity(targetNetId, out var targetUid))
        {
            if (!_playerManager.TryGetSessionByUsername(args[0], out var player)
                || player.AttachedEntity == null)
            {
                shell.WriteError(Loc.GetString("cmd-camera-wrong-argument"));
                return;
            }

            if (await _adminActionGuard.TryDenyProtectedTargetAsync(
                    user,
                    player.UserId,
                    Loc.GetString("admin-hierarchy-action-camera"),
                    player.Name,
                    shell.WriteError))
            {
                return;
            }

            targetUid = player.AttachedEntity.Value;
        }
        else if (await _adminActionGuard.TryDenyProtectedEntityTargetAsync(
                     user,
                     targetUid.Value,
                     Loc.GetString("admin-hierarchy-action-camera"),
                     notify: shell.WriteError))
        {
            return;
        }

        var ui = new AdminCameraEui(targetUid.Value);
        _eui.OpenEui(ui, user);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(players: _playerManager),
                Loc.GetString("cmd-camera-hint"));
        }

        return CompletionResult.Empty;
    }
}
