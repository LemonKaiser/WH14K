using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Interaction.Components;

/// <summary>
/// Request to start fold do-after for an entity that supports handheld placement fold flow.
/// </summary>
public sealed partial class HandheldEntityFoldRequestEvent : HandledEntityEventArgs
{
    public EntityUid User;

    public HandheldEntityFoldRequestEvent(EntityUid user)
    {
        User = user;
    }
}

/// <summary>
/// Server-side fold validation hook fired before starting fold do-after.
/// </summary>
public sealed partial class HandheldEntityFoldAttemptEvent : CancellableEntityEventArgs
{
    public EntityUid User;
    public TimeSpan FoldDelay = TimeSpan.Zero;

    public bool BreakOnMove = true;
    public bool BreakOnDamage;
    public bool BreakOnHandChange = true;
    public bool NeedHand = true;

    public HandheldEntityFoldAttemptEvent(EntityUid user)
    {
        User = user;
    }
}

/// <summary>
/// Server-side fold completion hook fired after fold do-after finishes.
/// </summary>
public sealed partial class HandheldEntityFoldCompleteEvent : HandledEntityEventArgs
{
    public EntityUid User;

    public HandheldEntityFoldCompleteEvent(EntityUid user)
    {
        User = user;
    }
}

[Serializable, NetSerializable]
public sealed partial class HandheldEntityFoldDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone()
    {
        return new HandheldEntityFoldDoAfterEvent();
    }
}
