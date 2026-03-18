using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Manipulator;

[Serializable, NetSerializable]
public sealed class WH40KManipulatorArcAnimationEvent : EntityEventArgs
{
    public readonly NetEntity Item;
    public readonly NetCoordinates Start;
    public readonly NetCoordinates End;
    public readonly float Duration;
    public readonly float ArcHeight;
    public readonly Angle InitialAngle;

    public WH40KManipulatorArcAnimationEvent(
        NetEntity item,
        NetCoordinates start,
        NetCoordinates end,
        float duration,
        float arcHeight,
        Angle initialAngle)
    {
        Item = item;
        Start = start;
        End = end;
        Duration = duration;
        ArcHeight = arcHeight;
        InitialAngle = initialAngle;
    }
}
