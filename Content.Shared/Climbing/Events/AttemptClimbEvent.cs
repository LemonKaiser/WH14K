namespace Content.Shared.Climbing.Events;

[ByRefEvent]
public record struct AttemptClimbEvent(EntityUid User, EntityUid Climber, EntityUid Climbable)
{
    public bool Cancelled;

    /// <summary>
    /// Optional override for current climb delay (in seconds) for this attempt.
    /// </summary>
    public float? Delay;
}
