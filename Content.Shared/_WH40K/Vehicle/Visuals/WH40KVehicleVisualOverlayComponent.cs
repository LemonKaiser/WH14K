using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Vehicle.Visuals;

[RegisterComponent]
public sealed partial class WH40KVehicleVisualOverlayComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Overlay = default!;

    [ViewVariables]
    public EntityUid? OverlayEntity;
}
