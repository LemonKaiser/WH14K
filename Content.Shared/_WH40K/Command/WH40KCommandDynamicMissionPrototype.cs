using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandDynamicMissionProfile")]
public sealed partial class WH40KCommandDynamicMissionProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("enabled")]
    public bool Enabled = true;

    [DataField("firstSpawnAfterRoundStartSeconds")]
    public int FirstSpawnAfterRoundStartSeconds = 720;

    [DataField("respawnIntervalSecondsMin")]
    public int RespawnIntervalSecondsMin = 600;

    [DataField("respawnIntervalSecondsMax")]
    public int RespawnIntervalSecondsMax = 900;

    [DataField("maxActiveGlobalMissions")]
    public int MaxActiveGlobalMissions = 1;

    [DataField("maxActiveFactionMissionsPerTeam")]
    public int MaxActiveFactionMissionsPerTeam = 1;

    [DataField("missions", required: true)]
    public List<WH40KCommandDynamicMissionConfig> Missions = new();
}

[DataDefinition]
public sealed partial class WH40KCommandDynamicMissionConfig
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("scope")]
    public WH40KCommandDynamicMissionScope Scope = WH40KCommandDynamicMissionScope.Global;

    [DataField("teamId")]
    public string TeamId = string.Empty;

    [DataField("title", required: true)]
    public string Title = string.Empty;

    [DataField("description", required: true)]
    public string Description = string.Empty;

    [DataField("baseWeight")]
    public float BaseWeight = 1f;

    [DataField("durationSeconds")]
    public int DurationSeconds = 420;

    [DataField("requiredSubsystems")]
    public List<WH40KCommandRuntimeSubsystem> RequiredSubsystems = new();

    [DataField("tags")]
    public List<string> Tags = new();

    [DataField("rewardMajorDevelopmentPoints")]
    public int RewardMajorDevelopmentPoints = 14;

    [DataField("rewardMinorDevelopmentPoints")]
    public int RewardMinorDevelopmentPoints = 6;

    [DataField("rewardTimeoutDevelopmentPoints")]
    public int RewardTimeoutDevelopmentPoints = 1;

    [DataField("rewardFailureDevelopmentPoints")]
    public int RewardFailureDevelopmentPoints = 0;

    [DataField("rewardTempoBonusPercent")]
    public int RewardTempoBonusPercent = 0;

    [DataField("rewardTokenId")]
    public string RewardTokenId = string.Empty;

    [DataField("rewardTokenDurationSeconds")]
    public int RewardTokenDurationSeconds = 0;

    [DataField("objectiveType")]
    public WH40KCommandMissionObjectiveType ObjectiveType = WH40KCommandMissionObjectiveType.Auto;

    [DataField("objectiveRequiredEntityPrototypes")]
    public List<string> ObjectiveRequiredEntityPrototypes = new();
}

[Prototype("wh40kCommandDynamicMissionTeamMap")]
public sealed partial class WH40KCommandDynamicMissionTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandDynamicMissionProfilePrototype> DefaultProfile =
        "WH40KCommandDynamicMissionProfileDefault";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandDynamicMissionProfilePrototype>> TeamProfiles = new();
}

[Serializable, NetSerializable]
public enum WH40KCommandDynamicMissionScope : byte
{
    Global = 0,
    Faction = 1
}

[Serializable, NetSerializable]
public enum WH40KCommandMissionObjectiveType : byte
{
    Auto = 0,
    ZoneControl = 1,
    CargoDelivery = 2,
    BannerHold = 3
}
