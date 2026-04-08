using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.CCVar;
using Lidgren.Network;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Diagnostics;

/// <summary>
/// Runtime diagnostics for net-buffer pressure without engine changes.
/// Reports outgoing bursts, blocked channels and hot dirty entities/prototypes.
/// </summary>
public sealed class WH40KNetBufferDiagnosticsSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly WH40KNetDiagAttributionSystem _attribution = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<Type, long> _lastMessageTotals = new();
    private readonly Dictionary<NetEntity, DirtyEntitySample> _dirtyByNetEntity = new();
    private readonly Dictionary<string, int> _dirtyByPrototype = new(StringComparer.Ordinal);

    private ISawmill _sawmill = default!;

    private NetworkStats _lastStats;
    private TimeSpan _lastSampleAt = TimeSpan.Zero;
    private TimeSpan _nextSampleAt = TimeSpan.Zero;
    private bool _hasLastSample;
    private bool _enabled;
    private bool _traceEverySample;
    private bool _warnNoTypeMetrics;
    private bool _dirtySubscriptionActive;
    private float _sampleIntervalSeconds = 1.0f;
    private float _burstOutgoingKiBPerSecond = 512f;
    private int _burstOutgoingPacketsPerSecond = 1200;
    private int _topEntries = 8;
    private int _highPingMs = 220;
    private int _maxBlockedClientDetails = 6;
    private int _intervalDirtyEvents;
    private int _consecutiveNoTypeMetricSamples;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.netdiag");

        Subs.CVar(_config, CCVars.WH40KNetDiagEnabled, OnEnabledChanged, true);
        Subs.CVar(_config, CCVars.WH40KNetDiagSampleIntervalSeconds, value =>
        {
            _sampleIntervalSeconds = Math.Max(0.1f, value);
            if (_enabled)
                _nextSampleAt = _timing.CurTime + TimeSpan.FromSeconds(_sampleIntervalSeconds);
        }, true);
        Subs.CVar(_config, CCVars.WH40KNetDiagBurstOutgoingKiBPerSecond, value =>
            _burstOutgoingKiBPerSecond = Math.Max(0f, value), true);
        Subs.CVar(_config, CCVars.WH40KNetDiagBurstOutgoingPacketsPerSecond, value =>
            _burstOutgoingPacketsPerSecond = Math.Max(0, value), true);
        Subs.CVar(_config, CCVars.WH40KNetDiagTraceEverySample, value => _traceEverySample = value, true);
        Subs.CVar(_config, CCVars.WH40KNetDiagTopEntries, value => _topEntries = Math.Clamp(value, 1, 64), true);
        Subs.CVar(_config, CCVars.WH40KNetDiagHighPingMs, value => _highPingMs = Math.Max(1, value), true);
        Subs.CVar(_config, CCVars.WH40KNetDiagMaxBlockedClientDetails, value => _maxBlockedClientDetails = Math.Clamp(value, 1, 32), true);
        Subs.CVar(_config, CCVars.WH40KNetDiagWarnNoTypeMetrics, value => _warnNoTypeMetrics = value, true);
    }

    public override void Shutdown()
    {
        SetDirtySubscription(false);
        ClearIntervalCounters();
        _lastMessageTotals.Clear();
        _hasLastSample = false;
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled || !_netManager.IsServer || !_netManager.IsRunning)
            return;

        var now = _timing.CurTime;
        if (!_hasLastSample)
        {
            ResetSamplingState(now);
            return;
        }

        if (now < _nextSampleAt)
            return;

        Sample(now);
    }

    private void OnEnabledChanged(bool value)
    {
        var wasEnabled = _enabled;
        _enabled = value;

        SetDirtySubscription(_enabled);

        if (!_enabled)
        {
            ClearIntervalCounters();
            _lastMessageTotals.Clear();
            _hasLastSample = false;
            _consecutiveNoTypeMetricSamples = 0;

            if (wasEnabled)
                _sawmill.Info("WH40K net diagnostics disabled.");

            return;
        }

        ResetSamplingState(_timing.CurTime);

        if (!wasEnabled)
        {
            _sawmill.Info(
                $"WH40K net diagnostics enabled: sample={_sampleIntervalSeconds:F2}s, " +
                $"burstKiBps={_burstOutgoingKiBPerSecond:F1}, burstPps={_burstOutgoingPacketsPerSecond}.");
        }
    }

    private void SetDirtySubscription(bool subscribe)
    {
        if (_dirtySubscriptionActive == subscribe)
            return;

        if (subscribe)
            EntityManager.EntityDirtied += OnEntityDirtied;
        else
            EntityManager.EntityDirtied -= OnEntityDirtied;

        _dirtySubscriptionActive = subscribe;
    }

    private void OnEntityDirtied(Entity<MetaDataComponent> ent)
    {
        if (!_enabled)
            return;

        _intervalDirtyEvents++;

        var prototypeId = ent.Comp.EntityPrototype?.ID ?? "<dynamic>";
        _dirtyByPrototype.TryGetValue(prototypeId, out var prototypeCount);
        _dirtyByPrototype[prototypeId] = prototypeCount + 1;

        if (ent.Comp.NetEntity == NetEntity.Invalid)
            return;

        if (!_dirtyByNetEntity.TryGetValue(ent.Comp.NetEntity, out var sample))
        {
            sample = new DirtyEntitySample
            {
                Uid = ent.Owner,
                PrototypeId = prototypeId,
                DirtyTicks = 1,
            };
            _dirtyByNetEntity[ent.Comp.NetEntity] = sample;
            return;
        }

        sample.Uid = ent.Owner;
        sample.PrototypeId = prototypeId;
        sample.DirtyTicks++;
    }

    private void ResetSamplingState(TimeSpan now)
    {
        _lastStats = _netManager.Statistics;
        _lastSampleAt = now;
        _nextSampleAt = now + TimeSpan.FromSeconds(_sampleIntervalSeconds);
        _hasLastSample = true;
        _consecutiveNoTypeMetricSamples = 0;

        _lastMessageTotals.Clear();
        foreach (var (type, bytes) in _netManager.MessageBandwidthUsage)
        {
            _lastMessageTotals[type] = bytes;
        }

        ClearIntervalCounters();
    }

    private void Sample(TimeSpan now)
    {
        var stats = _netManager.Statistics;
        var elapsedSeconds = Math.Max(0.001, (now - _lastSampleAt).TotalSeconds);

        var sentBytes = Delta(stats.SentBytes, _lastStats.SentBytes);
        var receivedBytes = Delta(stats.ReceivedBytes, _lastStats.ReceivedBytes);
        var sentPackets = Delta(stats.SentPackets, _lastStats.SentPackets);
        var receivedPackets = Delta(stats.ReceivedPackets, _lastStats.ReceivedPackets);

        var outgoingBytesPerSecond = sentBytes / elapsedSeconds;
        var incomingBytesPerSecond = receivedBytes / elapsedSeconds;
        var outgoingPacketsPerSecond = sentPackets / elapsedSeconds;
        var incomingPacketsPerSecond = receivedPackets / elapsedSeconds;

        var channelSummary = BuildChannelSummary();
        var typeDeltas = CollectMessageTypeDeltas();
        var wh40kMessageScopes = CollectWh40KMessageScopeDeltas(typeDeltas);
        var wh40kSources = _attribution.ConsumeIntervalSnapshot(_topEntries);
        HandleMissingTypeMetrics(typeDeltas);

        var reasons = new List<string>(4);
        if (_burstOutgoingKiBPerSecond > 0f &&
            outgoingBytesPerSecond / 1024.0 >= _burstOutgoingKiBPerSecond)
        {
            reasons.Add("out_kibps");
        }

        if (_burstOutgoingPacketsPerSecond > 0 &&
            outgoingPacketsPerSecond >= _burstOutgoingPacketsPerSecond)
        {
            reasons.Add("out_pps");
        }

        if (channelSummary.BlockedReliableOrdered > 0 || channelSummary.BlockedReliableUnordered > 0)
            reasons.Add("blocked_channels");

        var burst = reasons.Count > 0;
        if (_traceEverySample || burst)
        {
            LogSnapshot(
                burst,
                reasons,
                elapsedSeconds,
                sentBytes,
                receivedBytes,
                outgoingBytesPerSecond,
                incomingBytesPerSecond,
                outgoingPacketsPerSecond,
                incomingPacketsPerSecond,
                channelSummary,
                typeDeltas,
                wh40kMessageScopes,
                wh40kSources);
        }

        _lastStats = stats;
        _lastSampleAt = now;
        _nextSampleAt = now + TimeSpan.FromSeconds(_sampleIntervalSeconds);
        ClearIntervalCounters();
    }

    private ChannelSummary BuildChannelSummary()
    {
        var summary = new ChannelSummary();

        foreach (var channel in _netManager.Channels)
        {
            summary.Total++;

            if (!channel.IsConnected)
                summary.Disconnected++;

            if (!channel.IsHandshakeComplete)
                summary.HandshakePending++;

            if (channel.Ping >= _highPingMs)
                summary.HighPing++;

            var blockedReliableOrdered = !channel.CanSendImmediately(NetDeliveryMethod.ReliableOrdered, 0);
            if (blockedReliableOrdered)
            {
                summary.BlockedReliableOrdered++;
                if (summary.BlockedDetails.Count < _maxBlockedClientDetails)
                    summary.BlockedDetails.Add(DescribeChannel(channel, "ro"));
            }

            var blockedReliableUnordered = !channel.CanSendImmediately(NetDeliveryMethod.ReliableUnordered, 0);
            if (blockedReliableUnordered)
            {
                summary.BlockedReliableUnordered++;
                if (!blockedReliableOrdered && summary.BlockedDetails.Count < _maxBlockedClientDetails)
                    summary.BlockedDetails.Add(DescribeChannel(channel, "ru"));
            }
        }

        return summary;
    }

    private List<(Type Type, long Bytes)> CollectMessageTypeDeltas()
    {
        var result = new List<(Type, long)>();
        var current = _netManager.MessageBandwidthUsage;

        foreach (var (type, bytes) in current)
        {
            _lastMessageTotals.TryGetValue(type, out var previousBytes);
            var delta = bytes - previousBytes;
            if (delta > 0)
                result.Add((type, delta));
        }

        _lastMessageTotals.Clear();
        foreach (var (type, bytes) in current)
        {
            _lastMessageTotals[type] = bytes;
        }

        return result;
    }

    private IReadOnlyList<(string Scope, long Bytes)> CollectWh40KMessageScopeDeltas(
        IReadOnlyList<(Type Type, long Bytes)> typeDeltas)
    {
        if (typeDeltas.Count == 0)
            return Array.Empty<(string Scope, long Bytes)>();

        var grouped = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (type, bytes) in typeDeltas)
        {
            if (bytes <= 0 || !TryResolveWh40KMessageScope(type, out var scope))
                continue;

            grouped.TryGetValue(scope, out var currentBytes);
            grouped[scope] = currentBytes + bytes;
        }

        if (grouped.Count == 0)
            return Array.Empty<(string Scope, long Bytes)>();

        return grouped
            .OrderByDescending(pair => pair.Value)
            .Take(_topEntries)
            .Select(pair => (pair.Key, pair.Value))
            .ToArray();
    }

    private static bool TryResolveWh40KMessageScope(Type type, out string scope)
    {
        scope = string.Empty;

        var fullName = type.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
            return false;

        var tokenIndex = fullName.IndexOf("._WH40K.", StringComparison.Ordinal);
        if (tokenIndex < 0)
            return false;

        var after = fullName[(tokenIndex + "._WH40K.".Length)..];
        if (string.IsNullOrWhiteSpace(after))
            return false;

        var separator = after.IndexOf('.');
        var segment = separator < 0 ? after : after[..separator];
        if (string.IsNullOrWhiteSpace(segment))
            return false;

        scope = ToSnakeCase(segment);
        return true;
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch))
            {
                if (i > 0 && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1])))
                    result.Append('_');

                result.Append(char.ToLowerInvariant(ch));
                continue;
            }

            result.Append(ch == '-' ? '_' : ch);
        }

        return result.ToString();
    }

    private void HandleMissingTypeMetrics(List<(Type Type, long Bytes)> typeDeltas)
    {
        if (!_warnNoTypeMetrics)
            return;

        if (typeDeltas.Count > 0 || _netManager.MessageBandwidthUsage.Count > 0)
        {
            _consecutiveNoTypeMetricSamples = 0;
            return;
        }

        _consecutiveNoTypeMetricSamples++;
        if (_consecutiveNoTypeMetricSamples != 1 && _consecutiveNoTypeMetricSamples % 30 != 0)
            return;

        _sawmill.Info(
            "[netdiag] MessageBandwidthUsage buckets are empty. This is expected on non-DEBUG networking builds; " +
            "use dirty/channel sections for culprit search.");
    }

    private void LogSnapshot(
        bool burst,
        List<string> reasons,
        double elapsedSeconds,
        long sentBytes,
        long receivedBytes,
        double outgoingBytesPerSecond,
        double incomingBytesPerSecond,
        double outgoingPacketsPerSecond,
        double incomingPacketsPerSecond,
        ChannelSummary channels,
        List<(Type Type, long Bytes)> typeDeltas,
        IReadOnlyList<(string Scope, long Bytes)> wh40kMessageScopes,
        IReadOnlyList<WH40KNetDiagSourceAttributionEntry> wh40kSources)
    {
        var prefix = burst ? "[netdiag][burst]" : "[netdiag][sample]";
        var reasonSuffix = reasons.Count == 0 ? string.Empty : $" reason={string.Join(",", reasons)}";
        var header =
            $"{prefix} dt={elapsedSeconds:F2}s out={FormatRate(outgoingBytesPerSecond)} ({sentBytes} B) " +
            $"outPps={outgoingPacketsPerSecond:F0} in={FormatRate(incomingBytesPerSecond)} ({receivedBytes} B) " +
            $"inPps={incomingPacketsPerSecond:F0} channels={channels.Total} " +
            $"blocked(ro/ru)={channels.BlockedReliableOrdered}/{channels.BlockedReliableUnordered} " +
            $"highPing={channels.HighPing} handshakePending={channels.HandshakePending} dirty={_intervalDirtyEvents}{reasonSuffix}";

        LogLine(burst, header);

        if (channels.BlockedDetails.Count > 0)
            LogLine(burst, $"{prefix} blockedClients={string.Join("; ", channels.BlockedDetails)}");

        if (_intervalDirtyEvents > 0)
        {
            var topPrototypes = _dirtyByPrototype
                .OrderByDescending(pair => pair.Value)
                .Take(_topEntries)
                .Select(pair => $"{pair.Key}={pair.Value}")
                .ToArray();

            if (topPrototypes.Length > 0)
                LogLine(burst, $"{prefix} dirtyPrototypes={string.Join(", ", topPrototypes)}");

            var topEntities = _dirtyByNetEntity
                .OrderByDescending(pair => pair.Value.DirtyTicks)
                .Take(_topEntries)
                .Select(pair =>
                    $"net={pair.Key.Id} uid={pair.Value.Uid} proto={pair.Value.PrototypeId} ticks={pair.Value.DirtyTicks}")
                .ToArray();

            if (topEntities.Length > 0)
                LogLine(burst, $"{prefix} dirtyEntities={string.Join(", ", topEntities)}");
        }

        if (typeDeltas.Count > 0)
        {
            var topTypes = typeDeltas
                .OrderByDescending(entry => entry.Bytes)
                .Take(_topEntries)
                .Select(entry => $"{entry.Type.Name}={FormatSize(entry.Bytes)}")
                .ToArray();
            LogLine(burst, $"{prefix} messageTypes={string.Join(", ", topTypes)}");
        }

        if (wh40kMessageScopes.Count > 0)
        {
            var scopes = wh40kMessageScopes
                .Select(entry => $"{entry.Scope}={FormatSize(entry.Bytes)}")
                .ToArray();
            LogLine(burst, $"{prefix} wh40kMessageScopes={string.Join(", ", scopes)}");
        }

        if (wh40kSources.Count > 0)
        {
            var topSources = wh40kSources
                .Select(entry =>
                {
                    var lastUid = entry.LastUid == EntityUid.Invalid ? "-" : entry.LastUid.ToString();
                    return $"{entry.Source}[dirty={entry.DirtyHits},act={entry.ActivityHits},ents={entry.UniqueNetEntities},last={lastUid}]";
                })
                .ToArray();

            LogLine(burst, $"{prefix} wh40kSources={string.Join(", ", topSources)}");
        }
    }

    private void LogLine(bool burst, string line)
    {
        if (burst)
            _sawmill.Warning(line);
        else
            _sawmill.Info(line);
    }

    private string DescribeChannel(INetChannel channel, string mode)
    {
        var name = channel.UserName;
        if (string.IsNullOrWhiteSpace(name) &&
            _players.TryGetSessionByChannel(channel, out var session))
        {
            name = session.Name;
        }

        if (string.IsNullOrWhiteSpace(name))
            name = channel.UserId.ToString();

        return $"{name}@{channel.RemoteEndPoint} ping={channel.Ping}ms mode={mode}";
    }

    private void ClearIntervalCounters()
    {
        _intervalDirtyEvents = 0;
        _dirtyByNetEntity.Clear();
        _dirtyByPrototype.Clear();
    }

    private static long Delta(long current, long previous)
    {
        if (current >= previous)
            return current - previous;

        return current;
    }

    private static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024.0 * 1024.0)
            return $"{bytesPerSecond / (1024.0 * 1024.0):F2} MiB/s";

        if (bytesPerSecond >= 1024.0)
            return $"{bytesPerSecond / 1024.0:F1} KiB/s";

        return $"{bytesPerSecond:F0} B/s";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
            return $"{bytes / (1024.0 * 1024.0):F2} MiB";

        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KiB";

        return $"{bytes} B";
    }

    private sealed class DirtyEntitySample
    {
        public EntityUid Uid;
        public string PrototypeId = "<dynamic>";
        public int DirtyTicks;
    }

    private sealed class ChannelSummary
    {
        public int Total;
        public int HighPing;
        public int HandshakePending;
        public int Disconnected;
        public int BlockedReliableOrdered;
        public int BlockedReliableUnordered;
        public readonly List<string> BlockedDetails = new();
    }
}
