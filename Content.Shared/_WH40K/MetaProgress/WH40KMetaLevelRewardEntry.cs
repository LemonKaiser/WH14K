using System.Collections.Generic;

namespace Content.Shared._WH40K.MetaProgress;

[DataDefinition]
public sealed partial class WH40KMetaLevelRewardEntry
{
    [DataField("level", required: true)]
    public int Level;

    [DataField("decorations")]
    public int Decorations;

    [DataField("skillPoints")]
    public int SkillPoints;

    [DataField("unlockIds")]
    public List<string> UnlockIds = new();

    [DataField("requiredAchievements")]
    public List<string> RequiredAchievements = new();
}
