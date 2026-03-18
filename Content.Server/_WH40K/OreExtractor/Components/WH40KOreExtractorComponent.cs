using System;
using System.Collections.Generic;
using Content.Shared._WH40K.Tiers;
using Content.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.OreExtractor.Components;

[RegisterComponent, Access(typeof(WH40KOreExtractorSystem))]
public sealed partial class WH40KOreExtractorComponent : Component
{
    [DataField]
    public float SpawnIntervalTier0 = 4f;

    [DataField]
    public float SpawnIntervalTier1 = 3f;

    [DataField]
    public float SpawnIntervalTier2 = 2f;

    [DataField]
    public float SpawnIntervalTier3 = 1f;

    [DataField]
    public int SpawnCountTier0 = 1;

    [DataField]
    public int SpawnCountTier1 = 2;

    [DataField]
    public int SpawnCountTier2 = 3;

    [DataField]
    public int SpawnCountTier3 = 4;

    [DataField]
    public bool RequirePowered = true;

    [DataField]
    public int MaxItemsOnOutputTile = 30;

    [DataField]
    public string TeamId = string.Empty;

    [DataField]
    public List<string> TeamIds = new();

    [DataField("tierThresholdProfile")]
    public ProtoId<WH40KTierThresholdProfilePrototype>? TierThresholdProfile;

    [DataField]
    public int Tier1MinBaseLevel = 2;

    [DataField]
    public int Tier2MinBaseLevel = 3;

    [DataField]
    public int Tier3MinBaseLevel = 4;

    [DataField("randomOrePool")]
    public ProtoId<WeightedRandomOrePrototype> RandomOrePool = "RandomOreDistributionStandard";

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

    /// <summary>
    /// Legacy fallback list. If tier lists are empty, can still be used by older presets.
    /// </summary>
    [DataField("availableOres")]
    public List<string> AvailableOres = new();

    [DataField]
    public bool Enabled = true;

    [DataField("selectedOre")]
    public string? SelectedOre;

    [ViewVariables]
    public TimeSpan NextSpawnAt;
}
