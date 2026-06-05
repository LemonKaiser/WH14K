using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server._WH40K.Weapons.ServoSkulls.Components;

[RegisterComponent]
public sealed partial class WH40KDeployServoSkullComponent : Component
{
    [DataField("mobPrototype", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string MobPrototype = string.Empty;

    [DataField("startFollowingOwner")]
    public bool StartFollowingOwner;
}
