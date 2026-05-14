using System;
using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.StrategicPoints;

public sealed class WH40KStrategicPointBuiltEvent : EntityEventArgs
{
    public EntityUid PointUid { get; }
    public EntityUid UserUid { get; }
    public string TeamId { get; }
    public WH40KStrategicPointType PointType { get; }
    public WH40KStrategicPointTier Tier { get; }

    public WH40KStrategicPointBuiltEvent(
        EntityUid pointUid,
        EntityUid userUid,
        string teamId,
        WH40KStrategicPointType pointType,
        WH40KStrategicPointTier tier)
    {
        PointUid = pointUid;
        UserUid = userUid;
        TeamId = teamId;
        PointType = pointType;
        Tier = tier;
    }
}

public sealed class WH40KStrategicPointUpgradedEvent : EntityEventArgs
{
    public EntityUid PointUid { get; }
    public EntityUid UserUid { get; }
    public string TeamId { get; }
    public WH40KStrategicPointType PointType { get; }
    public WH40KStrategicPointTier Tier { get; }

    public WH40KStrategicPointUpgradedEvent(
        EntityUid pointUid,
        EntityUid userUid,
        string teamId,
        WH40KStrategicPointType pointType,
        WH40KStrategicPointTier tier)
    {
        PointUid = pointUid;
        UserUid = userUid;
        TeamId = teamId;
        PointType = pointType;
        Tier = tier;
    }
}

public sealed class WH40KStrategicPointDestroyedEvent : EntityEventArgs
{
    public EntityUid PointUid { get; }
    public EntityUid AttackerUid { get; }
    public string AttackerTeamId { get; }
    public string OwnerTeamId { get; }
    public WH40KStrategicPointType PointType { get; }
    public WH40KStrategicPointTier Tier { get; }

    public WH40KStrategicPointDestroyedEvent(
        EntityUid pointUid,
        EntityUid attackerUid,
        string attackerTeamId,
        string ownerTeamId,
        WH40KStrategicPointType pointType,
        WH40KStrategicPointTier tier)
    {
        PointUid = pointUid;
        AttackerUid = attackerUid;
        AttackerTeamId = attackerTeamId;
        OwnerTeamId = ownerTeamId;
        PointType = pointType;
        Tier = tier;
    }
}

public sealed class WH40KStrategicPointTripleHoldCompletedEvent : EntityEventArgs
{
    public string TeamId { get; }
    public int OwnedPointCount { get; }
    public TimeSpan HeldDuration { get; }

    public WH40KStrategicPointTripleHoldCompletedEvent(string teamId, int ownedPointCount, TimeSpan heldDuration)
    {
        TeamId = teamId;
        OwnedPointCount = ownedPointCount;
        HeldDuration = heldDuration;
    }
}
