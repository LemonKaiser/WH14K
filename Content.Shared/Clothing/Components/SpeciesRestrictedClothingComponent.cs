using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Clothing.Components;

/// <summary>
/// Restricts a clothing item to a fixed list of species when worn.
/// Pocketing or carrying the item is unaffected.
/// </summary>
[RegisterComponent,
 Access(typeof(Content.Shared.Clothing.EntitySystems.SpeciesRestrictedClothingSystem),
     typeof(Content.Shared.Clothing.EntitySystems.SpeciesArmorRequirementSystem))]
public sealed partial class SpeciesRestrictedClothingComponent : Component
{
    [DataField(required: true)]
    public List<ProtoId<SpeciesPrototype>> Species = new();

    [DataField]
    public string Popup = "species-restricted-clothing-component-restricted";
}