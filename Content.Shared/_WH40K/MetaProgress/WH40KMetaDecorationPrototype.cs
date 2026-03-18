using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.MetaProgress;

[Prototype("wh40kMetaDecoration")]
public sealed partial class WH40KMetaDecorationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("category", required: true)]
    public WH40KMetaDecorationCategory Category;

    [DataField("title", required: true)]
    public string TitleKey = string.Empty;

    [DataField("preview")]
    public string PreviewKey = string.Empty;

    [DataField("requiredLevel")]
    public int RequiredLevel = 1;

    [DataField("requiredAchievements")]
    public List<string> RequiredAchievements = new();

    [DataField("requiredDiscordGuildMember")]
    public bool RequiredDiscordGuildMember;

    [DataField("requiredDiscordRoleIds")]
    public List<string> RequiredDiscordRoleIds = new();

    [DataField("adminOnly")]
    public bool AdminOnly;

    [DataField("oocColorHex")]
    public string OocColorHex = string.Empty;

    [DataField("oocGradientColors")]
    public List<string> OocGradientColors = new();

    [DataField("oocGradientAnimated")]
    public bool OocGradientAnimated;

    [DataField("oocGradientDurationMs")]
    public int OocGradientDurationMs = 3500;

    [DataField("oocAuraHex")]
    public string OocAuraHex = string.Empty;

    [DataField("oocAuraRadius")]
    public int OocAuraRadius;

    [DataField("oocAuraAlphaPercent")]
    public int OocAuraAlphaPercent = 65;

    [DataField("oocTitleEffect")]
    public string OocTitleEffect = string.Empty;

    [DataField("oocTitleEffectRevealMs")]
    public int OocTitleEffectRevealMs = 900;

    [DataField("oocTitleEffectHoldMs")]
    public int OocTitleEffectHoldMs = 10000;

    [DataField("oocTitleEffectDissolveMs")]
    public int OocTitleEffectDissolveMs = 900;

    [DataField("oocTitleOutlineHex")]
    public string OocTitleOutlineHex = string.Empty;

    [DataField("oocTitleOutlineWidth")]
    public int OocTitleOutlineWidth;

    [DataField("oocTitleOutlineAlphaPercent")]
    public int OocTitleOutlineAlphaPercent = 70;

    [DataField("ghostRsiPath")]
    public string GhostRsiPath = string.Empty;

    [DataField("ghostState")]
    public string GhostState = "animated";

    [DataField("ghostTintHex")]
    public string GhostTintHex = string.Empty;

    [DataField("defaultSelected")]
    public bool DefaultSelected;

    [DataField("suppressTitlePrefix")]
    public bool SuppressTitlePrefix;

    [DataField("sortOrder")]
    public int SortOrder;
}
