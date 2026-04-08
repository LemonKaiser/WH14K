using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WH40K.Tank;

[RegisterComponent]
public sealed partial class WH40KTankTrackVisualizerComponent : Component
{
    [DataField("idleState", required: true)]
    public string IdleState = default!;

    [DataField("movingState", required: true)]
    public string MovingState = default!;
}