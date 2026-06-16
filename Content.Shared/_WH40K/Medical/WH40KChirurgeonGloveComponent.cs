using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Medical;

[RegisterComponent]
public sealed partial class WH40KChirurgeonGloveComponent : Component
{
    [DataField]
    public TimeSpan DoAfter = TimeSpan.FromSeconds(4);

    [DataField]
    public EntProtoId SkullPrototype = "WH40KHumanSkull";
}

[Serializable, NetSerializable]
public sealed partial class WH40KChirurgeonSkullExtractionDoAfterEvent : SimpleDoAfterEvent;

