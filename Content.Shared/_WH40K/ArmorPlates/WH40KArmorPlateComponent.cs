using Content.Shared.DoAfter;
using Content.Shared.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ArmorPlates;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true, raiseAfterAutoHandleState: true)]
public sealed partial class WH40KArmorPlateComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public WH40KArmorPlateType PlateType;

    [DataField(required: true), AutoNetworkedField]
    public int Tier;

    [DataField(required: true), AutoNetworkedField]
    public float BonusPercent;

    [DataField(required: true), AutoNetworkedField]
    public int MaxDurability;

    [DataField, AutoNetworkedField]
    public int CurrentDurability;

    [DataField(required: true), AutoNetworkedField]
    public float SpeedModifier = 1f;

    [DataField, AutoNetworkedField]
    public float RepairDelay = 1f;

    [DataField, AutoNetworkedField]
    public float RepairFuelCost;

    [DataField, AutoNetworkedField]
    public ProtoId<ToolQualityPrototype> RepairQuality = "Welding";

    public bool Broken => CurrentDurability <= 0;
}

[Serializable, NetSerializable]
public sealed partial class WH40KArmorPlateRepairDoAfterEvent : SimpleDoAfterEvent;
