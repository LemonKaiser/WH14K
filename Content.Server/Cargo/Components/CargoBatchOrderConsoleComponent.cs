using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

namespace Content.Server.Cargo.Components;

/// <summary>
/// Enables delayed batch-order flow for a cargo console.
/// </summary>
[RegisterComponent]
public sealed partial class CargoBatchOrderConsoleComponent : Component
{
    /// <summary>
    /// Delay before an approved batch is delivered.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int BatchDelaySeconds = 600;

    /// <summary>
    /// Prototype spawned in the "orders in transit" list as batch summary row.
    /// </summary>
    [DataField]
    public EntProtoId SummaryProduct = "CrateGenericSteel";
}

