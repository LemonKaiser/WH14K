using System.Numerics;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Marks a gun entity as usable by an entity buckled to this entity's strap.
/// </summary>
[RegisterComponent]
public sealed partial class BuckleMountedGunComponent : Component
{
    /// <summary>
    /// If true, mounted shooting is only allowed while the strap is enabled.
    /// </summary>
    [DataField]
    public bool RequireEnabledStrap = true;

    /// <summary>
    /// If true, an operator buckled to this mounted gun will not collide with the gun itself.
    /// Applied before buckle placement to avoid immediate collision pushes.
    /// </summary>
    [DataField]
    public bool OperatorDontCollide = true;

    /// <summary>
    /// If true, generic strap unbuckle relocation is skipped.
    /// Mounted-gun specific systems can then place the operator deterministically.
    /// </summary>
    [DataField]
    public bool SkipDefaultUnbuckleRelocation = true;

    /// <summary>
    /// Local-space offset for mounted shot origin, relative to the gun entity center.
    /// Use this to move fire/arc geometry closer to a visible barrel.
    /// </summary>
    [DataField]
    public Vector2 ShootOriginOffset = Vector2.Zero;
}
