using System;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Shared constants and helper math for Imperium astral progression.
/// Keeps strain scaling identical between client validation and server runtime.
/// </summary>
public static class WH40KPsykerAstralMath
{
    public const string AstralProjectionActionId = "ActionWH40KPsykerAstralProjection";
    public const float MaxAstralStrain = 25f;
    public const float WarpCostPerStrain = 0.02f;
    public const float WarpInstabilityPerStrain = 0.03f;
    public const float MaxWarpCostMultiplierBonus = 0.5f;
    public const float MaxWarpInstabilityMultiplierBonus = 0.75f;
    public static readonly TimeSpan AstralSleepIntroDuration = TimeSpan.FromSeconds(1.25f);

    public static float ClampAstralStrain(float strain)
    {
        return Math.Clamp(strain, 0f, MaxAstralStrain);
    }

    public static float GetWarpCostMultiplier(float strain)
    {
        var normalized = ClampAstralStrain(strain);
        return 1f + MathF.Min(MaxWarpCostMultiplierBonus, normalized * WarpCostPerStrain);
    }

    public static float GetWarpInstabilityMultiplier(float strain)
    {
        var normalized = ClampAstralStrain(strain);
        return 1f + MathF.Min(MaxWarpInstabilityMultiplierBonus, normalized * WarpInstabilityPerStrain);
    }
}
