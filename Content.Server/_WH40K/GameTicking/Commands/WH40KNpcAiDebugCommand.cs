using Content.Server.Administration;
using Content.Server._WH40K.WaveDefence;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WH40K.GameTicking.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class WH40KNpcAiDebugCommand : IConsoleCommand
{
    [Dependency] private  IEntityManager _entityManager = default!;

    public string Command => "wh40kai";
    public string Description => "General AI debug for HTN NPCs.";
    public string Help =>
        "Usage:\n" +
        "wh40kai status\n" +
        "wh40kai debug [on|off|status]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var debugOverlay = _entityManager.EntitySysManager.GetEntitySystem<WH40KWaveDefenceAiDebugOverlaySystem>();

        if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            shell.WriteLine(debugOverlay.BuildStatusText());
            return;
        }

        if (args[0].Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            HandleDebug(shell, debugOverlay, args);
            return;
        }

        shell.WriteLine(Help);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(new[] { "status", "debug" }, "<action>");

        if (args.Length == 2 && args[0].Equals("debug", StringComparison.OrdinalIgnoreCase))
            return CompletionResult.FromHintOptions(new[] { "on", "off", "status" }, "<mode>");

        return CompletionResult.Empty;
    }

    private static void HandleDebug(
        IConsoleShell shell,
        WH40KWaveDefenceAiDebugOverlaySystem debugOverlay,
        string[] args)
    {
        var player = shell.Player;
        if (player == null)
        {
            shell.WriteLine("You must be an in-game player to use the NPC AI debug overlay.");
            return;
        }

        if (args.Length >= 2)
        {
            switch (args[1].ToLowerInvariant())
            {
                case "on":
                    debugOverlay.AddObserver(player);
                    shell.WriteLine("Enabled NPC AI debug overlay.");
                    return;

                case "off":
                    debugOverlay.RemoveObserver(player);
                    shell.WriteLine("Disabled NPC AI debug overlay.");
                    return;

                case "status":
                    shell.WriteLine(debugOverlay.HasObserver(player)
                        ? "NPC AI debug overlay is enabled."
                        : "NPC AI debug overlay is disabled.");
                    return;
            }
        }

        var enabled = debugOverlay.ToggleObserver(player);
        shell.WriteLine(enabled
            ? "Enabled NPC AI debug overlay."
            : "Disabled NPC AI debug overlay.");
    }
}
