using Content.Server.Administration.Managers;
using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class AddPolymorphActionCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private PolymorphSystem _polySystem = default!;

    public override string Command => "addpolymorphaction";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var entityUidNet) || !EntityManager.TryGetEntity(entityUidNet, out var entityUid))
        {
            shell.WriteError(Loc.GetString("shell-could-not-find-entity-with-uid", ("uid", args[0])));
            return;
        }

        if (await _adminActionGuard.TryDenyProtectedEntityTargetAsync(
                shell.Player,
                entityUid.Value,
                Loc.GetString("admin-hierarchy-action-add-polymorph-action"),
                notify: shell.WriteLine))
        {
            return;
        }

        var polymorphable = EntityManager.EnsureComponent<PolymorphableComponent>(entityUid.Value);
        _polySystem.CreatePolymorphAction(args[1], (entityUid.Value, polymorphable));
    }
}
