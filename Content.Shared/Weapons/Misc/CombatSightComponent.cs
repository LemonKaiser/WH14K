using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Misc;

/// <summary>
/// Lets held items override the combat reticle drawn around the cursor.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CombatSightComponent : Component
{
    /// <summary>
    /// Reticle used while the weapon is available to fire / attack.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier? Sight;

    /// <summary>
    /// Reticle used while the weapon is temporarily unavailable.
    /// Currently intended for things like open-bolt / not-ready guns.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier? Unavailable;
}
