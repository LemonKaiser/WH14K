using Content.Shared.NPC.Components;
using Content.Shared.Turrets;
using Robust.Shared.GameObjects;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Ensures the owner entity has DeployableTurretComponent and it is enabled.
/// Intended to prevent turret HTN (GunOperator branches) from running while DeployableTurretComponent is disabled
/// (e.g. tier gates for strategic points).
/// </summary>
public sealed partial class DeployableTurretEnabledPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return false;

        if (!owner.IsValid())
            return false;

        if (!_entManager.TryGetComponent(owner, out DeployableTurretComponent? turretComp))
            return false;

        return turretComp.Enabled;
    }
}
