using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandMissionBoardProfile")]
public sealed partial class WH40KCommandMissionBoardProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("activeMissionTitleKey", required: true)]
    public string ActiveMissionTitleKey = string.Empty;

    [DataField("activeMissionDescriptionKey", required: true)]
    public string ActiveMissionDescriptionKey = string.Empty;

    [DataField("activeRewardBasePoints")]
    public int ActiveRewardBasePoints = 8;

    [DataField("activeRewardPerBaseLevel")]
    public int ActiveRewardPerBaseLevel = 4;

    [DataField("activeRewardMinPoints")]
    public int ActiveRewardMinPoints = 12;

    [DataField("activeTimerPreparationKey")]
    public string ActiveTimerPreparationKey = "w40k-cmd-mission-board-timer-prep";

    [DataField("activeTimerAssaultKey")]
    public string ActiveTimerAssaultKey = "w40k-cmd-mission-board-timer-active";

    [DataField("systemTasks", required: true)]
    public List<WH40KCommandMissionBoardSystemTaskConfig> SystemTasks = new();

    [DataField("selectableTasks", required: true)]
    public List<WH40KCommandMissionBoardSelectableTaskConfig> SelectableTasks = new();
}

[DataDefinition]
public sealed partial class WH40KCommandMissionBoardSystemTaskConfig
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("titleKey", required: true)]
    public string TitleKey = string.Empty;

    [DataField("rewardKey", required: true)]
    public string RewardKey = string.Empty;

    [DataField("descriptionKey", required: true)]
    public string DescriptionKey = string.Empty;

    [DataField("statusPreparation")]
    public WH40KCommandMissionBoardTaskStatus StatusPreparation = WH40KCommandMissionBoardTaskStatus.Pending;

    [DataField("statusAssault")]
    public WH40KCommandMissionBoardTaskStatus StatusAssault = WH40KCommandMissionBoardTaskStatus.Active;

    [DataField("statusApocalypse")]
    public WH40KCommandMissionBoardTaskStatus StatusApocalypse = WH40KCommandMissionBoardTaskStatus.Active;
}

[DataDefinition]
public sealed partial class WH40KCommandMissionBoardSelectableTaskConfig
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("titleKey", required: true)]
    public string TitleKey = string.Empty;

    [DataField("rewardKey", required: true)]
    public string RewardKey = string.Empty;

    [DataField("durationKey", required: true)]
    public string DurationKey = string.Empty;

    [DataField("descriptionKey", required: true)]
    public string DescriptionKey = string.Empty;
}

[Prototype("wh40kCommandMissionBoardTeamMap")]
public sealed partial class WH40KCommandMissionBoardTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandMissionBoardProfilePrototype> DefaultProfile = "WH40KCommandMissionBoardProfileDefault";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandMissionBoardProfilePrototype>> TeamProfiles = new();
}

[Serializable, NetSerializable]
public enum WH40KCommandMissionBoardTaskStatus : byte
{
    Pending,
    Active,
    Queued
}

[Serializable, NetSerializable]
public sealed class WH40KCommandMissionBoardSystemTaskState
{
    public string Id { get; }
    public string TitleKey { get; }
    public string RewardKey { get; }
    public string DescriptionKey { get; }
    public WH40KCommandMissionBoardTaskStatus Status { get; }

    public WH40KCommandMissionBoardSystemTaskState(
        string id,
        string titleKey,
        string rewardKey,
        string descriptionKey,
        WH40KCommandMissionBoardTaskStatus status)
    {
        Id = id;
        TitleKey = titleKey;
        RewardKey = rewardKey;
        DescriptionKey = descriptionKey;
        Status = status;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandMissionBoardSelectableTaskState
{
    public string Id { get; }
    public string TitleKey { get; }
    public string RewardKey { get; }
    public string DurationKey { get; }
    public string DescriptionKey { get; }

    public WH40KCommandMissionBoardSelectableTaskState(
        string id,
        string titleKey,
        string rewardKey,
        string durationKey,
        string descriptionKey)
    {
        Id = id;
        TitleKey = titleKey;
        RewardKey = rewardKey;
        DurationKey = durationKey;
        DescriptionKey = descriptionKey;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandMissionBoardState
{
    public string ActiveMissionTitleKey { get; }
    public string ActiveMissionDescriptionKey { get; }
    public int ActiveMissionRewardPoints { get; }
    public string ActiveTimerPreparationKey { get; }
    public string ActiveTimerAssaultKey { get; }
    public string SelectedTaskId { get; }
    public WH40KCommandMissionBoardSystemTaskState[] SystemTasks { get; }
    public WH40KCommandMissionBoardSelectableTaskState[] SelectableTasks { get; }

    public WH40KCommandMissionBoardState(
        string activeMissionTitleKey,
        string activeMissionDescriptionKey,
        int activeMissionRewardPoints,
        string activeTimerPreparationKey,
        string activeTimerAssaultKey,
        string selectedTaskId,
        WH40KCommandMissionBoardSystemTaskState[] systemTasks,
        WH40KCommandMissionBoardSelectableTaskState[] selectableTasks)
    {
        ActiveMissionTitleKey = activeMissionTitleKey;
        ActiveMissionDescriptionKey = activeMissionDescriptionKey;
        ActiveMissionRewardPoints = activeMissionRewardPoints;
        ActiveTimerPreparationKey = activeTimerPreparationKey;
        ActiveTimerAssaultKey = activeTimerAssaultKey;
        SelectedTaskId = selectedTaskId;
        SystemTasks = systemTasks;
        SelectableTasks = selectableTasks;
    }
}
