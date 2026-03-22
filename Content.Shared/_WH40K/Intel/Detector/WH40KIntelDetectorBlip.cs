using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Intel.Detector;

[Serializable, NetSerializable]
public readonly record struct WH40KIntelDetectorBlip(MapCoordinates Coordinates, bool Special, Vector2 Direction);
