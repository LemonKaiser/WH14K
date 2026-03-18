using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Overlays;

/// <summary>
/// Applies a radial grayscale effect around this entity on clients.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KRadialGreyscaleComponent : Component
{
    /// <summary>
    /// Effect radius in world meters.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float Radius = 3f;

    /// <summary>
    /// Soft edge width in world meters.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float Feather = 0.35f;

    /// <summary>
    /// Walk/sprint speed multiplier inside the zone.
    /// 0.05 means 95% slow.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float MovementSpeedMultiplier = 0.05f;

    /// <summary>
    /// Physics velocity multiplier inside the zone (projectiles, thrown items, etc.).
    /// 0.05 means 95% slow.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float PhysicsVelocityMultiplier = 0.05f;

    /// <summary>
    /// Active grenade fuse timer multiplier inside the zone.
    /// Applies only to entities tagged as HandGrenade.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float GrenadeFuseTimerMultiplier = 0.05f;
}
