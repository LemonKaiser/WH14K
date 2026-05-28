using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Server._WH40K.Diagnostics;

/// <summary>
/// Lightweight WH40K-only attribution markers for net diagnostics.
/// Systems can report dirty/activity hits with a source tag, then the diagnostics system
/// prints top contributors for each sampling window.
/// </summary>
public sealed partial class WH40KNetDiagAttributionSystem : EntitySystem
{
    [Dependency] private  IConfigurationManager _config = default!;

    private const int MaxTrackedNetEntitiesPerSource = 48;

    private readonly Dictionary<string, SourceCounter> _sourceCounters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _enabledScopes = new(StringComparer.OrdinalIgnoreCase);

    private bool _diagEnabled;
    private bool _attributionEnabled;
    private bool _autoDirtyEnabled = true;
    private bool _allScopesEnabled = true;
    private bool _dirtySubscriptionActive;
    private int _autoDirtyStackDepth = 24;

    [ThreadStatic]
    private static Stack<string>? _ambientContextsThreadLocal;

    /// <summary>
    /// Enters an explicit diagnostic scope for the current thread.
    /// Any dirty events triggered within this using block will be attributed to this source
    /// without relying on expensive StackTrace reflection.
    /// </summary>
    public IDisposable EnterScope(string source)
    {
        _ambientContextsThreadLocal ??= new Stack<string>();
        _ambientContextsThreadLocal.Push(source);
        return new AttributionScope(this);
    }

    private void PopScope()
    {
        if (_ambientContextsThreadLocal != null && _ambientContextsThreadLocal.Count > 0)
            _ambientContextsThreadLocal.Pop();
    }

    private readonly struct AttributionScope : IDisposable
    {
        private readonly WH40KNetDiagAttributionSystem _system;
        public AttributionScope(WH40KNetDiagAttributionSystem system) => _system = system;
        public void Dispose() => _system.DisposeScope();
    }

    private void DisposeScope()
    {
        PopScope();
    }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, CCVars.WH40KNetDiagEnabled, value =>
        {
            _diagEnabled = value;
            if (!IsEnabled())
                _sourceCounters.Clear();
            RefreshAutoDirtySubscription();
        }, true);

        Subs.CVar(_config, CCVars.WH40KNetDiagAttributionEnabled, value =>
        {
            _attributionEnabled = value;
            if (!IsEnabled())
                _sourceCounters.Clear();
            RefreshAutoDirtySubscription();
        }, true);

        Subs.CVar(_config, CCVars.WH40KNetDiagAttributionScopes, value =>
        {
            ParseScopes(value);
            if (!IsEnabled())
                _sourceCounters.Clear();
        }, true);

        Subs.CVar(_config, CCVars.WH40KNetDiagAttributionAutoDirtyEnabled, value =>
        {
            _autoDirtyEnabled = value;
            if (!IsEnabled())
                _sourceCounters.Clear();
            RefreshAutoDirtySubscription();
        }, true);

        Subs.CVar(_config, CCVars.WH40KNetDiagAttributionAutoDirtyStackDepth, value =>
            _autoDirtyStackDepth = Math.Clamp(value, 6, 64), true);
    }

    public override void Shutdown()
    {
        SetDirtySubscription(false);
        _sourceCounters.Clear();
        base.Shutdown();
    }

    /// <summary>
    /// Marks that the source triggered a network-dirty update.
    /// </summary>
    public void RecordDirty(string source, EntityUid uid, int hits = 1)
    {
        if (!TryGetCounter(source, hits, out var counter))
            return;

        counter.DirtyHits += hits;
        counter.LastUid = uid;
        TryTrackNetEntity(counter, uid);
    }

    /// <summary>
    /// Marks local activity that may correlate with network churn (for example UI/toggle spam).
    /// </summary>
    public void RecordActivity(string source, EntityUid? uid = null, int hits = 1)
    {
        if (!TryGetCounter(source, hits, out var counter))
            return;

        counter.ActivityHits += hits;

        if (uid is { } entityUid)
        {
            counter.LastUid = entityUid;
            TryTrackNetEntity(counter, entityUid);
        }
    }

    /// <summary>
    /// Returns top source entries for the current interval and clears counters.
    /// </summary>
    public IReadOnlyList<WH40KNetDiagSourceAttributionEntry> ConsumeIntervalSnapshot(int topEntries)
    {
        if (_sourceCounters.Count == 0)
            return Array.Empty<WH40KNetDiagSourceAttributionEntry>();

        var take = Math.Clamp(topEntries, 1, 64);
        var entries = _sourceCounters
            .Select(pair => new WH40KNetDiagSourceAttributionEntry(
                pair.Key,
                pair.Value.DirtyHits,
                pair.Value.ActivityHits,
                pair.Value.UniqueNetEntities.Count,
                pair.Value.LastUid))
            .OrderByDescending(entry => entry.DirtyHits)
            .ThenByDescending(entry => entry.ActivityHits)
            .ThenBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToArray();

        _sourceCounters.Clear();
        return entries;
    }

    private bool TryGetCounter(string source, int hits, out SourceCounter counter)
    {
        counter = default!;

        if (hits <= 0 || !IsEnabled())
            return false;

        if (string.IsNullOrWhiteSpace(source))
            return false;

        var normalized = source.Trim();
        var scope = GetScope(normalized);

        if (!_allScopesEnabled && !_enabledScopes.Contains(scope))
            return false;

        if (_sourceCounters.TryGetValue(normalized, out counter!))
            return true;

        counter = new SourceCounter();
        _sourceCounters[normalized] = counter;
        return true;
    }

    private bool IsEnabled()
    {
        return _diagEnabled && _attributionEnabled;
    }

    private void RefreshAutoDirtySubscription()
    {
        SetDirtySubscription(IsEnabled() && _autoDirtyEnabled);
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
        if (!IsEnabled() || !_autoDirtyEnabled)
            return;

        if (ent.Comp.NetEntity == NetEntity.Invalid)
            return;

        if (!TryResolveAutoDirtySource(out var source))
            return;

        RecordDirty(source, ent.Owner);
    }

    private bool TryResolveAutoDirtySource(out string source)
    {
        source = string.Empty;

        if (_ambientContextsThreadLocal != null && _ambientContextsThreadLocal.TryPeek(out var ambientSource))
        {
            source = ambientSource;
            return true;
        }

        var trace = new StackTrace(skipFrames: 2, fNeedFileInfo: false);
        var frames = trace.GetFrames();
        if (frames == null || frames.Length == 0)
            return false;

        var depth = Math.Min(_autoDirtyStackDepth, frames.Length);
        for (var i = 0; i < depth; i++)
        {
            var method = frames[i].GetMethod();
            var type = method?.DeclaringType;
            if (type == null)
                continue;

            if (!TryGetWh40KTypeScope(type, out var scope))
                continue;

            if (scope.Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
                continue;

            var typeName = type.Name;
            if (typeName.EndsWith("System", StringComparison.Ordinal))
                typeName = typeName[..^"System".Length];

            source = $"{scope}.auto_{ToSnakeCase(typeName)}";
            return true;
        }

        return false;
    }

    private static bool TryGetWh40KTypeScope(Type type, out string scope)
    {
        scope = string.Empty;

        var ns = type.Namespace;
        if (string.IsNullOrWhiteSpace(ns))
            return false;

        var tokenIndex = ns.IndexOf("._WH40K.", StringComparison.Ordinal);
        if (tokenIndex < 0)
            return false;

        var after = ns[(tokenIndex + "._WH40K.".Length)..];
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

    private void TryTrackNetEntity(SourceCounter counter, EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var meta = MetaData(uid);
        if (meta.NetEntity == NetEntity.Invalid)
            return;

        if (counter.UniqueNetEntities.Count >= MaxTrackedNetEntitiesPerSource &&
            !counter.UniqueNetEntities.Contains(meta.NetEntity.Id))
        {
            return;
        }

        counter.UniqueNetEntities.Add(meta.NetEntity.Id);
    }

    private void ParseScopes(string raw)
    {
        _enabledScopes.Clear();
        _allScopesEnabled = true;

        if (string.IsNullOrWhiteSpace(raw))
            return;

        var tokens = raw.Split([',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return;

        if (tokens.Any(token => token == "*"))
            return;

        _allScopesEnabled = false;
        foreach (var token in tokens)
        {
            _enabledScopes.Add(token);
        }
    }

    private static string GetScope(string source)
    {
        var separator = source.IndexOf('.');
        return separator < 0 ? source : source[..separator];
    }

    private sealed class SourceCounter
    {
        public int DirtyHits;
        public int ActivityHits;
        public EntityUid LastUid = EntityUid.Invalid;
        public readonly HashSet<int> UniqueNetEntities = new();
    }
}

public readonly record struct WH40KNetDiagSourceAttributionEntry(
    string Source,
    int DirtyHits,
    int ActivityHits,
    int UniqueNetEntities,
    EntityUid LastUid);
