using Content.Shared.Explosion;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Weapons.Plasma;

[RegisterComponent]
public sealed partial class WH40KPlasmaOverheatComponent : Component
{
    [DataField]
    public float Chance = 0.05f;

    [DataField]
    public ProtoId<ExplosionPrototype> ExplosionType = "Plasma";

    [DataField]
    public float TotalIntensity = 8f;

    [DataField]
    public float IntensitySlope = 8f;

    [DataField]
    public float MaxTileIntensity = 8f;

    [DataField]
    public float TileBreakScale = 0f;

    [DataField]
    public int MaxTileBreak;

    [DataField]
    public bool CanCreateVacuum;

    [DataField]
    public bool DeleteWeapon = true;
}
