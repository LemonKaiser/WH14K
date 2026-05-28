using System;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared._WH40K.DiscordAuth;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Server._WH40K.DiscordAuth.Commands;

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class WH40KDiscordAuthAdminCommand : LocalizedCommands
{
    [Dependency] private  IServerDbManager _db = default!;
    [Dependency] private  IEntityManager _entity = default!;
    [Dependency] private  ILogManager _log = default!;
    [Dependency] private  IPlayerLocator _playerLocator = default!;
    [Dependency] private  IPlayerManager _players = default!;

    private ISawmill? _sawmill;

    private ISawmill Sawmill => _sawmill ??= _log.GetSawmill("wh40k.discord_auth.admin");

    public override string Command => "wh40kdiscord";

    public override string Description => "WH40K Discord auth admin tools.";

    public override string Help =>
        "Usage:\n" +
        "wh40kdiscord status <user>\n" +
        "wh40kdiscord unlink <user>\n" +
        "wh40kdiscord find <discordUserId>\n" +
        "User token supports exact username, user GUID, or 'self'/'me' (in-game admin console only).";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        var system = _entity.EntitySysManager.GetEntitySystem<WH40KDiscordAuthSystem>();

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                await ExecuteStatus(system, shell, args);
                break;

            case "unlink":
                await ExecuteUnlink(system, shell, args);
                break;

            case "find":
                await ExecuteFind(shell, args);
                break;

            default:
                shell.WriteError($"Unknown subcommand '{args[0]}'.");
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                new[]
                {
                    new CompletionOption("status"),
                    new CompletionOption("unlink"),
                    new CompletionOption("find"),
                },
                "<action>");
        }

        if (args.Length == 2 && (args[0].Equals("status", StringComparison.OrdinalIgnoreCase)
            || args[0].Equals("unlink", StringComparison.OrdinalIgnoreCase)))
        {
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
        }

        if (args.Length == 2 && args[0].Equals("find", StringComparison.OrdinalIgnoreCase))
            return CompletionResult.FromHint("<discordUserId>");

        return CompletionResult.Empty;
    }

    private async Task ExecuteStatus(WH40KDiscordAuthSystem system, IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Usage: wh40kdiscord status <user>");
            return;
        }

        var player = await ResolvePlayer(shell, args[1]);
        if (player == null)
            return;

        var blockReason = await system.GetConnectionBlockReasonAsync(player.UserId);
        var link = await _db.GetWH40KDiscordLink(player.UserId);

        shell.WriteLine($"[{player.Username}] userId={player.UserId}");
        if (link == null)
        {
            shell.WriteLine("Linked Discord: <none>.");
        }
        else
        {
            shell.WriteLine($"Linked Discord: {GetDiscordDisplayName(link)} (ID: {link.DiscordUserId}).");
            shell.WriteLine($"Guild cached: {(link.GuildMemberCached ? "yes" : "no")}; last guild refresh: {FormatTimestamp(link.LastGuildRefreshAt)}.");
            shell.WriteLine($"Token expires: {FormatTimestamp(link.TokenExpiresAt)}.");
        }

        shell.WriteLine(blockReason == WH40KDiscordAuthGateBlockReason.None
            ? "Connect gate: pass."
            : $"Connect gate: blocked ({blockReason}).");

        if (blockReason != WH40KDiscordAuthGateBlockReason.None)
            shell.WriteLine(system.GetConnectionDenyMessage(player.UserId, blockReason));
    }

    private async Task ExecuteUnlink(WH40KDiscordAuthSystem system, IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Usage: wh40kdiscord unlink <user>");
            return;
        }

        var player = await ResolvePlayer(shell, args[1]);
        if (player == null)
            return;

        var link = await _db.GetWH40KDiscordLink(player.UserId);
        if (link == null)
        {
            shell.WriteError($"[{player.Username}] has no linked Discord account.");
            return;
        }

        await system.ClearLinkAsync(player.UserId);

        shell.WriteLine($"[{player.Username}] Discord link cleared: {GetDiscordDisplayName(link)} (ID: {link.DiscordUserId}).");
        Audit(shell, $"cleared Discord link for {player.UserId} ({link.DiscordUserId}).");
    }

    private async Task ExecuteFind(IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Usage: wh40kdiscord find <discordUserId>");
            return;
        }

        var discordUserId = args[1].Trim();
        if (string.IsNullOrWhiteSpace(discordUserId))
        {
            shell.WriteError("Discord user ID cannot be empty.");
            return;
        }

        var ownerId = await _db.GetWH40KDiscordLinkOwner(discordUserId);
        if (ownerId == null)
        {
            shell.WriteError($"Discord user ID '{discordUserId}' is not linked to any game account.");
            return;
        }

        var owner = await _playerLocator.LookupIdAsync(ownerId.Value);
        var link = await _db.GetWH40KDiscordLink(ownerId.Value);
        var ownerName = owner?.Username ?? ownerId.Value.ToString();

        shell.WriteLine($"Discord user ID {discordUserId} belongs to [{ownerName}] userId={ownerId.Value}.");
        if (link != null)
            shell.WriteLine($"Linked Discord: {GetDiscordDisplayName(link)} (ID: {link.DiscordUserId}).");
    }

    private async Task<LocatedPlayerData?> ResolvePlayer(IConsoleShell shell, string userToken)
    {
        var normalized = userToken.Trim();
        if (string.Equals(normalized, "self", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "me", StringComparison.OrdinalIgnoreCase))
        {
            if (shell.Player == null)
            {
                shell.WriteError("'self'/'me' can only be used from an in-game admin session.");
                return null;
            }

            return await _playerLocator.LookupIdAsync(shell.Player.UserId);
        }

        if (shell.Player != null && string.Equals(normalized, shell.Player.Name, StringComparison.OrdinalIgnoreCase))
            return await _playerLocator.LookupIdAsync(shell.Player.UserId);

        var player = await _playerLocator.LookupIdByNameOrIdAsync(normalized);
        if (player == null)
            shell.WriteError($"Player '{userToken}' was not found.");

        return player;
    }

    private static string GetDiscordDisplayName(WH40KDiscordAuthDbData link)
    {
        var rawName = string.IsNullOrWhiteSpace(link.GlobalName) ? link.Username : link.GlobalName!;
        var sanitized = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(rawName);
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = WH40KDiscordAuthDisplayNameSanitizer.Sanitize(link.Username);
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = link.DiscordUserId;

        return WH40KDiscordAuthDisplayNameSanitizer.Ellipsize(sanitized, 48);
    }

    private static string FormatTimestamp(DateTimeOffset? value)
    {
        return value?.UtcDateTime.ToString("u") ?? "never";
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("u");
    }

    private void Audit(IConsoleShell shell, string message)
    {
        var actor = shell.Player?.Name ?? "SERVER";
        Sawmill.Info($"[{actor}] {message}");
    }
}
