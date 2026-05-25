using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables WH40K projectile prediction reconciliation (client hit report + server validation).
    /// </summary>
    public static readonly CVarDef<bool> WH40KGunPrediction =
        CVarDef.Create("wh40k.gun_prediction", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum allowed deviation for client-reported coordinates around lag-compensated target position.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionCoordinateDeviation =
        CVarDef.Create("wh40k.gun_prediction_coordinate_deviation", 1.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Secondary wider deviation window for older lag-comp snapshot fallback.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionLowestCoordinateDeviation =
        CVarDef.Create("wh40k.gun_prediction_lowest_coordinate_deviation", 1.5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Expands server-side fixture bounds during prediction validation to reduce false negatives.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionAabbEnlargement =
        CVarDef.Create("wh40k.gun_prediction_aabb_enlargement", 0.1f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum age (seconds) for a predicted hit report since projectile spawn. 0 disables the age gate.
    /// </summary>
    public static readonly CVarDef<float> WH40KGunPredictionMaxReportAgeSeconds =
        CVarDef.Create("wh40k.gun_prediction_max_report_age_seconds", 2.0f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum number of targets accepted in one predicted hit report payload.
    /// </summary>
    public static readonly CVarDef<int> WH40KGunPredictionMaxHitsPerReport =
        CVarDef.Create("wh40k.gun_prediction_max_hits_per_report", 8, CVar.SERVERONLY);

    /// <summary>
    ///     Enables debug logs for rejected WH40K predicted projectile hit reports.
    /// </summary>
    public static readonly CVarDef<bool> WH40KGunPredictionLogRejectedHits =
        CVarDef.Create("wh40k.gun_prediction_log_rejected_hits", false, CVar.SERVERONLY);
}
