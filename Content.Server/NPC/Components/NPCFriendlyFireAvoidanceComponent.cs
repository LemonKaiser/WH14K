using Robust.Shared.GameObjects;

namespace Content.Server.NPC.Components;

/// <summary>
/// Enables ranged NPC logic that avoids firing through friendlies by first
/// shifting into a nearby safe firing position.
/// </summary>
[RegisterComponent]
public sealed partial class NPCFriendlyFireAvoidanceComponent : Component
{
    /// <summary>
    /// Preferred lateral sidestep distance when a friendly blocks the shot.
    /// </summary>
    [DataField("repositionDistance")]
    public float RepositionDistance = 1.05f;

    /// <summary>
    /// Wider sidestep distance if the closer angle is still unsafe.
    /// </summary>
    [DataField("extendedRepositionDistance")]
    public float ExtendedRepositionDistance = 1.45f;

    /// <summary>
    /// Small forward step to favor aggressive firing angles.
    /// </summary>
    [DataField("forwardOffset")]
    public float ForwardOffset = 0.22f;

    /// <summary>
    /// Small backward step fallback when no forward sidestep is safe.
    /// </summary>
    [DataField("backwardOffset")]
    public float BackwardOffset = 0.25f;

    /// <summary>
    /// How close the NPC needs to get to the firing position before it can stop repositioning.
    /// </summary>
    [DataField("arrivalRange")]
    public float ArrivalRange = 0.25f;
}
