using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.MetaProgress;

[Prototype("wh40kMetaLevelRewardTable")]
public sealed partial class WH40KMetaLevelRewardTablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultDecorations")]
    public int DefaultDecorations;

    [DataField("defaultSkillPoints")]
    public int DefaultSkillPoints;

    [DataField("entries")]
    public List<WH40KMetaLevelRewardEntry> Entries = new();
}
