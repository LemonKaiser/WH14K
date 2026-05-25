using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables verbose WH40K cinematic timeline trace logs (start, step transitions, stop).
    /// </summary>
    public static readonly CVarDef<bool> WH40KCinematicTrace =
        CVarDef.Create("wh40k.cinematic.trace", false, CVar.SERVERONLY);

    /// <summary>
    ///     Enables periodic WH40K net-buffer diagnostics logs (traffic bursts, blocked channels, dirty hot spots).
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagEnabled =
        CVarDef.Create("wh40k.netdiag.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Enables WH40K-local source attribution markers (who dirtied what) for net diagnostics.
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagAttributionEnabled =
        CVarDef.Create("wh40k.netdiag.attribution_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Comma-separated WH40K source scopes for attribution.
    ///     Empty value or "*" enables all scopes.
    /// </summary>
    public static readonly CVarDef<string> WH40KNetDiagAttributionScopes =
        CVarDef.Create("wh40k.netdiag.attribution_scopes", "*", CVar.SERVERONLY);

    /// <summary>
    ///     If true, auto-captures Dirty callsites for all WH40K systems via stack attribution.
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagAttributionAutoDirtyEnabled =
        CVarDef.Create("wh40k.netdiag.attribution_auto_dirty_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum stack depth inspected for automatic WH40K Dirty callsite attribution.
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagAttributionAutoDirtyStackDepth =
        CVarDef.Create("wh40k.netdiag.attribution_auto_dirty_stack_depth", 24, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between WH40K net diagnostics snapshots.
    /// </summary>
    public static readonly CVarDef<float> WH40KNetDiagSampleIntervalSeconds =
        CVarDef.Create("wh40k.netdiag.sample_interval_seconds", 1.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Outgoing throughput threshold (KiB/s) that marks a snapshot as a burst. 0 disables this trigger.
    /// </summary>
    public static readonly CVarDef<float> WH40KNetDiagBurstOutgoingKiBPerSecond =
        CVarDef.Create("wh40k.netdiag.burst_outgoing_kib_per_sec", 512f, CVar.SERVERONLY);

    /// <summary>
    ///     Outgoing packet-rate threshold (packets/s) that marks a snapshot as a burst. 0 disables this trigger.
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagBurstOutgoingPacketsPerSecond =
        CVarDef.Create("wh40k.netdiag.burst_outgoing_packets_per_sec", 1200, CVar.SERVERONLY);

    /// <summary>
    ///     If true, emits a diagnostics line every sampling window, not only on burst/blocked-channel cases.
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagTraceEverySample =
        CVarDef.Create("wh40k.netdiag.trace_every_sample", false, CVar.SERVERONLY);

    /// <summary>
    ///     Number of top entries to print in diagnostics sections (message types, entities, prototypes, clients).
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagTopEntries =
        CVarDef.Create("wh40k.netdiag.top_entries", 8, CVar.SERVERONLY);

    /// <summary>
    ///     Ping (ms) above which a channel is considered high-latency in diagnostics.
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagHighPingMs =
        CVarDef.Create("wh40k.netdiag.high_ping_ms", 220, CVar.SERVERONLY);

    /// <summary>
    ///     Maximum number of blocked channel details printed in one diagnostics line.
    /// </summary>
    public static readonly CVarDef<int> WH40KNetDiagMaxBlockedClientDetails =
        CVarDef.Create("wh40k.netdiag.max_blocked_client_details", 6, CVar.SERVERONLY);

    /// <summary>
    ///     Warns if per-message bandwidth buckets are unavailable (for example non-DEBUG networking builds).
    /// </summary>
    public static readonly CVarDef<bool> WH40KNetDiagWarnNoTypeMetrics =
        CVarDef.Create("wh40k.netdiag.warn_no_type_metrics", true, CVar.SERVERONLY);
}
