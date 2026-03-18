using Robust.Shared.Map;
using Robust.Shared.Serialization;
using System.Numerics;

namespace Content.Shared._RMC14.MotionDetector;

[Serializable, NetSerializable]
public readonly record struct Blip(MapCoordinates Coordinates, bool Special, Vector2 Direction);
