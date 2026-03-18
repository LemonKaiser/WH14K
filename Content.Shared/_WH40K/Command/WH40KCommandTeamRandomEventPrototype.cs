using Content.Shared._WH40K.GameMode;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandTeamRandomEventProfile")]
public sealed partial class WH40KCommandTeamRandomEventProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("rollIntervalSecondsMin")]
    public int RollIntervalSecondsMin = 480;

    [DataField("rollIntervalSecondsMax")]
    public int RollIntervalSecondsMax = 720;

    [DataField("antiRepeat")]
    public bool AntiRepeat = true;

    [DataField("maxActiveEventsPerTeam")]
    public int MaxActiveEventsPerTeam = 1;

    [DataField("maxRerolls")]
    public int MaxRerolls = 3;

    [DataField("events", required: true)]
    public List<WH40KCommandTeamRandomEventConfig> Events = new();
}

[DataDefinition]
public sealed partial class WH40KCommandTeamRandomEventConfig
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("title", required: true)]
    public string Title = string.Empty;

    [DataField("description", required: true)]
    public string Description = string.Empty;

    [DataField("baseWeight")]
    public float BaseWeight = 1f;

    [DataField("durationSeconds")]
    public int DurationSeconds = 90;

    [DataField("cooldownSeconds")]
    public int CooldownSeconds = 600;

    [DataField("allowedPhases")]
    public List<WH40KBattlePhase> AllowedPhases = new();

    [DataField("requiredSubsystems")]
    public List<WH40KCommandRuntimeSubsystem> RequiredSubsystems = new();

    [DataField("doctrineWeightBias")]
    public Dictionary<string, float> DoctrineWeightBias = new();

    [DataField("trailingWeightBonusPerLevelGap")]
    public float TrailingWeightBonusPerLevelGap = 0.08f;

    [DataField("maxTrailingWeightBonus")]
    public float MaxTrailingWeightBonus = 0.3f;

    [DataField("tags")]
    public List<string> Tags = new();
}

[Prototype("wh40kCommandTeamRandomEventTeamMap")]
public sealed partial class WH40KCommandTeamRandomEventTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandTeamRandomEventProfilePrototype> DefaultProfile =
        "WH40KCommandTeamRandomEventProfileDefault";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandTeamRandomEventProfilePrototype>> TeamProfiles = new();
}

[Serializable, NetSerializable]
public enum WH40KCommandRuntimeSubsystem : byte
{
    Cargo = 0,
    Reclaimer,
    OreExtractor,
    MissionBoard
}
