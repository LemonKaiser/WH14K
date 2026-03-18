using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Influence;

/// <summary>
/// Raised when an influence point is captured by a team.
/// </summary>
public sealed class WH40KInfluencePointCapturedEvent : EntityEventArgs
{
    public string TeamId { get; }
    public EntityUid PointUid { get; }

    public WH40KInfluencePointCapturedEvent(string teamId, EntityUid pointUid)
    {
        TeamId = teamId;
        PointUid = pointUid;
    }
}

/// <summary>
/// Raised when an owned influence point yields periodic defense reward for a team.
/// </summary>
public sealed class WH40KInfluencePointRewardTickEvent : EntityEventArgs
{
    public string TeamId { get; }
    public EntityUid PointUid { get; }
    public int FrontPointReward { get; }

    public WH40KInfluencePointRewardTickEvent(string teamId, EntityUid pointUid, int frontPointReward)
    {
        TeamId = teamId;
        PointUid = pointUid;
        FrontPointReward = frontPointReward;
    }
}
