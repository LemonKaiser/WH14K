using System;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Lathe;

[Serializable, NetSerializable]
public sealed class LatheUpdateState : BoundUserInterfaceState
{
    public List<ProtoId<LatheRecipePrototype>> Recipes;

    public LatheRecipeBatch[] Queue;

    public ProtoId<LatheRecipePrototype>? CurrentlyProducing;

    public bool IsProducing;

    public TimeSpan ProductionStartTime;

    public TimeSpan ProductionLength;

    public int? MaterialStorageLimit;

    public LatheUpdateState(
        List<ProtoId<LatheRecipePrototype>> recipes,
        LatheRecipeBatch[] queue,
        ProtoId<LatheRecipePrototype>? currentlyProducing = null,
        bool isProducing = false,
        TimeSpan? productionStartTime = null,
        TimeSpan? productionLength = null,
        int? materialStorageLimit = null)
    {
        Recipes = recipes;
        Queue = queue;
        CurrentlyProducing = currentlyProducing;
        IsProducing = isProducing;
        ProductionStartTime = productionStartTime ?? TimeSpan.Zero;
        ProductionLength = productionLength ?? TimeSpan.Zero;
        MaterialStorageLimit = materialStorageLimit;
    }
}

/// <summary>
///     Sent to the server to sync material storage and the recipe queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheSyncRequestMessage : BoundUserInterfaceMessage
{

}

/// <summary>
///     Sent to the server when a client queues a new recipe.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheQueueRecipeMessage : BoundUserInterfaceMessage
{
    public readonly string ID;
    public readonly int Quantity;
    public readonly bool Infinite;

    public LatheQueueRecipeMessage(string id, int quantity, bool infinite = false)
    {
        ID = id;
        Quantity = quantity;
        Infinite = infinite;
    }
}

/// <summary>
///     Sent to the server to remove a batch from the queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheDeleteRequestMessage(int index) : BoundUserInterfaceMessage
{
    public int Index = index;
}

/// <summary>
///     Sent to the server to move the position of a batch in the queue.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheMoveRequestMessage(int index, int change) : BoundUserInterfaceMessage
{
    public int Index = index;
    public int Change = change;
}

/// <summary>
///     Sent to the server to stop producing the current item.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheAbortFabricationMessage() : BoundUserInterfaceMessage
{
}

[NetSerializable, Serializable]
public enum LatheUiKey
{
    Key,
}
