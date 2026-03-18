using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Command;

[Prototype("wh40kCommandOreExtractorIntelProfile")]
public sealed partial class WH40KCommandOreExtractorIntelProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("tier1MinBaseLevel")]
    public int Tier1MinBaseLevel = 2;

    [DataField("tier2MinBaseLevel")]
    public int Tier2MinBaseLevel = 3;

    [DataField("tier3MinBaseLevel")]
    public int Tier3MinBaseLevel = 4;

    [DataField("spawnIntervalTier0")]
    public float SpawnIntervalTier0 = 4f;

    [DataField("spawnIntervalTier1")]
    public float SpawnIntervalTier1 = 3f;

    [DataField("spawnIntervalTier2")]
    public float SpawnIntervalTier2 = 2f;

    [DataField("spawnIntervalTier3")]
    public float SpawnIntervalTier3 = 1f;

    [DataField("spawnCountTier0")]
    public int SpawnCountTier0 = 1;

    [DataField("spawnCountTier1")]
    public int SpawnCountTier1 = 2;

    [DataField("spawnCountTier2")]
    public int SpawnCountTier2 = 3;

    [DataField("spawnCountTier3")]
    public int SpawnCountTier3 = 4;

    [DataField("tier0Ores")]
    public List<string> Tier0Ores = new()
    {
        "OreSteel",
        "OreCoal",
    };

    [DataField("tier1Ores")]
    public List<string> Tier1Ores = new()
    {
        "OreSpaceQuartz",
    };

    [DataField("tier2Ores")]
    public List<string> Tier2Ores = new()
    {
        "OreGold",
        "OreSilver",
    };

    [DataField("tier3Ores")]
    public List<string> Tier3Ores = new()
    {
        "OrePlasma",
        "OreUranium",
    };
}

[Prototype("wh40kCommandOreExtractorIntelTeamMap")]
public sealed partial class WH40KCommandOreExtractorIntelTeamMapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("defaultProfile", required: true)]
    public ProtoId<WH40KCommandOreExtractorIntelProfilePrototype> DefaultProfile = "WH40KCommandOreExtractorIntelProfileDefault";

    [DataField("teamProfiles")]
    public Dictionary<string, ProtoId<WH40KCommandOreExtractorIntelProfilePrototype>> TeamProfiles = new();
}
