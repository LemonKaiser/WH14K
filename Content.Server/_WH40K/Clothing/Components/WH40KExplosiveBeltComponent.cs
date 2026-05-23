using Content.Server._WH40K.Clothing;

namespace Content.Server._WH40K.Clothing.Components;

[RegisterComponent, Access(typeof(WH40KExplosiveBeltSystem))]
public sealed partial class WH40KExplosiveBeltComponent : Component
{
    [DataField]
    public float DefaultDelaySeconds = 1f;

    public EntityUid? Wearer;
}
