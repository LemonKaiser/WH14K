namespace Content.Shared._WH40K.Clothing;

/// <summary>
/// Placed on a chameleoline cloak. On equip, grants <see cref="Stealth.Components.StealthComponent"/>
/// and <see cref="Stealth.Components.StealthOnMoveComponent"/> to the wearer.
/// When the wearer stands still, they gradually become invisible.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KChameleonCloakComponent : Component
{
    /// <summary>
    /// Rate at which visibility passively changes when standing still.
    /// Negative values decrease visibility (become more invisible).
    /// </summary>
    [DataField("passiveVisibilityRate")]
    public float PassiveVisibilityRate = -0.1f;

    /// <summary>
    /// Rate for movement-induced visibility changes. Positive values increase visibility (become visible).
    /// </summary>
    [DataField("movementVisibilityRate")]
    public float MovementVisibilityRate = 0.2f;

    public bool AddedStealth;
    public bool AddedStealthOnMove;
    public float PreviousPassiveVisibilityRate;
    public float PreviousMovementVisibilityRate;
}
