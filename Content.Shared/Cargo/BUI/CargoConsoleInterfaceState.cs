using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System;

namespace Content.Shared.Cargo.BUI;

[NetSerializable, Serializable]
public sealed class CargoConsoleInterfaceState : BoundUserInterfaceState
{
    public string Name;
    public int Count;
    public int Capacity;
    public NetEntity Station;
    public bool HasDeliveryEta;
    public TimeSpan DeliveryEtaEndTime;
    public TimeSpan DeliveryDuration;
    public List<CargoOrderData> Orders;
    public List<ProtoId<CargoProductPrototype>> Products;

    public CargoConsoleInterfaceState(
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
