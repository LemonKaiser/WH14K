using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Roadmap;

[Prototype("wh40kRoadmap")]
public sealed partial class WH40KRoadmapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("revision")]
    public int Revision = 1;

    [DataField("now")]
    public List<WH40KRoadmapTaskEntry> Now = new();

    [DataField("next")]
    public List<WH40KRoadmapTaskEntry> Next = new();

    [DataField("later")]
    public List<WH40KRoadmapTaskEntry> Later = new();
}

[DataDefinition]
public sealed partial class WH40KRoadmapTaskEntry
{
    [DataField("title", required: true)]
    public LocId Title = string.Empty;

    [DataField("description")]
    public LocId? Description;

    [DataField("state")]
    public WH40KRoadmapTaskState State = WH40KRoadmapTaskState.Planned;

    [DataField("sortOrder")]
    public int SortOrder;
}

public enum WH40KRoadmapTaskState : byte
{
    Planned = 0,
    InProgress = 1,
    Complete = 2
}
