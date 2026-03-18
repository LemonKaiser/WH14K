namespace Content.Shared.EnergyDome;

/// <summary>
/// Raised on the generator entity when a dome has been activated and spawned.
/// </summary>
[ByRefEvent]
public readonly record struct EnergyDomeActivatedEvent(EntityUid Generator, EntityUid Dome);

/// <summary>
/// Reason for generator-side dome break/disable.
/// </summary>
public enum EnergyDomeBreakReason : byte
{
    Manual,
    Depleted,
    Overloaded,
    ParentChanged,
    Shutdown,
    Conflict,
    ExternalDeletion
}

/// <summary>
/// Raised on the generator entity when the active dome is broken/disabled for a non-manual reason.
/// </summary>
[ByRefEvent]
public readonly record struct EnergyDomeBrokenEvent(EntityUid Generator, EnergyDomeBreakReason Reason);

/// <summary>
/// Raised on the generator entity when reload delay has elapsed and dome can be activated again.
/// </summary>
[ByRefEvent]
public readonly record struct EnergyDomeRechargeReadyEvent(EntityUid Generator);
