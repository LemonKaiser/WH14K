using System;
using Content.Shared.Inventory;

namespace Content.Shared.Damage.Events;

/// <summary>
/// Raised before stamina damage is dealt to allow other systems to cancel or modify it.
/// </summary>
[Serializable]
public enum StaminaDamageType : byte
{
    Generic,
    ForceStand,
}

[ByRefEvent]
public record struct BeforeStaminaDamageEvent(
    float Value,
    StaminaDamageType Type = StaminaDamageType.Generic,
    bool Cancelled = false) : IInventoryRelayEvent
{
    SlotFlags IInventoryRelayEvent.TargetSlots =>  ~SlotFlags.POCKET;
}
