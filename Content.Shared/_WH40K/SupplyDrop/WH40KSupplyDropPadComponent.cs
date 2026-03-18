using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SupplyDrop;

[RegisterComponent]
[Access(typeof(SharedWH40KSupplyDropSystem))]
public sealed partial class WH40KSupplyDropPadComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CargoAccountPrototype> Account = "WH40KImperium";

    [DataField]
    public string TeamId = string.Empty;

    [DataField]
    public EntProtoId CratePrototype = "WH40KSupplyDropAmmoCrate";

    [DataField]
    public EntProtoId? MarkerPrototype = "WH40KSupplyDropParachuteCrateVisual";

    [DataField]
    public int Cost = 4000;

    [DataField]
    public float CooldownSeconds = 90f;

    [DataField]
    public float DropDelaySeconds = 5f;

    public TimeSpan LastLaunchAt;
    public TimeSpan NextLaunchAt;
    public TimeSpan NextUiRefresh;
}
