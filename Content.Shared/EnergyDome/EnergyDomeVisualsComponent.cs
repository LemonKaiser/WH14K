namespace Content.Shared.EnergyDome;

/// <summary>
/// Marker for client-side dome integrity shader visualization.
/// </summary>
[RegisterComponent]
public sealed partial class EnergyDomeVisualsComponent : Component
{
    [DataField]
    public int Layer = 0;

    [DataField]
    public string Shader = "EnergyDomeDamageHoles";

    [DataField]
    public float DebugView = 0.0f;

    [DataField("effectStartCharge")]
    public float EffectStartCharge = 0.80f;

    [DataField("effectFullCharge")]
    public float EffectFullCharge = 0.01f;

    [DataField("maxCoverage")]
    public float MaxCoverage = 0.80f;

    [DataField("minCoverageFactor")]
    public float MinCoverageFactor = 0.12f;

    [DataField("coverageExponent")]
    public float CoverageExponent = 1.20f;

    [DataField("darkenCoreStrength")]
    public float DarkenCoreStrength = 0.42f;

    [DataField("darkenHazeStrength")]
    public float DarkenHazeStrength = 0.12f;

    [DataField("alphaCoreStrength")]
    public float AlphaCoreStrength = 0.24f;

    [DataField("alphaHazeStrength")]
    public float AlphaHazeStrength = 0.10f;

    [DataField("minColorFactor")]
    public float MinColorFactor = 0.18f;

    [DataField("minAlphaFactor")]
    public float MinAlphaFactor = 0.35f;

    [DataField("insideTransparencyEnabled")]
    public bool InsideTransparencyEnabled = true;

    [DataField("insideTransparencyAlpha")]
    public float InsideTransparencyAlpha = 0.45f;

    [DataField("insideTransparencyRadius")]
    public float InsideTransparencyRadius = 1.1f;

    [DataField("insideTransparencyFadeSpeed")]
    public float InsideTransparencyFadeSpeed = 7.5f;
}
