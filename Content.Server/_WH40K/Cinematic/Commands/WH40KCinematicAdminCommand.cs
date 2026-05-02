using System;
using System.Linq;
using Content.Server.Administration;
using Content.Shared._WH40K.Cinematic;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Cinematic.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KCinematicAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public string Command => "wh40kcinematic";
    public string Description => "Admin control for WH40K cinematic runtime and queue orchestration.";
    public string Help =>
        "Usage:\n" +
        "wh40kcinematic list [cinematics|camera|anchors|action-anchors|spawn-anchors|sound-anchors|npc-anchors|flows]\n" +
        "wh40kcinematic describe <prototypeId>\n" +
        "wh40kcinematic start <prototypeId>\n" +
        "wh40kcinematic stop [runId]\n" +
        "wh40kcinematic status\n" +
        "wh40kcinematic runs\n" +
        "wh40kcinematic pause <runId>\n" +
        "wh40kcinematic resume <runId>\n" +
        "wh40kcinematic advance-step <runId>\n" +
        "wh40kcinematic jump-to-step <runId> <stepId>\n" +
        "wh40kcinematic emit-signal <runId> <signal>\n" +
        "wh40kcinematic validate <prototypeId>\n" +
        "wh40kcinematic validate-loaded <prototypeId>\n" +
        "wh40kcinematic preview-shot <prototypeId> <stepId> [lifetimeSeconds]\n" +
        "wh40kcinematic preview-anchor <anchorId> [any|action|spawn|sound] [lifetimeSeconds]\n" +
        "wh40kcinematic validate-flow <flowId>\n" +
        "wh40kcinematic preview-flow <flowId> [lifetimeSeconds]\n" +
        "wh40kcinematic preview-cinematic <prototypeId> [lifetimeSeconds]\n" +
        "wh40kcinematic npc-record-start <runId> <npcId> <trackId> <segmentId>\n" +
        "wh40kcinematic npc-record-pause\n" +
        "wh40kcinematic npc-record-resume\n" +
        "wh40kcinematic npc-record-stop\n" +
        "wh40kcinematic npc-record-export [relativePath]\n" +
        "wh40kcinematic clear-queue";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        var system = _entities.EntitySysManager.GetEntitySystem<WH40KCinematicSystem>();

        switch (args[0].ToLowerInvariant())
        {
            case "list":
                ExecuteList(shell, system, args, _prototypes);
                return;

            case "describe":
                ExecuteDescribe(shell, system, args);
                return;

            case "start":
                ExecuteStart(shell, system, args);
                return;

            case "stop":
                ExecuteStop(shell, system, args);
                return;

            case "status":
                ExecuteStatus(shell, system);
                return;

            case "runs":
            case "list-active-runs":
                ExecuteRuns(shell, system);
                return;

            case "pause":
                ExecutePause(shell, system, args);
                return;

            case "resume":
                ExecuteResume(shell, system, args);
                return;

            case "advance":
            case "advance-step":
                ExecuteAdvance(shell, system, args);
                return;

            case "jump":
            case "jump-to-step":
                ExecuteJump(shell, system, args);
                return;

            case "signal":
            case "emit-signal":
                ExecuteSignal(shell, system, args);
                return;

            case "validate":
                ExecuteValidate(shell, system, args);
                return;

            case "validate-loaded":
                ExecuteValidateLoaded(shell, system, args);
                return;

            case "preview-shot":
                ExecutePreviewShot(shell, system, args);
                return;

            case "preview-anchor":
                ExecutePreviewAnchor(shell, system, args);
                return;

            case "validate-flow":
                ExecuteValidateFlow(shell, system, args);
                return;

            case "preview-flow":
                ExecutePreviewFlow(shell, system, args);
                return;

            case "preview-cinematic":
                ExecutePreviewCinematic(shell, system, args);
                return;

            case "npc-record-start":
                ExecuteNpcRecordStart(shell, system, args);
                return;

            case "npc-record-pause":
                ExecuteNpcRecordPause(shell, system);
                return;

            case "npc-record-resume":
                ExecuteNpcRecordResume(shell, system);
                return;

            case "npc-record-stop":
                ExecuteNpcRecordStop(shell, system);
                return;

            case "npc-record-export":
                ExecuteNpcRecordExport(shell, system, args);
                return;

            case "clear":
            case "clear-queue":
                system.ClearQueue();
                shell.WriteLine("Cinematic queue cleared.");
                return;

            default:
                shell.WriteError($"Unknown subcommand '{args[0]}'.");
                shell.WriteLine(Help);
                return;
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        var actions = new[]
        {
            "list",
            "describe",
            "start",
            "stop",
            "status",
            "runs",
            "pause",
            "resume",
            "advance-step",
            "jump-to-step",
            "emit-signal",
            "validate",
            "validate-loaded",
            "preview-shot",
            "preview-anchor",
            "validate-flow",
            "preview-flow",
            "preview-cinematic",
            "npc-record-start",
            "npc-record-pause",
            "npc-record-resume",
            "npc-record-stop",
            "npc-record-export",
            "clear-queue"
        };

        if (args.Length == 1)
            return CompletionResult.FromHintOptions(actions, "<action>");

        if (args.Length == 2 && args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return CompletionResult.FromHintOptions(
                new[] { "cinematics", "camera", "anchors", "action-anchors", "spawn-anchors", "sound-anchors", "npc-anchors", "flows" },
                "<category>");
        }

        if (args.Length == 2 && (args[0].Equals("describe", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("start", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("validate", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("validate-loaded", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("preview-cinematic", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("preview-shot", StringComparison.OrdinalIgnoreCase)))
        {
            var ids = _prototypes.EnumeratePrototypes<WH40KCinematicPrototype>()
                .Select(proto => proto.ID)
                .OrderBy(id => id)
                .ToArray();

            return CompletionResult.FromHintOptions(ids, "<prototypeId>");
        }

        if (args.Length == 2 && (args[0].Equals("stop", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("pause", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("resume", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("advance", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("advance-step", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("jump", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("jump-to-step", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("signal", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("emit-signal", StringComparison.OrdinalIgnoreCase)))
        {
            var system = _entities.EntitySysManager.GetEntitySystem<WH40KCinematicSystem>();
            return CompletionResult.FromHintOptions(system.GetActiveRunIds(), "<runId>");
        }

        if (args.Length == 3 && (args[0].Equals("jump", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("jump-to-step", StringComparison.OrdinalIgnoreCase)) &&
            int.TryParse(args[1], out var runSerial))
        {
            var system = _entities.EntitySysManager.GetEntitySystem<WH40KCinematicSystem>();
            return CompletionResult.FromHintOptions(system.GetActiveRunStepIds(runSerial), "<stepId>");
        }

        if (args.Length == 3 && args[0].Equals("preview-shot", StringComparison.OrdinalIgnoreCase))
        {
            var system = _entities.EntitySysManager.GetEntitySystem<WH40KCinematicSystem>();
            var ids = system.GetKnownShotStepIds(args[1]).ToArray();
            return CompletionResult.FromHintOptions(ids, "<stepId>");
        }

        if (args.Length == 2 && args[0].Equals("preview-anchor", StringComparison.OrdinalIgnoreCase))
        {
            var system = _entities.EntitySysManager.GetEntitySystem<WH40KCinematicSystem>();
            var anchorIds = system.GetKnownAnchorIds().ToArray();
            return CompletionResult.FromHintOptions(anchorIds, "<anchorId>");
        }

        if (args.Length == 3 && args[0].Equals("preview-anchor", StringComparison.OrdinalIgnoreCase))
        {
            return CompletionResult.FromHintOptions(
                new[] { "any", "action", "spawn", "sound", "npc" },
                "<mode>");
        }

        if (args.Length == 2 && args[0].Equals("npc-record-start", StringComparison.OrdinalIgnoreCase))
        {
            var system = _entities.EntitySysManager.GetEntitySystem<WH40KCinematicSystem>();
            return CompletionResult.FromHintOptions(system.GetActiveRunIds(), "<runId>");
        }

        if (args.Length == 2 && (args[0].Equals("validate-flow", StringComparison.OrdinalIgnoreCase) ||
                                 args[0].Equals("preview-flow", StringComparison.OrdinalIgnoreCase)))
        {
            var system = _entities.EntitySysManager.GetEntitySystem<WH40KCinematicSystem>();
            var flowIds = system.GetKnownLavaFlowIds().ToArray();
            return CompletionResult.FromHintOptions(flowIds, "<flowId>");
        }

        return CompletionResult.Empty;
    }

    private static void ExecuteList(IConsoleShell shell, WH40KCinematicSystem system, string[] args, IPrototypeManager prototypes)
    {
        var category = args.Length >= 2 ? args[1].ToLowerInvariant() : "cinematics";

        switch (category)
        {
            case "cinematics":
            {
                var ids = prototypes.EnumeratePrototypes<WH40KCinematicPrototype>()
                    .Select(proto => proto.ID)
                    .OrderBy(id => id)
                    .ToArray();

                shell.WriteLine(ids.Length == 0
                    ? "No WH40K cinematic prototypes are loaded."
                    : $"Cinematics ({ids.Length}): {string.Join(", ", ids)}");
                return;
            }

            case "camera":
            {
                var ids = system.GetKnownCameraPointIds();
                shell.WriteLine(ids.Count == 0
                    ? "No cinematic camera points are currently loaded."
                    : $"Camera points ({ids.Count}): {string.Join(", ", ids)}");
                return;
            }

            case "anchors":
            {
                var ids = system.GetKnownAnchorIds();
                shell.WriteLine(ids.Count == 0
                    ? "No cinematic anchors are currently loaded."
                    : $"Anchors ({ids.Count}): {string.Join(", ", ids)}");
                return;
            }

            case "action-anchors":
            {
                var ids = system.GetKnownAnchorIds(WH40KCinematicPreviewAnchorMode.Action);
                shell.WriteLine(ids.Count == 0
                    ? "No cinematic action anchors are currently loaded."
                    : $"Action anchors ({ids.Count}): {string.Join(", ", ids)}");
                return;
            }

            case "spawn-anchors":
            {
                var ids = system.GetKnownAnchorIds(WH40KCinematicPreviewAnchorMode.Spawn);
                shell.WriteLine(ids.Count == 0
                    ? "No cinematic spawn anchors are currently loaded."
                    : $"Spawn anchors ({ids.Count}): {string.Join(", ", ids)}");
                return;
            }

            case "sound-anchors":
            {
                var ids = system.GetKnownAnchorIds(WH40KCinematicPreviewAnchorMode.Sound);
                shell.WriteLine(ids.Count == 0
                    ? "No cinematic sound anchors are currently loaded."
                    : $"Sound anchors ({ids.Count}): {string.Join(", ", ids)}");
                return;
            }

            case "npc-anchors":
            {
                var ids = system.GetKnownAnchorIds(WH40KCinematicPreviewAnchorMode.Npc);
                shell.WriteLine(ids.Count == 0
                    ? "No cinematic NPC anchors are currently loaded."
                    : $"NPC anchors ({ids.Count}): {string.Join(", ", ids)}");
                return;
            }

            case "flows":
            {
                var ids = system.GetKnownLavaFlowIds();
                shell.WriteLine(ids.Count == 0
                    ? "No cinematic lava flows are currently loaded."
                    : $"Lava flows ({ids.Count}): {string.Join(", ", ids)}");
                return;
            }

            default:
                shell.WriteError($"Unknown list category '{category}'.");
                return;
        }
    }

    private static void ExecuteDescribe(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic describe <prototypeId>");
            return;
        }

        if (!system.TryDescribePrototype(args[1], out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteStart(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic start <prototypeId>");
            return;
        }

        if (!system.TryQueue(args[1], out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteStop(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length >= 2)
        {
            if (!int.TryParse(args[1], out var runSerial))
            {
                shell.WriteError($"Invalid runId '{args[1]}'.");
                return;
            }

            if (!system.TryStopRun(runSerial, "Stopped by admin command.", markCompleted: false, out var scopedMessage))
            {
                shell.WriteError(scopedMessage);
                return;
            }

            shell.WriteLine(scopedMessage);
            return;
        }

        if (!system.TryStopActive("Stopped by admin command.", markCompleted: false))
            shell.WriteLine("No active global cinematic to stop.");
        else
            shell.WriteLine("Active global cinematic stopped.");
    }

    private static void ExecuteStatus(IConsoleShell shell, WH40KCinematicSystem system)
    {
        var snapshot = system.GetSnapshot();
        var runLines = system.GetActiveRunDebugLines();
        if (!snapshot.IsActive && runLines.Count == 0)
            shell.WriteLine($"No active cinematic. Queue length: {snapshot.QueueLength}. Completed non-repeatable: {snapshot.CompletedNonRepeatableCount}.");
        else if (!snapshot.IsActive)
            shell.WriteLine($"No active global cinematic. Queue length: {snapshot.QueueLength}. Completed non-repeatable: {snapshot.CompletedNonRepeatableCount}.");
        else
            shell.WriteLine($"ActiveGlobal='{snapshot.ActiveCinematicId}', stepIndex={snapshot.ActiveStepIndex}, stepId='{snapshot.ActiveStepId}', waitMode={snapshot.ActiveWaitMode}, queue={snapshot.QueueLength}, completedNonRepeatable={snapshot.CompletedNonRepeatableCount}.");

        foreach (var line in runLines)
        {
            shell.WriteLine(line);
        }
    }

    private static void ExecuteRuns(IConsoleShell shell, WH40KCinematicSystem system)
    {
        var lines = system.GetActiveRunDebugLines();
        if (lines.Count == 0)
        {
            shell.WriteLine("No active cinematic runs.");
            return;
        }

        foreach (var line in lines)
        {
            shell.WriteLine(line);
        }
    }

    private static void ExecutePause(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic pause <runId>");
            return;
        }

        if (!int.TryParse(args[1], out var runSerial))
        {
            shell.WriteError($"Invalid runId '{args[1]}'.");
            return;
        }

        if (!system.TryPauseRun(runSerial, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteResume(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic resume <runId>");
            return;
        }

        if (!int.TryParse(args[1], out var runSerial))
        {
            shell.WriteError($"Invalid runId '{args[1]}'.");
            return;
        }

        if (!system.TryResumeRun(runSerial, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteAdvance(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic advance-step <runId>");
            return;
        }

        if (!int.TryParse(args[1], out var runSerial))
        {
            shell.WriteError($"Invalid runId '{args[1]}'.");
            return;
        }

        if (!system.TryAdvanceRun(runSerial, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteJump(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError("Usage: wh40kcinematic jump-to-step <runId> <stepId>");
            return;
        }

        if (!int.TryParse(args[1], out var runSerial))
        {
            shell.WriteError($"Invalid runId '{args[1]}'.");
            return;
        }

        if (!system.TryJumpRun(runSerial, args[2], out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteSignal(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError("Usage: wh40kcinematic emit-signal <runId> <signal>");
            return;
        }

        if (!int.TryParse(args[1], out var runSerial))
        {
            shell.WriteError($"Invalid runId '{args[1]}'.");
            return;
        }

        if (!system.TryEmitSignal(runSerial, args[2], out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteValidate(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic validate <prototypeId>");
            return;
        }

        var errors = system.ValidatePrototype(args[1]);
        if (errors.Count == 0)
        {
            shell.WriteLine($"Cinematic '{args[1]}' is valid for the current WH40K cinematic runtime.");
            return;
        }

        shell.WriteError($"Cinematic '{args[1]}' is invalid:");
        foreach (var error in errors)
        {
            shell.WriteError($"- {error}");
        }
    }

    private static void ExecuteValidateLoaded(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic validate-loaded <prototypeId>");
            return;
        }

        if (!system.TryValidateLoadedPrototype(args[1], out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecutePreviewShot(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError("Usage: wh40kcinematic preview-shot <prototypeId> <stepId> [lifetimeSeconds]");
            return;
        }

        var lifetime = 8f;
        if (args.Length >= 4 && !float.TryParse(args[3], out lifetime))
        {
            shell.WriteError($"Invalid preview lifetime '{args[3]}'.");
            return;
        }

        if (!system.TryPreviewShot(args[1], args[2], out var message, lifetime))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecutePreviewAnchor(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic preview-anchor <anchorId> [any|action|spawn|sound] [lifetimeSeconds]");
            return;
        }

        var mode = WH40KCinematicPreviewAnchorMode.Any;
        var lifetime = 8f;

        if (args.Length >= 3)
        {
            if (TryParseAnchorMode(args[2], out var parsedMode))
            {
                mode = parsedMode;
                if (args.Length >= 4 && !float.TryParse(args[3], out lifetime))
                {
                    shell.WriteError($"Invalid preview lifetime '{args[3]}'.");
                    return;
                }
            }
            else if (!float.TryParse(args[2], out lifetime))
            {
                shell.WriteError($"Unknown preview-anchor mode or lifetime '{args[2]}'.");
                return;
            }
        }

        if (!system.TryPreviewAnchor(args[1], mode, out var message, lifetime))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteValidateFlow(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic validate-flow <flowId>");
            return;
        }

        if (!system.TryGetLavaFlowDebugInfo(args[1], out var info, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);

        if (info.Truncated && !string.IsNullOrWhiteSpace(info.TruncationReason))
            shell.WriteLine($"Truncation: {info.TruncationReason}");
    }

    private static void ExecutePreviewFlow(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic preview-flow <flowId> [lifetimeSeconds]");
            return;
        }

        var lifetime = 8f;
        if (args.Length >= 3 && !float.TryParse(args[2], out lifetime))
        {
            shell.WriteError($"Invalid preview lifetime '{args[2]}'.");
            return;
        }

        if (!system.TryPreviewLavaFlow(args[1], out var message, lifetime))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecutePreviewCinematic(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kcinematic preview-cinematic <prototypeId> [lifetimeSeconds]");
            return;
        }

        var lifetime = 8f;
        if (args.Length >= 3 && !float.TryParse(args[2], out lifetime))
        {
            shell.WriteError($"Invalid preview lifetime '{args[2]}'.");
            return;
        }

        if (!system.TryPreviewCinematic(args[1], out var message, lifetime))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteNpcRecordStart(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        if (args.Length < 5)
        {
            shell.WriteError("Usage: wh40kcinematic npc-record-start <runId> <npcId> <trackId> <segmentId>");
            return;
        }

        if (!int.TryParse(args[1], out var runSerial))
        {
            shell.WriteError($"Invalid runId '{args[1]}'.");
            return;
        }

        if (!system.TryStartNpcRecording(shell, runSerial, args[2], args[3], args[4], out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteNpcRecordPause(IConsoleShell shell, WH40KCinematicSystem system)
    {
        if (!system.TryPauseNpcRecording(shell, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteNpcRecordResume(IConsoleShell shell, WH40KCinematicSystem system)
    {
        if (!system.TryResumeNpcRecording(shell, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteNpcRecordStop(IConsoleShell shell, WH40KCinematicSystem system)
    {
        if (!system.TryStopNpcRecording(shell, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static void ExecuteNpcRecordExport(IConsoleShell shell, WH40KCinematicSystem system, string[] args)
    {
        var relativePath = args.Length >= 2 ? args[1] : null;
        if (!system.TryExportNpcRecording(shell, relativePath, out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }

    private static bool TryParseAnchorMode(string rawMode, out WH40KCinematicPreviewAnchorMode mode)
    {
        switch (rawMode.ToLowerInvariant())
        {
            case "any":
                mode = WH40KCinematicPreviewAnchorMode.Any;
                return true;

            case "action":
                mode = WH40KCinematicPreviewAnchorMode.Action;
                return true;

            case "spawn":
                mode = WH40KCinematicPreviewAnchorMode.Spawn;
                return true;

            case "sound":
                mode = WH40KCinematicPreviewAnchorMode.Sound;
                return true;

            case "npc":
                mode = WH40KCinematicPreviewAnchorMode.Npc;
                return true;

            default:
                mode = WH40KCinematicPreviewAnchorMode.Any;
                return false;
        }
    }
}
