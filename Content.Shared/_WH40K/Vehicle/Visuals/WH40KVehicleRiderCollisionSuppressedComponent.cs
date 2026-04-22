using System.Collections.Generic;

namespace Content.Shared._WH40K.Vehicle.Visuals;

[RegisterComponent]
public sealed partial class WH40KVehicleRiderCollisionSuppressedComponent : Component
{
    public Dictionary<string, int> OriginalMasks { get; } = new();
}
