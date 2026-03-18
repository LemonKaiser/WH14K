using System;
using System.Collections.Generic;
using Content.Shared._WH40K.Tiers;
using Content.Shared.Lathe.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Store.Components;

[RegisterComponent, Access(typeof(WH40KTieredLatheProcessingSystem))]
public sealed partial class WH40KTieredLatheProcessingComponent : Component
{
    [DataField]
    public List<string> TeamIds = new();

    [DataField]
    public string TeamId = string.Empty;

    [DataField("tierMachineProfile")]
    public ProtoId<WH40KTierMachineProfilePrototype>? TierMachineProfile;

    [DataField]
    public int Tier1MinBaseLevel = 2;

    [DataField]
    public int Tier2MinBaseLevel = 3;

    [DataField]
    public int Tier3MinBaseLevel = 4;

    [DataField]
    public float GlobalTimeMultiplier = 1f;

    [DataField]
    public float MinProcessSecondsTier0 = 10f;

    [DataField]
    public float MinProcessSecondsTier1 = 8f;

    [DataField]
    public float MinProcessSecondsTier2 = 5f;

    [DataField]
    public float MinProcessSecondsTier3 = 3f;

    [DataField]
    public int? MaterialStorageLimitTier0 = 10;

    [DataField]
    public int? MaterialStorageLimitTier1 = 15;

    [DataField]
    public int? MaterialStorageLimitTier2 = 20;

    [DataField]
    public int? MaterialStorageLimitTier3 = 30;

    [DataField]
    public ProtoId<LatheRecipePackPrototype>? Tier0Pack;

    [DataField]
    public ProtoId<LatheRecipePackPrototype>? Tier1Pack;

    [DataField]
    public ProtoId<LatheRecipePackPrototype>? Tier2Pack;

    [DataField]
    public ProtoId<LatheRecipePackPrototype>? Tier3Pack;

    [DataField]
    public bool RemapQueueToSelectedTierPack = true;

    [ViewVariables]
    public TimeSpan NextUpdate;
}
