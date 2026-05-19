using System;
using Content.Server.Administration;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.WaveDefence;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Localization;

namespace Content.Server._WH40K.GameTicking.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KWaveDefenceAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "wh40kwave";
    public string Description => "Admin control for WH40K WaveDefence.";
    public string Help =>
        "Usage:\n" +
        "wh40kwave status\n" +
        "wh40kwave ai\n" +
        "wh40kwave debug [on|off|status]\n" +
        "wh40kwave next\n" +
        "wh40kwave victory\n" +
        "wh40kwave defeat";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var system = _entityManager.EntitySysManager.GetEntitySystem<WH40KWaveDefenceRuleSystem>();
        var aiSystem = _entityManager.EntitySysManager.GetEntitySystem<WH40KWaveDefenceAISystem>();
        var debugOverlay = _entityManager.EntitySysManager.GetEntitySystem<WH40KWaveDefenceAiDebugOverlaySystem>();

        if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            shell.WriteLine(system.BuildStatusText());
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "ai":
                shell.WriteLine(aiSystem.BuildAiStatusText());
                break;

            case "debug":
                HandleDebug(shell, debugOverlay, args);
                break;

            case "next":
                shell.WriteLine(system.ForceNextWave() ? "Wave state advanced." : "Unable to advance wave state.");
                break;

            case "victory":
                shell.WriteLine(system.ForceVictory() ? "Forced victory." : "Unable to force victory.");
                break;

            case "defeat":
                shell.WriteLine(system.ForceDefeat(Loc.GetString("wh40k-wave-defence-defeat-admin")) ? "Forced defeat." : "Unable to force defeat.");
                break;

            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(new[] { "status", "ai", "debug", "next", "victory", "defeat" }, "<action>");

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
            shell.WriteLine("You must be an in-game player to use the WaveDefence AI debug overlay.");
            return;
        }

        if (args.Length >= 2)
        {
            switch (args[1].ToLowerInvariant())
            {
                case "on":
                    debugOverlay.AddObserver(player);
                    shell.WriteLine("Enabled WaveDefence AI debug overlay.");
                    return;

                case "off":
                    debugOverlay.RemoveObserver(player);
                    shell.WriteLine("Disabled WaveDefence AI debug overlay.");
                    return;

                case "status":
                    shell.WriteLine(debugOverlay.HasObserver(player)
                        ? "WaveDefence AI debug overlay is enabled."
                        : "WaveDefence AI debug overlay is disabled.");
                    return;
            }
        }

        var enabled = debugOverlay.ToggleObserver(player);
        shell.WriteLine(enabled
            ? "Enabled WaveDefence AI debug overlay."
            : "Disabled WaveDefence AI debug overlay.");
    }
}
