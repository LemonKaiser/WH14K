using Content.Shared.Cargo;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Cargo.Components;

/// <summary>
/// Stores active delayed batch deliveries for station cargo accounts.
/// </summary>
[RegisterComponent]
public sealed partial class StationCargoOrderBatchComponent : Component
{
    [ViewVariables]
    public int NextBatchId = 1;

    [ViewVariables]
    public Dictionary<ProtoId<CargoAccountPrototype>, CargoOrderBatchTransitData> ActiveBatches = new();
}

public sealed class CargoOrderBatchTransitData
{
    public int BatchId;
    public TimeSpan DeliverAt;
    public int SummaryOrderId;
    public int LastEtaSeconds = int.MinValue;
    public ProtoId<CargoAccountPrototype> Account = "Cargo";
    public EntProtoId CratePrototype = "CrateGenericSteel";
    public List<CargoOrderBatchItemData> Items = new();
}

public sealed class CargoOrderBatchItemData
{
    public ProtoId<CargoProductPrototype> Product = string.Empty;
    public string ProductId = string.Empty;
    public string ProductName = string.Empty;
    public int Price;
    public int Quantity;

    public CargoOrderData ToOrderData(int orderId, ProtoId<CargoAccountPrototype> account)
    {
        var data = new CargoOrderData(orderId, ProductId, ProductName, Price, 1, string.Empty, string.Empty, account);
        data.Product = Product;
        return data;
    }
}
