using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Raised on the performer entity after a warp action is processed by warp-resource runtime.
/// Used to drive role-specific progression without duplicating ActionPerformed subscriptions.
/// </summary>
public sealed class WH40KWarpActionCastEvent : EntityEventArgs
{
    public EntityUid Performer { get; }
    public EntityUid ActionEntity { get; }
    public string ActionKey { get; }

    public WH40KWarpActionCastEvent(EntityUid performer, EntityUid actionEntity, string actionKey)
    {
        Performer = performer;
        ActionEntity = actionEntity;
        ActionKey = actionKey;
    }
}
