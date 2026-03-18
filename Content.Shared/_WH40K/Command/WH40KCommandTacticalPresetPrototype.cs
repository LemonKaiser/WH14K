using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandTacticalPresetProfile")]
public sealed partial class WH40KCommandTacticalPresetProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("forecastCount")]
    public int ForecastCount = 3;

    [DataField("activeDurationMinSeconds")]
    public int ActiveDurationMinSeconds = 75;

    [DataField("activeDurationMaxSeconds")]
    public int ActiveDurationMaxSeconds = 140;

    [DataField("queueDurationMinSeconds")]
    public int QueueDurationMinSeconds = 65;

    [DataField("queueDurationMaxSeconds")]
    public int QueueDurationMaxSeconds = 140;

    [DataField("queueEtaBaseSeconds")]
    public int QueueEtaBaseSeconds = 35;

    [DataField("queueEtaStepSeconds")]
    public int QueueEtaStepSeconds = 55;

    [DataField("queueEtaJitterSeconds")]
    public int QueueEtaJitterSeconds = 20;

    [DataField("queueChancePenaltyPerIndex")]
    public int QueueChancePenaltyPerIndex = 2;

    [DataField("presets", required: true)]
    public List<WH40KCommandTacticalPresetConfig> Presets = new();
}

[DataDefinition]
public sealed partial class WH40KCommandTacticalPresetConfig
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("titleKey", required: true)]
    public string TitleKey = string.Empty;

    [DataField("descriptionKey", required: true)]
    public string DescriptionKey = string.Empty;

    [DataField("chancePreparation")]
    public int ChancePreparation = 16;

    [DataField("chanceAssault")]
    public int ChanceAssault = 16;

    [DataField("chanceApocalypse")]
    public int ChanceApocalypse = 16;
}

[Prototype("wh40kCommandTacticalPresetTeamMap")]
public sealed partial class WH40KCommandTacticalPresetTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandTacticalPresetProfilePrototype> DefaultProfile = "WH40KCommandTacticalPresetProfileDefault";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandTacticalPresetProfilePrototype>> TeamProfiles = new();
}
