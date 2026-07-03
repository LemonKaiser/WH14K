using Content.Shared._WH40K.StrategicPoints;
using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.StrategicPoints;

public readonly record struct WH40KStrategicPointAdminSnapshot(
    NetEntity Target,
    NetEntity Anchor,
    NetEntity BuiltPoint,
    string Callsign,
    WH40KStrategicPointType PointType,
    WH40KStrategicPointTier Tier,
    string OwnerTeamId,
    int TeamXpIncome,
    int InfluenceIncome,
    int ResearchIncome,
    int ArtifactIncome,
    int FundsIncome);
