namespace Content.Server._WH40K.GameTicking.Rules;

public readonly record struct WH40KTeamEconomySnapshot(
    string TeamId,
    int TeamXp,
    int Influence,
    int ResearchPoints,
    int Funds,
    int BaseLevel,
    int? PointsToNextLevel);
