using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.MetaProgress;

[Prototype("wh40kMetaAchievement")]
public sealed partial class WH40KMetaAchievementPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("category", required: true)]
    public WH40KMetaAchievementCategory Category;

    [DataField("title", required: true)]
    public string TitleKey = string.Empty;

    [DataField("description", required: true)]
    public string DescriptionKey = string.Empty;

    [DataField("task", required: true)]
    public string TaskKey = string.Empty;

    [DataField("reward")]
    public string RewardKey = "wh40k-meta-progress-achievements-reward-none";

    [DataField("target", required: true)]
    public int Target = 1;

    [DataField("progressStatKey")]
    public string ProgressStatKey = string.Empty;

    [DataField("progressScope")]
    public WH40KMetaAchievementProgressScope ProgressScope = WH40KMetaAchievementProgressScope.Lifetime;

    [DataField("roundBlockerStatKeys")]
    public List<string> RoundBlockerStatKeys = new();

    [DataField("hidden")]
    public bool Hidden;

    [DataField("sortOrder")]
    public int SortOrder;
}
