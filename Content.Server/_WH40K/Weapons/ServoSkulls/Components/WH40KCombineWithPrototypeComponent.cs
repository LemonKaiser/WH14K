using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server._WH40K.Weapons.ServoSkulls.Components;

[RegisterComponent]
public sealed partial class WH40KCombineWithPrototypeComponent : Component
{
    [DataField("requiredPrototype", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string RequiredPrototype = string.Empty;

    [DataField("resultPrototype", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string ResultPrototype = string.Empty;

    [DataField("pickupResult")]
    public bool PickupResult = true;
}
