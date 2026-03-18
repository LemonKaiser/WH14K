using System;
using System.Collections.Generic;
using Content.Shared.FixedPoint;
using Content.Shared.Store;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.SupplyDrop;

[Serializable, NetSerializable]
public enum WH40KSupplyDropUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class WH40KSupplyDropBuiState : BoundUserInterfaceState
{
    public string TeamName { get; }
    public string AccountId { get; }
    public int Balance { get; }
    public int Cost { get; }
    public int DropDelaySeconds { get; }
    public TimeSpan LastLaunchAt { get; }
    public TimeSpan NextLaunchAt { get; }

    public WH40KSupplyDropBuiState(
        string teamName,
        string accountId,
        int balance,
        int cost,
        int dropDelaySeconds,
        TimeSpan lastLaunchAt,
        TimeSpan nextLaunchAt)
    {
        TeamName = teamName;
        AccountId = accountId;
        Balance = balance;
        Cost = cost;
        DropDelaySeconds = dropDelaySeconds;
        LastLaunchAt = lastLaunchAt;
        NextLaunchAt = nextLaunchAt;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KVoxStoreUpdateState : BoundUserInterfaceState
{
    public HashSet<ListingDataWithCostModifiers> Listings { get; }
    public Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> Balance { get; }
    public TimeSpan NextLaunchAt { get; }
    public Dictionary<ProtoId<ListingPrototype>, int> ListingDropAmounts { get; }

    public WH40KVoxStoreUpdateState(
        HashSet<ListingDataWithCostModifiers> listings,
        Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> balance,
        TimeSpan nextLaunchAt,
        Dictionary<ProtoId<ListingPrototype>, int> listingDropAmounts)
    {
        Listings = listings;
        Balance = balance;
        NextLaunchAt = nextLaunchAt;
        ListingDropAmounts = listingDropAmounts;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KSupplyDropLaunchMessage : BoundUserInterfaceMessage;
