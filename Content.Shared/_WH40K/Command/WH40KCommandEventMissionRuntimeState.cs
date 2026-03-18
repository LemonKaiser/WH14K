using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Command;

[Serializable, NetSerializable]
public sealed class WH40KCommandEventCooldownRuntimeState
{
    public string EventId { get; }
    public string Title { get; }
    public string Description { get; }
    public int RemainingSeconds { get; }

    public WH40KCommandEventCooldownRuntimeState(
        string eventId,
        string title,
        string description,
        int remainingSeconds)
    {
        EventId = eventId;
        Title = title;
        Description = description;
        RemainingSeconds = remainingSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandTeamEventRuntimeState
{
    public bool HasProfile { get; }
    public bool HasActiveEvent { get; }
    public string ActiveEventId { get; }
    public string ActiveEventTitle { get; }
    public string ActiveEventDescription { get; }
    public int ActiveRemainingSeconds { get; }
    public int ActiveDurationSeconds { get; }
    public int NextRollSeconds { get; }
    public WH40KCommandEventCooldownRuntimeState[] Cooldowns { get; }

    public WH40KCommandTeamEventRuntimeState(
        bool hasProfile,
        bool hasActiveEvent,
        string activeEventId,
        string activeEventTitle,
        string activeEventDescription,
        int activeRemainingSeconds,
        int activeDurationSeconds,
        int nextRollSeconds,
        WH40KCommandEventCooldownRuntimeState[] cooldowns)
    {
        HasProfile = hasProfile;
        HasActiveEvent = hasActiveEvent;
        ActiveEventId = activeEventId;
        ActiveEventTitle = activeEventTitle;
        ActiveEventDescription = activeEventDescription;
        ActiveRemainingSeconds = activeRemainingSeconds;
        ActiveDurationSeconds = activeDurationSeconds;
        NextRollSeconds = nextRollSeconds;
        Cooldowns = cooldowns;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandMissionRuntimeState
{
    public bool IsActive { get; }
    public string MissionId { get; }
    public string MissionTitle { get; }
    public string MissionDescription { get; }
    public WH40KCommandDynamicMissionScope Scope { get; }
    public string TeamId { get; }
    public int RemainingSeconds { get; }
    public int DurationSeconds { get; }
    public int RewardMajorDevelopmentPoints { get; }
    public int RewardMinorDevelopmentPoints { get; }
    public int RewardTimeoutDevelopmentPoints { get; }
    public int RewardFailureDevelopmentPoints { get; }
    public int RewardTempoBonusPercent { get; }
    public string RewardTokenId { get; }
    public int RewardTokenDurationSeconds { get; }

    public WH40KCommandMissionRuntimeState(
        bool isActive,
        string missionId,
        string missionTitle,
        string missionDescription,
        WH40KCommandDynamicMissionScope scope,
        string teamId,
        int remainingSeconds,
        int durationSeconds,
        int rewardMajorDevelopmentPoints,
        int rewardMinorDevelopmentPoints,
        int rewardTimeoutDevelopmentPoints,
        int rewardFailureDevelopmentPoints,
        int rewardTempoBonusPercent,
        string rewardTokenId,
        int rewardTokenDurationSeconds)
    {
        IsActive = isActive;
        MissionId = missionId;
        MissionTitle = missionTitle;
        MissionDescription = missionDescription;
        Scope = scope;
        TeamId = teamId;
        RemainingSeconds = remainingSeconds;
        DurationSeconds = durationSeconds;
        RewardMajorDevelopmentPoints = rewardMajorDevelopmentPoints;
        RewardMinorDevelopmentPoints = rewardMinorDevelopmentPoints;
        RewardTimeoutDevelopmentPoints = rewardTimeoutDevelopmentPoints;
        RewardFailureDevelopmentPoints = rewardFailureDevelopmentPoints;
        RewardTempoBonusPercent = rewardTempoBonusPercent;
        RewardTokenId = rewardTokenId;
        RewardTokenDurationSeconds = rewardTokenDurationSeconds;
    }
}
