using Content.Server.Cargo.Systems;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Components;

[RegisterComponent]
[Access(typeof(CargoSystem))]
public sealed partial class CargoPalletConsoleComponent : Component
{
    /// <summary>
    /// Optional list of allowed stack types for this sale console.
    /// If empty, any sellable entity is accepted.
    /// </summary>
    [DataField]
    public List<ProtoId<StackPrototype>> AcceptedStackTypes = new();
}
