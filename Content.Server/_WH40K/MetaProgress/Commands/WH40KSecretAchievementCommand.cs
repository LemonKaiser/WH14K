using System.Threading.Tasks;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.MetaProgress.Commands;

[AnyCommand]
public sealed class WH40KSecretAchievementCommand : LocalizedCommands
{
    private const string SecretAchievementId = "wh40k-ach-whispers-in-void";

    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override string Command => "kaiser";

    public override string Description => "Unlocks a hidden WH40K meta achievement.";

    public override string Help => "Usage: kaiser";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player == null)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length > 0)
        {
            shell.WriteLine(Help);
            return;
        }

        var userId = shell.Player.UserId;
        var metaProgress = _entityManager.EntitySysManager.GetEntitySystem<WH40KMetaProgressSystem>();
        await metaProgress.EnsureStateLoadedForUserAsync(userId);

        var snapshot = metaProgress.GetSnapshot(userId);
        var alreadyCompleted = snapshot.Achievements.Exists(entry =>
            entry.Id == SecretAchievementId &&
            entry.Completed);

        if (alreadyCompleted)
        {
            shell.WriteLine(Loc.GetString("wh40k-meta-progress-secret-kaiser-already"));
            return;
        }

        if (!metaProgress.TrySetAchievementUnlocked(userId, SecretAchievementId, true,
                out _, out _, out _, out var error))
        {
            shell.WriteError(Loc.GetString("wh40k-meta-progress-secret-kaiser-error", ("error", error)));
            return;
        }

        shell.WriteLine(Loc.GetString("wh40k-meta-progress-secret-kaiser-unlocked"));
    }
}
