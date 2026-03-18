using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Components;

/// <summary>
/// Marks cargo pallets that are assigned to a specific cargo account.
/// Used by delayed batch deliveries.
/// </summary>
[RegisterComponent]
public sealed partial class CargoOrderBatchPalletComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CargoAccountPrototype> Account = "Cargo";
}
