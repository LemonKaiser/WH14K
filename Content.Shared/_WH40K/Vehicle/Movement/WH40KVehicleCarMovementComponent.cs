using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Vehicle.Movement;

/// <summary>
/// Replaces mob-like WASD movement with simple top-down car handling.
/// Forward/backward input becomes throttle/brake, left/right input becomes steering wheel input.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KVehicleCarMovementComponent : Component
{
    [DataField]
    public float MaxForwardSpeed = 6.2f;

    [DataField]
    public float MaxReverseSpeed = 2.2f;

    [DataField]
    public float ForwardAcceleration = 2.1f;

    [DataField]
    public float ReverseAcceleration = 1.4f;

    [DataField]
    public float BrakeDeceleration = 5.8f;

    [DataField]
    public float CoastDeceleration = 0.85f;

    [DataField]
    public float LateralGrip = 7.5f;

    [DataField]
    public float SteerInputRate = 2.4f;

    [DataField]
    public float SteerReturnRate = 3.8f;

    [DataField]
    public float LowSpeedTurnRateDegrees = 105f;

    [DataField]
    public float HighSpeedTurnRateDegrees = 42f;

    [DataField]
    public float SpeedForFullSteer = 4.2f;

    [DataField]
    public float EngineFalloffPower = 0.85f;

    [DataField]
    public float StopSpeed = 0.04f;

    [ViewVariables]
    public float CurrentSteer;
}
