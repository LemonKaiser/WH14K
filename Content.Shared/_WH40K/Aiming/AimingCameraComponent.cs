using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Aiming;

/// <summary>
/// Enables an action-toggled aiming camera offset for an item.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AimingCameraComponent : Component
{
    /// <summary>
    /// Whether aiming camera offset is currently enabled.
    /// </summary>
    [DataField]
    public bool Enabled;

    /// <summary>
    /// Max camera offset in tiles.
    /// </summary>
    [DataField]
    public float MaxOffset = 5f;

    /// <summary>
    /// Speed of camera offset smoothing (tiles per update).
    /// </summary>
    [DataField]
    public float OffsetSpeed = 0.5f;

    /// <summary>
    /// Extra PVS scale to account for the max offset.
    /// </summary>
    [DataField]
    public float PvsIncrease = 0.5f;

    /// <summary>
    /// Distance to keep the camera offset away from walls.
    /// </summary>
    [DataField]
    public float WallBuffer = 0.2f;

    /// <summary>
    /// Slowdown multiplier when the camera is being pulled back by a wall clamp.
    /// </summary>
    [DataField]
    public float WallPullMultiplier = 0.1f;

    /// <summary>
    /// If true, aiming only works while the item is wielded.
    /// </summary>
    [DataField]
    public bool RequireWield = true;

    /// <summary>
    /// Toggle action prototype for enabling/disabling aiming.
    /// </summary>
    [DataField]
    public EntProtoId ToggleAction = "ActionToggleAiming";

    [DataField]
    public EntityUid? ToggleActionEntity;

    // Client-only smoothing state.
    public Vector2 TargetPosition = Vector2.Zero;
    public Vector2 CurrentPosition = Vector2.Zero;
}

[Serializable, NetSerializable]
public sealed class AimingCameraComponentState : ComponentState
{
    public bool Enabled { get; }

    public AimingCameraComponentState(bool enabled)
    {
        Enabled = enabled;
    }
}
