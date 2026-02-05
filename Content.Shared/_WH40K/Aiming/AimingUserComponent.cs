using System.Numerics;
using System;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.WH40K.Aiming;

/// <summary>
/// Stores global aiming toggle state for a user.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AimingUserComponent : Component
{
    [DataField]
    public bool Enabled;

    // Client-only smoothing state.
    public Vector2 TargetPosition = Vector2.Zero;
    public Vector2 CurrentPosition = Vector2.Zero;
    public float LastOffsetSpeed = 0.5f;
    public float LastWallBuffer = 0.25f;
    public float LastWallPullMultiplier = 0.2f;
    public float LastWallClampDistance;
    public Vector2 LastWallClampDir = Vector2.Zero;
    public bool LastWallClamped;
    public TimeSpan LastWallClampTime = TimeSpan.Zero;
    public EntityUid? LastAimingItem;
    public TimeSpan LastValidTime = TimeSpan.Zero;
    public bool WasValid;
    public float LoseGraceSeconds = 0.12f;
    public float ReturnMultiplier = 0.35f;
    public float WallReleaseMultiplier = 0.2f;
    public float WallStickSeconds = 0.12f;
}

[Serializable, NetSerializable]
public sealed class AimingUserComponentState : ComponentState
{
    public bool Enabled { get; }

    public AimingUserComponentState(bool enabled)
    {
        Enabled = enabled;
    }
}
