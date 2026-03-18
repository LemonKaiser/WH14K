using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Waypointer.Events;

[ByRefEvent]
public record struct WaypointerChangedEvent() : IInventoryRelayEvent
{
    public HashSet<ProtoId<WaypointerPrototype>> Waypointers = [];
    SlotFlags IInventoryRelayEvent.TargetSlots => SlotFlags.WITHOUT_POCKET;
}
