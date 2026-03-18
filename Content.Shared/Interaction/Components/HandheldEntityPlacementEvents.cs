using Content.Shared.DoAfter;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Interaction.Components;

/// <summary>
/// Server-side placement validation hook for items using <see cref="HandheldEntityPlacementComponent"/>.
/// Handlers should configure delay / do-after behavior and can cancel invalid placements.
/// </summary>
public sealed partial class HandheldEntityPlacementAttemptEvent : CancellableEntityEventArgs
{
    public EntityUid User;
    public EntityCoordinates Coordinates;
    public Direction Direction;

    public TimeSpan DeployDelay = TimeSpan.Zero;
    public bool BreakOnMove = true;
    public bool BreakOnDamage;
    public bool BreakOnHandChange = true;
    public bool NeedHand = true;

    public HandheldEntityPlacementAttemptEvent(EntityUid user, EntityCoordinates coordinates, Direction direction)
    {
        User = user;
        Coordinates = coordinates;
        Direction = direction;
    }
}

/// <summary>
/// Server-side placement completion hook fired after placement do-after finishes and baseline checks pass.
/// Handlers should execute placement logic and set <see cref="HandledEntityEventArgs.Handled"/> on success.
/// </summary>
public sealed partial class HandheldEntityPlacementCompleteEvent : HandledEntityEventArgs
{
    public EntityUid User;
    public EntityCoordinates Coordinates;
    public Direction Direction;

    public HandheldEntityPlacementCompleteEvent(EntityUid user, EntityCoordinates coordinates, Direction direction)
    {
        User = user;
        Coordinates = coordinates;
        Direction = direction;
    }
}

[Serializable, NetSerializable]
public sealed partial class HandheldEntityPlacementDoAfterEvent : DoAfterEvent
{
    public NetCoordinates Coordinates;
    public Direction Direction = Direction.Invalid;

    public HandheldEntityPlacementDoAfterEvent()
    {
    }

    public HandheldEntityPlacementDoAfterEvent(NetCoordinates coordinates, Direction direction)
    {
        Coordinates = coordinates;
        Direction = direction;
    }

    public override DoAfterEvent Clone()
    {
        return new HandheldEntityPlacementDoAfterEvent(Coordinates, Direction);
    }
}
