using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Server.Administration.Managers;
using Content.Server._WH40K.Administration;
using Robust.Server.Console;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Reflection;

namespace Content.Server._WH40K.Administration.Commands;

[Reflect(false)]
internal sealed partial class WH40KProtectedKickCommand : LocalizedCommands
{
    [Dependency] private IAdminActionGuard _adminActionGuard = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerNetManager _netManager = default!;

    public override string Command => "kick";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            var player = shell.Player;
            var toKickPlayer = player ?? _players.Sessions.FirstOrDefault();
            if (toKickPlayer == null)
            {
                shell.WriteLine("You need to provide a player to kick.");
                return;
            }

            shell.WriteLine($"You need to provide a player to kick. Try running 'kick {toKickPlayer.Name}' as an example.");
            return;
        }

        var name = args[0];
        if (!_players.TryGetSessionByUsername(name, out var target))
            return;

        if (WH40KStaffProtection.HasHostBypass(_adminManager.GetAdminData(target, includeDeAdmin: true), _adminManager.IsPromotedHost(target.UserId)))
        {
            shell.WriteError(Loc.GetString("wh40k-kick-host-protected", ("player", target.Name)));
            return;
        }

        if (await _adminActionGuard.TryDenyProtectedTargetAsync(
                shell.Player,
                target.UserId,
                Loc.GetString("admin-player-actions-kick"),
                target.Name,
                shell.WriteError))
        {
            return;
        }

        var reason = args.Length >= 2
            ? $"Kicked by console: {string.Join(' ', args[1..])}"
            : "Kicked by console";

        _netManager.DisconnectChannel(target.Channel, reason);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = _players.Sessions.OrderBy(c => c.Name).Select(c => c.Name).ToArray();
            return CompletionResult.FromHintOptions(options, "<PlayerIndex>");
        }

        if (args.Length > 1)
            return CompletionResult.FromHint("[<Reason>]");

        return CompletionResult.Empty;
    }
}

public sealed partial class WH40KKickCommandOverrideSystem : EntitySystem
{
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private ILogManager _logManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        OverrideKickCommand();
    }

    private void OverrideKickCommand()
    {
        var sawmill = _logManager.GetSawmill("wh40k.kick_override");
        var field = typeof(ConsoleHost).GetField("RegisteredCommands", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field?.GetValue(_console) is not Dictionary<string, IConsoleCommand> commands)
        {
            sawmill.Error("Failed to locate console command registry while overriding kick command.");
            return;
        }

        if (!commands.ContainsKey("kick"))
        {
            sawmill.Error("Kick command is missing from the console registry.");
            return;
        }

        var command = new WH40KProtectedKickCommand();
        IoCManager.InjectDependencies(command);
        commands["kick"] = command;
    }
}
