using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Cinematic;

[Prototype("wh40kCinematicActorTrack")]
public sealed partial class WH40KCinematicActorTrackPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("segments", required: true)]
    public List<WH40KCinematicActorTrackSegmentDefinition> Segments = new();
}

[DataDefinition]
public sealed partial class WH40KCinematicActorTrackSegmentDefinition
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("entries", required: true)]
    public List<WH40KCinematicActorTrackEntryDefinition> Entries = new();
}

[DataDefinition]
public sealed partial class WH40KCinematicActorTrackEntryDefinition
{
    [DataField("at", required: true)]
    public float AtSeconds;

    [DataField("waitForCompletion")]
    public bool WaitForCompletion;

    [DataField("action", required: true)]
    public WH40KCinematicActionDefinition Action = new();
}
