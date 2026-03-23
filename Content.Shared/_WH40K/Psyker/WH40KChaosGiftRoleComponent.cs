using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Explicit role marker for the Chaos gifts path.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KChaosGiftRoleComponent : Component
{
    public override bool SendOnlyToOwner => true;
}
