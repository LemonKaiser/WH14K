using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Medical;

[RegisterComponent]
public sealed partial class WH40KChirurgeonCprComponent : Component
{
    [DataField]
    public TimeSpan DoAfter = TimeSpan.FromSeconds(5);

    [DataField]
    public FixedPoint2 AsphyxiationHeal = FixedPoint2.New(10);

    [DataField]
    public float ReviveChance = 0.05f;

    [DataField]
    public FixedPoint2 ReviveBuffer = FixedPoint2.New(1);

    [DataField]
    public ProtoId<DamageTypePrototype> AsphyxiationDamageType = "Asphyxiation";
}

[Serializable, NetSerializable]
public sealed partial class WH40KChirurgeonCprDoAfterEvent : SimpleDoAfterEvent;
