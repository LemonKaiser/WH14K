using Content.Shared.Damage;

namespace Content.Shared._WH40K.Vehicle.Mech;

[RegisterComponent]
public sealed partial class WH40KMechPilotCatastrophicEjectComponent : Component
{
    [DataField]
    public float StunSeconds = 10f;

    [DataField]
    public DamageSpecifier Damage = new()
    {
        DamageDict = new()
        {
            { "Blunt", 24.0 },
            { "Heat", 23.0 },
            { "Piercing", 23.0 },
        },
    };
}
