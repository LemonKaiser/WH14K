using System;
using System.Collections.Generic;
using DiagnosticsStopwatch = System.Diagnostics.Stopwatch;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

/// <summary>
/// Lightweight runtime benchmark instrumentation for NPC subsystems.
/// Enabled only through CVar to avoid production overhead.
/// </summary>
public sealed class NPCBenchmarkSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<string, StageAccumulator> _stages = new();
    private readonly object _lock = new();

    private ISawmill _sawmill = default!;
    private bool _enabled;
    private bool _detailed;
    private float _logIntervalSeconds = 5f;
    private TimeSpan _nextLogAt = TimeSpan.Zero;
    private TimeSpan _windowStart = TimeSpan.Zero;

    public bool Enabled => _enabled;
    public bool Detailed => _detailed;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("npc.benchmark");

        Subs.CVar(_cfg, CCVars.NPCBenchmarkEnabled, SetEnabled, true);
        Subs.CVar(_cfg, CCVars.NPCBenchmarkLogIntervalSeconds, value => _logIntervalSeconds = Math.Max(0.25f, value), true);
        Subs.CVar(_cfg, CCVars.NPCBenchmarkDetailed, value => _detailed = value, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        var now = _timing.CurTime;
        if (now < _nextLogAt)
            return;

        _nextLogAt = now + TimeSpan.FromSeconds(_logIntervalSeconds);
        var snapshot = SnapshotAndReset();
        LogSnapshot(snapshot);
    }

    /// <summary>
    /// Measures an execution scope and records the elapsed milliseconds for the benchmark stage.
    /// </summary>
    public StageScope Measure(string stage, int workItems = 1)
    {
        if (!_enabled)
            return default;

        return new StageScope(this, stage, workItems);
    }

    /// <summary>
    /// Records elapsed time for benchmark stage.
    /// </summary>
    public void RecordDuration(string stage, double elapsedMilliseconds, int workItems = 1)
    {
        if (!_enabled)
            return;

        lock (_lock)
        {
            if (!_stages.TryGetValue(stage, out var acc))
            {
                acc = new StageAccumulator();
                _stages[stage] = acc;
            }

            acc.Samples++;
            acc.WorkItems += Math.Max(1, workItems);
            acc.TotalMilliseconds += elapsedMilliseconds;
            acc.MaxMilliseconds = Math.Max(acc.MaxMilliseconds, elapsedMilliseconds);
        }
    }

    /// <summary>
    /// Adds a count-only sample for a stage without duration.
    /// Useful for queue sizes and attempts.
    /// </summary>
    public void RecordCount(string stage, int count)
    {
        if (!_enabled || count <= 0)
            return;

        lock (_lock)
        {
            if (!_stages.TryGetValue(stage, out var acc))
            {
                acc = new StageAccumulator();
                _stages[stage] = acc;
            }

            acc.Samples++;
            acc.WorkItems += count;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _stages.Clear();
            _windowStart = _timing.CurTime;
            _nextLogAt = _windowStart + TimeSpan.FromSeconds(_logIntervalSeconds);
        }
    }

    public NpcBenchmarkSnapshot SnapshotAndReset()
    {
        lock (_lock)
        {
            var now = _timing.CurTime;
            var stages = new List<NpcBenchmarkStageSnapshot>(_stages.Count);

            foreach (var (name, acc) in _stages)
            {
                var avgMs = acc.Samples > 0 ? acc.TotalMilliseconds / acc.Samples : 0.0;
                var avgUsPerItem = acc.WorkItems > 0 ? (acc.TotalMilliseconds * 1000.0) / acc.WorkItems : 0.0;
                stages.Add(new NpcBenchmarkStageSnapshot(name, acc.Samples, acc.WorkItems, acc.TotalMilliseconds, avgMs, acc.MaxMilliseconds, avgUsPerItem));
            }

            stages.Sort((a, b) => b.TotalMilliseconds.CompareTo(a.TotalMilliseconds));

            var snapshot = new NpcBenchmarkSnapshot(_windowStart, now, stages);
            _stages.Clear();
            _windowStart = now;
            return snapshot;
        }
    }

    private void SetEnabled(bool value)
    {
        _enabled = value;
        if (!_enabled)
            return;

        _windowStart = _timing.CurTime;
        _nextLogAt = _windowStart + TimeSpan.FromSeconds(_logIntervalSeconds);
    }

    private void LogSnapshot(NpcBenchmarkSnapshot snapshot)
    {
        if (snapshot.Stages.Count == 0)
            return;

        var top = _detailed ? snapshot.Stages.Count : Math.Min(12, snapshot.Stages.Count);

        _sawmill.Info(
            $"NPCBENCH window={snapshot.WindowSeconds:F2}s stages={snapshot.Stages.Count} top={top}");

        for (var i = 0; i < top; i++)
        {
            var stage = snapshot.Stages[i];
            _sawmill.Info(
                $"NPCBENCH stage={stage.Name} samples={stage.Samples} work={stage.WorkItems} total_ms={stage.TotalMilliseconds:F3} avg_ms={stage.AverageMilliseconds:F4} max_ms={stage.MaxMilliseconds:F4} avg_item_us={stage.AverageItemMicroseconds:F3}");
        }
    }

    private sealed class StageAccumulator
    {
        public int Samples;
        public int WorkItems;
        public double TotalMilliseconds;
        public double MaxMilliseconds;
    }

    public readonly struct StageScope : IDisposable
    {
        private readonly NPCBenchmarkSystem? _bench;
        private readonly string _stage;
        private readonly int _workItems;
        private readonly long _startTimestamp;

        public StageScope(NPCBenchmarkSystem bench, string stage, int workItems)
        {
            _bench = bench;
            _stage = stage;
            _workItems = Math.Max(1, workItems);
            _startTimestamp = DiagnosticsStopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_bench == null)
                return;

            var end = DiagnosticsStopwatch.GetTimestamp();
            var elapsedMs = (end - _startTimestamp) * 1000.0 / DiagnosticsStopwatch.Frequency;
            _bench.RecordDuration(_stage, elapsedMs, _workItems);
        }
    }
}

public readonly record struct NpcBenchmarkSnapshot(
    TimeSpan WindowStart,
    TimeSpan WindowEnd,
    IReadOnlyList<NpcBenchmarkStageSnapshot> Stages)
{
    public double WindowSeconds => Math.Max(0.0, (WindowEnd - WindowStart).TotalSeconds);
}

public readonly record struct NpcBenchmarkStageSnapshot(
    string Name,
    int Samples,
    int WorkItems,
    double TotalMilliseconds,
    double AverageMilliseconds,
    double MaxMilliseconds,
    double AverageItemMicroseconds);
