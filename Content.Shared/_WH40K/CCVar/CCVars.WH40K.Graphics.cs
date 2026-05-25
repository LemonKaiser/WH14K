using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables the WH40K sprite wave post-shader for entities that opt into it.
    /// </summary>
    public static readonly CVarDef<bool> WH40KWaveShaderEnabled =
        CVarDef.Create("wh40k.wave_shader_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Enables the WH40K fullscreen post-process pass for additive lighting and falloff shaping.
    /// </summary>
    public static readonly CVarDef<bool> WH40KPostProcessEnabled =
        CVarDef.Create("wh40k.post_process_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
