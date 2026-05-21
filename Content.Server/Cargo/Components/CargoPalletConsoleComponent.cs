using Content.Server.Cargo.Systems;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Components;

[RegisterComponent]
[Access(typeof(CargoSystem))]
public sealed partial class CargoPalletConsoleComponent : Component
{
    /// <summary>
    /// Legacy sale filter kept only for prototype and map compatibility.
    /// Sale consoles now accept any sellable entity regardless of this field.
    /// </summary>
    [DataField]
    public List<ProtoId<StackPrototype>> AcceptedStackTypes = new();
}
