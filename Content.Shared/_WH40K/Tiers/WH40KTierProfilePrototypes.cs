using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Tiers;

[Prototype("wh40kTierThresholdProfile")]
public sealed partial class WH40KTierThresholdProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("tier1MinBaseLevel")]
    public int Tier1MinBaseLevel = 2;

    [DataField("tier2MinBaseLevel")]
    public int Tier2MinBaseLevel = 3;

    [DataField("tier3MinBaseLevel")]
    public int Tier3MinBaseLevel = 4;
}

[Prototype("wh40kTierMachineProfile")]
public sealed partial class WH40KTierMachineProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("thresholdProfile")]
    public ProtoId<WH40KTierThresholdProfilePrototype>? ThresholdProfile;

    [DataField("globalTimeMultiplier")]
    public float GlobalTimeMultiplier = 1f;

    [DataField("minProcessSecondsTier0")]
    public float MinProcessSecondsTier0 = 10f;

    [DataField("minProcessSecondsTier1")]
    public float MinProcessSecondsTier1 = 8f;

    [DataField("minProcessSecondsTier2")]
    public float MinProcessSecondsTier2 = 5f;

    [DataField("minProcessSecondsTier3")]
    public float MinProcessSecondsTier3 = 3f;

    [DataField("materialStorageLimitTier0")]
    public int? MaterialStorageLimitTier0 = 10;

    [DataField("materialStorageLimitTier1")]
    public int? MaterialStorageLimitTier1 = 15;

    [DataField("materialStorageLimitTier2")]
    public int? MaterialStorageLimitTier2 = 20;

    [DataField("materialStorageLimitTier3")]
    public int? MaterialStorageLimitTier3 = 30;
}

[Prototype("wh40kTierLogisticsProfile")]
public sealed partial class WH40KTierLogisticsProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("thresholdProfile")]
    public ProtoId<WH40KTierThresholdProfilePrototype>? ThresholdProfile;

    [DataField("tier1MaxItemsBonus")]
    public int Tier1MaxItemsBonus = 2;

    [DataField("tier2MaxItemsBonus")]
    public int Tier2MaxItemsBonus = 5;

    [DataField("tier3MaxItemsBonus")]
    public int Tier3MaxItemsBonus = 10;

    [DataField("tier1DeliveryMinutesReduction")]
    public int Tier1DeliveryMinutesReduction = 1;

    [DataField("tier2DeliveryMinutesReduction")]
    public int Tier2DeliveryMinutesReduction = 2;

    [DataField("tier3DeliveryMinutesReduction")]
    public int Tier3DeliveryMinutesReduction = 5;
}
