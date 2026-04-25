using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Weapons.Melee;

/// <summary>
/// Restricts activation of a toggleable item to specific humanoid species.
/// Carrying or using the item while it is off is unaffected.
/// </summary>
[RegisterComponent, Access(typeof(WH40KAstartesOnlyItemToggleSystem))]
public sealed partial class WH40KAstartesOnlyItemToggleComponent : Component
{
    [DataField(required: true)]
    public List<ProtoId<SpeciesPrototype>> Species = new();

    [DataField]
    public LocId Popup = "wh40k-astartes-only-power-sword";
}
