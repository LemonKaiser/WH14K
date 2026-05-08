using Robust.Shared.Prototypes;
using Content.Shared.Stacks;

namespace Content.Shared._WH40K.StrategicPoints;

[Prototype("wh40kStrategicPointProfile")]
public sealed partial class WH40KStrategicPointProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("pointType", required: true)]
    public WH40KStrategicPointType PointType;

    [DataField("tiers", required: true)]
    public Dictionary<int, WH40KStrategicPointTierProfile> Tiers = new();

    [DataField("upgrades")]
    public Dictionary<int, WH40KStrategicPointUpgradeProfile> Upgrades = new();
}

[DataDefinition]
public sealed partial class WH40KStrategicPointTierProfile
{
    [DataField("maxHp")]
    public int MaxHp = 250;

    [DataField("teamXpIncome")]
    public int TeamXpIncome = 1;

    [DataField("fundsIncome")]
    public int FundsIncome;

    [DataField("researchIncome")]
    public int ResearchIncome;

    [DataField("influenceIncome")]
    public int InfluenceIncome;

    [DataField("destroyTeamXpReward")]
    public int DestroyTeamXpReward;

    [DataField("destroyInfluenceReward")]
    public int DestroyInfluenceReward;
}

[DataDefinition]
public sealed partial class WH40KStrategicPointUpgradeProfile
{
    [DataField("materials")]
    public Dictionary<ProtoId<StackPrototype>, int> Materials = new();

    [DataField("seconds")]
    public float Seconds = 30f;
}
