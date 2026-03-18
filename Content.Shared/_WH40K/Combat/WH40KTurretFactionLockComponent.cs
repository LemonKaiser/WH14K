using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Combat;

/// <summary>
/// Marks deployable WH40K turrets that cannot be deactivated by enemy faction members.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KTurretFactionLockComponent : Component
{
    [DataField]
    public bool PreventEnemyDeactivation = true;

    [DataField]
    public bool TreatNoFactionAsEnemy = true;
}

