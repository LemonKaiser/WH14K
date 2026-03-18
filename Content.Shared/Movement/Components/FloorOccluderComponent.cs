using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Applies floor occlusion to any <see cref="FloorOcclusionComponent"/> that intersect us.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FloorOccluderComponent : Component
{
    /// <summary>
    /// If true, occlusion applies only when the target entity center is on the same tile
    /// as this floor occluder.
    /// </summary>
    [DataField]
    public bool RequireSameTile;

    /// <summary>
    /// If the target is intersecting an entity from this whitelist,
    /// this occluder does not apply.
    /// Useful for bypassing water floor-occlusion while standing on catwalks.
    /// </summary>
    [DataField]
    public EntityWhitelist? IgnoreWhenIntersectingWhitelist;
}
