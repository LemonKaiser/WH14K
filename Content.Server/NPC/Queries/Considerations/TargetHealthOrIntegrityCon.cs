using Content.Shared.Mobs;

namespace Content.Server.NPC.Queries.Considerations;

/// <summary>
/// Scores healthy mobs and durable non-mob combat targets such as mechs higher than heavily damaged ones.
/// </summary>
public sealed partial class TargetHealthOrIntegrityCon : UtilityConsideration
{
    /// <summary>
    /// Which MobState the consideration returns 0f at, defaults to choosing earliest incapacitating MobState.
    /// </summary>
    [DataField("targetState")]
    public MobState TargetState = MobState.Invalid;
}
