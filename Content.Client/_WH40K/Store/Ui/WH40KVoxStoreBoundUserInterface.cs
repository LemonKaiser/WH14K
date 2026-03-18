using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WH40K.SupplyDrop;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

namespace Content.Client._WH40K.Store.Ui;

[UsedImplicitly]
public sealed class WH40KVoxStoreBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WH40KVoxStoreMenu? _menu;

    [ViewVariables]
    private HashSet<ListingDataWithCostModifiers> _listings = new();

    public WH40KVoxStoreBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<WH40KVoxStoreMenu>();
        if (EntMan.TryGetComponent<StoreComponent>(Owner, out var store))
            _menu.Title = Loc.GetString(store.Name);

        _menu.OnPurchasePressed += (_, listing) =>
        {
            SendMessage(new StoreBuyListingMessage(listing.ID));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_menu == null)
            return;

        if (state is WH40KVoxStoreUpdateState voxState)
        {
            _listings = voxState.Listings;
            _menu.UpdateBalance(voxState.Balance);
            _menu.UpdateSupplyDropCooldown(voxState.NextLaunchAt);
            _menu.UpdateListingDropAmounts(voxState.ListingDropAmounts);
            _menu.UpdateListings(_listings.ToList());
            return;
        }

        if (state is not StoreUpdateState msg)
            return;

        _listings = msg.Listings;
        _menu.UpdateBalance(msg.Balance);
        _menu.UpdateSupplyDropCooldown(TimeSpan.Zero);
        _menu.UpdateListingDropAmounts(new Dictionary<ProtoId<ListingPrototype>, int>());
        _menu.UpdateListings(_listings.ToList());
    }
}
