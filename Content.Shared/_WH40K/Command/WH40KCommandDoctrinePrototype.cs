using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandDoctrineProfile")]
public sealed partial class WH40KCommandDoctrineProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("unlockLevel")]
    public int UnlockLevel = 3;

    [DataField("defaultDoctrineId")]
    public string DefaultDoctrineId = "doctrine_adaptive_reserve";

    [DataField("doctrines", required: true)]
    public List<WH40KCommandDoctrineConfig> Doctrines = new();
}

[DataDefinition]
public sealed partial class WH40KCommandDoctrineConfig
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("nameImperiumKey", required: true)]
    public string NameImperiumKey = string.Empty;

    [DataField("nameHereticsKey", required: true)]
    public string NameHereticsKey = string.Empty;

    [DataField("briefFocusKey", required: true)]
    public string BriefFocusKey = string.Empty;

    [DataField("briefEffectKey", required: true)]
    public string BriefEffectKey = string.Empty;

    [DataField("debuffKey", required: true)]
    public string DebuffKey = string.Empty;

    [DataField("summaryKey", required: true)]
    public string SummaryKey = string.Empty;

    [DataField("positiveKey", required: true)]
    public string PositiveKey = string.Empty;

    [DataField("negativeKey", required: true)]
    public string NegativeKey = string.Empty;

    [DataField("lockKey", required: true)]
    public string LockKey = string.Empty;

    [DataField("fullBriefingKey", required: true)]
    public string FullBriefingKey = string.Empty;

    [DataField("themeImperiumKey", required: true)]
    public string ThemeImperiumKey = string.Empty;

    [DataField("themeHereticsKey", required: true)]
    public string ThemeHereticsKey = string.Empty;

    [DataField("lockedDomain")]
    public string LockedDomain = string.Empty;

    [DataField("isNeutral")]
    public bool IsNeutral;
}

[Prototype("wh40kCommandDoctrineTeamMap")]
public sealed partial class WH40KCommandDoctrineTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandDoctrineProfilePrototype> DefaultProfile = "WH40KCommandDoctrineProfileDefault";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandDoctrineProfilePrototype>> TeamProfiles = new();
}
