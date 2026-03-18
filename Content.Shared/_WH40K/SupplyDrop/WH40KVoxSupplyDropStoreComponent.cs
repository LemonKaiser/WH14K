using System.Collections.Generic;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SupplyDrop;

[RegisterComponent]
public sealed partial class WH40KVoxSupplyDropStoreComponent : Component
{
    [DataField(required: true)]
    public ProtoId<CargoAccountPrototype> Account = "WH40KImperium";

    [DataField]
    public string TeamId = string.Empty;

    [DataField(required: true)]
    public ProtoId<CurrencyPrototype> FundsCurrency = "WH40KFactionFunds";

    [DataField(required: true)]
    public List<ProtoId<ListingPrototype>> Listings = new();

    [DataField]
    public Dictionary<ProtoId<ListingPrototype>, int> ListingDropAmounts = new();

    [DataField]
    public EntProtoId? MarkerPrototype = "WH40KSupplyDropParachuteCrateVisual";

    [DataField]
    public EntProtoId? DeliveryCratePrototype = "WH40KVoxSupplyDropCrate";

    [DataField]
    public float DropDelaySeconds = 4f;

    [DataField]
    public float CooldownSeconds = 1f;

    public TimeSpan NextLaunchAt;
    public TimeSpan NextUiRefresh;
}
