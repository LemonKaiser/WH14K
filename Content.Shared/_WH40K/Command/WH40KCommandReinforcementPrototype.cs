using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command;

[DataDefinition]
public sealed partial class WH40KCommandReinforcementOptionPrototype
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("nameKey", required: true)]
    public string NameKey = string.Empty;

    [DataField("descriptionKey", required: true)]
    public string DescriptionKey = string.Empty;

    [DataField("job", required: true)]
    public ProtoId<JobPrototype> Job = default!;

    [DataField("previewPrototype", required: true)]
    public EntProtoId PreviewPrototype = default!;

    [DataField("baseCost")]
    public int BaseCost = 20;

    [DataField("maxCount")]
    public int MaxCount = 3;

    [DataField("additionalUnitCostMultiplier")]
    public float AdditionalUnitCostMultiplier = 0.55f;
}

[Prototype("wh40kCommandReinforcementProfile")]
public sealed partial class WH40KCommandReinforcementProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("teamId", required: true)]
    public string TeamId = string.Empty;

    [DataField("options", required: true)]
    public List<WH40KCommandReinforcementOptionPrototype> Options = new();
}

[Prototype("wh40kCommandReinforcementTeamMap")]
public sealed partial class WH40KCommandReinforcementTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandReinforcementProfilePrototype> DefaultProfile = "WH40KCommandReinforcementProfileImperium";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandReinforcementProfilePrototype>> TeamProfiles = new();
}
