using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.PropHunt;

[Serializable, NetSerializable]
public sealed class WH40KPropHuntRoleCountEvent : EntityEventArgs
{
    public bool Visible { get; }
    public int RoundId { get; }
    public int SeekerCount { get; }
    public int HiderCount { get; }

    public WH40KPropHuntRoleCountEvent(bool visible, int roundId, int seekerCount, int hiderCount)
    {
        Visible = visible;
        RoundId = roundId;
        SeekerCount = seekerCount;
        HiderCount = hiderCount;
    }
}
