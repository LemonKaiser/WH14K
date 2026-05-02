using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Cinematic;

public sealed class WH40KQueueCinematicEvent : EntityEventArgs
{
    public ProtoId<WH40KCinematicPrototype> CinematicId { get; }

    public WH40KQueueCinematicEvent(ProtoId<WH40KCinematicPrototype> cinematicId)
    {
        CinematicId = cinematicId;
    }
}

public sealed class WH40KStopCinematicEvent : EntityEventArgs
{
    public string Reason { get; }
    public bool MarkCompleted { get; }

    public WH40KStopCinematicEvent(string reason = "Stopped by event.", bool markCompleted = false)
    {
        Reason = reason;
        MarkCompleted = markCompleted;
    }
}
