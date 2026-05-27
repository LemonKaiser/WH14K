using Robust.Shared.GameStates;

namespace Content.Shared.NPC;

/// <summary>
/// Keeps an entity's hard fixtures out of NPC pathfinding while leaving normal physics intact.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PathfindingIgnoredComponent : Component
{
}
