using Content.Server.NPC.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server.NPC.Components;

/// <summary>
/// Added to an NPC doing ranged combat.
/// </summary>
[RegisterComponent]
public sealed partial class NPCRangedCombatComponent : Component
{
    [ViewVariables]
    public EntityUid Target;

    [ViewVariables]
    public EntityCoordinates TargetCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public CombatStatus Status = CombatStatus.Normal;

    // Most of the below is to deal with turrets.

    /// <summary>
    /// If null it will instantly turn.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)] public Angle? RotationSpeed;

    /// <summary>
    /// Maximum distance, between our rotation and the target's, to consider shooting it.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Angle AccuracyThreshold = Angle.FromDegrees(30);

    /// <summary>
    /// How long until the last line of sight check.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float LOSAccumulator = 0f;

    /// <summary>
    ///  Is the target still considered in LOS since the last check.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool TargetInLOS = false;

    /// <summary>
    /// If true, only opaque objects will block line of sight.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    // ReSharper disable once InconsistentNaming
    public bool UseOpaqueForLOSChecks = false;

    /// <summary>
    /// Delay after target is in LOS before we start shooting.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float ShootDelay = 0.2f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float ShootAccumulator;

    /// <summary>
    /// Sound to play if the target enters line of sight.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? SoundTargetInLOS;

    [ViewVariables]
    public bool FriendlyFireRepositionActive;

    [ViewVariables]
    public EntityCoordinates FriendlyFireRepositionCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public EntityUid FriendlyFireBlockedBy = EntityUid.Invalid;

    [ViewVariables]
    public bool FriendlyFireHadSteeringSnapshot;

    [ViewVariables]
    public EntityCoordinates FriendlyFireSnapshotCoordinates = EntityCoordinates.Invalid;

    [ViewVariables]
    public float FriendlyFireSnapshotRange;

    [ViewVariables]
    public bool FriendlyFireSnapshotDirectMove;

    [ViewVariables]
    public bool FriendlyFireSnapshotArriveOnLineOfSight;

    [ViewVariables]
    public bool FriendlyFireSnapshotHasInRangeMaxSpeed;

    [ViewVariables]
    public float FriendlyFireSnapshotInRangeMaxSpeed;
}
