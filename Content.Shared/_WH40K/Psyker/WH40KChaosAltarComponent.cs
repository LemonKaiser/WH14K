namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Active ritual profile for the chaos altar sacrifice loop.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KChaosAltarComponent : Component
{
    [DataField("sacrificeXpReward")]
    public float SacrificeXpReward = 45f;

    [DataField("attunedSacrificeXpMultiplier")]
    public float AttunedSacrificeXpMultiplier = 1.2f;

    [DataField("ritualBoostMultiplier")]
    public float RitualBoostMultiplier = 1.35f;

    [DataField("ritualBoostDuration")]
    public TimeSpan RitualBoostDuration = TimeSpan.FromMinutes(2);

    [DataField("warpChargeRestore")]
    public float WarpChargeRestore = 20f;

    [DataField("instabilityGain")]
    public float InstabilityGain = 10f;

    [DataField("sacrificeCooldown")]
    public TimeSpan SacrificeCooldown = TimeSpan.FromSeconds(120);

    [DataField("consumeStackAmount")]
    public int ConsumeStackAmount = 1;

    [DataField("soulHarvestRange")]
    public float SoulHarvestRange = 5f;

    [DataField("soulXpSamePatron")]
    public float SoulXpSamePatron = 50f;

    [DataField("soulXpOtherPatron")]
    public float SoulXpOtherPatron = 10f;

    [DataField("requireAttunement")]
    public bool RequireAttunement = true;
}
