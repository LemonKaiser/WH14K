using System;
using System.Collections.Generic;

namespace Content.Shared._WH40K.Command;

public readonly record struct WH40KCommandNodeTacticPreset(
    string Id,
    string NameLocKey,
    string SummaryLocKey,
    string DescriptionLocKey);

public static class WH40KCommandNodeTactics
{
    public const string DefaultTacticId = "tactic_flexible_front";
    public const string IronDisciplineTacticId = "tactic_iron_discipline";
    public const string ConvoySupremacyTacticId = "tactic_convoy_supremacy";
    public const string SiegeProtocolTacticId = "tactic_siege_protocol";
    public const string MachineCultTacticId = "tactic_machine_cult";

    private static readonly Dictionary<string, string> LegacyTacticIdAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["codex_flexible_front"] = DefaultTacticId,
            ["codex_iron_discipline"] = IronDisciplineTacticId,
            ["codex_convoy_supremacy"] = ConvoySupremacyTacticId,
            ["codex_siege_protocol"] = SiegeProtocolTacticId,
            ["codex_machine_cult"] = MachineCultTacticId
        };

    private static readonly WH40KCommandNodeTacticPreset[] TacticPresets =
    {
        new(DefaultTacticId,
            "wh40k-command-node-battle-tactic-flexible-front-name",
            "wh40k-command-node-battle-tactic-flexible-front-summary",
            "wh40k-command-node-battle-tactic-flexible-front-description"),
        new(IronDisciplineTacticId,
            "wh40k-command-node-battle-tactic-iron-discipline-name",
            "wh40k-command-node-battle-tactic-iron-discipline-summary",
            "wh40k-command-node-battle-tactic-iron-discipline-description"),
        new(ConvoySupremacyTacticId,
            "wh40k-command-node-battle-tactic-convoy-supremacy-name",
            "wh40k-command-node-battle-tactic-convoy-supremacy-summary",
            "wh40k-command-node-battle-tactic-convoy-supremacy-description"),
        new(SiegeProtocolTacticId,
            "wh40k-command-node-battle-tactic-siege-protocol-name",
            "wh40k-command-node-battle-tactic-siege-protocol-summary",
            "wh40k-command-node-battle-tactic-siege-protocol-description"),
        new(MachineCultTacticId,
            "wh40k-command-node-battle-tactic-machine-cult-name",
            "wh40k-command-node-battle-tactic-machine-cult-summary",
            "wh40k-command-node-battle-tactic-machine-cult-description")
    };

    public static IReadOnlyList<WH40KCommandNodeTacticPreset> Presets => TacticPresets;

    public static WH40KCommandNodeTacticPreset FindOrDefault(string? tacticId)
    {
        var canonicalId = CanonicalizeTacticId(tacticId);
        if (!string.IsNullOrWhiteSpace(canonicalId))
        {
            foreach (var preset in TacticPresets)
            {
                if (string.Equals(preset.Id, canonicalId, StringComparison.OrdinalIgnoreCase))
                    return preset;
            }
        }

        return TacticPresets[0];
    }

    public static string CanonicalizeTacticId(string? tacticId)
    {
        if (string.IsNullOrWhiteSpace(tacticId))
            return string.Empty;

        return LegacyTacticIdAliases.TryGetValue(tacticId, out var alias) ? alias : tacticId;
    }
}
