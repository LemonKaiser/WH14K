using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.StrategicPoints;

[Serializable, NetSerializable]
public enum WH40KStrategicPointType : byte
{
    Resource = 0,
    Research = 1,
    Influence = 2
}

[Serializable, NetSerializable]
public enum WH40KStrategicPointTier : byte
{
    T0 = 0,
    T1 = 1,
    T2 = 2,
    T3 = 3
}

[Serializable, NetSerializable]
public enum WH40KStrategicPointCurrency : byte
{
    TeamXp = 0,
    Funds = 1,
    Research = 2,
    Influence = 3,
    Artifact = 4
}

[Serializable, NetSerializable]
public enum WH40KStrategicPointVisuals : byte
{
    PointType = 0,
    Tier = 1,
    OwnerTeamId = 2,
    AnchorHidden = 3
}

[Serializable, NetSerializable]
public enum WH40KStrategicPointVisualLayers : byte
{
    Base = 0
}
