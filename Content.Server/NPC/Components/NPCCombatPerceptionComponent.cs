using Robust.Shared.Timing;

namespace Content.Server.NPC.Components;

/// <summary>
/// Handles active combat perception for NPCs that should acquire enemies by sight
/// instead of relying on utility queries seeing through walls.
/// </summary>
[RegisterComponent]
public sealed partial class NPCCombatPerceptionComponent : Component
{
    [DataField("visionRadius"), ViewVariables(VVAccess.ReadWrite)]
    public float VisionRadius = 10f;

    [DataField("aggroVisionRadius"), ViewVariables(VVAccess.ReadWrite)]
    public float AggroVisionRadius = 12f;

    [DataField("visionCheckInterval"), ViewVariables(VVAccess.ReadWrite)]
    public float VisionCheckInterval = 0.2f;

    [DataField("shareContactInterval"), ViewVariables(VVAccess.ReadWrite)]
    public float ShareContactInterval = 0.45f;

    [DataField("assignmentInterval"), ViewVariables(VVAccess.ReadWrite)]
    public float AssignmentInterval = 0.45f;

    [DataField("shareContactRadius"), ViewVariables(VVAccess.ReadWrite)]
    public float ShareContactRadius = 8f;

    [DataField("shareRequiresLineOfSight"), ViewVariables(VVAccess.ReadWrite)]
    public bool ShareRequiresLineOfSight = true;

    [DataField("requireSameGroupForReports"), ViewVariables(VVAccess.ReadWrite)]
    public bool RequireSameGroupForReports = true;

    [DataField("memoryDuration"), ViewVariables(VVAccess.ReadWrite)]
    public float MemoryDuration = 8f;

    [DataField("searchDuration"), ViewVariables(VVAccess.ReadWrite)]
    public float SearchDuration = 3f;

    [DataField("visibleGrace"), ViewVariables(VVAccess.ReadWrite)]
    public float VisibleGrace = 0.35f;

    [DataField("reportConfidenceMultiplier"), ViewVariables(VVAccess.ReadWrite)]
    public float ReportConfidenceMultiplier = 0.75f;

    [DataField("minimumContactConfidence"), ViewVariables(VVAccess.ReadWrite)]
    public float MinimumContactConfidence = 0.15f;

    [DataField("meleeSlotsPerTarget"), ViewVariables(VVAccess.ReadWrite)]
    public int MeleeSlotsPerTarget = 3;

    [DataField("rangedSlotsPerTarget"), ViewVariables(VVAccess.ReadWrite)]
    public int RangedSlotsPerTarget = 2;

    [DataField("useOpaqueForLOSChecks"), ViewVariables(VVAccess.ReadWrite)]
    public bool UseOpaqueForLOSChecks = true;

    /// <summary>
    /// Allows hostile damageable static combat entities, such as deployable turrets,
    /// to be remembered even though they do not have mob-state.
    /// </summary>
    [DataField("recognizeStaticThreats"), ViewVariables(VVAccess.ReadWrite)]
    public bool RecognizeStaticThreats = true;

    [ViewVariables]
    public TimeSpan NextVisionCheck = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextShare = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextAssignment = TimeSpan.Zero;
}
