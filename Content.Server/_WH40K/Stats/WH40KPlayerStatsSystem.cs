using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.GameTicking;
using Content.Shared.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Stats;

/// <summary>
/// Global WH40K runtime player action statistics and action-log pipeline.
/// It is intentionally generic and currently used by meta progression only.
/// </summary>
public sealed class WH40KPlayerStatsSystem : EntitySystem
{
    private const int RecentEntriesLimit = 512;

    [Dependency] private readonly IConfigurationManager _config = default!;

    private readonly Dictionary<NetUserId, Dictionary<string, long>> _lifetimeCounters = new();
    private readonly Dictionary<NetUserId, Dictionary<string, long>> _roundCounters = new();
    private readonly LinkedList<WH40KPlayerStatLogEntry> _recent = new();
    private ISawmill _sawmill = default!;
    private bool _traceEnabled;

    public event Action<WH40KPlayerStatLogEntry>? ActionRecorded;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.player.stats");
        Subs.CVar(_config, CCVars.WH40KMetaStatsTrace, OnStatsTraceChanged, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnStatsTraceChanged(bool value)
    {
        _traceEnabled = value;
        _sawmill.Info($"WH40K stats trace logging {(value ? "enabled" : "disabled")}.");
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        if (_traceEnabled)
        {
            _sawmill.Info(
                $"[trace] Round restart cleanup: clearing round counters for {_roundCounters.Count} tracked players.");
        }

        _roundCounters.Clear();
    }

    public void Record(
        NetUserId userId,
        string key,
        long delta = 1,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(key) || delta == 0)
            return;

        var normalizedKey = key.Trim();
        IncrementCounter(_lifetimeCounters, userId, normalizedKey, delta);
        IncrementCounter(_roundCounters, userId, normalizedKey, delta);
        var lifetimeTotal = GetLifetimeCounter(userId, normalizedKey);
        var roundTotal = GetRoundCounter(userId, normalizedKey);

        var entry = new WH40KPlayerStatLogEntry(
            userId,
            normalizedKey,
            delta,
            DateTimeOffset.UtcNow,
            metadata is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(metadata, StringComparer.Ordinal));

        PushRecent(entry);
        RaiseLocalEvent(new WH40KPlayerStatRecordedEvent(entry));
        ActionRecorded?.Invoke(entry);

        _sawmill.Debug(
            $"Recorded stat: user={userId}, key={normalizedKey}, delta={delta}, meta={FormatMetadata(entry.Metadata)}");

        if (_traceEnabled)
        {
            _sawmill.Info(
                $"[trace] stat+ user={userId}, key={normalizedKey}, delta={delta}, " +
                $"roundTotal={roundTotal}, lifetimeTotal={lifetimeTotal}, meta={FormatMetadata(entry.Metadata)}");
        }
    }

    public IReadOnlyDictionary<string, long> GetLifetimeCounters(NetUserId userId)
    {
        return _lifetimeCounters.TryGetValue(userId, out var counters)
            ? new Dictionary<string, long>(counters, StringComparer.Ordinal)
            : new Dictionary<string, long>(StringComparer.Ordinal);
    }

    public long GetLifetimeCounter(NetUserId userId, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        if (!_lifetimeCounters.TryGetValue(userId, out var counters))
            return 0;

        return counters.TryGetValue(key.Trim(), out var value) ? value : 0;
    }

    public IReadOnlyDictionary<string, long> GetRoundCounters(NetUserId userId)
    {
        return _roundCounters.TryGetValue(userId, out var counters)
            ? new Dictionary<string, long>(counters, StringComparer.Ordinal)
            : new Dictionary<string, long>(StringComparer.Ordinal);
    }

    public long GetRoundCounter(NetUserId userId, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        if (!_roundCounters.TryGetValue(userId, out var counters))
            return 0;

        return counters.TryGetValue(key.Trim(), out var value) ? value : 0;
    }

    public IReadOnlyList<WH40KPlayerStatLogEntry> GetRecentEntries(int maxCount = 50)
    {
        if (maxCount <= 0 || _recent.Count == 0)
            return Array.Empty<WH40KPlayerStatLogEntry>();

        var take = Math.Min(maxCount, _recent.Count);
        return _recent.Reverse().Take(take).ToArray();
    }

    private void PushRecent(WH40KPlayerStatLogEntry entry)
    {
        _recent.AddLast(entry);

        while (_recent.Count > RecentEntriesLimit)
        {
            _recent.RemoveFirst();
        }
    }

    private static void IncrementCounter(
        Dictionary<NetUserId, Dictionary<string, long>> source,
        NetUserId userId,
        string key,
        long delta)
    {
        if (!source.TryGetValue(userId, out var counters))
        {
            counters = new Dictionary<string, long>(StringComparer.Ordinal);
            source[userId] = counters;
        }

        counters.TryGetValue(key, out var current);
        counters[key] = current + delta;
    }

    private static string FormatMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count == 0)
            return "{}";

        return "{" + string.Join(", ", metadata.Select(pair => $"{pair.Key}={pair.Value}")) + "}";
    }
}

public readonly record struct WH40KPlayerStatLogEntry(
    NetUserId UserId,
    string Key,
    long Delta,
    DateTimeOffset RecordedAt,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class WH40KPlayerStatRecordedEvent : EntityEventArgs
{
    public WH40KPlayerStatLogEntry Entry { get; }

    public WH40KPlayerStatRecordedEvent(WH40KPlayerStatLogEntry entry)
    {
        Entry = entry;
    }
}

public static class WH40KPlayerStatKeys
{
    public const string CombatEnemyKills = "combat.kill.enemy";
    public const string CombatEnemyAssists = "combat.assist.enemy";
    public const string CombatDeaths = "combat.death";
    public const string SupportRevives = "support.revive";
    public const string SupportStabilizations = "support.stabilize";
    public const string SupportHealBucket100 = "support.heal.bucket100";
    public const string LogisticsDeliverySuccess = "logistics.delivery.success";
    public const string LogisticsDeliveryValue = "logistics.delivery.value";
    public const string ObjectiveCaptureSuccess = "objective.capture.success";
    public const string ObjectiveDefenseSuccess = "objective.defense.success";
    public const string EconomyCommandTreePurchaseCount = "economy.command.tree.purchase.count";
    public const string EconomyCommandTreePurchaseCost = "economy.command.tree.purchase.cost";
    public const string EconomyCommandUpgradeCount = "economy.command.upgrade.count";
    public const string EconomyCommandUpgradeCost = "economy.command.upgrade.cost";
    public const string EconomyCommandReinforcementCallCount = "economy.command.reinforcement.call.count";
    public const string EconomyCommandReinforcementCost = "economy.command.reinforcement.cost";
    public const string RoundWins = "round.win";
    public const string RoundCompletedFaction = "round.completed.faction";
    public const string RoundParticipationActive = "round.participation.active";
    public const string RoundLeftEarly = "round.left.early";
    public const string MissionOutcomes = "mission.outcome";
    public const string MetaSessionRoundsPlayed = "meta.session.rounds_played";
    public const string MetaXpManualAdjust = "meta.xp.manual_adjust";
    public const string MetaXpManualSet = "meta.xp.manual_set_delta";
    public const string MetaXpKill = "meta.xp.kill";
    public const string MetaXpRoundWin = "meta.xp.round_win";
    public const string MetaXpObjective = "meta.xp.objective";
    public const string MetaAchievementProgress = "meta.achievement.progress_delta";
    public const string MetaAchievementCompleted = "meta.achievement.completed";
    public const string MetaAchievementRevoked = "meta.achievement.revoked";
    public const string MetaDecorationSelection = "meta.decoration.selection";
}
