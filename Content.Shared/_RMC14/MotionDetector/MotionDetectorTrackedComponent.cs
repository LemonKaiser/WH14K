using Robust.Shared.GameStates;
using Robust.Shared.Map;
using System.Numerics;

namespace Content.Shared._RMC14.MotionDetector;

[RegisterComponent, NetworkedComponent]
public sealed partial class MotionDetectorTrackedComponent : Component
{
    [DataField]
    public TimeSpan LastMove;

    [DataField]
    public bool Special;

    public List<MotionDetectorMoveSample> Samples { get; } = new();
}

public readonly record struct MotionDetectorMoveSample(TimeSpan Time, MapId MapId, Vector2 Position);
