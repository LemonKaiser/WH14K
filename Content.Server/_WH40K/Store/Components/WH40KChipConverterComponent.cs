using System;
using Content.Shared.Lathe.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.Store.Components;

[RegisterComponent, Access(typeof(WH40KChipConverterSystem))]
public sealed partial class WH40KChipConverterComponent : Component
{
    [DataField(required: true)]
    public string TeamId = string.Empty;

    [DataField("tier1Pack", required: true)]
    public ProtoId<LatheRecipePackPrototype> Tier1Pack = default!;

    [DataField("tier2Pack", required: true)]
    public ProtoId<LatheRecipePackPrototype> Tier2Pack = default!;

    [DataField("tier3Pack", required: true)]
    public ProtoId<LatheRecipePackPrototype> Tier3Pack = default!;

    [DataField]
    public int Tier1MinBaseLevel = 2;

    [DataField]
    public int Tier2MinBaseLevel = 3;

    [DataField]
    public int Tier3MinBaseLevel = 4;

    [DataField]
    public int MaxConcurrentJobsTier1 = 1;

    [DataField]
    public int MaxConcurrentJobsTier2 = 2;

    [DataField]
    public int MaxConcurrentJobsTier3 = 3;

    [DataField]
    public int MaterialStorageLimitTier1 = 10;

    [DataField]
    public int MaterialStorageLimitTier2 = 25;

    [DataField]
    public int MaterialStorageLimitTier3 = 50;

    [ViewVariables]
    public TimeSpan NextUpdate;
}
