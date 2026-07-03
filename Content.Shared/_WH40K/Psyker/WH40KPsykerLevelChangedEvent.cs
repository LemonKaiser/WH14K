using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Raised when an Imperium psyker gains one or more progression levels.
/// </summary>
public sealed class WH40KPsykerLevelChangedEvent : EntityEventArgs
{
    public EntityUid Performer { get; }
    public int PreviousLevel { get; }
    public int CurrentLevel { get; }

    public WH40KPsykerLevelChangedEvent(EntityUid performer, int previousLevel, int currentLevel)
    {
        Performer = performer;
        PreviousLevel = previousLevel;
        CurrentLevel = currentLevel;
    }
}
