using System;
using System.Collections.Generic;
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
public sealed class WH40KBattleAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public string Command => "wh40kbattle";
    public string Description => "WH40K admin control for TeamXP, influence, team research, funds, strategic points, and unlocks.";
    public string Help =>
        "Usage:\n" +
        "wh40kbattle phase-set <preparation|assault|apocalypse>\n" +
        "wh40kbattle status [teamId]\n" +
        "wh40kbattle setlevel <teamId> <level>\n" +
        "wh40kbattle teamxp <teamId> <delta>\n" +
        "wh40kbattle frontpoint <teamId> <delta>\n" +
        "wh40kbattle influencepoint <teamId> <delta>\n" +
        "wh40kbattle researchpoint <teamId> <delta>\n" +
        "wh40kbattle fund <teamId> <delta>\n" +
        "wh40kbattle point-list\n" +
        "wh40kbattle point-reset <pointUid>\n" +
        "wh40kbattle point-set-owner <pointUid> <teamId>\n" +
        "wh40kbattle point-set-tier <pointUid> <0|1|2|3>\n" +
        "wh40kbattle eco-telemetry <on|off> [intervalSeconds]\n" +
        "wh40kbattle tech-unlock <teamId> <technologyId>\n" +
        "wh40kbattle tech-lock <teamId> <technologyId>\n" +
        "wh40kbattle cargo-unlock <teamId> <cargoProductId>\n" +
        "wh40kbattle cargo-lock <teamId> <cargoProductId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        var action = args[0].ToLowerInvariant();

        switch (action)
        {
            case "status":
            case "state":
                ExecuteStatus(shell, rule, args);
                return;

            case "phase-set":
            case "phase":
                WH40KBattleAdminCommandShared.ExecutePhase(shell, rule, args, "Usage: wh40kbattle phase-set <phase>", 1);
                return;

            case "setlevel":
                WH40KBattleAdminCommandShared.ExecuteSetLevel(shell, rule, args, "Usage: wh40kbattle setlevel <teamId> <level>", 1);
                return;

            case "teamxp":
            case "xp":
            case "fronpoint":
            case "frontpoint":
            case "front":
                WH40KBattleAdminCommandShared.ExecuteFrontPoint(shell, rule, args, "Usage: wh40kbattle frontpoint <teamId> <delta>", 1);
                return;

            case "influencepoint":
            case "influence":
            case "ip":
            case "commandpoint":
            case "command":
            case "cmd":
            case "cp":
                WH40KBattleAdminCommandShared.ExecuteCommandPoint(shell, rule, args, "Usage: wh40kbattle commandpoint <teamId> <delta>", 1);
                return;

            case "researchpoint":
            case "rppoint":
            case "rp":
                ExecuteResearchPoint(shell, rule, args);
                return;

            case "fund":
            case "funds":
            case "money":
                ExecuteFund(shell, rule, args);
                return;

            case "point-list":
            case "points":
                ExecutePointList(shell);
                return;

            case "point-reset":
                ExecutePointReset(shell, args);
                return;

            case "point-set-owner":
                ExecutePointSetOwner(shell, rule, args);
                return;

            case "point-set-tier":
                ExecutePointSetTier(shell, args);
                return;

            case "eco-telemetry":
            case "telemetry":
                ExecuteEconomyTelemetry(shell, rule, args);
                return;

            case "research-unlock":
            case "tech-unlock":
            case "tech-add":
                ExecuteTechnologyUnlock(shell, rule, args);
                return;

            case "research-lock":
            case "tech-lock":
            case "tech-remove":
                ExecuteTechnologyLock(shell, rule, args);
                return;

            case "cargo-unlock":
            case "cargo-add":
                ExecuteCargoUnlock(shell, rule, args);
                return;

            case "cargo-lock":
            case "cargo-remove":
                ExecuteCargoLock(shell, rule, args);
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
            "phase-set",
            "status",
            "setlevel",
            "teamxp",
            "fronpoint",
            "frontpoint",
            "influencepoint",
            "researchpoint",
            "fund",
            "point-list",
            "point-reset",
            "point-set-owner",
            "point-set-tier",
            "eco-telemetry",
            "tech-unlock",
            "tech-lock",
            "cargo-unlock",
            "cargo-lock"
        };

        if (args.Length == 1)
            return CompletionResult.FromHintOptions(subcommands, "<action>");

        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        var action = args[0].ToLowerInvariant();
        switch (action)
        {
            case "phase-set":
            case "phase":
                return WH40KBattleAdminCommandShared.GetPhaseCompletion(args, 1);

            case "status":
            case "state":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
                return CompletionResult.Empty;

            case "setlevel":
            case "teamxp":
            case "xp":
            case "fronpoint":
            case "frontpoint":
            case "front":
            case "influencepoint":
            case "influence":
            case "ip":
            case "commandpoint":
            case "command":
            case "cmd":
            case "cp":
            case "researchpoint":
            case "rppoint":
            case "rp":
            case "fund":
            case "funds":
            case "money":
                return WH40KBattleAdminCommandShared.GetTeamAndValueCompletion(_entityManager, args, 1);

            case "point-reset":
                if (args.Length == 2)
                    return CompletionResult.FromHint("<pointUid>");
                return CompletionResult.Empty;

            case "point-set-tier":
                if (args.Length == 2)
                    return CompletionResult.FromHint("<pointUid>");
                if (args.Length == 3)
                    return CompletionResult.FromHintOptions(new[] { "0", "1", "2", "3" }, "<tier>");
                return CompletionResult.Empty;

            case "point-set-owner":
                if (args.Length == 2)
                    return CompletionResult.FromHint("<pointUid>");
                if (args.Length == 3)
                    return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
                return CompletionResult.Empty;

            case "eco-telemetry":
            case "telemetry":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(new[] { "on", "off" }, "<on|off>");
                if (args.Length == 3)
                    return CompletionResult.FromHint("<intervalSeconds>");
                return CompletionResult.Empty;

            case "research-unlock":
            case "tech-unlock":
            case "tech-add":
            case "research-lock":
            case "tech-lock":
            case "tech-remove":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
                if (args.Length == 3)
                    return CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<TechnologyPrototype>(proto: _proto), "<technologyId>");
                return CompletionResult.Empty;

            case "cargo-unlock":
            case "cargo-add":
            case "cargo-lock":
            case "cargo-remove":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
                if (args.Length == 3)
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
            $"Team '{snapshot.TeamId}': level={snapshot.BaseLevel}, teamXP={snapshot.TeamXp}, toNext={toNextText}, influence={snapshot.Influence}, " +
            $"funds={snapshot.Funds}, teamResearch={snapshot.ResearchPoints}, serverResearch={serverResearchPoints}, " +
            $"unlockedTechs={unlockedTechs.Count}, researchServers={researchServers}, activeResearch={activeText}");
    }

    private void ExecuteResearchPoint(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError("Usage: wh40kbattle researchpoint <teamId> <delta>");
            return;
        }

        if (!int.TryParse(args[2], out var delta))
        {
            shell.WriteError("Delta must be an integer (use negative to subtract).");
            return;
        }

        if (!TryResolveCanonicalTeamId(rule, args[1], out var teamId))
        {
            WriteUnknownTeam(shell, rule, args[1]);
            return;
        }

        if (!rule.TryAdjustTeamResearchPoints(teamId, delta, out var resolvedTeamId, out var points, source: "admin"))
        {
            WriteUnknownTeam(shell, rule, args[1]);
            return;
        }

        shell.WriteLine($"Team '{resolvedTeamId}': team research adjusted by {delta}, total now {points}.");
    }

    private void ExecuteFund(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError("Usage: wh40kbattle fund <teamId> <delta>");
            return;
        }

        if (!int.TryParse(args[2], out var delta))
        {
            shell.WriteError("Delta must be an integer (use negative to subtract).");
            return;
        }

        if (!TryResolveCanonicalTeamId(rule, args[1], out var teamId))
        {
            WriteUnknownTeam(shell, rule, args[1]);
            return;
        }

        if (!TryAdjustTeamFunds(teamId, delta, out var funds))
        {
            shell.WriteError($"Failed to resolve cargo funds for team '{teamId}'.");
            return;
        }

        shell.WriteLine($"Team '{teamId}': funds adjusted by {delta}, total now {funds}.");
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

    private void ExecutePointReset(IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Usage: wh40kbattle point-reset <pointUid>");
            return;
        }

        if (!TryResolvePointEntity(args[1], out var targetUid))
        {
            shell.WriteError($"Could not resolve point entity '{args[1]}'.");
            return;
        }

        var strategicPoints = _entityManager.EntitySysManager.GetEntitySystem<WH40KStrategicPointSystem>();
        if (!strategicPoints.TryAdminResetPoint(targetUid, out var error))
        {
            shell.WriteError(error);
            return;
        }

        shell.WriteLine($"Strategic point '{args[1]}' reset to T0.");
    }

    private void ExecutePointSetOwner(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError("Usage: wh40kbattle point-set-owner <pointUid> <teamId>");
            return;
        }

        if (!TryResolvePointEntity(args[1], out var targetUid))
        {
            shell.WriteError($"Could not resolve point entity '{args[1]}'.");
            return;
        }

        var requestedOwner = args[2];
        var strategicPoints = _entityManager.EntitySysManager.GetEntitySystem<WH40KStrategicPointSystem>();
        if (string.Equals(requestedOwner, "neutral", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestedOwner, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (!strategicPoints.TryAdminResetPoint(targetUid, out var resetError))
            {
                shell.WriteError(resetError);
                return;
            }

            shell.WriteLine($"Strategic point '{args[1]}' reset to T0 (neutral owner requested).");
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

        shell.WriteLine($"Strategic point '{args[1]}' owner set to '{resolvedTeamId}'.");
    }

    private void ExecutePointSetTier(IConsoleShell shell, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError("Usage: wh40kbattle point-set-tier <pointUid> <0|1|2|3>");
            return;
        }

        if (!TryResolvePointEntity(args[1], out var targetUid))
        {
            shell.WriteError($"Could not resolve point entity '{args[1]}'.");
            return;
        }

        if (!int.TryParse(args[2], out var tierValue) || tierValue < 0 || tierValue > 3)
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

        shell.WriteLine($"Strategic point '{args[1]}' forced to T{tierValue}.");
    }

    private void ExecuteEconomyTelemetry(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        if (args.Length != 2 && args.Length != 3)
        {
            shell.WriteError("Usage: wh40kbattle eco-telemetry <on|off> [intervalSeconds]");
            return;
        }

        var value = args[1];
        var enabled = value.ToLowerInvariant() switch
        {
            "on" or "true" or "1" => true,
            "off" or "false" or "0" => false,
            _ => (bool?) null
        };

        if (enabled == null)
        {
            shell.WriteError("Value must be 'on' or 'off'.");
            return;
        }

        if (args.Length == 3)
        {
            if (!float.TryParse(args[2], out var intervalSeconds))
            {
                shell.WriteError("Interval must be a number of seconds.");
                return;
            }

            rule.SetEconomyTelemetrySnapshotIntervalSeconds(intervalSeconds);
        }

        rule.SetEconomyTelemetryTrace(enabled.Value);
        rule.GetEconomyTelemetrySettings(out var currentEnabled, out var interval);
        shell.WriteLine($"Economy telemetry {(currentEnabled ? "enabled" : "disabled")}, snapshot interval {interval:0.#}s.");
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

    private void ExecuteTechnologyUnlock(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        ExecuteTechnologyToggle(shell, rule, args, unlock: true);
    }

    private void ExecuteTechnologyLock(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        ExecuteTechnologyToggle(shell, rule, args, unlock: false);
    }

    private void ExecuteTechnologyToggle(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args, bool unlock)
    {
        var actionName = unlock ? "tech-unlock" : "tech-lock";
        if (args.Length != 3)
        {
            shell.WriteError($"Usage: wh40kbattle {actionName} <teamId> <technologyId>");
            return;
        }

        if (!TryResolveCanonicalTeamId(rule, args[1], out var teamId))
        {
            WriteUnknownTeam(shell, rule, args[1]);
            return;
        }

        if (!_proto.TryIndex<TechnologyPrototype>(args[2], out var technology))
        {
            shell.WriteError($"Unknown technology '{args[2]}'.");
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

    private void ExecuteCargoUnlock(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        ExecuteCargoToggle(shell, rule, args, unlock: true);
    }

    private void ExecuteCargoLock(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args)
    {
        ExecuteCargoToggle(shell, rule, args, unlock: false);
    }

    private void ExecuteCargoToggle(IConsoleShell shell, WH40KTeamBattleRuleSystem rule, string[] args, bool unlock)
    {
        var actionName = unlock ? "cargo-unlock" : "cargo-lock";
        if (args.Length != 3)
        {
            shell.WriteError($"Usage: wh40kbattle {actionName} <teamId> <cargoProductId>");
            return;
        }

        if (!TryResolveCanonicalTeamId(rule, args[1], out var teamId))
        {
            WriteUnknownTeam(shell, rule, args[1]);
            return;
        }

        if (!_proto.TryIndex<CargoProductPrototype>(args[2], out var product))
        {
            shell.WriteError($"Unknown cargo product '{args[2]}'.");
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

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KPhaseSetAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "phase-set";
    public string Description => "Set WH40K battle phase.";
    public string Help => "Usage: phase-set <preparation|assault|apocalypse>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        WH40KBattleAdminCommandShared.ExecutePhase(shell, rule, args, Help, 0);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return WH40KBattleAdminCommandShared.GetPhaseCompletion(args, 0);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KSetLevelAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "setlevel";
    public string Description => "Set WH40K team base level.";
    public string Help => "Usage: setlevel <teamId> <level>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        WH40KBattleAdminCommandShared.ExecuteSetLevel(shell, rule, args, Help, 0);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return WH40KBattleAdminCommandShared.GetTeamAndValueCompletion(_entityManager, args, 0);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KFrontPointAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "frontpoint";
    public string Description => "Legacy alias: add/subtract WH40K team TeamXP.";
    public string Help => "Usage: frontpoint <teamId> <delta>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        WH40KBattleAdminCommandShared.ExecuteFrontPoint(shell, rule, args, Help, 0);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return WH40KBattleAdminCommandShared.GetTeamAndValueCompletion(_entityManager, args, 0);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KFronPointAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "fronpoint";
    public string Description => "Alias for frontpoint.";
    public string Help => "Usage: fronpoint <teamId> <delta>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        WH40KBattleAdminCommandShared.ExecuteFrontPoint(shell, rule, args, Help, 0);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return WH40KBattleAdminCommandShared.GetTeamAndValueCompletion(_entityManager, args, 0);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KCommandPointAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "commandpoint";
    public string Description => "Legacy alias: add/subtract WH40K team influence points.";
    public string Help => "Usage: commandpoint <teamId> <delta>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        WH40KBattleAdminCommandShared.ExecuteCommandPoint(shell, rule, args, Help, 0);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return WH40KBattleAdminCommandShared.GetTeamAndValueCompletion(_entityManager, args, 0);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KInfluencePointAdminCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "influencepoint";
    public string Description => "Add/subtract WH40K team influence points.";
    public string Help => "Usage: influencepoint <teamId> <delta>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var rule = _entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
        WH40KBattleAdminCommandShared.ExecuteCommandPoint(shell, rule, args, Help, 0);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return WH40KBattleAdminCommandShared.GetTeamAndValueCompletion(_entityManager, args, 0);
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

        shell.WriteLine($"Team '{teamId}': base level set to {level}, TeamXP now {frontPoints}.");
    }

    public static void ExecuteFrontPoint(
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

        if (!int.TryParse(args[argOffset + 1], out var delta))
        {
            shell.WriteError("Delta must be an integer (use negative to subtract).");
            return;
        }

        if (!rule.TryAdjustTeamFrontPoints(args[argOffset], delta, out var teamId, out var frontPoints, out var level, source: "admin"))
        {
            WriteTeamNotFound(shell, rule, args[argOffset]);
            return;
        }

        shell.WriteLine($"Team '{teamId}': TeamXP {frontPoints}, base level {level}.");
    }

    public static void ExecuteCommandPoint(
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

        if (!int.TryParse(args[argOffset + 1], out var delta))
        {
            shell.WriteError("Delta must be an integer (use negative to subtract).");
            return;
        }

        if (!rule.TryAdjustTeamCommandPoints(args[argOffset], delta, out var teamId, out var commandPoints, source: "admin"))
        {
            WriteTeamNotFound(shell, rule, args[argOffset]);
            return;
        }

        shell.WriteLine($"Team '{teamId}': influence points {commandPoints}.");
    }

    public static CompletionResult GetPhaseCompletion(string[] args, int argOffset)
    {
        if (args.Length == argOffset + 1)
            return CompletionResult.FromHintOptions(GetPhaseOptions(), "<phase>");

        return CompletionResult.Empty;
    }

    public static CompletionResult GetTeamAndValueCompletion(IEntityManager entityManager, string[] args, int argOffset)
    {
        if (args.Length == argOffset + 1)
        {
            var rule = entityManager.EntitySysManager.GetEntitySystem<WH40KTeamBattleRuleSystem>();
            return CompletionResult.FromHintOptions(rule.GetTeamIds(), "<teamId>");
        }

        if (args.Length == argOffset + 2)
            return CompletionResult.FromHint("<value>");

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
