using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Oskvernitel;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KOskvernitelWeaponComponent : Component
{
    [DataField(required: true)]
    public EntProtoId MinigunPrototype;

    [DataField(required: true)]
    public EntProtoId AutogunPrototype;

    [DataField]
    public string WeaponContainerId = "wh40k-oskvernitel-weapon-container";

    [DataField, AutoNetworkedField]
    public EntityUid? MinigunEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? AutogunEntity;

    [DataField, AutoNetworkedField]
    public WH40KOskvernitelWeaponSlot SelectedWeapon = WH40KOskvernitelWeaponSlot.Minigun;
}

[Serializable, NetSerializable]
public enum WH40KOskvernitelWeaponSlot : byte
{
    Minigun,
    Autogun,
}
