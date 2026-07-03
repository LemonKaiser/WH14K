using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Weapons.Plasma;

[RegisterComponent, NetworkedComponent]
[Access(typeof(WH40KPlasmaFireModesSystem))]
[AutoGenerateComponentState]
public sealed partial class WH40KPlasmaFireModesComponent : Component
{
    [DataField(required: true)]
    [AutoNetworkedField]
    public List<WH40KPlasmaFireMode> FireModes = new();

    [DataField]
    [AutoNetworkedField]
    public int CurrentFireMode;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class WH40KPlasmaFireMode
{
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField("proto", required: true)]
    public EntProtoId Prototype = default!;

    [DataField]
    public float OverheatChance;
}
