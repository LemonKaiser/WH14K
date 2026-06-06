using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Blocks ALL users from switching DeployableTurretComponent.Enabled (activate/deactivate).
/// Intended for special WH40K strategic point embedded turrets.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KTurretGlobalActivationLockComponent : Component
{
    /// <summary>
    /// When true, any Attempt to toggle DeployableTurretComponent.Enabled is blocked.
    /// </summary>
    [DataField]
    public bool PreventAllActivationToggle = true;
}

