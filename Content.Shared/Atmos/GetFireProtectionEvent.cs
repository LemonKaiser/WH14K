using Content.Shared.Inventory;

namespace Content.Shared.Atmos;

/// <summary>
/// Raised on a burning entity to check its fire protection.
/// Damage taken is multiplied by the final amount, but not temperature.
/// TemperatureProtection is needed for that.
/// </summary>
[ByRefEvent]
public sealed class GetFireProtectionEvent : EntityEventArgs, IInventoryRelayEvent
{
    private const float MinimumDamageMultiplier = 0.05f;

    public SlotFlags TargetSlots { get; } = ~SlotFlags.POCKET;

    /// <summary>
    /// What to multiply the fire damage by.
    /// Fire protection can no longer reduce this to 0 so burning always has an effect.
    /// </summary>
    public float Multiplier;

    public GetFireProtectionEvent()
    {
        Multiplier = 1f;
    }

    /// <summary>
    /// Reduce fire damage taken by a percentage.
    /// </summary>
    public void Reduce(float by)
    {
        Multiplier -= by;
        Multiplier = Math.Clamp(Multiplier, MinimumDamageMultiplier, 1f);
    }
}
