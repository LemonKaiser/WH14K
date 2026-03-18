using System.Collections.Generic;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Cargo.Components;

/// <summary>
/// Station-level cargo whitelist used by WH40K command-node unlocks.
/// Products not listed for an account stay hidden in its cargo console.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KCargoProductUnlocksComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<CargoAccountPrototype>, List<ProtoId<CargoProductPrototype>>> UnlockedProductsByAccount = new();

    /// <summary>
    /// Optional per-account research requirements for specific cargo products.
    /// Product stays hidden until all required technologies are unlocked by the account's team.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<CargoAccountPrototype>, Dictionary<ProtoId<CargoProductPrototype>, List<ProtoId<TechnologyPrototype>>>>
        ResearchRequirementsByAccount = new();
}
