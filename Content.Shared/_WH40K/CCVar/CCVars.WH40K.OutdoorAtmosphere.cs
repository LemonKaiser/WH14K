using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables ambient outdoor atmosphere recovery for unroofed tiles on non-space maps.
    /// </summary>
    public static readonly CVarDef<bool> WH40KOutdoorAtmosphereEnabled =
        CVarDef.Create("wh40k.outdoor_atmosphere.enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between outdoor atmosphere recovery passes.
    /// </summary>
    public static readonly CVarDef<float> WH40KOutdoorAtmosphereIntervalSeconds =
        CVarDef.Create("wh40k.outdoor_atmosphere.interval_seconds", 2.0f, CVar.SERVERONLY);

    /// <summary>
    ///     Fraction of gas difference corrected per outdoor recovery pass.
    /// </summary>
    public static readonly CVarDef<float> WH40KOutdoorAtmosphereBlendFactor =
        CVarDef.Create("wh40k.outdoor_atmosphere.blend_factor", 0.5f, CVar.SERVERONLY);

    /// <summary>
    ///     Fraction of temperature difference corrected per outdoor recovery pass.
    /// </summary>
    public static readonly CVarDef<float> WH40KOutdoorAtmosphereTemperatureBlendFactor =
        CVarDef.Create("wh40k.outdoor_atmosphere.temperature_blend_factor", 0.25f, CVar.SERVERONLY);
}
