using Content.Shared._WH40K.Command;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Command;

/// <summary>
/// Public mission outcome tier exposed to server listeners (meta progression, analytics, etc.).
/// </summary>
public enum WH40KMissionOutcomeTier : byte
{
    Major = 0,
    Minor = 1,
    Timeout = 2,
    Failure = 3
}

/// <summary>
/// Public mission objective kind exposed to server listeners (meta progression, analytics, etc.).
/// </summary>
public enum WH40KMissionObjectiveType : byte
{
    Unknown = 0,
    CargoDelivery = 1,
    ZoneControl = 2,
    BannerHold = 3
}

/// <summary>
/// Raised when mission runtime applies an outcome reward/effect to a concrete team.
/// </summary>
public sealed class WH40KMissionOutcomeAppliedEvent : EntityEventArgs
{
    public string TeamId { get; }
    public string MissionId { get; }
    public WH40KMissionObjectiveType ObjectiveType { get; }
    public WH40KCommandDynamicMissionScope Scope { get; }
    public WH40KMissionOutcomeTier Tier { get; }
    public int AwardedDevelopmentPoints { get; }
    public long MissionStartedAtTicks { get; }

    public WH40KMissionOutcomeAppliedEvent(
        string teamId,
        string missionId,
        WH40KMissionObjectiveType objectiveType,
        WH40KCommandDynamicMissionScope scope,
        WH40KMissionOutcomeTier tier,
        int awardedDevelopmentPoints,
        long missionStartedAtTicks)
    {
        TeamId = teamId;
        MissionId = missionId;
        ObjectiveType = objectiveType;
        Scope = scope;
        Tier = tier;
        AwardedDevelopmentPoints = awardedDevelopmentPoints;
        MissionStartedAtTicks = missionStartedAtTicks;
    }
}
