using System;
using System.Collections.Generic;
using Content.Shared.Cargo;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.Vehicle.Fabrication;

[RegisterComponent]
public sealed partial class WH40KVehicleFabricationConsoleComponent : Component
{
    [DataField]
    public List<ProtoId<WH40KVehicleRecipePrototype>> Recipes = new();

    [DataField]
    public int QueueCapacity = 4;

    [DataField]
    public float AssemblyPadRange = 8f;

    [DataField]
    public string PartContainerId = "vehicle_parts";

    [DataField]
    public TimeSpan UiRefreshInterval = TimeSpan.FromSeconds(1);

    [ViewVariables]
    public int NextOrderId = 1;

    [ViewVariables]
    public List<WH40KVehicleQueuedOrder> Queue = new();

    [ViewVariables]
    public WH40KVehicleActiveBuild? ActiveBuild;

    [ViewVariables]
    public TimeSpan NextUiRefresh = TimeSpan.Zero;

    [ViewVariables]
    public Container? PartsContainer;
}

[RegisterComponent]
public sealed partial class WH40KVehicleAssemblyPadComponent : Component;

public sealed class WH40KVehicleQueuedOrder
{
    public int OrderId { get; }
    public ProtoId<WH40KVehicleRecipePrototype> Recipe { get; }
    public CargoOrderData OrderData { get; }

    public WH40KVehicleQueuedOrder(int orderId, ProtoId<WH40KVehicleRecipePrototype> recipe, CargoOrderData orderData)
    {
        OrderId = orderId;
        Recipe = recipe;
        OrderData = orderData;
    }
}

public sealed class WH40KVehicleActiveBuild
{
    public WH40KVehicleQueuedOrder QueueOrder { get; }
    public TimeSpan StartedAt { get; }
    public TimeSpan EndsAt { get; }

    public WH40KVehicleActiveBuild(WH40KVehicleQueuedOrder queueOrder, TimeSpan startedAt, TimeSpan endsAt)
    {
        QueueOrder = queueOrder;
        StartedAt = startedAt;
        EndsAt = endsAt;
    }
}
