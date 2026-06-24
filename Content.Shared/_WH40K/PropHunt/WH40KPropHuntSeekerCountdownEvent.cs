using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.PropHunt;

[Serializable, NetSerializable]
public sealed class WH40KPropHuntSeekerCountdownEvent : EntityEventArgs
{
    public bool Active { get; }
    public int RoundId { get; }
    public int RemainingSeconds { get; }

    public WH40KPropHuntSeekerCountdownEvent(bool active, int roundId, int remainingSeconds)
    {
        Active = active;
        RoundId = roundId;
        RemainingSeconds = remainingSeconds;
    }
}
