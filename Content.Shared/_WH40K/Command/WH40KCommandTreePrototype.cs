using Content.Shared.Cargo.Prototypes;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandTreeProfile")]
public sealed partial class WH40KCommandTreeProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("domains", required: true)]
    public List<WH40KCommandTreeDomainConfig> Domains = new();

    [DataField("nodes", required: true)]
    public List<WH40KCommandTreeNodeConfig> Nodes = new();
}

[DataDefinition]
public sealed partial class WH40KCommandTreeDomainConfig
{
    [DataField("id", required: true)]
    public string Id = string.Empty;
}

[DataDefinition]
public sealed partial class WH40KCommandTreeNodeConfig
{
    [DataField("id", required: true)]
    public string Id = string.Empty;

    [DataField("domain", required: true)]
    public string Domain = string.Empty;

    [DataField("titleKey", required: true)]
    public string TitleKey = string.Empty;

    [DataField("descriptionKey", required: true)]
    public string DescriptionKey = string.Empty;

    [DataField("parents")]
    public List<string> Parents = new();

    [DataField("cost")]
    public int Cost;

    [DataField("minBaseLevel")]
    public int MinBaseLevel = 1;

    [DataField("minRoundTimeSeconds")]
    public int MinRoundTimeSeconds;

    [DataField("technologyUnlocks")]
    public List<ProtoId<TechnologyPrototype>> TechnologyUnlocks = new();

    [DataField("teamTechnologyUnlocks")]
    public Dictionary<string, List<ProtoId<TechnologyPrototype>>> TeamTechnologyUnlocks = new();

    [DataField("latheRecipeUnlocks")]
    public List<ProtoId<LatheRecipePrototype>> LatheRecipeUnlocks = new();

    [DataField("teamLatheRecipeUnlocks")]
    public Dictionary<string, List<ProtoId<LatheRecipePrototype>>> TeamLatheRecipeUnlocks = new();

    [DataField("cargoProductUnlocks")]
    public List<ProtoId<CargoProductPrototype>> CargoProductUnlocks = new();

    [DataField("teamCargoProductUnlocks")]
    public Dictionary<string, List<ProtoId<CargoProductPrototype>>> TeamCargoProductUnlocks = new();

    [DataField("researchPointGrant")]
    public int ResearchPointGrant;

    [DataField("machineSpeedBonusPercent")]
    public int MachineSpeedBonusPercent;

    [DataField("machineStorageBonus")]
    public int MachineStorageBonus;

    [DataField("cargoDeliverySpeedBonusPercent")]
    public int CargoDeliverySpeedBonusPercent;

    [DataField("cargoMaxItemsBonusPercent")]
    public int CargoMaxItemsBonusPercent;

    [DataField("cargoPriceDiscountPercent")]
    public int CargoPriceDiscountPercent;

    [DataField("researchTimeSpeedBonusPercent")]
    public int ResearchTimeSpeedBonusPercent;

    [DataField("researchPointBonusPercent")]
    public int ResearchPointBonusPercent;
}

[Prototype("wh40kCommandTreeTeamMap")]
public sealed partial class WH40KCommandTreeTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandTreeProfilePrototype> DefaultProfile = "WH40KCommandTreeProfileDefault";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandTreeProfilePrototype>> TeamProfiles = new();
}
