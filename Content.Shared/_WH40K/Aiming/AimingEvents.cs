using Robust.Shared.GameObjects;

namespace Content.Shared._WH40K.Aiming;

/// <summary>
/// Raised when aiming is toggled on a weapon.
/// </summary>
public sealed class AimingToggledEvent : EntityEventArgs
{
    public bool Enabled { get; }
    public EntityUid? User { get; }

    public AimingToggledEvent(bool enabled, EntityUid? user = null)
    {
        Enabled = enabled;
        User = user;
    }
}
