using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Explicit marker for chaos leaders that should receive warp runtime and advanced gift access.
/// Ordinary cultists can still attune to a patron without gaining leader-only abilities.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KChaosLeaderRoleComponent : Component
{
    public override bool SendOnlyToOwner => true;
}