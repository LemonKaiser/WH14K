using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Diagnostics;

/// <summary>
/// WH40K-local DB diagnostics.
/// Aggregates DB call latency/errors and emits periodic top-operation summaries.
/// </summary>
public sealed class WH40KDbDiagnosticsSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly object _sync = new();
    private readonly Dictionary<string, DbOpCounter> _operations = new(StringComparer.Ordinal);

    private ISawmill _sawmill = default!;

    private bool _enabled;
    private bool _traceEverySample;
    private float _sampleIntervalSeconds = 10f;
    private int _slowMs = 150;
    private int _criticalMs = 1000;
    private int _topEntries = 8;
    private bool _hasWindowStart;
    private TimeSpan _windowStart = TimeSpan.Zero;
    private TimeSpan _nextSampleAt = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("wh40k.dbdiag");

        Subs.CVar(_config, CCVars.WH40KDbDiagEnabled, value =>
        {
            _enabled = value;
            if (!_enabled)
            {
                lock (_sync)
                {
                    _operations.Clear();
                }
            }
        }, true);

        Subs.CVar(_config, CCVars.WH40KDbDiagSampleIntervalSeconds, value =>
            _sampleIntervalSeconds = Math.Max(1f, value), true);
        Subs.CVar(_config, CCVars.WH40KDbDiagSlowMs, value =>
            _slowMs = Math.Max(1, value), true);
        Subs.CVar(_config, CCVars.WH40KDbDiagCriticalMs, value =>
            _criticalMs = Math.Max(1, value), true);
        Subs.CVar(_config, CCVars.WH40KDbDiagTraceEverySample, value =>
            _traceEverySample = value, true);
        Subs.CVar(_config, CCVars.WH40KDbDiagTopEntries, value =>
            _topEntries = Math.Clamp(value, 1, 64), true);
    }

    public override void Shutdown()
    {
        lock (_sync)
        {
            _operations.Clear();
        }

        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        var now = _timing.CurTime;
        if (!_hasWindowStart)
        {
            _windowStart = now;
            _nextSampleAt = now + TimeSpan.FromSeconds(_sampleIntervalSeconds);
            _hasWindowStart = true;
            return;
        }

        if (now < _nextSampleAt)
            return;

        FlushWindow(now);
    }

    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        if (!_enabled)
            return await action();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await action();
            Record(operation, sw.Elapsed.TotalMilliseconds, null);
            return result;
        }
        catch (Exception ex)
        {
            Record(operation, sw.Elapsed.TotalMilliseconds, ex);
            throw;
        }
    }

    public async Task MeasureAsync(string operation, Func<Task> action)
    {
        if (!_enabled)
        {
            await action();
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await action();
            Record(operation, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (Exception ex)
        {
            Record(operation, sw.Elapsed.TotalMilliseconds, ex);
            throw;
        }
    }

    private void Record(string operation, double elapsedMs, Exception? error)
    {
        if (!_enabled)
            return;

        if (string.IsNullOrWhiteSpace(operation))
            operation = "unknown";

        operation = operation.Trim();

        var slowThreshold = _slowMs;
        var criticalThreshold = Math.Max(slowThreshold, _criticalMs);
        var isSlow = elapsedMs >= slowThreshold;
        var isCritical = elapsedMs >= criticalThreshold;

        lock (_sync)
        {
            if (!_operations.TryGetValue(operation, out var counter))
            {
                counter = new DbOpCounter();
                _operations[operation] = counter;
            }

            counter.Calls++;
            counter.TotalMs += elapsedMs;
            counter.MaxMs = Math.Max(counter.MaxMs, elapsedMs);

            if (isSlow)
                counter.Slow++;

            if (isCritical)
                counter.Critical++;

            if (error != null)
            {
                counter.Errors++;
                counter.LastErrorType = error.GetType().Name;
                counter.LastErrorMessage = TrimError(error.Message);
            }
        }

        if (error != null)
        {
            _sawmill.Warning(
                $"[dbdiag] op={operation} failed after {elapsedMs:F1}ms err={error.GetType().Name}: {TrimError(error.Message)}");
            return;
        }

        if (isCritical)
        {
            _sawmill.Warning(
                $"[dbdiag] op={operation} critical slow call: {elapsedMs:F1}ms (threshold={criticalThreshold}ms)");
        }
    }

    private void FlushWindow(TimeSpan now)
    {
        var elapsed = Math.Max(0.001, (now - _windowStart).TotalSeconds);
        _windowStart = now;
        _nextSampleAt = now + TimeSpan.FromSeconds(_sampleIntervalSeconds);

        List<(string Name, DbOpCounter Counter)> snapshot;
        lock (_sync)
        {
            snapshot = _operations
                .Select(pair => (pair.Key, pair.Value.Clone()))
                .ToList();
            _operations.Clear();
        }

        if (snapshot.Count == 0)
        {
            if (_traceEverySample)
                _sawmill.Info($"[dbdiag][sample] dt={elapsed:F2}s calls=0 err=0 slow=0 crit=0");
            return;
        }

        var calls = snapshot.Sum(entry => entry.Counter.Calls);
        var errors = snapshot.Sum(entry => entry.Counter.Errors);
        var slow = snapshot.Sum(entry => entry.Counter.Slow);
        var critical = snapshot.Sum(entry => entry.Counter.Critical);
        var burst = errors > 0 || critical > 0;
        if (!_traceEverySample && !burst)
            return;

        var prefix = burst ? "[dbdiag][alert]" : "[dbdiag][sample]";
        var header =
            $"{prefix} dt={elapsed:F2}s calls={calls} err={errors} slow={slow} crit={critical} ops={snapshot.Count}";
        LogLine(burst, header);

        var top = snapshot
            .OrderByDescending(entry => entry.Counter.Errors)
            .ThenByDescending(entry => entry.Counter.Critical)
            .ThenByDescending(entry => entry.Counter.Slow)
            .ThenByDescending(entry => entry.Counter.TotalMs)
            .Take(_topEntries)
            .Select(entry =>
            {
                var c = entry.Counter;
                var avg = c.Calls == 0 ? 0 : c.TotalMs / c.Calls;
                var errorPart = c.Errors == 0
                    ? string.Empty
                    : $",lastErr={c.LastErrorType}:{c.LastErrorMessage}";
                return $"{entry.Name}[calls={c.Calls},err={c.Errors},slow={c.Slow},crit={c.Critical},avg={avg:F1}ms,max={c.MaxMs:F1}ms{errorPart}]";
            })
            .ToArray();

        if (top.Length > 0)
            LogLine(burst, $"{prefix} topOps={string.Join(", ", top)}");
    }

    private void LogLine(bool burst, string message)
    {
        if (burst)
            _sawmill.Warning(message);
        else
            _sawmill.Info(message);
    }

    private static string TrimError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "-";

        const int max = 140;
        var trimmed = message.Trim();
        if (trimmed.Length <= max)
            return trimmed;

        return $"{trimmed[..max]}...";
    }

    private sealed class DbOpCounter
    {
        public int Calls;
        public int Errors;
        public int Slow;
        public int Critical;
        public double TotalMs;
        public double MaxMs;
        public string LastErrorType = string.Empty;
        public string LastErrorMessage = string.Empty;

        public DbOpCounter Clone()
        {
            return new DbOpCounter
            {
                Calls = Calls,
                Errors = Errors,
                Slow = Slow,
                Critical = Critical,
                TotalMs = TotalMs,
                MaxMs = MaxMs,
                LastErrorType = LastErrorType,
                LastErrorMessage = LastErrorMessage,
            };
        }
    }
}
