using Robust.Shared.Map;

namespace Content.Server.NPC.Components;

/// <summary>
/// Added to NPCs whenever they're in melee combat so they can be handled by the dedicated system.
/// </summary>
[RegisterComponent]
public sealed partial class NPCMeleeCombatComponent : Component
{
    /// <summary>
    /// If the target is moving what is the chance for this NPC to miss.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float MissChance;

    [ViewVariables]
    public EntityUid Target;

    [ViewVariables]
    public EntityCoordinates TargetCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public CombatStatus Status = CombatStatus.Normal;
}

public enum CombatStatus : byte
{
    /// <summary>
    /// The target isn't in LOS anymore.
    /// </summary>
    NotInSight,

    /// <summary>
    /// Due to some generic reason we are unable to attack the target.
    /// </summary>
    Unspecified,

    /// <summary>
    /// Set if we can't reach the target for whatever reason.
    /// </summary>
    TargetUnreachable,

    /// <summary>
    /// If the target is outside of our melee range.
    /// </summary>
    TargetOutOfRange,

    /// <summary>
    /// Set if the weapon we were assigned is no longer valid.
    /// </summary>
    NoWeapon,

    /// <summary>
    /// We have a target in sight, but a friendly is blocking the shot line so we need to reposition.
    /// </summary>
    FriendlyFireBlocked,

    /// <summary>
    /// No dramas.
    /// </summary>
    Normal,
}
