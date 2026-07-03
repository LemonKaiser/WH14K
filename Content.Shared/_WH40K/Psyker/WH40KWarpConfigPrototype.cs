using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Psyker;

[Prototype("wh40kWarpConfig")]
public sealed partial class WH40KWarpConfigPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("runtime")]
    public WH40KWarpRuntimeConfig Runtime = new();

    [DataField("backlashThresholds")]
    public WH40KWarpBacklashThresholdConfig BacklashThresholds = new();

    [DataField("globalPulses")]
    public WH40KWarpGlobalPulseConfig GlobalPulses = new();

    [DataField("effects")]
    public WH40KWarpEffectConfig Effects = new();

    [DataField("psykerOverload")]
    public WH40KPsykerOverloadConfig PsykerOverload = new();
}

[DataDefinition]
public sealed partial class WH40KWarpRuntimeConfig
{
    [DataField("enabled")]
    public bool Enabled = true;

    [DataField("maxInstability")]
    public float MaxInstability = 1000f;

    [DataField("decayPerSecond")]
    public float DecayPerSecond = 1.2f;

    [DataField("personalBacklashEnabled")]
    public bool PersonalBacklashEnabled = true;

    [DataField("globalPulsesEnabled")]
    public bool GlobalPulsesEnabled = true;

    [DataField("catastropheEnabled")]
    public bool CatastropheEnabled = true;

    [DataField("highestTierChance")]
    public float HighestTierChance = 0.8f;
}

[DataDefinition]
public sealed partial class WH40KWarpBacklashThresholdConfig
{
    [DataField("mildBurn")]
    public float MildBurn = 350f;

    [DataField("stun")]
    public float Stun = 400f;

    [DataField("collapse")]
    public float Collapse = 500f;

    [DataField("drop")]
    public float Drop = 550f;

    [DataField("bleed")]
    public float Bleed = 600f;

    [DataField("doppelganger")]
    public float Doppelganger = 650f;

    [DataField("fleshRift")]
    public float FleshRift = 700f;

    [DataField("possession")]
    public float Possession = 800f;

    [DataField("mutation")]
    public float Mutation = 900f;
}

[DataDefinition]
public sealed partial class WH40KWarpGlobalPulseConfig
{
    [DataField("threshold500")]
    public float Threshold500 = 500f;

    [DataField("threshold550")]
    public float Threshold550 = 550f;

    [DataField("threshold600")]
    public float Threshold600 = 600f;

    [DataField("threshold650")]
    public float Threshold650 = 650f;

    [DataField("threshold700")]
    public float Threshold700 = 700f;

    [DataField("threshold750")]
    public float Threshold750 = 750f;

    [DataField("threshold800")]
    public float Threshold800 = 800f;

    [DataField("threshold850")]
    public float Threshold850 = 850f;

    [DataField("threshold900")]
    public float Threshold900 = 900f;

    [DataField("interval500Seconds")]
    public float Interval500Seconds = 60f;

    [DataField("interval600Seconds")]
    public float Interval600Seconds = 45f;

    [DataField("interval700Seconds")]
    public float Interval700Seconds = 30f;

    [DataField("interval800Seconds")]
    public float Interval800Seconds = 20f;

    [DataField("interval900Seconds")]
    public float Interval900Seconds = 11f;
}

[DataDefinition]
public sealed partial class WH40KWarpEffectConfig
{
    [DataField("mildBurnDamage")]
    public float MildBurnDamage = 10f;

    [DataField("stunDurationSeconds")]
    public float StunDurationSeconds = 1f;

    [DataField("stunDrunkennessSeconds")]
    public float StunDrunkennessSeconds = 10f;

    [DataField("collapseStunSeconds")]
    public float CollapseStunSeconds = 5f;

    [DataField("collapseDrunkennessSeconds")]
    public float CollapseDrunkennessSeconds = 20f;

    [DataField("bleedTarget")]
    public float BleedTarget = 5f;

    [DataField("dropMaxCount")]
    public int DropMaxCount = 3;

    [DataField("fleshRiftDemonChance")]
    public float FleshRiftDemonChance = 0.15f;

    [DataField("fleshRiftDeathChance")]
    public float FleshRiftDeathChance = 0.35f;

    [DataField("fleshRiftDeathDamage")]
    public float FleshRiftDeathDamage = 500f;

    [DataField("mutationMinSeverity")]
    public float MutationMinSeverity = 0.25f;

    [DataField("mutationMaxSeverity")]
    public float MutationMaxSeverity = 0.75f;
}

[DataDefinition]
public sealed partial class WH40KPsykerOverloadConfig
{
    [DataField("enabled")]
    public bool Enabled = true;

    [DataField("chance700")]
    public float Chance700 = 0.05f;

    [DataField("chance800")]
    public float Chance800 = 0.11f;

    [DataField("chance900")]
    public float Chance900 = 0.18f;

    [DataField("dropEquipment")]
    public bool DropEquipment = true;

    [DataField("announceGlobally")]
    public bool AnnounceGlobally = true;
}
