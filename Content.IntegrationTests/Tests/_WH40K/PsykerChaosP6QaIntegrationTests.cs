#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Content.Shared._WH40K.Psyker;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class PsykerChaosP6QaIntegrationTests
{
    // Chaos progression contract is decoupled from psyker cast-XP fields.
    // Keep deterministic anti-spam profile local to this QA gate.
    private const float ChaosCastXpBase = 4f;
    private static readonly TimeSpan ChaosCastRepeatWindow = TimeSpan.FromSeconds(12);
    private const float ChaosCastRepeatFalloff = 0.75f;
    private const float ChaosCastMinMultiplier = 0.25f;

    private static readonly HashSet<string> AllowedReuseParents = new(StringComparer.Ordinal)
    {
        "ActionRepulse",
        "ActionBlink",
        "ActionKnock",
        "ActionFireball",
        "ActionFireballII",
        "ActionSmoke",
        "ActionVoidApplause",
        "ActionForceWall",
        "ActionMindSwap",
    };

    private static readonly HashSet<string> AllowedCustomStarterActions = new(StringComparer.Ordinal)
    {
        // Forward-only instant leap; current baseline action parents are target-driven and do not match this behavior.
        "ActionWH40KChaosKhorneExecutionStep",
    };

    private static readonly WH40KChaosPatron[] ChaosPatrons =
    {
        WH40KChaosPatron.Undivided,
        WH40KChaosPatron.Khorne,
        WH40KChaosPatron.Nurgle,
        WH40KChaosPatron.Slaanesh,
        WH40KChaosPatron.Tzeentch,
    };

    [Test]
    public void P6AntiDupStarterActionsReuseBaselineAndKeepRoleGates()
    {
        var psykerLoadout = new WH40KPsykerStarterActionLoadoutComponent();
        var chaosLoadout = new WH40KChaosGiftStarterActionLoadoutComponent();

        var psykerActionIds = EnumeratePsykerActionIds(psykerLoadout).ToHashSet(StringComparer.Ordinal);
        var chaosActionIds = EnumerateChaosActionIds(chaosLoadout).ToHashSet(StringComparer.Ordinal);
        var actionPack = LoadActionPackEntries();

        Assert.That(psykerActionIds.Overlaps(chaosActionIds), Is.False,
            "Psyker and chaos starter action pools must stay disjoint.");

        Assert.Multiple(() =>
        {
            foreach (var actionId in psykerActionIds)
            {
                ValidateActionEntry(
                    actionPack,
                    actionId,
                    expectPsykerRole: true,
                    expectChaosRole: false);
            }

            foreach (var actionId in chaosActionIds)
            {
                ValidateActionEntry(
                    actionPack,
                    actionId,
                    expectPsykerRole: false,
                    expectChaosRole: true);
            }
        });
    }

    [Test]
    public void P6ProgressionPacingScenariosHaveExpectedOrdering()
    {
        var duration = TimeSpan.FromMinutes(120);
        var halfDuration = TimeSpan.FromMinutes(60);

        var psyker = new WH40KPsykerProgressionComponent();
        var chaos = new WH40KChaosGiftProgressionComponent();
        var altar = new WH40KChaosAltarComponent();
        var skrizhal = new WH40KChaosSkrizhalComponent();

        var sleepOnlyXp = SimulatePsykerSleepXp(duration, onBed: true, psyker);
        var castOnlyXp = SimulatePsykerCastXp(duration, TimeSpan.FromSeconds(10), rotatingActions: true, psyker);
        var hybridXp = SimulatePsykerSleepXp(halfDuration, onBed: true, psyker) +
                       SimulatePsykerCastXp(halfDuration, TimeSpan.FromSeconds(10), rotatingActions: true, psyker);
        var ritualHeavyChaosXp = SimulateChaosRitualHeavyXp(duration, TimeSpan.FromSeconds(10), chaos, altar, skrizhal);

        var psykerSleepLevel = ResolveLevelFromXp(sleepOnlyXp, psyker.BaseXpForNextLevel, psyker.XpGrowthFactor, psyker.MaxLevel);
        var psykerCastLevel = ResolveLevelFromXp(castOnlyXp, psyker.BaseXpForNextLevel, psyker.XpGrowthFactor, psyker.MaxLevel);
        var chaosRitualLevel = ResolveLinearLevelFromXp(ritualHeavyChaosXp, chaos.XpPerLevelStep, chaos.MaxLevel);

        var chaosToCastRatio = ritualHeavyChaosXp / Math.Max(1f, castOnlyXp);

        Assert.Multiple(() =>
        {
            Assert.That(sleepOnlyXp, Is.GreaterThan(0f));
            Assert.That(castOnlyXp, Is.GreaterThan(0f));
            Assert.That(hybridXp, Is.GreaterThan(0f));
            Assert.That(ritualHeavyChaosXp, Is.GreaterThan(0f));

            Assert.That(hybridXp, Is.GreaterThan(sleepOnlyXp),
                "Hybrid pacing should outperform sleep-only pacing in 120-minute profile.");
            Assert.That(hybridXp, Is.LessThan(castOnlyXp),
                "Hybrid pacing should stay below dedicated cast-only pacing in 120-minute profile.");
            Assert.That(castOnlyXp, Is.GreaterThan(sleepOnlyXp),
                "Dedicated cast usage should outperform pure sleep progression.");

            Assert.That(ritualHeavyChaosXp, Is.GreaterThan(castOnlyXp),
                "Ritual-heavy chaos path should outpace Imperium cast-only path in this baseline profile.");
            Assert.That(chaosToCastRatio, Is.GreaterThan(1.2f).And.LessThan(4.0f),
                "Chaos ritual acceleration ratio is outside sanity envelope for first P6 baseline.");

            Assert.That(psykerSleepLevel, Is.LessThanOrEqualTo(psykerCastLevel));
            Assert.That(psykerCastLevel, Is.LessThanOrEqualTo(chaosRitualLevel));
        });
    }

    [Test]
    public void P6InstabilitySustainedCombatStaysWithinSanityEnvelope()
    {
        var instability = new WH40KWarpInstabilityComponent();
        var duration = TimeSpan.FromMinutes(120);

        var result = SimulateInstabilityProfile(
            duration,
            castInterval: TimeSpan.FromSeconds(12),
            instabilityPerCast: 16f,
            decayPerSecond: instability.DecayPerSecond,
            maxInstability: instability.MaxInstability);

        Assert.Multiple(() =>
        {
            Assert.That(result.MinObserved, Is.GreaterThanOrEqualTo(0f));
            Assert.That(result.MaxObserved, Is.LessThanOrEqualTo(instability.MaxInstability + 0.001f));

            Assert.That(result.CriticalFraction, Is.GreaterThan(0.05f),
                "Critical instability band should appear under sustained combat profile.");
            Assert.That(result.CriticalFraction, Is.LessThan(0.95f),
                "Critical instability band should not be permanent under sustained combat profile.");

            Assert.That(result.MeanFraction, Is.GreaterThan(0.2f).And.LessThan(0.9f),
                "Mean instability fraction is outside sanity envelope.");
        });
    }

    [Test]
    public void P6120MActionParityBetweenPsykerAndChaosPathsIsBounded()
    {
        var psykerLoadout = new WH40KPsykerStarterActionLoadoutComponent();
        var chaosLoadout = new WH40KChaosGiftStarterActionLoadoutComponent();

        Assert.Multiple(() =>
        {
            foreach (var level in Enumerable.Range(1, 10))
            {
                var psykerCount = CountPsykerActionsAtLevel(psykerLoadout, level);
                Assert.That(psykerCount, Is.GreaterThan(0), $"Psyker action count must be positive at level {level}.");

                foreach (var patron in ChaosPatrons)
                {
                    var chaosCount = CountChaosActionsAtLevel(chaosLoadout, patron, level);
                    var delta = Math.Abs(psykerCount - chaosCount);
                    Assert.That(delta, Is.LessThanOrEqualTo(1),
                        $"Action-count parity drift too large at level {level} for patron {patron} (psyker={psykerCount}, chaos={chaosCount}).");
                }
            }

            var level1Psyker = CountPsykerActionsAtLevel(psykerLoadout, 1);
            var level10Psyker = CountPsykerActionsAtLevel(psykerLoadout, 10);
            var level1Chaos = CountChaosActionsAtLevel(chaosLoadout, WH40KChaosPatron.Tzeentch, 1);
            var level10Chaos = CountChaosActionsAtLevel(chaosLoadout, WH40KChaosPatron.Tzeentch, 10);

            Assert.That(level1Psyker, Is.EqualTo(level1Chaos),
                "Entry-level action access must stay symmetric between paths.");
            Assert.That(level10Psyker, Is.EqualTo(level10Chaos),
                "End-level action access must stay symmetric between paths.");
        });
    }

    private static IEnumerable<string> EnumeratePsykerActionIds(WH40KPsykerStarterActionLoadoutComponent loadout)
    {
        return loadout.StarterActions
            .Concat(loadout.ScaledActions.Select(x => x.ActionPrototype))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateChaosActionIds(WH40KChaosGiftStarterActionLoadoutComponent loadout)
    {
        return loadout.BaseActions
            .Concat(loadout.BaseScaledActions.Select(x => x.ActionPrototype))
            .Concat(loadout.UndividedActions)
            .Concat(loadout.UndividedScaledActions.Select(x => x.ActionPrototype))
            .Concat(loadout.KhorneActions)
            .Concat(loadout.KhorneScaledActions.Select(x => x.ActionPrototype))
            .Concat(loadout.NurgleActions)
            .Concat(loadout.NurgleScaledActions.Select(x => x.ActionPrototype))
            .Concat(loadout.SlaaneshActions)
            .Concat(loadout.SlaaneshScaledActions.Select(x => x.ActionPrototype))
            .Concat(loadout.TzeentchActions)
            .Concat(loadout.TzeentchScaledActions.Select(x => x.ActionPrototype))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal);
    }

    private static Dictionary<string, ActionPackEntry> LoadActionPackEntries()
    {
        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        var actionPackPath = Path.Combine(root, "Resources", "Prototypes", "_WH40K", "Actions", "psyker_chaos.yml");
        Assert.That(File.Exists(actionPackPath), Is.True, $"Action pack not found: {actionPackPath}");

        var entries = new Dictionary<string, ActionPackEntry>(StringComparer.Ordinal);
        ActionPackEntry? current = null;
        string? currentComponentType = null;

        foreach (var raw in File.ReadLines(actionPackPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("- type: entity", StringComparison.Ordinal))
            {
                if (current is { Id.Length: > 0 })
                    entries[current.Id] = current;

                current = new ActionPackEntry();
                currentComponentType = null;
                continue;
            }

            if (current == null)
                continue;

            if (TryParseScalar(line, "id", out var id))
            {
                current.Id = id;
                continue;
            }

            if (TryParseScalar(line, "parent", out var parent))
            {
                current.Parent = parent;
                continue;
            }

            if (line.StartsWith("- type:", StringComparison.Ordinal))
            {
                currentComponentType = line["- type:".Length..].Trim();
                if (string.Equals(currentComponentType, "Action", StringComparison.Ordinal))
                    current.HasActionComponent = true;

                if (string.Equals(currentComponentType, "WH40KWarpActionCost", StringComparison.Ordinal))
                    current.HasWarpCostComponent = true;

                continue;
            }

            if (!string.Equals(currentComponentType, "WH40KWarpActionCost", StringComparison.Ordinal))
                continue;

            if (TryParseScalar(line, "requireWarpRole", out var requireRole) &&
                bool.TryParse(requireRole, out var requireRoleValue))
            {
                current.RequireWarpRole = requireRoleValue;
                continue;
            }

            if (TryParseScalar(line, "allowPsykerRole", out var allowPsyker) &&
                bool.TryParse(allowPsyker, out var allowPsykerValue))
            {
                current.AllowPsykerRole = allowPsykerValue;
                continue;
            }

            if (TryParseScalar(line, "allowChaosRole", out var allowChaos) &&
                bool.TryParse(allowChaos, out var allowChaosValue))
            {
                current.AllowChaosRole = allowChaosValue;
                continue;
            }

            if (TryParseScalar(line, "warpChargeCost", out var chargeCost) &&
                float.TryParse(chargeCost, NumberStyles.Float, CultureInfo.InvariantCulture, out var chargeCostValue))
            {
                current.WarpChargeCost = chargeCostValue;
                continue;
            }

            if (TryParseScalar(line, "instabilityGain", out var instabilityGain) &&
                float.TryParse(instabilityGain, NumberStyles.Float, CultureInfo.InvariantCulture, out var instabilityGainValue))
            {
                current.InstabilityGain = instabilityGainValue;
            }
        }

        if (current is { Id.Length: > 0 })
            entries[current.Id] = current;

        return entries;
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            var probe = Path.Combine(directory.FullName, "Resources", "Prototypes", "_WH40K", "Actions", "psyker_chaos.yml");
            if (File.Exists(probe))
                return directory.FullName;

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate repository root for psyker/chaos action pack parsing.");
        return startDirectory;
    }

    private static bool TryParseScalar(string line, string key, out string value)
    {
        value = string.Empty;
        var prefix = key + ":";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        value = line[prefix.Length..].Trim();
        return true;
    }

    private static void ValidateActionEntry(
        IReadOnlyDictionary<string, ActionPackEntry> actionPack,
        string actionId,
        bool expectPsykerRole,
        bool expectChaosRole)
    {
        Assert.That(actionPack.TryGetValue(actionId, out var entry), Is.True,
            $"Missing action '{actionId}' in psyker_chaos action pack.");

        Assert.That(entry, Is.Not.Null);

        Assert.That(entry!.Parent, Is.Not.Empty,
            $"Action '{actionId}' must define parent prototype.");
        Assert.That(AllowedReuseParents.Contains(entry.Parent) || AllowedCustomStarterActions.Contains(actionId), Is.True,
            $"Action '{actionId}' parent '{entry.Parent}' is outside reuse-first allowed set.");

        Assert.That(entry.HasActionComponent, Is.True,
            $"Action '{actionId}' must contain Action component block.");
        Assert.That(entry.HasWarpCostComponent, Is.True,
            $"Action '{actionId}' must contain WH40KWarpActionCost component block.");

        Assert.That(entry.RequireWarpRole, Is.EqualTo(true),
            $"Action '{actionId}' must require warp role.");
        Assert.That(entry.AllowPsykerRole, Is.EqualTo(expectPsykerRole),
            $"Action '{actionId}' has invalid allowPsykerRole gate.");
        Assert.That(entry.AllowChaosRole, Is.EqualTo(expectChaosRole),
            $"Action '{actionId}' has invalid allowChaosRole gate.");

        Assert.That(entry.WarpChargeCost, Is.GreaterThan(0f),
            $"Action '{actionId}' must spend positive warp charge.");
        Assert.That(entry.InstabilityGain, Is.GreaterThan(0f),
            $"Action '{actionId}' must add positive warp instability.");
    }

    private static float SimulatePsykerSleepXp(
        TimeSpan duration,
        bool onBed,
        WH40KPsykerProgressionComponent progression)
    {
        var interval = Math.Max(1, (int) Math.Round(progression.MeditationInterval.TotalSeconds));
        var gain = progression.MeditationXpPerInterval * (onBed ? progression.MeditationBedBonusMultiplier : 1f);

        float totalXp = 0f;
        var durationSeconds = Math.Max(0, (int) Math.Floor(duration.TotalSeconds));
        for (var sec = interval; sec <= durationSeconds; sec += interval)
        {
            totalXp += gain;
        }

        return totalXp;
    }

    private static float SimulatePsykerCastXp(
        TimeSpan duration,
        TimeSpan castInterval,
        bool rotatingActions,
        WH40KPsykerProgressionComponent progression)
    {
        var castEvery = Math.Max(1, (int) Math.Round(castInterval.TotalSeconds));
        var repeatWindow = Math.Max(1, (int) Math.Round(progression.CastRepeatWindow.TotalSeconds));
        var durationSeconds = Math.Max(0, (int) Math.Floor(duration.TotalSeconds));

        float totalXp = 0f;
        var lastCastSecond = -10000;
        string? lastAction = null;
        var repeatStreak = 0;
        var castIndex = 0;

        for (var sec = castEvery; sec <= durationSeconds; sec += castEvery)
        {
            var actionKey = rotatingActions ? $"action-{castIndex % 3}" : "action-spam";
            castIndex++;

            if (lastAction == actionKey && sec - lastCastSecond <= repeatWindow)
                repeatStreak++;
            else
                repeatStreak = 0;

            lastAction = actionKey;
            lastCastSecond = sec;

            var antiSpam = MathF.Max(
                progression.CastMinMultiplier,
                MathF.Pow(progression.CastRepeatFalloff, repeatStreak));

            totalXp += progression.CastXpBase * antiSpam;
        }

        return totalXp;
    }

    private static float SimulateChaosRitualHeavyXp(
        TimeSpan duration,
        TimeSpan castInterval,
        WH40KChaosGiftProgressionComponent progression,
        WH40KChaosAltarComponent altar,
        WH40KChaosSkrizhalComponent skrizhal)
    {
        var durationSeconds = Math.Max(0, (int) Math.Floor(duration.TotalSeconds));
        var castEvery = Math.Max(1, (int) Math.Round(castInterval.TotalSeconds));
        var repeatWindow = Math.Max(1, (int) Math.Round(ChaosCastRepeatWindow.TotalSeconds));
        var sacrificeCooldown = Math.Max(1, (int) Math.Round(altar.SacrificeCooldown.TotalSeconds));
        var ritualDuration = Math.Max(0, (int) Math.Round(altar.RitualBoostDuration.TotalSeconds));

        float totalXp = Math.Max(0f, skrizhal.AttunementXpReward);
        var attunementMultiplier = MathF.Max(1f, skrizhal.AttunementXpMultiplier);

        var ritualExpirySecond = -1;
        var lastCastSecond = -10000;
        var repeatStreak = 0;
        for (var sec = 0; sec <= durationSeconds; sec++)
        {
            if (sec % sacrificeCooldown == 0)
            {
                var sacrificeXp = altar.SacrificeXpReward * MathF.Max(1f, altar.AttunedSacrificeXpMultiplier);
                totalXp += sacrificeXp;

                var extensionStart = ritualExpirySecond > sec ? ritualExpirySecond : sec;
                ritualExpirySecond = extensionStart + ritualDuration;
            }

            if (sec == 0 || sec % castEvery != 0)
                continue;

            if (sec - lastCastSecond <= repeatWindow)
                repeatStreak++;
            else
                repeatStreak = 0;

            lastCastSecond = sec;

            var antiSpam = MathF.Max(
                ChaosCastMinMultiplier,
                MathF.Pow(ChaosCastRepeatFalloff, repeatStreak));

            var ritualMultiplier = sec < ritualExpirySecond
                ? MathF.Max(1f, altar.RitualBoostMultiplier)
                : 1f;

            totalXp += ChaosCastXpBase * antiSpam * attunementMultiplier * ritualMultiplier;
        }

        return totalXp;
    }

    private static int ResolveLevelFromXp(float totalXp, float baseXpForNextLevel, float xpGrowthFactor, int maxLevel)
    {
        var level = 1;
        var remaining = Math.Max(0f, totalXp);

        while (level < maxLevel)
        {
            var need = MathF.Max(1f, baseXpForNextLevel * MathF.Pow(xpGrowthFactor, Math.Max(0, level - 1)));
            if (remaining + 0.0001f < need)
                break;

            remaining -= need;
            level++;
        }

        return level;
    }

    private static int ResolveLinearLevelFromXp(float totalXp, float xpPerLevelStep, int maxLevel)
    {
        var level = 1;
        var remaining = Math.Max(0f, totalXp);

        while (level < maxLevel)
        {
            var need = MathF.Max(1f, xpPerLevelStep * Math.Max(1, level));
            if (remaining + 0.0001f < need)
                break;

            remaining -= need;
            level++;
        }

        return level;
    }

    private static InstabilitySimulationResult SimulateInstabilityProfile(
        TimeSpan duration,
        TimeSpan castInterval,
        float instabilityPerCast,
        float decayPerSecond,
        float maxInstability)
    {
        var durationSeconds = Math.Max(1, (int) Math.Floor(duration.TotalSeconds));
        var castEvery = Math.Max(1, (int) Math.Round(castInterval.TotalSeconds));

        var current = 0f;
        var minObserved = float.MaxValue;
        var maxObserved = float.MinValue;
        var sum = 0f;
        var criticalSeconds = 0;

        for (var sec = 1; sec <= durationSeconds; sec++)
        {
            current = Math.Clamp(current - MathF.Max(0f, decayPerSecond), 0f, maxInstability);

            if (sec % castEvery == 0)
                current = Math.Clamp(current + MathF.Max(0f, instabilityPerCast), 0f, maxInstability);

            minObserved = MathF.Min(minObserved, current);
            maxObserved = MathF.Max(maxObserved, current);
            sum += current;

            if (current >= maxInstability * 0.67f)
                criticalSeconds++;
        }

        return new InstabilitySimulationResult(
            minObserved,
            maxObserved,
            sum / (durationSeconds * MathF.Max(1f, maxInstability)),
            criticalSeconds / (float) durationSeconds);
    }

    private static int CountPsykerActionsAtLevel(WH40KPsykerStarterActionLoadoutComponent loadout, int level)
    {
        return loadout.StarterActions.Count + loadout.ScaledActions.Count(x => x.RequiredLevel <= level);
    }

    private static int CountChaosActionsAtLevel(
        WH40KChaosGiftStarterActionLoadoutComponent loadout,
        WH40KChaosPatron patron,
        int level)
    {
        var total = loadout.BaseActions.Count + loadout.BaseScaledActions.Count(x => x.RequiredLevel <= level);

        switch (patron)
        {
            case WH40KChaosPatron.Khorne:
                total += loadout.KhorneActions.Count + loadout.KhorneScaledActions.Count(x => x.RequiredLevel <= level);
                break;
            case WH40KChaosPatron.Nurgle:
                total += loadout.NurgleActions.Count + loadout.NurgleScaledActions.Count(x => x.RequiredLevel <= level);
                break;
            case WH40KChaosPatron.Slaanesh:
                total += loadout.SlaaneshActions.Count + loadout.SlaaneshScaledActions.Count(x => x.RequiredLevel <= level);
                break;
            case WH40KChaosPatron.Tzeentch:
                total += loadout.TzeentchActions.Count + loadout.TzeentchScaledActions.Count(x => x.RequiredLevel <= level);
                break;
            default:
                total += loadout.UndividedActions.Count + loadout.UndividedScaledActions.Count(x => x.RequiredLevel <= level);
                break;
        }

        return total;
    }

    private sealed class ActionPackEntry
    {
        public string Id = string.Empty;
        public string Parent = string.Empty;
        public bool HasActionComponent;
        public bool HasWarpCostComponent;
        public bool? RequireWarpRole;
        public bool? AllowPsykerRole;
        public bool? AllowChaosRole;
        public float? WarpChargeCost;
        public float? InstabilityGain;
    }

    private readonly record struct InstabilitySimulationResult(
        float MinObserved,
        float MaxObserved,
        float MeanFraction,
        float CriticalFraction);
}
