using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Server-side contribution to the global warp instability pool.
/// Raised by any psyker/chaos action or ritual that should destabilize the warp.
/// </summary>
public sealed class WH40KWarpInstabilityContributionEvent : EntityEventArgs
{
    public EntityUid Performer { get; }
    public float Amount { get; }
    public string SourceKey { get; }

    public WH40KWarpInstabilityContributionEvent(EntityUid performer, float amount, string sourceKey)
    {
        Performer = performer;
        Amount = amount;
        SourceKey = sourceKey;
    }
}