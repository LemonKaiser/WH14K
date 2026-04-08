using System;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Tank;

[Serializable, NetSerializable]
public sealed class WH40KTankAimRequestEvent(MapCoordinates target) : EntityEventArgs
{
    public MapCoordinates Target { get; } = target;
}

[Serializable, NetSerializable]
public sealed class WH40KTankFireMainGunRequestEvent(MapCoordinates target) : EntityEventArgs
{
    public MapCoordinates Target { get; } = target;
}
