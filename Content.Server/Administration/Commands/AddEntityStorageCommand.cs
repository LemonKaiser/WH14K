using Content.Shared.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Admin)]
    public sealed class AddEntityStorageCommand : IConsoleCommand
    {
        [Dependency] private readonly IAdminActionGuard _adminActionGuard = default!;
        [Dependency] private readonly IEntityManager _entManager = default!;

        public string Command => "addstorage";
        public string Description => "Adds a given entity to a containing storage.";
        public string Help => "Usage: addstorage <entity uid> <storage uid>";

        public async void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 2)
            {
                shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
                return;
            }

            if (!NetEntity.TryParse(args[0], out var entityUidNet) || !_entManager.TryGetEntity(entityUidNet, out var entityUid))
            {
                shell.WriteError(Loc.GetString("shell-entity-uid-must-be-number"));
                return;
            }

            if (!NetEntity.TryParse(args[1], out var storageUidNet) || !_entManager.TryGetEntity(storageUidNet, out var storageUid))
            {
                shell.WriteError(Loc.GetString("shell-entity-uid-must-be-number"));
                return;
            }

            if (await _adminActionGuard.TryDenyProtectedEntityTargetAsync(
                    shell.Player,
                    entityUid.Value,
                    Loc.GetString("admin-hierarchy-action-add-storage"),
                    notify: shell.WriteLine)
                || await _adminActionGuard.TryDenyProtectedEntityTargetAsync(
                    shell.Player,
                    storageUid.Value,
                    Loc.GetString("admin-hierarchy-action-add-storage"),
                    notify: shell.WriteLine))
            {
                return;
            }

            if (_entManager.HasComponent<EntityStorageComponent>(storageUid) &&
                _entManager.EntitySysManager.TryGetEntitySystem<EntityStorageSystem>(out var storageSys))
            {
                storageSys.Insert(entityUid.Value, storageUid.Value);
            }
            else
            {
                shell.WriteError("Could not insert into non-storage.");
            }
        }
    }
}
