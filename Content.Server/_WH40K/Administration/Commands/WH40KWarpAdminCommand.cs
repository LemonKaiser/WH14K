using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Server._WH40K.Psyker;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class WH40KWarpAdminCommand : LocalizedCommands
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private static readonly string[] Subcommands =
    {
        "status",
        "set",
        "add",
        "reset",
        "contribute",
        "backlash",
        "pulse",
        "catastrophe",
        "config",
        "control",
    };

    private static readonly string[] ConfigActions =
    {
        "list",
        "get",
        "set",
    };

    private static readonly string[] ControlActions =
    {
        "get",
        "set",
        "clear",
    };

    private static readonly string[] PulseTiers =
    {
        "auto",
        "500",
        "550",
        "600",
        "650",
        "700",
        "750",
        "800",
        "850",
        "900",
    };

    private static readonly string[] BacklashTiers =
    {
        "auto",
        "mild",
        "stun",
        "collapse",
        "drop",
        "bleed",
        "doppelganger",
        "flesh-rift",
        "possession",
        "mutation",
    };

    private static readonly string[] ControlFields =
    {
        "contribution-multiplier",
        "flat-bonus",
        "threshold-bias",
        "ignore-personal-backlash",
        "ignore-global-pulses",
        "ignore-catastrophe",
        "all",
    };

    private static readonly WarpSetting[] Settings =
    {
        BoolSetting("enabled", "Enable or disable the warp runtime.", CCVars.WH40KWarpEnabled),
        FloatSetting("max-instability", "Maximum shared warp instability.", CCVars.WH40KWarpMaxInstability),
        FloatSetting("decay", "Passive warp recovery per second.", CCVars.WH40KWarpDecayPerSecond),
        BoolSetting("personal-backlash", "Enable contributor backlash effects.", CCVars.WH40KWarpPersonalBacklashEnabled),
        BoolSetting("global-pulses", "Enable scheduled global warp pulses.", CCVars.WH40KWarpGlobalPulsesEnabled),
        BoolSetting("catastrophe", "Enable the max-instability catastrophe.", CCVars.WH40KWarpCatastropheEnabled),
        FloatSetting("highest-tier-chance", "Chance to pick the highest unlocked backlash tier.", CCVars.WH40KWarpHighestTierChance),
        FloatSetting("threshold-mild", "Backlash threshold for mild burn.", CCVars.WH40KWarpMildBacklashThreshold),
        FloatSetting("threshold-stun", "Backlash threshold for stun.", CCVars.WH40KWarpStunBacklashThreshold),
        FloatSetting("threshold-collapse", "Backlash threshold for collapse.", CCVars.WH40KWarpCollapseBacklashThreshold),
        FloatSetting("threshold-drop", "Backlash threshold for forced drops.", CCVars.WH40KWarpDropBacklashThreshold),
        FloatSetting("threshold-bleed", "Backlash threshold for bleed.", CCVars.WH40KWarpBleedBacklashThreshold),
        FloatSetting("threshold-doppelganger", "Backlash threshold for doppelganger spawn.", CCVars.WH40KWarpDoppelgangerBacklashThreshold),
        FloatSetting("threshold-flesh-rift", "Backlash threshold for flesh-rift outcome.", CCVars.WH40KWarpFleshRiftBacklashThreshold),
        FloatSetting("threshold-possession", "Backlash threshold for possession.", CCVars.WH40KWarpPossessionBacklashThreshold),
        FloatSetting("threshold-mutation", "Backlash threshold for irreversible mutation.", CCVars.WH40KWarpMutationBacklashThreshold),
        FloatSetting("pulse-500-threshold", "Threshold for the 500 pulse band.", CCVars.WH40KWarpPulse500Threshold),
        FloatSetting("pulse-550-threshold", "Threshold for the 550 pulse band.", CCVars.WH40KWarpPulse550Threshold),
        FloatSetting("pulse-600-threshold", "Threshold for the 600 pulse band.", CCVars.WH40KWarpPulse600Threshold),
        FloatSetting("pulse-650-threshold", "Threshold for the 650 pulse band.", CCVars.WH40KWarpPulse650Threshold),
        FloatSetting("pulse-700-threshold", "Threshold for the 700 pulse band.", CCVars.WH40KWarpPulse700Threshold),
        FloatSetting("pulse-750-threshold", "Threshold for the 750 pulse band.", CCVars.WH40KWarpPulse750Threshold),
        FloatSetting("pulse-800-threshold", "Threshold for the 800 pulse band.", CCVars.WH40KWarpPulse800Threshold),
        FloatSetting("pulse-850-threshold", "Threshold for the 850 pulse band.", CCVars.WH40KWarpPulse850Threshold),
        FloatSetting("pulse-900-threshold", "Threshold for the 900 pulse band.", CCVars.WH40KWarpPulse900Threshold),
        FloatSetting("pulse-500-interval", "Seconds between 500/550 pulses.", CCVars.WH40KWarpPulse500IntervalSeconds),
        FloatSetting("pulse-600-interval", "Seconds between 600/650 pulses.", CCVars.WH40KWarpPulse600IntervalSeconds),
        FloatSetting("pulse-700-interval", "Seconds between 700/750 pulses.", CCVars.WH40KWarpPulse700IntervalSeconds),
        FloatSetting("pulse-800-interval", "Seconds between 800/850 pulses.", CCVars.WH40KWarpPulse800IntervalSeconds),
        FloatSetting("pulse-900-interval", "Seconds between 900 pulses.", CCVars.WH40KWarpPulse900IntervalSeconds),
        FloatSetting("mild-burn-damage", "Heat damage for mild backlash.", CCVars.WH40KWarpMildBurnDamage),
        FloatSetting("stun-duration", "Stun duration for stun backlash.", CCVars.WH40KWarpStunDurationSeconds),
        FloatSetting("stun-drunkenness", "Drunkenness duration for stun backlash.", CCVars.WH40KWarpStunDrunkennessSeconds),
        FloatSetting("collapse-stun", "Stun duration for collapse backlash.", CCVars.WH40KWarpCollapseStunSeconds),
        FloatSetting("collapse-drunkenness", "Drunkenness duration for collapse backlash.", CCVars.WH40KWarpCollapseDrunkennessSeconds),
        FloatSetting("bleed-target", "Target bleed amount for bleed backlash.", CCVars.WH40KWarpBleedTarget),
        IntSetting("drop-max-count", "Maximum dropped items from drop backlash.", CCVars.WH40KWarpDropMaxCount),
        FloatSetting("flesh-rift-demon-chance", "Chance that flesh-rift creates a hellspawn outcome.", CCVars.WH40KWarpFleshRiftDemonChance),
        FloatSetting("flesh-rift-death-chance", "Chance that flesh-rift kills after demon roll fails.", CCVars.WH40KWarpFleshRiftDeathChance),
        FloatSetting("flesh-rift-death-damage", "Damage used by the lethal flesh-rift branch.", CCVars.WH40KWarpFleshRiftDeathDamage),
        FloatSetting("mutation-min-severity", "Minimum irreversible mutation severity.", CCVars.WH40KWarpMutationMinSeverity),
        FloatSetting("mutation-max-severity", "Maximum irreversible mutation severity.", CCVars.WH40KWarpMutationMaxSeverity),
    };

    public override string Command => "wh40kwarp";

    public override string Description => "WH40K warp runtime admin controls and live settings.";

    public override string Help =>
        "Usage:\n" +
        "wh40kwarp status\n" +
        "wh40kwarp set <instability>\n" +
        "wh40kwarp add <delta>\n" +
        "wh40kwarp reset\n" +
        "wh40kwarp contribute <user|entityUid> <amount> [sourceKey]\n" +
        "wh40kwarp backlash <user|entityUid> [auto|mild|stun|collapse|drop|bleed|doppelganger|flesh-rift|possession|mutation]\n" +
        "wh40kwarp pulse [auto|500|550|600|650|700|750|800|850|900]\n" +
        "wh40kwarp catastrophe [user|entityUid]\n" +
        "wh40kwarp config list\n" +
        "wh40kwarp config get <setting>\n" +
        "wh40kwarp config set <setting> <value>\n" +
        "wh40kwarp control get <user|entityUid>\n" +
        "wh40kwarp control set <user|entityUid> <field> <value>\n" +
        "wh40kwarp control clear <user|entityUid> <field|all>\n" +
        "User token supports exact username, user GUID, net entity UID, or 'self'/'me' from an in-game admin console.";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        var system = _entity.EntitySysManager.GetEntitySystem<WH40KGlobalWarpInstabilitySystem>();
        switch (args[0].ToLowerInvariant())
        {
            case "status":
            case "state":
                ExecuteStatus(shell, system, args);
                return;

            case "set":
                ExecuteSet(shell, system, args);
                return;

            case "add":
                ExecuteAdd(shell, system, args);
                return;

            case "reset":
            case "clear":
                ExecuteReset(shell, system, args);
                return;

            case "contribute":
                ExecuteContribute(shell, system, args);
                return;

            case "backlash":
                ExecuteBacklash(shell, system, args);
                return;

            case "pulse":
                ExecutePulse(shell, system, args);
                return;

            case "catastrophe":
            case "cat":
                ExecuteCatastrophe(shell, system, args);
                return;

            case "config":
            case "cfg":
                ExecuteConfig(shell, args);
                return;

            case "control":
            case "ctl":
                ExecuteControl(shell, args);
                return;

            default:
                shell.WriteError($"Unknown subcommand '{args[0]}'.");
                shell.WriteLine(Help);
                return;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(Subcommands, "<action>");
        }

        switch (args[0].ToLowerInvariant())
        {
            case "contribute":
            case "backlash":
            case "catastrophe":
            case "cat":
                return GetTargetedActionCompletion(args);

            case "pulse":
                if (args.Length == 2)
                    return CompletionResult.FromHintOptions(PulseTiers, "<tier>");
                return CompletionResult.Empty;

            case "config":
            case "cfg":
                return GetConfigCompletion(args);

            case "control":
            case "ctl":
                return GetControlCompletion(args);

            case "set":
            case "add":
                if (args.Length == 2)
                    return CompletionResult.FromHint("<number>");
                return CompletionResult.Empty;

            default:
                return CompletionResult.Empty;
        }
    }

    private void ExecuteStatus(IConsoleShell shell, WH40KGlobalWarpInstabilitySystem system, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Usage: wh40kwarp status");
            return;
        }

        shell.WriteLine(
            $"Warp: enabled={system.WarpEnabled}, personalBacklash={system.PersonalBacklashEnabled}, globalPulses={system.GlobalPulsesEnabled}, catastrophe={system.CatastropheEnabled}, catastropheTriggered={system.CatastropheTriggered}");
        shell.WriteLine(
            $"Instability: {FormatFloat(system.CurrentInstability)} / {FormatFloat(system.MaxInstability)}; decay={FormatFloat(system.DecayPerSecond)}/s; highestTierChance={FormatFloat(system.HighestTierChance)}");
        shell.WriteLine(
            $"Temporary effects: possessions={system.ActivePossessionCount}, hallucinations={system.ActiveHallucinationCount}");

        if (system.TryGetCurrentGlobalPulse(out var tierId, out var threshold, out var interval))
        {
            var nextDelay = system.GetNextGlobalPulseDelay();
            var nextText = nextDelay == null ? "not scheduled" : FormatTimeSpan(nextDelay.Value);
            shell.WriteLine(
                $"Current pulse tier: {tierId} (threshold={FormatFloat(threshold)}, interval={FormatTimeSpan(interval)}, next={nextText})");
        }
        else
        {
            shell.WriteLine("Current pulse tier: none at the current instability/settings.");
        }
    }

    private void ExecuteSet(IConsoleShell shell, WH40KGlobalWarpInstabilitySystem system, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Usage: wh40kwarp set <instability>");
            return;
        }

        if (!TryParseFloat(args[1], out var value))
        {
            shell.WriteError("Instability must be a number.");
            return;
        }

        system.AdminSetInstability(value);
        shell.WriteLine($"Warp instability set to {FormatFloat(system.CurrentInstability)}.");
    }

    private void ExecuteAdd(IConsoleShell shell, WH40KGlobalWarpInstabilitySystem system, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError("Usage: wh40kwarp add <delta>");
            return;
        }

        if (!TryParseFloat(args[1], out var delta))
        {
            shell.WriteError("Delta must be a number.");
            return;
        }

        system.AdminAddInstability(delta);
        shell.WriteLine($"Warp instability is now {FormatFloat(system.CurrentInstability)}.");
    }

    private void ExecuteReset(IConsoleShell shell, WH40KGlobalWarpInstabilitySystem system, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Usage: wh40kwarp reset");
            return;
        }

        system.AdminResetState();
        shell.WriteLine("Warp instability state, temporary effects, and pulse timers were reset.");
    }

    private void ExecuteContribute(IConsoleShell shell, WH40KGlobalWarpInstabilitySystem system, string[] args)
    {
        if (args.Length < 3 || args.Length > 4)
        {
            shell.WriteError("Usage: wh40kwarp contribute <user|entityUid> <amount> [sourceKey]");
            return;
        }

        if (!TryResolveTarget(shell, args[1], out var target))
            return;

        if (!TryParseFloat(args[2], out var amount))
        {
            shell.WriteError("Amount must be a number.");
            return;
        }

        var sourceKey = args.Length == 4 ? args[3] : "admin.command";
        system.AdminContribute(target, amount, sourceKey);
        shell.WriteLine(
            $"Contributed {FormatFloat(amount)} warp instability through {target} using source '{sourceKey}'. Current instability: {FormatFloat(system.CurrentInstability)}.");
    }

    private void ExecuteBacklash(IConsoleShell shell, WH40KGlobalWarpInstabilitySystem system, string[] args)
    {
        if (args.Length < 2 || args.Length > 3)
        {
            shell.WriteError("Usage: wh40kwarp backlash <user|entityUid> [auto|mild|stun|collapse|drop|bleed|doppelganger|flesh-rift|possession|mutation]");
            return;
        }

        if (!TryResolveTarget(shell, args[1], out var target))
            return;

        if (!TryParseBacklashTier(args.Length == 3 ? args[2] : "auto", out var tier))
        {
            shell.WriteError("Unknown backlash tier.");
            return;
        }

        if (!system.TryAdminForceBacklash(target, tier, out var reason))
        {
            shell.WriteError(reason);
            return;
        }

        shell.WriteLine($"Applied warp backlash '{reason}' to {target}.");
    }

    private void ExecutePulse(IConsoleShell shell, WH40KGlobalWarpInstabilitySystem system, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError("Usage: wh40kwarp pulse [auto|500|550|600|650|700|750|800|850|900]");
            return;
        }

        var requestedTier = args.Length == 2 ? args[1] : "auto";
        if (!system.TryAdminForceGlobalPulse(requestedTier, out var resolvedTier))
        {
            shell.WriteError("No global pulse tier is available for that request/current instability.");
            return;
        }

        shell.WriteLine($"Forced global warp pulse tier {resolvedTier}.");
    }

    private void ExecuteCatastrophe(IConsoleShell shell, WH40KGlobalWarpInstabilitySystem system, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteError("Usage: wh40kwarp catastrophe [user|entityUid]");
            return;
        }

        EntityUid? trigger = null;
        if (args.Length == 2)
        {
            if (!TryResolveTarget(shell, args[1], out var target))
                return;

            trigger = target;
        }

        system.AdminForceCatastrophe(trigger);
        shell.WriteLine("Forced warp catastrophe.");
    }

    private void ExecuteConfig(IConsoleShell shell, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Usage: wh40kwarp config <list|get|set> ...");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "list":
                if (args.Length != 2)
                {
                    shell.WriteError("Usage: wh40kwarp config list");
                    return;
                }

                foreach (var setting in Settings.OrderBy(setting => setting.Key, StringComparer.Ordinal))
                {
                    shell.WriteLine($"{setting.Key} = {setting.GetValue(_config)} [{setting.CVarName}] - {setting.Description}");
                }

                return;

            case "get":
                if (args.Length != 3)
                {
                    shell.WriteError("Usage: wh40kwarp config get <setting>");
                    return;
                }

                if (!TryGetSetting(args[2], out var getSetting))
                {
                    shell.WriteError($"Unknown warp setting '{args[2]}'.");
                    return;
                }

                shell.WriteLine($"{getSetting.Key} = {getSetting.GetValue(_config)} [{getSetting.CVarName}]");
                return;

            case "set":
                if (args.Length != 4)
                {
                    shell.WriteError("Usage: wh40kwarp config set <setting> <value>");
                    return;
                }

                if (!TryGetSetting(args[2], out var setSetting))
                {
                    shell.WriteError($"Unknown warp setting '{args[2]}'.");
                    return;
                }

                try
                {
                    setSetting.SetValue(_config, args[3]);
                }
                catch (FormatException ex)
                {
                    shell.WriteError(ex.Message);
                    return;
                }

                shell.WriteLine($"{setSetting.Key} set to {setSetting.GetValue(_config)}.");
                return;

            default:
                shell.WriteError($"Unknown config action '{args[1]}'.");
                return;
        }
    }

    private void ExecuteControl(IConsoleShell shell, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError("Usage: wh40kwarp control <get|set|clear> <user|entityUid> ...");
            return;
        }

        if (!TryResolveTarget(shell, args[2], out var target))
            return;

        switch (args[1].ToLowerInvariant())
        {
            case "get":
                if (args.Length != 3)
                {
                    shell.WriteError("Usage: wh40kwarp control get <user|entityUid>");
                    return;
                }

                WriteControlState(shell, target);
                return;

            case "set":
                if (args.Length != 5)
                {
                    shell.WriteError("Usage: wh40kwarp control set <user|entityUid> <field> <value>");
                    return;
                }

                if (!TryApplyControlField(shell, target, args[3], args[4]))
                    return;

                WriteControlState(shell, target);
                return;

            case "clear":
                if (args.Length != 4)
                {
                    shell.WriteError("Usage: wh40kwarp control clear <user|entityUid> <field|all>");
                    return;
                }

                if (!TryClearControlField(shell, target, args[3]))
                    return;

                WriteControlState(shell, target);
                return;

            default:
                shell.WriteError($"Unknown control action '{args[1]}'.");
                return;
        }
    }

    private CompletionResult GetTargetedActionCompletion(string[] args)
    {
        if (args.Length == 2)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user|entityUid>");

        if (args.Length == 3 && args[0].Equals("backlash", StringComparison.OrdinalIgnoreCase))
            return CompletionResult.FromHintOptions(BacklashTiers, "<tier>");

        if (args.Length == 3 && args[0].Equals("contribute", StringComparison.OrdinalIgnoreCase))
            return CompletionResult.FromHint("<amount>");

        if (args.Length == 4 && args[0].Equals("contribute", StringComparison.OrdinalIgnoreCase))
            return CompletionResult.FromHint("<sourceKey>");

        return CompletionResult.Empty;
    }

    private CompletionResult GetConfigCompletion(string[] args)
    {
        if (args.Length == 2)
            return CompletionResult.FromHintOptions(ConfigActions, "<action>");

        if (args.Length == 3 && (args[1].Equals("get", StringComparison.OrdinalIgnoreCase) || args[1].Equals("set", StringComparison.OrdinalIgnoreCase)))
        {
            return CompletionResult.FromHintOptions(
                Settings.OrderBy(setting => setting.Key, StringComparer.Ordinal)
                    .Select(setting => new CompletionOption(setting.Key, setting.Description)),
                "<setting>");
        }

        if (args.Length == 4 && args[1].Equals("set", StringComparison.OrdinalIgnoreCase) && TryGetSetting(args[2], out var setting))
            return CompletionResult.FromHint(setting.ValueHint);

        return CompletionResult.Empty;
    }

    private CompletionResult GetControlCompletion(string[] args)
    {
        if (args.Length == 2)
            return CompletionResult.FromHintOptions(ControlActions, "<action>");

        if (args.Length == 3)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(sorted: true, _players), "<user|entityUid>");

        if (args.Length == 4 && (args[1].Equals("set", StringComparison.OrdinalIgnoreCase) || args[1].Equals("clear", StringComparison.OrdinalIgnoreCase)))
            return CompletionResult.FromHintOptions(ControlFields, "<field>");

        if (args.Length == 5 && args[1].Equals("set", StringComparison.OrdinalIgnoreCase))
            return CompletionResult.FromHint("<value>");

        return CompletionResult.Empty;
    }

    private void WriteControlState(IConsoleShell shell, EntityUid target)
    {
        if (!_entity.EntityExists(target))
        {
            shell.WriteError($"Entity {target} no longer exists.");
            return;
        }

        var hasControl = _entity.TryGetComponent<WH40KWarpControlComponent>(target, out var control);
        control ??= new WH40KWarpControlComponent();

        shell.WriteLine(
            $"Warp control for {target}: componentPresent={hasControl}, contributionMultiplier={FormatFloat(control.ContributionMultiplier)}, flatBonus={FormatFloat(control.FlatContributionBonus)}, thresholdBias={FormatFloat(control.PersonalBacklashThresholdBias)}, ignorePersonalBacklash={control.IgnorePersonalBacklash}, ignoreGlobalPulses={control.IgnoreGlobalPulseEffects}, ignoreCatastrophe={control.IgnoreCatastropheSacrifice}");
    }

    private bool TryApplyControlField(IConsoleShell shell, EntityUid target, string field, string value)
    {
        var control = _entity.EnsureComponent<WH40KWarpControlComponent>(target);

        switch (NormalizeKey(field))
        {
            case "contributionmultiplier":
                if (!TryParseFloat(value, out var multiplier))
                {
                    shell.WriteError("Contribution multiplier must be a number.");
                    return false;
                }

                control.ContributionMultiplier = multiplier;
                return true;

            case "flatbonus":
                if (!TryParseFloat(value, out var flatBonus))
                {
                    shell.WriteError("Flat bonus must be a number.");
                    return false;
                }

                control.FlatContributionBonus = flatBonus;
                return true;

            case "thresholdbias":
                if (!TryParseFloat(value, out var thresholdBias))
                {
                    shell.WriteError("Threshold bias must be a number.");
                    return false;
                }

                control.PersonalBacklashThresholdBias = thresholdBias;
                return true;

            case "ignorepersonalbacklash":
                if (!TryParseBool(value, out var ignorePersonal))
                {
                    shell.WriteError("ignore-personal-backlash must be a boolean.");
                    return false;
                }

                control.IgnorePersonalBacklash = ignorePersonal;
                return true;

            case "ignoreglobalpulses":
                if (!TryParseBool(value, out var ignorePulses))
                {
                    shell.WriteError("ignore-global-pulses must be a boolean.");
                    return false;
                }

                control.IgnoreGlobalPulseEffects = ignorePulses;
                return true;

            case "ignorecatastrophe":
                if (!TryParseBool(value, out var ignoreCatastrophe))
                {
                    shell.WriteError("ignore-catastrophe must be a boolean.");
                    return false;
                }

                control.IgnoreCatastropheSacrifice = ignoreCatastrophe;
                return true;

            default:
                shell.WriteError($"Unknown control field '{field}'.");
                return false;
        }
    }

    private bool TryClearControlField(IConsoleShell shell, EntityUid target, string field)
    {
        var normalizedField = NormalizeKey(field);
        if (normalizedField == "all")
        {
            if (_entity.HasComponent<WH40KWarpControlComponent>(target))
                _entity.RemoveComponent<WH40KWarpControlComponent>(target);

            return true;
        }

        if (!_entity.TryGetComponent<WH40KWarpControlComponent>(target, out var control))
        {
            shell.WriteLine("Target has no warp control component; defaults are already active.");
            return true;
        }

        switch (normalizedField)
        {
            case "contributionmultiplier":
                control.ContributionMultiplier = 1f;
                break;

            case "flatbonus":
                control.FlatContributionBonus = 0f;
                break;

            case "thresholdbias":
                control.PersonalBacklashThresholdBias = 0f;
                break;

            case "ignorepersonalbacklash":
                control.IgnorePersonalBacklash = false;
                break;

            case "ignoreglobalpulses":
                control.IgnoreGlobalPulseEffects = false;
                break;

            case "ignorecatastrophe":
                control.IgnoreCatastropheSacrifice = false;
                break;

            default:
                shell.WriteError($"Unknown control field '{field}'.");
                return false;
        }

        if (IsDefaultControl(control))
            _entity.RemoveComponent<WH40KWarpControlComponent>(target);

        return true;
    }

    private bool TryResolveTarget(IConsoleShell shell, string token, out EntityUid target)
    {
        target = default;
        var normalized = token.Trim();

        if (string.Equals(normalized, "self", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "me", StringComparison.OrdinalIgnoreCase))
        {
            if (shell.Player?.AttachedEntity is not { Valid: true } attached)
            {
                shell.WriteError("'self'/'me' can only be used from an in-game admin session.");
                return false;
            }

            target = attached;
            return true;
        }

        if (NetEntity.TryParse(normalized, out var netEntity)
            && _entity.TryGetEntity(netEntity, out EntityUid? parsed)
            && parsed != null
            && _entity.EntityExists(parsed.Value))
        {
            target = parsed.Value;
            return true;
        }

        if (_players.TryGetSessionByUsername(normalized, out var byName))
        {
            if (byName.AttachedEntity is not { Valid: true } attached)
            {
                shell.WriteError($"Player '{normalized}' has no attached entity.");
                return false;
            }

            target = attached;
            return true;
        }

        if (Guid.TryParse(normalized, out var guid)
            && _players.TryGetSessionById(new NetUserId(guid), out var byId))
        {
            if (byId.AttachedEntity is not { Valid: true } attached)
            {
                shell.WriteError($"Player '{normalized}' has no attached entity.");
                return false;
            }

            target = attached;
            return true;
        }

        shell.WriteError($"Target '{token}' was not found.");
        return false;
    }

    private static bool TryGetSetting(string key, out WarpSetting setting)
    {
        var normalized = NormalizeKey(key);
        foreach (var candidate in Settings)
        {
            if (NormalizeKey(candidate.Key) != normalized)
                continue;

            setting = candidate;
            return true;
        }

        setting = null!;
        return false;
    }

    private static bool TryParseBacklashTier(string rawTier, out WH40KWarpBacklashTier? tier)
    {
        switch (NormalizeKey(rawTier))
        {
            case "":
            case "auto":
                tier = null;
                return true;

            case "mild":
            case "mildburn":
                tier = WH40KWarpBacklashTier.MildBurn;
                return true;

            case "stun":
                tier = WH40KWarpBacklashTier.Stun;
                return true;

            case "collapse":
                tier = WH40KWarpBacklashTier.Collapse;
                return true;

            case "drop":
                tier = WH40KWarpBacklashTier.Drop;
                return true;

            case "bleed":
                tier = WH40KWarpBacklashTier.Bleed;
                return true;

            case "doppelganger":
                tier = WH40KWarpBacklashTier.Doppelganger;
                return true;

            case "fleshrift":
                tier = WH40KWarpBacklashTier.FleshRift;
                return true;

            case "possession":
                tier = WH40KWarpBacklashTier.Possession;
                return true;

            case "mutation":
                tier = WH40KWarpBacklashTier.Mutation;
                return true;

            default:
                tier = null;
                return false;
        }
    }

    private static bool TryParseBool(string value, out bool parsed)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                parsed = true;
                return true;

            case "false":
            case "0":
            case "no":
            case "off":
                parsed = false;
                return true;

            default:
                parsed = false;
                return false;
        }
    }

    private static bool TryParseFloat(string value, out float parsed)
    {
        return float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed);
    }

    private static string NormalizeKey(string key)
    {
        return key.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static bool IsDefaultControl(WH40KWarpControlComponent control)
    {
        return Math.Abs(control.ContributionMultiplier - 1f) < 0.0001f
               && Math.Abs(control.FlatContributionBonus) < 0.0001f
               && Math.Abs(control.PersonalBacklashThresholdBias) < 0.0001f
               && !control.IgnorePersonalBacklash
               && !control.IgnoreGlobalPulseEffects
               && !control.IgnoreCatastropheSacrifice;
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatTimeSpan(TimeSpan value)
    {
        return value.ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
    }

    private static WarpSetting BoolSetting(string key, string description, CVarDef<bool> cvar)
    {
        return new WarpSetting(
            key,
            cvar.Name,
            description,
            "<bool>",
            config => config.GetCVar(cvar) ? "true" : "false",
            (config, value) =>
            {
                if (!TryParseBool(value, out var parsed))
                    throw new FormatException($"{key} expects a boolean value.");

                config.SetCVar(cvar, parsed);
            });
    }

    private static WarpSetting FloatSetting(string key, string description, CVarDef<float> cvar)
    {
        return new WarpSetting(
            key,
            cvar.Name,
            description,
            "<float>",
            config => FormatFloat(config.GetCVar(cvar)),
            (config, value) =>
            {
                if (!TryParseFloat(value, out var parsed))
                    throw new FormatException($"{key} expects a float value.");

                config.SetCVar(cvar, parsed);
            });
    }

    private static WarpSetting IntSetting(string key, string description, CVarDef<int> cvar)
    {
        return new WarpSetting(
            key,
            cvar.Name,
            description,
            "<int>",
            config => config.GetCVar(cvar).ToString(CultureInfo.InvariantCulture),
            (config, value) =>
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    throw new FormatException($"{key} expects an integer value.");

                config.SetCVar(cvar, parsed);
            });
    }

    private sealed class WarpSetting
    {
        public WarpSetting(
            string key,
            string cVarName,
            string description,
            string valueHint,
            Func<IConfigurationManager, string> getValue,
            Action<IConfigurationManager, string> setValue)
        {
            Key = key;
            CVarName = cVarName;
            Description = description;
            ValueHint = valueHint;
            GetValue = getValue;
            SetValue = setValue;
        }

        public string Key { get; }
        public string CVarName { get; }
        public string Description { get; }
        public string ValueHint { get; }
        public Func<IConfigurationManager, string> GetValue { get; }
        public Action<IConfigurationManager, string> SetValue { get; }
    }
}
