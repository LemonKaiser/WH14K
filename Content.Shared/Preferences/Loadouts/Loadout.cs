using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Preferences.Loadouts;

/// <summary>
/// Specifies the selected prototype and custom data for a loadout.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class Loadout : IEquatable<Loadout>
{
    [DataField]
    public ProtoId<LoadoutPrototype> Prototype;

    /// <summary>
    /// WH40K: selected weapon mods for this loadout, keyed by the host's slot definition Id
    /// (e.g. "optic", "muzzle", "stock"). Value is the mod entity prototype Id.
    /// Only meaningful when the loadout's equipment/dummy entity has a <see cref="Content.Shared._WH40K.Weapons.Mods.WH40KWeaponModHostComponent"/>.
    /// </summary>
    [DataField]
    public Dictionary<string, string> SelectedMods = new();

    public bool Equals(Loadout? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Prototype.Equals(other.Prototype);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is Loadout other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Prototype.GetHashCode();
    }
}
