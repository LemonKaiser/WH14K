using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Tau.Drones.Components;

[RegisterComponent]
public sealed partial class WH40KTauDroneControllerComponent : Component
{
    [DataField("aggressionEnabled")]
    public bool AggressionEnabled = true;

    [DataField("toggleAction")]
    public EntProtoId ToggleAction = "ActionWH40KTauToggleDroneAggression";

    [DataField("toggleActionEntity")]
    public EntityUid? ToggleActionEntity;
}
