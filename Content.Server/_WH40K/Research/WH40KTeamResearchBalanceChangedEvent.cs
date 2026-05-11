using Robust.Shared.GameObjects;

namespace Content.Server._WH40K.Research;

public sealed class WH40KTeamResearchBalanceChangedEvent : EntityEventArgs
{
    public string TeamId { get; }
    public int Points { get; }
    public int Delta { get; }
    public string Source { get; }

    public WH40KTeamResearchBalanceChangedEvent(string teamId, int points, int delta, string source)
    {
        TeamId = teamId;
        Points = points;
        Delta = delta;
        Source = source;
    }
}
