using System;
using System.Collections.Generic;
using Content.Shared._WH40K.Tiers;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

namespace Content.Server.Cargo.Components;

/// <summary>
/// Per-account logistics tier and optional external percentage bonuses for cargo.
/// Used by delayed batch delivery ETA, pending-capacity limits and order prices.
/// </summary>
[RegisterComponent]
public sealed partial class CargoLogisticsTierComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier1MaxItemsBonus = 2;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier2MaxItemsBonus = 5;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier3MaxItemsBonus = 10;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier1DeliveryMinutesReduction = 1;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier2DeliveryMinutesReduction = 2;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier3DeliveryMinutesReduction = 5;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier1MinBaseLevel = 2;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier2MinBaseLevel = 3;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Tier3MinBaseLevel = 4;

    [DataField("tierLogisticsProfile")]
    public ProtoId<WH40KTierLogisticsProfilePrototype>? TierLogisticsProfile;

    /// <summary>
    /// Logistics tier per cargo account. Supported range is [0..3].
    /// If account is absent, tier 0 is assumed.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<CargoAccountPrototype>, int> AccountTiers = new();

    /// <summary>
    /// Optional mapping from cargo account to WH40K team ID.
    /// If provided, external sync systems can auto-derive tier from team base level.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<CargoAccountPrototype>, string> AccountTeams = new();

    /// <summary>
    /// External delivery speed bonus in percent.
    /// Positive = faster delivery (lower ETA), negative = slower.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<CargoAccountPrototype>, float> ExternalDeliverySpeedBonusPercent = new();

    /// <summary>
    /// External pending-capacity bonus in percent.
    /// Positive = higher max pending items, negative = lower.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<CargoAccountPrototype>, float> ExternalMaxItemsBonusPercent = new();

    /// <summary>
    /// External order price discount in percent.
    /// Positive = cheaper orders, negative = surcharge.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<CargoAccountPrototype>, float> ExternalPriceDiscountPercent = new();

    public int GetTier(ProtoId<CargoAccountPrototype> account)
    {
        return Math.Clamp(AccountTiers.GetValueOrDefault(account, 0), 0, 3);
    }

    public int GetTierMaxItemsBonus(int tier)
    {
        return Math.Max(0, tier switch
        {
            1 => Tier1MaxItemsBonus,
            2 => Tier2MaxItemsBonus,
            3 => Tier3MaxItemsBonus,
            _ => 0
        });
    }

    public int GetTierDeliveryReductionSeconds(int tier)
    {
        var minutes = Math.Max(0, tier switch
        {
            1 => Tier1DeliveryMinutesReduction,
            2 => Tier2DeliveryMinutesReduction,
            3 => Tier3DeliveryMinutesReduction,
            _ => 0
        });

        return minutes * 60;
    }

    public float GetExternalDeliverySpeedBonusPercent(ProtoId<CargoAccountPrototype> account)
    {
        return ExternalDeliverySpeedBonusPercent.GetValueOrDefault(account, 0f);
    }

    public float GetExternalMaxItemsBonusPercent(ProtoId<CargoAccountPrototype> account)
    {
        return ExternalMaxItemsBonusPercent.GetValueOrDefault(account, 0f);
    }

    public float GetExternalPriceDiscountPercent(ProtoId<CargoAccountPrototype> account)
    {
        return ExternalPriceDiscountPercent.GetValueOrDefault(account, 0f);
    }

    public int GetTierForBaseLevel(int baseLevel)
    {
        return WH40KTierMath.SelectTier(baseLevel, Tier1MinBaseLevel, Tier2MinBaseLevel, Tier3MinBaseLevel);
    }
}
