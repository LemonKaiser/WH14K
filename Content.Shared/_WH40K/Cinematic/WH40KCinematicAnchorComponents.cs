using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Cinematic;

[RegisterComponent]
public sealed partial class WH40KCinematicActionAnchorComponent : Component
{
    [DataField("anchorId", required: true)]
    public string AnchorId = string.Empty;
}

[RegisterComponent]
public sealed partial class WH40KCinematicSpawnAnchorComponent : Component
{
    [DataField("anchorId", required: true)]
    public string AnchorId = string.Empty;
}

[RegisterComponent]
public sealed partial class WH40KCinematicSoundAnchorComponent : Component
{
    [DataField("anchorId", required: true)]
    public string AnchorId = string.Empty;
}

[RegisterComponent]
public sealed partial class WH40KCinematicNpcAnchorComponent : Component
{
    [DataField("anchorId", required: true)]
    public string AnchorId = string.Empty;

    [DataField("rotation")]
    public float RotationDegrees;

    [DataField("defaultPrototype")]
    public EntProtoId? DefaultPrototype;

    [DataField("defaultStartingGear")]
    public ProtoId<StartingGearPrototype>? DefaultStartingGear;

    [DataField("defaultFactionId")]
    public string? DefaultFactionId;
}
