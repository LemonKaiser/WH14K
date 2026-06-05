using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Combat.PhantomStep;

[Serializable, NetSerializable]
public sealed class WH40KPhantomStepTrailEvent(
    NetEntity entity,
    NetCoordinates start,
    NetCoordinates end,
    float duration,
    float trailLifetime,
    int copies) : EntityEventArgs
{
    public NetEntity Entity { get; } = entity;
    public NetCoordinates Start { get; } = start;
    public NetCoordinates End { get; } = end;
    public float Duration { get; } = duration;
    public float TrailLifetime { get; } = trailLifetime;
    public int Copies { get; } = copies;
}
