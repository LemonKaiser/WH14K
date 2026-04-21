using Content.Server.Administration.UI;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class CameraCommand : LocalizedCommands
{
    [Dependency] private readonly IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

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

        ICommonSession? targetSession = null;
        if (!NetEntity.TryParse(args[0], out var targetNetId) || !_entManager.TryGetEntity(targetNetId, out var targetUid))
        {
            if (!_playerManager.TryGetSessionByUsername(args[0], out var player)
                || player.AttachedEntity == null)
            {
                shell.WriteError(Loc.GetString("cmd-camera-wrong-argument"));
                return;
            }

            targetSession = player;
            targetUid = player.AttachedEntity.Value;
        }
        else if (_playerManager.TryGetSessionByEntity(targetUid.Value, out var session))
        {
            targetSession = session;
        }

        if (targetSession != null
            && await _adminActionGuard.TryDenyProtectedTargetAsync(
                user,
                targetSession.UserId,
                Loc.GetString("admin-hierarchy-action-camera"),
                targetSession.Name,
                shell.WriteError))
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
