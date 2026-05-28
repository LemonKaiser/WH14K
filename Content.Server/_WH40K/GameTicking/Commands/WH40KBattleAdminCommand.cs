using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration;
using Content.Server.Cargo.Systems;
using Content.Server.Commands;
using Content.Server.Research.Systems;
using Content.Server._WH40K.Cargo.Components;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Server._WH40K.StrategicPoints;
using Content.Server._WH40K.Research.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared._WH40K.GameMode;
using Content.Shared._WH40K.StrategicPoints;
using Content.Shared.Administration;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.GameTicking.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed partial class WH40KBattleAdminCommand : IConsoleCommand
{
    [Dependency] private  IEntityManager _entityManager = default!;
    [Dependency] private  IPrototypeManager _proto = default!;

    public string Command => "wh40kbattle";
    public string Description => "WH40K admin control for phase, team economy, strategic points, and unlocks.";
    public string Help =>
        "Usage:\n" +
        "wh40kbattle phase <preparation|assault|apocalypse>\n" +
        "wh40kbattle status [teamId]\n" +
        "wh40kbattle level <teamId> <level>\n" +
        "wh40kbattle adjust <teamId> <xp|influence|research|gelt> <delta>\n" +
        "wh40kbattle point <list|reset|owner|tier> ...\n" +
        "wh40kbattle point-list | point-reset <pointUid> | point-set-owner <pointUid> <teamId|neutral> | point-set-tier <pointUid> <0|1|2|3>\n" +
        "wh40kbattle unlock <tech|cargo> <teamId> <prototypeId>\n" +
        "wh40kbattle lock <tech|cargo> <teamId> <prototypeId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        args = NormalizeLegacyArgs(args);
        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        var action = args[0].ToLowerInvariant();

        switch (action)
        {
            case "status":
                ExecuteStatus(shell, rule, args);
                return;

            case "phase":
                WH40KBattleAdminCommandShared.ExecutePhase(shell, rule, args, "Usage: wh40kbattle phase <phase>", 1);
                return;

            case "level":
                WH40KBattleAdminCommandShared.ExecuteSetLevel(shell, rule, args, "Usage: wh40kbattle level <teamId> <level>", 1);
                return;

            case "adjust":
                ExecuteAdjust(shell, rule, args);
                return;

            case "point":
                ExecutePoint(shell, rule, args);
                return;

            case "unlock":
                ExecuteUnlock(shell, rule, args, unlock: true);
                return;

            case "lock":
                ExecuteUnlock(shell, rule, args, unlock: false);
                return;

            default:
                shell.WriteError($"Unknown subcommand '{args[0]}'.");
                shell.WriteLine(Help);
                return;
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        var subcommands = new[]
        {
            "phase",
            "status",
            "level",
            "adjust",
            "point",
            "point-list",
            "point-reset",
            "point-set-owner",
            "point-set-tier",
            "unlock",
            "lock"
        };

        if (args.Length == 1)
            return CompletionResult.FromHintOptions(subcommands, "<action>");

        args = NormalizeLegacyArgs(args);
        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        var action = args[0].ToLowerInvariant();
        switch (action)
        {
            case "phase":
                return WH40KBattleAdminCommandShared.GetPhaseCompletion(args, 1);

            case "status":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
                return CompletionResult.Empty;

            case "level":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
                if (args.Length == 3)
                    return CompletionResult.FromHint("<level>");
                return CompletionResult.Empty;

            case "adjust":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
                if (args.Length == 3)
                    return CompletionResult.FromHintOptions(new[] { "xp", "influence", "research", "gelt" }, "<resource>");
                if (args.Length == 4)
                    return CompletionResult.FromHint("<delta>");
                return CompletionResult.Empty;

            case "point":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(new[] { "list", "reset", "owner", "tier" }, "<action>");
                if (args.Length == 3 && args[1].Equals("reset", StringComparison.OrdinalIgnoreCase))
                    return CompletionResult.FromHint("<pointUid>");
                if (args.Length == 3 && (args[1].Equals("owner", StringComparison.OrdinalIgnoreCase) || args[1].Equals("tier", StringComparison.OrdinalIgnoreCase)))
                    return CompletionResult.FromHint("<pointUid>");
                if (args.Length == 4 && args[1].Equals("owner", StringComparison.OrdinalIgnoreCase))
                    return CompletionResult.FromHintOptions(rule.GetTeamIds().Concat(new[] { "neutral" }), "<teamId|neutral>");
                if (args.Length == 4 && args[1].Equals("tier", StringComparison.OrdinalIgnoreCase))
                    return CompletionResult.FromHintOptions(new[] { "0", "1", "2", "3" }, "<tier>");
                return CompletionResult.Empty;

            case "unlock":
            case "lock":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(new[] { "tech", "cargo" }, "<target>");
                if (args.Length == 3)
                    return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
                if (args.Length == 4 && args[1].Equals("tech", StringComparison.OrdinalIgnoreCase))
                    return CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<TechnologyPrototype>(proto: _proto), "<technologyId>");
                if (args.Length == 4 && args[1].Equals("cargo", StringComparison.OrdinalIgnoreCase))
                    return CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<CargoProductPrototype>(proto: _proto), "<cargoProductId>");
                return CompletionResult.Empty;
        }

        return CompletionResult.Empty;
    }

    private void ExecuteStatus(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError("Usage: wh40kbattle status [teamId]");
            return;
        }

        var teamIds = rule.GetTeamIds();
        if (teamIds.Count == 0)
        {
            shell.WriteError("Active WH40K team-battle rule not found.");
            return;
        }

        shell.WriteLine($"WH40K phase: {rule.GetCurrentPhase()}");

        if (args.Length == 2)
        {
            if (!TryResolveCanonicalTeamId(rule, args[1], out var singleTeamId))
            {
                WriteUnknownTeam(shell, rule, args[1]);
                return;
            }

            WriteTeamStatus(shell, rule, singleTeamId);
            return;
        }

        foreach (var teamId in teamIds)
        {
            WriteTeamStatus(shell, rule, teamId);
        }
    }

    private static string[] NormalizeLegacyArgs(string[] args)
    {
        if (args.Length == 0)
            return args;

        return args[0].ToLowerInvariant() switch
        {
            "point-list" => new[] { "point", "list" },
            "point-reset" => PrependArgs("point", "reset", args),
            "point-set-owner" => PrependArgs("point", "owner", args),
            "point-set-tier" => PrependArgs("point", "tier", args),
            _ => args
        };
    }

    private static string[] PrependArgs(string first, string[] args)
    {
        var normalized = new string[args.Length];
        normalized[0] = first;
        Array.Copy(args, 1, normalized, 1, args.Length - 1);
        return normalized;
    }

    private static string[] PrependArgs(string first, string second, string[] args)
    {
        var normalized = new string[args.Length + 1];
        normalized[0] = first;
        normalized[1] = second;
        Array.Copy(args, 1, normalized, 2, args.Length - 1);
        return normalized;
    }

    private void ExecuteAdjust(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        if (args.Length != 4)
        {
            shell.WriteError("Usage: wh40kbattle adjust <teamId> <xp|influence|research|gelt> <delta>");
            return;
        }

        if (!int.TryParse(args[3], out var delta))
        {
            shell.WriteError("Delta must be an integer (use negative to subtract).");
            return;
        }

        if (!TryResolveCanonicalTeamId(rule, args[1], out var teamId))
        {
            WriteUnknownTeam(shell, rule, args[1]);
            return;
        }

        switch (args[2].ToLowerInvariant())
        {
            case "xp":
                if (!rule.TryAdjustTeamFrontPoints(teamId, delta, out var xpTeamId, out var frontPoints, out var level, source: "admin"))
                {
                    WriteUnknownTeam(shell, rule, args[1]);
                    return;
                }

                shell.WriteLine($"Team '{xpTeamId}': XP adjusted by {delta}, total now {frontPoints}, base level {level}.");
                return;

            case "influence":
                if (!rule.TryAdjustTeamCommandPoints(teamId, delta, out var influenceTeamId, out var commandPoints, source: "admin"))
                {
                    WriteUnknownTeam(shell, rule, args[1]);
                    return;
                }

                shell.WriteLine($"Team '{influenceTeamId}': influence adjusted by {delta}, total now {commandPoints}.");
                return;

            case "research":
                if (!rule.TryAdjustTeamResearchPoints(teamId, delta, out var researchTeamId, out var researchPoints, source: "admin"))
                {
                    WriteUnknownTeam(shell, rule, args[1]);
                    return;
                }

                shell.WriteLine($"Team '{researchTeamId}': research adjusted by {delta}, total now {researchPoints}.");
                return;

            case "gelt":
            case "funds":
            case "money":
                if (!TryAdjustTeamFunds(teamId, delta, out var funds))
                {
                    shell.WriteError($"Failed to resolve throne-gelt account for team '{teamId}'.");
                    return;
                }

                shell.WriteLine($"Team '{teamId}': gelt adjusted by {delta}, total now {funds}.");
                return;

            default:
                shell.WriteError("Resource must be one of: xp, influence, research, gelt.");
                return;
        }
    }

    private void ExecutePoint(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kbattle point <list|reset|owner|tier> ...");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "list":
                if (args.Length != 2)
                {
                    shell.WriteError("Usage: wh40kbattle point list");
                    return;
                }

                ExecutePointList(shell);
                return;

            case "reset":
                if (args.Length != 3)
                {
                    shell.WriteError("Usage: wh40kbattle point reset <pointUid>");
                    return;
                }

                ExecutePointReset(shell, args[2]);
                return;

            case "owner":
                if (args.Length != 4)
                {
                    shell.WriteError("Usage: wh40kbattle point owner <pointUid> <teamId|neutral>");
                    return;
                }

                ExecutePointSetOwner(shell, rule, args[2], args[3]);
                return;

            case "tier":
                if (args.Length != 4)
                {
                    shell.WriteError("Usage: wh40kbattle point tier <pointUid> <0|1|2|3>");
                    return;
                }

                ExecutePointSetTier(shell, args[2], args[3]);
                return;

            default:
                shell.WriteError($"Unknown point action '{args[1]}'.");
                shell.WriteLine("Available point actions: list, reset, owner, tier");
                return;
        }
    }

    private void ExecuteUnlock(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args, bool unlock)
    {
        var verb = unlock ? "unlock" : "lock";
        if (args.Length != 4)
        {
            shell.WriteError($"Usage: wh40kbattle {verb} <tech|cargo> <teamId> <prototypeId>");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "tech":
                ExecuteTechnologyToggle(shell, rule, args[2], args[3], unlock);
                return;

            case "cargo":
                ExecuteCargoToggle(shell, rule, args[2], args[3], unlock);
                return;

            default:
                shell.WriteError($"Unknown unlock target '{args[1]}'. Use tech or cargo.");
                return;
        }
    }

    private void WriteTeamStatus(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string teamId)
    {
        if (!rule.TryGetTeamEconomySnapshot(null, teamId, out var snapshot))
        {
            shell.WriteError($"Failed to resolve team status for '{teamId}'.");
            return;
        }

        var researchServers = 0;
        var serverResearchPoints = 0;
        var unlockedTechs = new HashSet<ProtoId<TechnologyPrototype>>();
        string? activeResearch = null;
        var query = _entityManager.EntityQueryEnumerator<ResearchServerComponent, TechnologyDatabaseComponent, WH40KResearchTeamComponent>();
        while (query.MoveNext(out _, out var server, out var database, out var researchTeam))
        {
            if (!string.Equals(researchTeam.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            researchServers++;
            serverResearchPoints += server.Points;

            foreach (var tech in database.UnlockedTechnologies)
            {
                unlockedTechs.Add(tech);
            }

            if (activeResearch == null && server.ActiveTechnologyId != null)
            {
                activeResearch = $"{server.ActiveTechnologyId} ({Math.Max(0, (int) Math.Ceiling(server.ActiveTechnologyRemainingSeconds))}s)";
            }
        }

        var toNextText = snapshot.PointsToNextLevel is { } pointsToNext ? pointsToNext.ToString() : "-";
        var activeText = activeResearch ?? "none";
        shell.WriteLine(
            $"Team '{snapshot.TeamId}': level={snapshot.BaseLevel}, xp={snapshot.TeamXp}, toNext={toNextText}, influence={snapshot.Influence}, " +
            $"gelt={snapshot.Funds}, research={snapshot.ResearchPoints}, serverResearch={serverResearchPoints}, " +
            $"unlockedTechs={unlockedTechs.Count}, researchServers={researchServers}, activeResearch={activeText}");
    }

    private void ExecutePointList(IConsoleShell shell)
    {
        var strategicPoints = _entityManager.EntitySysManager.GetEntitySystem<WH40KStrategicPointSystem>();
        var snapshots = strategicPoints.GetAdminSnapshots();
        if (snapshots.Count == 0)
        {
            shell.WriteLine("No strategic point anchors were found.");
            return;
        }

        foreach (var point in snapshots)
        {
            var callsign = string.IsNullOrWhiteSpace(point.Callsign) ? "-" : point.Callsign;
            var owner = string.IsNullOrWhiteSpace(point.OwnerTeamId) ? "Neutral" : point.OwnerTeamId;
            var income = point.Tier <= WH40KStrategicPointTier.T0
                ? "income=inactive"
                : $"income=xp:{point.TeamXpIncome} inf:{point.InfluenceIncome} res:{point.ResearchIncome} funds:{point.FundsIncome}";

            shell.WriteLine(
                $"Point {point.Target} anchor={point.Anchor} built={point.BuiltPoint} callsign={callsign} " +
                $"type={point.PointType} tier={(int) point.Tier} owner={owner} {income}");
        }
    }

    private void ExecutePointReset(IConsoleShell shell, string pointUid)
    {
        if (!TryResolvePointEntity(pointUid, out var targetUid))
        {
            shell.WriteError($"Could not resolve point entity '{pointUid}'.");
            return;
        }

        var strategicPoints = _entityManager.EntitySysManager.GetEntitySystem<WH40KStrategicPointSystem>();
        if (!strategicPoints.TryAdminResetPoint(targetUid, out var error))
        {
            shell.WriteError(error);
            return;
        }

        shell.WriteLine($"Strategic point '{pointUid}' reset to T0.");
    }

    private void ExecutePointSetOwner(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string pointUid, string requestedOwner)
    {
        if (!TryResolvePointEntity(pointUid, out var targetUid))
        {
            shell.WriteError($"Could not resolve point entity '{pointUid}'.");
            return;
        }
        var strategicPoints = _entityManager.EntitySysManager.GetEntitySystem<WH40KStrategicPointSystem>();
        if (string.Equals(requestedOwner, "neutral", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestedOwner, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (!strategicPoints.TryAdminResetPoint(targetUid, out var resetError))
            {
                shell.WriteError(resetError);
                return;
            }

            shell.WriteLine($"Strategic point '{pointUid}' reset to T0 (neutral owner requested).");
            return;
        }

        if (!TryResolveCanonicalTeamId(rule, requestedOwner, out var resolvedTeamId))
        {
            WriteUnknownTeam(shell, rule, requestedOwner);
            return;
        }

        if (!strategicPoints.TryAdminSetPointOwner(targetUid, resolvedTeamId, out var error))
        {
            shell.WriteError(error);
            return;
        }

        shell.WriteLine($"Strategic point '{pointUid}' owner set to '{resolvedTeamId}'.");
    }

    private void ExecutePointSetTier(IConsoleShell shell, string pointUid, string tierText)
    {
        if (!TryResolvePointEntity(pointUid, out var targetUid))
        {
            shell.WriteError($"Could not resolve point entity '{pointUid}'.");
            return;
        }

        if (!int.TryParse(tierText, out var tierValue) || tierValue < 0 || tierValue > 3)
        {
            shell.WriteError("Tier must be 0, 1, 2, or 3.");
            return;
        }

        var strategicPoints = _entityManager.EntitySysManager.GetEntitySystem<WH40KStrategicPointSystem>();
        if (!strategicPoints.TryAdminSetPointTier(targetUid, (WH40KStrategicPointTier) tierValue, out var error))
        {
            shell.WriteError(error);
            return;
        }

        shell.WriteLine($"Strategic point '{pointUid}' forced to T{tierValue}.");
    }

    private bool TryGetTeamFunds(string teamId, out int funds)
    {
        funds = 0;

        if (!TryResolveCargoAccountForTeam(teamId, out var account) ||
            !TryGetTeamBank(out var bank))
        {
            return false;
        }

        var cargo = _entityManager.EntitySysManager.GetEntitySystem<CargoSystem>();
        funds = Math.Max(0, cargo.GetBalanceFromAccount(bank, account));
        return true;
    }

    private bool TryResolvePointEntity(string value, out EntityUid targetUid)
    {
        targetUid = EntityUid.Invalid;
        if (!NetEntity.TryParse(value, out var netEntity) ||
            !_entityManager.TryGetEntity(netEntity, out EntityUid? resolvedUid) ||
            resolvedUid == null)
        {
            return false;
        }

        targetUid = resolvedUid.Value;
        return _entityManager.EntityExists(targetUid);
    }

    private bool TryAdjustTeamFunds(string teamId, int delta, out int funds)
    {
        funds = 0;

        if (!TryResolveCargoAccountForTeam(teamId, out var account) ||
            !TryGetTeamBank(out var bank))
        {
            return false;
        }

        var cargo = _entityManager.EntitySysManager.GetEntitySystem<CargoSystem>();
        var current = Math.Max(0, cargo.GetBalanceFromAccount(bank, account));
        funds = Math.Max(0, current + delta);
        return cargo.TrySetBankAccount(bank, account, funds, createAccount: true);
    }

    private bool TryGetTeamBank(out Entity<StationBankAccountComponent?> bank)
    {
        bank = default;

        var query = _entityManager.EntityQueryEnumerator<StationBankAccountComponent>();
        while (query.MoveNext(out var uid, out var bankComponent))
        {
            bank = (uid, bankComponent);
            return true;
        }

        return false;
    }

    private void ExecuteTechnologyToggle(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string requestedTeamId, string technologyId, bool unlock)
    {
        if (!TryResolveCanonicalTeamId(rule, requestedTeamId, out var teamId))
        {
            WriteUnknownTeam(shell, rule, requestedTeamId);
            return;
        }

        if (!_proto.TryIndex<TechnologyPrototype>(technologyId, out var technology))
        {
            shell.WriteError($"Unknown technology '{technologyId}'.");
            return;
        }

        var research = _entityManager.EntitySysManager.GetEntitySystem<ResearchSystem>();
        var processedServers = 0;
        var changedServers = 0;

        var query = _entityManager.EntityQueryEnumerator<ResearchServerComponent, TechnologyDatabaseComponent, WH40KResearchTeamComponent>();
        while (query.MoveNext(out var uid, out _, out var database, out var researchTeam))
        {
            if (!string.Equals(researchTeam.TeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            processedServers++;
            if (unlock)
            {
                if (research.IsTechnologyUnlocked(uid, technology.ID, database))
                    continue;

                research.AddTechnology(uid, technology, database);
                research.TrySetMainDiscipline(technology, uid, database);
                research.UpdateTechnologyCards(uid, database);
                changedServers++;
            }
            else
            {
                if (!research.TryRemoveTechnology((uid, database), technology))
                    continue;

                research.UpdateTechnologyCards(uid, database);
                changedServers++;
            }
        }

        if (processedServers == 0)
        {
            shell.WriteError($"No WH40K research servers found for team '{teamId}'.");
            return;
        }

        if (changedServers > 0)
            RefreshWh40KCargoOrderState();

        var verb = unlock ? "unlocked" : "locked";
        shell.WriteLine(
            $"Team '{teamId}': technology '{technology.ID}' {verb} on {changedServers}/{processedServers} research server(s).");
    }

    private void ExecuteCargoToggle(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string requestedTeamId, string cargoProductId, bool unlock)
    {
        if (!TryResolveCanonicalTeamId(rule, requestedTeamId, out var teamId))
        {
            WriteUnknownTeam(shell, rule, requestedTeamId);
            return;
        }

        if (!_proto.TryIndex<CargoProductPrototype>(cargoProductId, out var product))
        {
            shell.WriteError($"Unknown cargo product '{cargoProductId}'.");
            return;
        }

        if (!TryResolveCargoAccountForTeam(teamId, out var account))
        {
            shell.WriteError($"Failed to resolve cargo account for team '{teamId}'.");
            return;
        }

        var cargo = _entityManager.EntitySysManager.GetEntitySystem<CargoSystem>();
        var stations = 0;
        var changedStations = 0;
        var query = _entityManager.EntityQueryEnumerator<WH40KCargoProductUnlocksComponent>();
        while (query.MoveNext(out var uid, out var unlocks))
        {
            stations++;
            if (!unlocks.UnlockedProductsByAccount.TryGetValue(account, out var unlockedProducts))
            {
                unlockedProducts = new List<ProtoId<CargoProductPrototype>>();
                unlocks.UnlockedProductsByAccount[account] = unlockedProducts;
            }

            var changed = unlock
                ? TryAddCargoProduct(unlockedProducts, product.ID)
                : unlockedProducts.Remove(product.ID);

            if (!changed)
                continue;

            _entityManager.Dirty(uid, unlocks);
            cargo.RefreshOrderStateForStation(uid);
            changedStations++;
        }

        if (stations == 0)
        {
            shell.WriteError("No WH40K cargo unlock components found on stations.");
            return;
        }

        var verb = unlock ? "unlocked" : "locked";
        shell.WriteLine($"Team '{teamId}': cargo product '{product.ID}' {verb} on {changedStations}/{stations} station(s).");
    }

    private void RefreshWh40KCargoOrderState()
    {
        var cargo = _entityManager.EntitySysManager.GetEntitySystem<CargoSystem>();
        var query = _entityManager.EntityQueryEnumerator<WH40KCargoProductUnlocksComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            cargo.RefreshOrderStateForStation(uid);
        }
    }

    private static bool TryAddCargoProduct(List<ProtoId<CargoProductPrototype>> target, ProtoId<CargoProductPrototype> product)
    {
        if (target.Contains(product))
            return false;

        target.Add(product);
        return true;
    }

    private static bool TryResolveCargoAccountForTeam(string teamId, out ProtoId<CargoAccountPrototype> account)
    {
        account = default;

        if (string.Equals(teamId, "Imperium", StringComparison.OrdinalIgnoreCase))
        {
            account = "WH40KImperium";
            return true;
        }

        if (string.Equals(teamId, "Heretics", StringComparison.OrdinalIgnoreCase))
        {
            account = "WH40KHeretics";
            return true;
        }

        return false;
    }

    private static bool TryResolveCanonicalTeamId(
        WH40KTeamBattleRuleSystem rule,
        string inputTeamId,
        out string resolvedTeamId)
    {
        resolvedTeamId = string.Empty;
        foreach (var teamId in rule.GetTeamIds())
        {
            if (!string.Equals(teamId, inputTeamId, StringComparison.OrdinalIgnoreCase))
                continue;

            resolvedTeamId = teamId;
            return true;
        }

        return false;
    }

    private static void WriteUnknownTeam(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string inputTeamId)
    {
        var ids = rule.GetTeamIds();
        if (ids.Count == 0)
        {
            shell.WriteError("Active WH40K team-battle rule not found.");
            return;
        }

        shell.WriteError($"Unknown team id '{inputTeamId}'. Available: {string.Join(", ", ids)}");
    }
}

internal static class WH40KBattleAdminCommandShared
{
    public static void ExecutePhase(
        IConsoleShell shell,
        WH40KTeamBattleRuleSystem rule,
        string[] args,
        string usage,
        int argOffset)
    {
        if (args.Length != argOffset + 1)
        {
            shell.WriteError(usage);
            return;
        }

        if (!TryParsePhase(args[argOffset], out var phase))
        {
            shell.WriteError("Phase must be one of: preparation, assault, apocalypse.");
            return;
        }

        if (!rule.TrySetCurrentPhase(phase))
        {
            shell.WriteError("Active WH40K team-battle rule not found.");
            return;
        }

        shell.WriteLine($"WH40K phase set to: {phase}");
    }

    public static void ExecuteSetLevel(
        IConsoleShell shell,
        WH40KTeamBattleRuleSystem rule,
        string[] args,
        string usage,
        int argOffset)
    {
        if (args.Length != argOffset + 2)
        {
            shell.WriteError(usage);
            return;
        }

        if (!int.TryParse(args[argOffset + 1], out var requestedLevel))
        {
            shell.WriteError("Level must be an integer.");
            return;
        }

        if (!rule.TrySetTeamBaseLevel(args[argOffset], requestedLevel, out var teamId, out var level, out var frontPoints))
        {
            WriteTeamNotFound(shell, rule, args[argOffset]);
            return;
        }

        shell.WriteLine($"Team '{teamId}': base level set to {level}, XP now {frontPoints}.");
    }

    public static CompletionResult GetPhaseCompletion(string[] args, int argOffset)
    {
        if (args.Length == argOffset + 1)
            return CompletionResult.FromHintOptions(GetPhaseOptions(), "<phase>");

        return CompletionResult.Empty;
    }

    private static string[] GetPhaseOptions()
    {
        var phases = Enum.GetValues<WH40KBattlePhase>();
        var options = new string[phases.Length];
        for (var i = 0; i < phases.Length; i++)
        {
            options[i] = phases[i].ToString().ToLowerInvariant();
        }

        return options;
    }

    private static bool TryParsePhase(string value, out WH40KBattlePhase phase)
    {
        switch (value.ToLowerInvariant())
        {
            case "preparation":
            case "prep":
                phase = WH40KBattlePhase.Preparation;
                return true;

            case "assault":
                phase = WH40KBattlePhase.Assault;
                return true;

            case "apocalypse":
            case "apo":
                phase = WH40KBattlePhase.Apocalypse;
                return true;

            default:
                phase = WH40KBattlePhase.Preparation;
                return false;
        }
    }

    private static void WriteTeamNotFound(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string inputTeamId)
    {
        var ids = rule.GetTeamIds();
        if (ids.Count == 0)
        {
            shell.WriteError("Active WH40K team-battle rule not found.");
            return;
        }

        shell.WriteError($"Unknown team id '{inputTeamId}'. Available: {string.Join(", ", ids)}");
    }
}
