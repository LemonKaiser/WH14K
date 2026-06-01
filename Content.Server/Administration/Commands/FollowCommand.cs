using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Follower;
using Robust.Shared.Console;
using Robust.Shared.Enums;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class FollowCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private FollowerSystem _followerSystem = default!;

    public override string Command => "follow";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-need-exactly-one-argument"));
            return;
        }

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { Valid: true } playerEntity)
        {
            shell.WriteError(Loc.GetString("shell-must-be-attached-to-entity"));
            return;
        }

        if (NetEntity.TryParse(args[0], out var uidNet) && EntityManager.TryGetEntity(uidNet, out var uid))
        {
            if (await _adminActionGuard.TryDenyProtectedEntityTargetAsync(
                    player,
                    uid.Value,
                    Loc.GetString("admin-hierarchy-action-follow"),
                    notify: shell.WriteError))
            {
                return;
            }

            _followerSystem.StartFollowingEntity(playerEntity, uid.Value);
        }
    }
}
