using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Explicit role marker for the Imperium psyker path.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KPsykerRoleComponent : Component
{
    public override bool SendOnlyToOwner => true;
}
