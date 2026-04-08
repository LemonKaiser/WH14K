namespace Content.Shared.Clothing.Components;

/// <summary>
/// Forces the wearer to use armor explicitly marked as compatible with their species.
/// Used for species like Astartes that should not be able to wear standard armor.
/// </summary>
[RegisterComponent, Access(typeof(Content.Shared.Clothing.EntitySystems.SpeciesArmorRequirementSystem))]
public sealed partial class SpeciesArmorRequirementComponent : Component
{
    [DataField]
    public string Popup = "species-armor-requirement-component-restricted";
}