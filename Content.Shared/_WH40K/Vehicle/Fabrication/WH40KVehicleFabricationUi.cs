using System;
using System.Collections.Generic;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Vehicle.Fabrication;

[Serializable, NetSerializable]
public enum WH40KVehicleFabricationUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class WH40KVehicleFabricationBuiState : BoundUserInterfaceState
{
    public string Name { get; }
    public int Count { get; }
    public int Capacity { get; }
    public NetEntity Station { get; }
    public bool HasDeliveryEta { get; }
    public TimeSpan DeliveryEtaEndTime { get; }
    public TimeSpan DeliveryDuration { get; }
    public List<CargoOrderData> Orders { get; }
    public List<ProtoId<CargoProductPrototype>> Products { get; }

    public WH40KVehicleFabricationBuiState(
        string name,
        int count,
        int capacity,
        NetEntity station,
        bool hasDeliveryEta,
        TimeSpan deliveryEtaEndTime,
        TimeSpan deliveryDuration,
        List<CargoOrderData> orders,
        List<ProtoId<CargoProductPrototype>> products)
    {
        Name = name;
        Count = count;
        Capacity = capacity;
        Station = station;
        HasDeliveryEta = hasDeliveryEta;
        DeliveryEtaEndTime = deliveryEtaEndTime;
        DeliveryDuration = deliveryDuration;
        Orders = orders;
        Products = products;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KVehicleFabricationAddOrderMessage : BoundUserInterfaceMessage
{
    public string Requester { get; }
    public string CargoProductId { get; }
    public int Amount { get; }

    public WH40KVehicleFabricationAddOrderMessage(string requester, string cargoProductId, int amount)
    {
        Requester = requester;
        CargoProductId = cargoProductId;
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KVehicleFabricationRemoveOrderMessage : BoundUserInterfaceMessage
{
    public int OrderId { get; }

    public WH40KVehicleFabricationRemoveOrderMessage(int orderId)
    {
        OrderId = orderId;
    }
}
