#nullable disable warnings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._WH40K.MetaProgress;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.MetaProgress.Commands;

[AdminCommand(AdminFlags.Host)]
public sealed class WH40KMetaAdminCommand : LocalizedCommands
{
	[Robust.Shared.IoC.Dependency]
	private readonly IEntityManager _entity = default!;

	[Robust.Shared.IoC.Dependency]
	private readonly IPlayerLocator _playerLocator = default!;

	[Robust.Shared.IoC.Dependency]
	private readonly IPlayerManager _players = default!;

	[Robust.Shared.IoC.Dependency]
	private readonly IPrototypeManager _prototypes = default!;

	[Robust.Shared.IoC.Dependency]
	private readonly ILogManager _log = default!;

	private ISawmill? _sawmill;

	private ISawmill Sawmill => _sawmill ?? (_sawmill = _log.GetSawmill("wh40k.meta.admin"));

	public override string Command => "wh40kmeta";

	public override string Description => "WH40K meta-progression admin control package.";

	public override string Help => "Usage:\nwh40kmeta level set <user> <level>\nwh40kmeta level add <user> <delta>\nwh40kmeta xp set <user> <xp>\nwh40kmeta xp add <user> <delta>\nwh40kmeta dev unlock <user> <nodeId>\nwh40kmeta dev lock <user> <nodeId>\nwh40kmeta dev reset <user>\nwh40kmeta ach unlock <user> <achievementId>\nwh40kmeta ach lock <user> <achievementId>\nwh40kmeta ach progress set <user> <achievementId> <value>\nwh40kmeta ach progress add <user> <achievementId> <delta>\nwh40kmeta decor unlock <user> <unlockId>\nwh40kmeta decor lock <user> <unlockId>\nwh40kmeta title set <user> <titleId|none>\nwh40kmeta ghostskin set <user> <skinId|none>\nwh40kmeta ooccolor set <user> <colorId|none>\nwh40kmeta snapshot <user>\nwh40kmeta reset <user> [progress|development|achievements|decorations|all]\nUser token supports exact username, user GUID, or 'self'/'me' (in-game admin console only).";

	public override async void Execute(IConsoleShell shell, string argStr, string[] args)
	{
		if (args.Length == 0)
		{
			shell.WriteLine(Help);
			return;
		}
		WH40KMetaProgressSystem entitySystem = _entity.EntitySysManager.GetEntitySystem<WH40KMetaProgressSystem>();
		switch (args[0].ToLowerInvariant())
		{
		case "level":
			await ExecuteLevel(entitySystem, shell, args);
			break;
		case "xp":
			await ExecuteXp(entitySystem, shell, args);
			break;
		case "dev":
			await ExecuteDevelopment(entitySystem, shell, args);
			break;
		case "ach":
			await ExecuteAchievement(entitySystem, shell, args);
			break;
		case "decor":
			await ExecuteDecoration(entitySystem, shell, args);
			break;
		case "title":
			await ExecuteSelection(entitySystem, shell, args, WH40KMetaDecorationCategory.OocTitles, "title");
			break;
		case "ghost":
		case "ghostskin":
			await ExecuteSelection(entitySystem, shell, args, WH40KMetaDecorationCategory.GhostSkins, "ghost skin");
			break;
		case "color":
		case "ooccolor":
			await ExecuteSelection(entitySystem, shell, args, WH40KMetaDecorationCategory.OocNameColors, "OOC color");
			break;
		case "snapshot":
			await ExecuteSnapshot(entitySystem, shell, args);
			break;
		case "reset":
			await ExecuteReset(entitySystem, shell, args);
			break;
		default:
			shell.WriteError("Unknown subcommand '" + args[0] + "'.");
			shell.WriteLine(Help);
			break;
		}
	}

	public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
	{
		if (args.Length == 1)
		{
			return CompletionResult.FromHintOptions(new CompletionOption[10]
			{
				new CompletionOption("level"),
				new CompletionOption("xp"),
				new CompletionOption("dev"),
				new CompletionOption("ach"),
				new CompletionOption("decor"),
				new CompletionOption("title"),
				new CompletionOption("ghostskin"),
				new CompletionOption("ooccolor"),
				new CompletionOption("snapshot"),
				new CompletionOption("reset")
			}, "<section>");
		}
		switch (args[0].ToLowerInvariant())
		{
		case "level":
		case "xp":
			if (args.Length == 2)
			{
				return CompletionResult.FromHintOptions(new string[2] { "set", "add" }, "<action>");
			}
			if (args.Length == 3)
			{
				return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
			}
			if (args.Length == 4)
			{
				return CompletionResult.FromHint("<value>");
			}
			return CompletionResult.Empty;
		case "ach":
			if (args.Length == 2)
			{
				return CompletionResult.FromHintOptions(new string[3] { "unlock", "lock", "progress" }, "<action>");
			}
			if (args[1].Equals("progress", StringComparison.OrdinalIgnoreCase))
			{
				if (args.Length == 3)
				{
					return CompletionResult.FromHintOptions(new string[2] { "set", "add" }, "<mode>");
				}
				if (args.Length == 4)
				{
					return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
				}
				if (args.Length == 5)
				{
					return CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<WH40KMetaAchievementPrototype>(sorted: true, _prototypes), "<achievementId>");
				}
				if (args.Length == 6)
				{
					return CompletionResult.FromHint("<value>");
				}
				return CompletionResult.Empty;
			}
			if (args.Length == 3)
			{
				return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
			}
			if (args.Length == 4)
			{
				return CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<WH40KMetaAchievementPrototype>(sorted: true, _prototypes), "<achievementId>");
			}
			return CompletionResult.Empty;
		case "dev":
			if (args.Length == 2)
			{
				return CompletionResult.FromHintOptions(new string[3] { "unlock", "lock", "reset" }, "<action>");
			}
			if (args.Length == 3)
			{
				return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
			}
			if (args.Length == 4 && !args[1].Equals("reset", StringComparison.OrdinalIgnoreCase))
			{
				return CompletionResult.FromHintOptions(from id in WH40KMetaDevelopmentCatalog.Nodes.Keys.OrderBy<string, string>((string id) => id, StringComparer.Ordinal)
					select new CompletionOption(id), "<nodeId>");
			}
			return CompletionResult.Empty;
		case "decor":
			if (args.Length == 2)
			{
				return CompletionResult.FromHintOptions(new string[2] { "unlock", "lock" }, "<action>");
			}
			if (args.Length == 3)
			{
				return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
			}
			if (args.Length == 4)
			{
				return CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<WH40KMetaDecorationPrototype>(sorted: true, _prototypes), "<unlockId>");
			}
			return CompletionResult.Empty;
		case "title":
			return GetSelectionCompletion(args, WH40KMetaDecorationCategory.OocTitles);
		case "ghost":
		case "ghostskin":
			return GetSelectionCompletion(args, WH40KMetaDecorationCategory.GhostSkins);
		case "color":
		case "ooccolor":
			return GetSelectionCompletion(args, WH40KMetaDecorationCategory.OocNameColors);
		case "snapshot":
			if (args.Length != 2)
			{
				return CompletionResult.Empty;
			}
			return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
		case "reset":
			if (args.Length == 2)
			{
				return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
			}
			if (args.Length == 3)
			{
				return CompletionResult.FromHintOptions(new string[5] { "progress", "development", "achievements", "decorations", "all" }, "<scope>");
			}
			return CompletionResult.Empty;
		default:
			return CompletionResult.Empty;
		}
	}

	private CompletionResult GetSelectionCompletion(string[] args, WH40KMetaDecorationCategory category)
	{
		if (args.Length == 2)
		{
			return CompletionResult.FromHintOptions(new string[1] { "set" }, "<action>");
		}
		if (args.Length == 3)
		{
			return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user>");
		}
		if (args.Length == 4)
		{
			return CompletionResult.FromHintOptions(GetDecorationCompletionOptions(category, includeNone: true), "<id|none>");
		}
		return CompletionResult.Empty;
	}

	private IEnumerable<CompletionOption> GetDecorationCompletionOptions(WH40KMetaDecorationCategory category, bool includeNone)
	{
		if (includeNone)
		{
			yield return new CompletionOption("none");
		}
		IEnumerable<string> enumerable = from p in (from p in _prototypes.EnumeratePrototypes<WH40KMetaDecorationPrototype>()
				where p.Category == category
				select p).OrderBy<WH40KMetaDecorationPrototype, string>((WH40KMetaDecorationPrototype p) => p.ID, StringComparer.Ordinal)
			select p.ID;
		foreach (string item in enumerable)
		{
			yield return new CompletionOption(item);
		}
	}

	private async Task ExecuteLevel(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args)
	{
		if (args.Length != 4)
		{
			shell.WriteError("Usage: wh40kmeta level <set|add> <user> <value>");
			return;
		}
		if (!int.TryParse(args[3], out var value))
		{
			shell.WriteError("Value must be an integer.");
			return;
		}
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[2]);
		if (!(locatedPlayerData == null))
		{
			string text = args[1].ToLowerInvariant();
			if (text == "set")
			{
				system.TrySetLevel(locatedPlayerData.UserId, value, out var resolvedLevel, out var resolvedLifetimeXp);
				shell.WriteLine($"[{locatedPlayerData.Username}] level set: level={resolvedLevel}, lifetimeXp={resolvedLifetimeXp}.");
				Audit(shell, $"set level for {locatedPlayerData.UserId} to {resolvedLevel}.");
			}
			else if (text == "add")
			{
				system.TryAddLevels(locatedPlayerData.UserId, value, out var resolvedLevel2, out var resolvedLifetimeXp2);
				shell.WriteLine($"[{locatedPlayerData.Username}] level adjusted: level={resolvedLevel2}, lifetimeXp={resolvedLifetimeXp2}.");
				Audit(shell, $"added levels {value} for {locatedPlayerData.UserId}, now {resolvedLevel2}.");
			}
			else
			{
				shell.WriteError("Action must be 'set' or 'add'.");
			}
		}
	}

	private async Task ExecuteXp(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args)
	{
		if (args.Length != 4)
		{
			shell.WriteError("Usage: wh40kmeta xp <set|add> <user> <value>");
			return;
		}
		if (!int.TryParse(args[3], out var value))
		{
			shell.WriteError("Value must be an integer.");
			return;
		}
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[2]);
		if (locatedPlayerData == null)
		{
			return;
		}
		string text = args[1].ToLowerInvariant();
		if (!(text == "set"))
		{
			if (!(text == "add"))
			{
				shell.WriteError("Action must be 'set' or 'add'.");
				return;
			}
			system.AddLifetimeXp(locatedPlayerData.UserId, value);
			Audit(shell, $"added lifetime XP {value} for {locatedPlayerData.UserId}.");
		}
		else
		{
			system.SetLifetimeXp(locatedPlayerData.UserId, value);
			Audit(shell, $"set lifetime XP for {locatedPlayerData.UserId} to {Math.Max(0, value)}.");
		}
		WH40KMetaProgressSnapshot snapshot = system.GetSnapshot(locatedPlayerData.UserId);
		shell.WriteLine($"[{locatedPlayerData.Username}] XP: {snapshot.CurrentXp}/{snapshot.RequiredXp}, lifetime={snapshot.LifetimeXp}, level={snapshot.Level}.");
	}

	private async Task ExecuteAchievement(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args)
	{
		if (args.Length < 2)
		{
			shell.WriteError("Usage: wh40kmeta ach <unlock|lock|progress> ...");
			return;
		}
		string text = args[1].ToLowerInvariant();
		switch (text)
		{
		case "unlock":
		case "lock":
			await ExecuteAchievementUnlock(system, shell, args, text == "unlock");
			break;
		case "progress":
			await ExecuteAchievementProgress(system, shell, args);
			break;
		default:
			shell.WriteError("Action must be 'unlock', 'lock', or 'progress'.");
			break;
		}
	}

	private async Task ExecuteAchievementUnlock(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args, bool unlock)
	{
		if (args.Length != 4)
		{
			shell.WriteError("Usage: wh40kmeta ach <unlock|lock> <user> <achievementId>");
			return;
		}
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[2]);
		if (!(locatedPlayerData == null))
		{
			if (!system.TrySetAchievementUnlocked(locatedPlayerData.UserId, args[3], unlock, out int resolvedProgress, out int target, out bool completed, out string error))
			{
				shell.WriteError(error);
				return;
			}
			shell.WriteLine($"[{locatedPlayerData.Username}] achievement '{args[3]}': progress={resolvedProgress}/{target}, completed={completed}.");
			Audit(shell, $"{(unlock ? "unlocked" : "locked")} achievement '{args[3]}' for {locatedPlayerData.UserId}.");
		}
	}

	private async Task ExecuteDevelopment(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args)
	{
		if (args.Length < 3)
		{
			shell.WriteError("Usage: wh40kmeta dev <unlock|lock|reset> <user> [nodeId]");
			return;
		}
		string mode = args[1].ToLowerInvariant();
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[2]);
		if (locatedPlayerData == null)
		{
			return;
		}
		switch (mode)
		{
		case "unlock":
		case "lock":
		{
			if (args.Length != 4)
			{
				shell.WriteError("Usage: wh40kmeta dev <unlock|lock> <user> <nodeId>");
				break;
			}
			if (!system.TrySetDevelopmentNodeUnlocked(locatedPlayerData.UserId, args[3], mode == "unlock", out string error))
			{
				shell.WriteError(error);
				break;
			}
			WH40KMetaProgressSnapshot snapshot2 = system.GetSnapshot(locatedPlayerData.UserId);
			shell.WriteLine($"[{locatedPlayerData.Username}] development '{args[3]}' => {((mode == "unlock") ? "opened" : "locked")} (opened={snapshot2.Development.OpenedNodeIds.Count}, spent={snapshot2.Development.SpentSkillPoints}, available={snapshot2.Development.AvailableSkillPoints}).");
			Audit(shell, $"{mode} development node '{args[3]}' for {locatedPlayerData.UserId}.");
			break;
		}
		case "reset":
		{
			if (args.Length != 3)
			{
				shell.WriteError("Usage: wh40kmeta dev reset <user>");
				break;
			}
			system.ResetForAdmin(locatedPlayerData.UserId, WH40KMetaProgressSystem.AdminResetScope.Development);
			WH40KMetaProgressSnapshot snapshot = system.GetSnapshot(locatedPlayerData.UserId);
			shell.WriteLine($"[{locatedPlayerData.Username}] development reset (opened={snapshot.Development.OpenedNodeIds.Count}, available={snapshot.Development.AvailableSkillPoints}).");
			Audit(shell, $"reset development state for {locatedPlayerData.UserId}.");
			break;
		}
		default:
			shell.WriteError("Action must be 'unlock', 'lock', or 'reset'.");
			break;
		}
	}

	private async Task ExecuteAchievementProgress(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args)
	{
		if (args.Length != 6)
		{
			shell.WriteError("Usage: wh40kmeta ach progress <set|add> <user> <achievementId> <value>");
			return;
		}
		if (!int.TryParse(args[5], out var value))
		{
			shell.WriteError("Value must be an integer.");
			return;
		}
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[3]);
		if (locatedPlayerData == null)
		{
			return;
		}
		string text = args[2].ToLowerInvariant();
		if (!((text == "set") ? (system.TrySetAchievementProgress(locatedPlayerData.UserId, args[4], value, out int resolvedProgress, out int target, out bool completed, out string error) ? ReportAchievementProgress(shell, locatedPlayerData, args[4], resolvedProgress, target, completed, text, value, error) : ReportAchievementError(shell, error)) : (text == "add" && (system.TryAddAchievementProgress(locatedPlayerData.UserId, args[4], value, out int resolvedProgress2, out int target2, out bool completed2, out string error2) ? ReportAchievementProgress(shell, locatedPlayerData, args[4], resolvedProgress2, target2, completed2, text, value, error2) : ReportAchievementError(shell, error2)))))
		{
			if ((!(text == "set") && !(text == "add")) || 1 == 0)
			{
				shell.WriteError("Mode must be 'set' or 'add'.");
			}
			return;
		}
		Audit(shell, $"{text} achievement progress '{args[4]}' for {locatedPlayerData.UserId} by {value}.");
	}

	private bool ReportAchievementProgress(IConsoleShell shell, LocatedPlayerData player, string achievementId, int progress, int target, bool completed, string mode, int value, string error)
	{
		if (!string.IsNullOrWhiteSpace(error))
		{
			shell.WriteError(error);
			return false;
		}
		shell.WriteLine($"[{player.Username}] achievement '{achievementId}' after {mode} {value}: {progress}/{target}, completed={completed}.");
		return true;
	}

	private bool ReportAchievementError(IConsoleShell shell, string error)
	{
		shell.WriteError(error);
		return false;
	}

	private async Task ExecuteDecoration(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args)
	{
		if (args.Length != 4)
		{
			shell.WriteError("Usage: wh40kmeta decor <unlock|lock> <user> <unlockId>");
			return;
		}
		string mode = args[1].ToLowerInvariant();
		string text = mode;
		if ((!(text == "unlock") && !(text == "lock")) || 1 == 0)
		{
			shell.WriteError("Action must be 'unlock' or 'lock'.");
			return;
		}
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[2]);
		if (!(locatedPlayerData == null))
		{
			bool flag = mode == "unlock";
			if (!system.TrySetDecorationUnlocked(locatedPlayerData.UserId, args[3], flag, out string error))
			{
				shell.WriteError(error);
				return;
			}
			shell.WriteLine($"[{locatedPlayerData.Username}] decoration '{args[3]}' => {(flag ? "unlocked" : "locked")}.");
			Audit(shell, $"{(flag ? "unlocked" : "locked")} decoration '{args[3]}' for {locatedPlayerData.UserId}.");
		}
	}

	private async Task ExecuteSelection(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args, WH40KMetaDecorationCategory category, string label)
	{
		if (args.Length != 4 || !args[1].Equals("set", StringComparison.OrdinalIgnoreCase))
		{
			shell.WriteError("Usage: wh40kmeta " + args[0] + " set <user> <id|none>");
			return;
		}
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[2]);
		if (!(locatedPlayerData == null))
		{
			string decorationId = (IsNoneToken(args[3]) ? string.Empty : args[3].Trim());
			if (!system.TrySetDecorationSelection(locatedPlayerData.UserId, category, decorationId, out string resolvedSelection, out string error))
			{
				shell.WriteError(error);
				return;
			}
			string value = (string.IsNullOrWhiteSpace(resolvedSelection) ? "<default>" : resolvedSelection);
			shell.WriteLine($"[{locatedPlayerData.Username}] selected {label}: {value}.");
			Audit(shell, $"set {label} selection for {locatedPlayerData.UserId} to '{value}'.");
		}
	}

	private async Task ExecuteSnapshot(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args)
	{
		if (args.Length != 2)
		{
			shell.WriteError("Usage: wh40kmeta snapshot <user>");
			return;
		}
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[1]);
		if (!(locatedPlayerData == null))
		{
			WH40KMetaProgressSnapshot snapshot = system.GetSnapshot(locatedPlayerData.UserId);
			WriteSnapshot(shell, locatedPlayerData, snapshot);
		}
	}

	private async Task ExecuteReset(WH40KMetaProgressSystem system, IConsoleShell shell, string[] args)
	{
		int num = args.Length;
		if ((num < 2 || num > 3) ? true : false)
		{
			shell.WriteError("Usage: wh40kmeta reset <user> [progress|development|achievements|decorations|all]");
			return;
		}
		LocatedPlayerData locatedPlayerData = await ResolvePlayer(shell, args[1]);
		if (!(locatedPlayerData == null))
		{
			WH40KMetaProgressSystem.AdminResetScope scope = WH40KMetaProgressSystem.AdminResetScope.All;
			if (args.Length == 3 && !TryParseResetScope(args[2], out scope))
			{
				shell.WriteError("Scope must be one of: progress, development, achievements, decorations, all.");
				return;
			}
			system.ResetForAdmin(locatedPlayerData.UserId, scope);
			WH40KMetaProgressSnapshot snapshot = system.GetSnapshot(locatedPlayerData.UserId);
			shell.WriteLine($"[{locatedPlayerData.Username}] reset scope '{scope}' applied.");
			WriteSnapshot(shell, locatedPlayerData, snapshot);
			Audit(shell, $"reset scope '{scope}' for {locatedPlayerData.UserId}.");
		}
	}

	private void WriteSnapshot(IConsoleShell shell, LocatedPlayerData player, WH40KMetaProgressSnapshot snapshot)
	{
		List<WH40KMetaDecorationSnapshotEntry> decorations = snapshot.Decorations;
		int value = decorations.Count((WH40KMetaDecorationSnapshotEntry entry) => entry.Category == WH40KMetaDecorationCategory.GhostSkins && entry.Unlocked);
		int value2 = decorations.Count((WH40KMetaDecorationSnapshotEntry entry) => entry.Category == WH40KMetaDecorationCategory.GhostSkins);
		int value3 = decorations.Count((WH40KMetaDecorationSnapshotEntry entry) => entry.Category == WH40KMetaDecorationCategory.OocTitles && entry.Unlocked);
		int value4 = decorations.Count((WH40KMetaDecorationSnapshotEntry entry) => entry.Category == WH40KMetaDecorationCategory.OocTitles);
		int value5 = decorations.Count((WH40KMetaDecorationSnapshotEntry entry) => entry.Category == WH40KMetaDecorationCategory.OocNameColors && entry.Unlocked);
		int value6 = decorations.Count((WH40KMetaDecorationSnapshotEntry entry) => entry.Category == WH40KMetaDecorationCategory.OocNameColors);
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 7);
		defaultInterpolatedStringHandler.AppendLiteral("[");
		defaultInterpolatedStringHandler.AppendFormatted(player.Username);
		defaultInterpolatedStringHandler.AppendLiteral("] userId=");
		defaultInterpolatedStringHandler.AppendFormatted(player.UserId);
		defaultInterpolatedStringHandler.AppendLiteral(" Lv.");
		defaultInterpolatedStringHandler.AppendFormatted(snapshot.Level);
		defaultInterpolatedStringHandler.AppendLiteral(" XP ");
		defaultInterpolatedStringHandler.AppendFormatted(snapshot.CurrentXp);
		defaultInterpolatedStringHandler.AppendLiteral("/");
		defaultInterpolatedStringHandler.AppendFormatted(snapshot.RequiredXp);
		defaultInterpolatedStringHandler.AppendLiteral(" ");
		defaultInterpolatedStringHandler.AppendLiteral("(lifetime ");
		defaultInterpolatedStringHandler.AppendFormatted(snapshot.LifetimeXp);
		defaultInterpolatedStringHandler.AppendLiteral(", cap ");
		defaultInterpolatedStringHandler.AppendFormatted((snapshot.LevelCap <= 0) ? "unlimited" : ((object)snapshot.LevelCap));
		defaultInterpolatedStringHandler.AppendLiteral(").");
		shell.WriteLine(defaultInterpolatedStringHandler.ToStringAndClear());
		shell.WriteLine($"Development: opened {snapshot.Development.OpenedNodeIds.Count}, spent {snapshot.Development.SpentSkillPoints}/{snapshot.Development.TotalSkillPoints}, available {snapshot.Development.AvailableSkillPoints}.");
		shell.WriteLine($"Achievements: {snapshot.CompletedAchievements}/{snapshot.TotalAchievements}.");
		shell.WriteLine($"Decorations: ghosts {value}/{value2}, titles {value3}/{value4}, colors {value5}/{value6}.");
		shell.WriteLine($"Selected: ghost='{snapshot.DecorationSelection.SelectedGhostSkinId}', title='{snapshot.DecorationSelection.SelectedOocTitleId}', oocColor='{snapshot.DecorationSelection.SelectedOocNameColorId}'.");
	}

	private async Task<LocatedPlayerData?> ResolvePlayer(IConsoleShell shell, string userToken)
	{
		string normalized = userToken.Trim();
		if (string.Equals(normalized, "self", StringComparison.OrdinalIgnoreCase) || string.Equals(normalized, "me", StringComparison.OrdinalIgnoreCase))
		{
			if (shell.Player == null)
			{
				shell.WriteError("'self'/'me' can only be used from an in-game admin session.");
				return null;
			}
			return await _playerLocator.LookupIdAsync(shell.Player.UserId);
		}
		if (shell.Player != null && string.Equals(normalized, shell.Player.Name, StringComparison.OrdinalIgnoreCase))
		{
			return await _playerLocator.LookupIdAsync(shell.Player.UserId);
		}
		LocatedPlayerData obj = await _playerLocator.LookupIdByNameOrIdAsync(normalized);
		if (obj == null)
		{
			if (shell.Player != null && !normalized.Contains('@') && shell.Player.Name.Contains('@'))
			{
				shell.WriteError($"Player '{userToken}' was not found. Tip: use exact username '{shell.Player.Name}' or 'self'.");
			}
			else
			{
				shell.WriteError("Player '" + userToken + "' was not found.");
			}
		}
		return obj;
	}

	private static bool TryParseResetScope(string value, out WH40KMetaProgressSystem.AdminResetScope scope)
	{
		switch (value.ToLowerInvariant())
		{
		case "progress":
			scope = WH40KMetaProgressSystem.AdminResetScope.Progress;
			return true;
		case "achievement":
		case "ach":
		case "achievements":
			scope = WH40KMetaProgressSystem.AdminResetScope.Achievements;
			return true;
		case "development":
		case "dev":
			scope = WH40KMetaProgressSystem.AdminResetScope.Development;
			return true;
		case "decorations":
		case "decor":
			scope = WH40KMetaProgressSystem.AdminResetScope.Decorations;
			return true;
		case "all":
			scope = WH40KMetaProgressSystem.AdminResetScope.All;
			return true;
		default:
			scope = WH40KMetaProgressSystem.AdminResetScope.Progress;
			return false;
		}
	}

	private static bool IsNoneToken(string value)
	{
		if (!string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(value, "default", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private void Audit(IConsoleShell shell, string message)
	{
		string text = shell.Player?.Name ?? "SERVER";
		Sawmill.Info("[" + text + "] " + message);
	}
}
