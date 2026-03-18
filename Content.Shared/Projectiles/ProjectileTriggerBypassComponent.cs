using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Content.Shared.Projectiles;

/// <summary>
/// Stores collision targets for which TriggerOnCollide effects should be skipped.
/// ProjectileSystem fills this when a projectile is allowed to pass through an obstacle.
/// </summary>
[RegisterComponent]
public sealed partial class ProjectileTriggerBypassComponent : Component
{
    public HashSet<EntityUid> PassThroughTargets = new();
}
