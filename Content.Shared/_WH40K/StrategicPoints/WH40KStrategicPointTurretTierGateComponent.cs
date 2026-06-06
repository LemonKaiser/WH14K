using Content.Shared.Turrets;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.StrategicPoints;

/// <summary>
/// Enables or disables embedded turret combat on strategic points based on point Tier/Profile.
/// Intended for influence point T1 -> fire only after it becomes Tier 2.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KStrategicPointTurretTierGateComponent : Component
{
    [DataField("requiredTier", required: true)]
    public WH40KStrategicPointTier RequiredTier = WH40KStrategicPointTier.T2;

    [DataField("turretEnabledOnMatch")]
    public bool TurretEnabledOnMatch = true;

    [DataField("turretEnabledOffMatch")]
    public bool TurretEnabledOnMismatch = false;
}
