using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Psyker;

/// <summary>
/// Optional per-entity warp participation overrides for administrative tuning and prototype configuration.
/// Global warp runtime settings still come from CVars; this component only adjusts how a specific entity interacts with that runtime.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KWarpControlComponent : Component
{
    /// <summary>
    /// Multiplies instability contributions raised by this entity.
    /// </summary>
    [DataField("contributionMultiplier")]
    public float ContributionMultiplier = 1f;

    /// <summary>
    /// Flat amount added after the contribution multiplier is applied.
    /// Negative values can suppress or soften specific contributors.
    /// </summary>
    [DataField("flatContributionBonus")]
    public float FlatContributionBonus;

    /// <summary>
    /// Bias added to the shared instability value when selecting a personal backlash tier for this entity.
    /// Positive values unlock harsher tiers earlier; negative values delay them.
    /// </summary>
    [DataField("personalBacklashThresholdBias")]
    public float PersonalBacklashThresholdBias;

    /// <summary>
    /// If true, personal backlash is skipped for this entity.
    /// </summary>
    [DataField("ignorePersonalBacklash")]
    public bool IgnorePersonalBacklash;

    /// <summary>
    /// If true, this entity is ignored when global warp pulse effects target living actors or corpses.
    /// </summary>
    [DataField("ignoreGlobalPulseEffects")]
    public bool IgnoreGlobalPulseEffects;

    /// <summary>
    /// If true, the entity is spared from the max-instability catastrophe sacrifice sweep.
    /// </summary>
    [DataField("ignoreCatastropheSacrifice")]
    public bool IgnoreCatastropheSacrifice;
}
