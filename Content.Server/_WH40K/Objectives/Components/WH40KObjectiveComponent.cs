using Content.Server._WH40K.Objectives;
using Content.Shared.FixedPoint;
using Robust.Shared.Localization;

namespace Content.Server._WH40K.Objectives.Components;

[RegisterComponent, Access(typeof(WH40KObjectiveSystem))]
public sealed partial class WH40KObjectiveComponent : Component
{
    [DataField("teamId", required: true)]
    public string TeamId = string.Empty;

    [DataField("name", required: true)]
    public LocId Name = string.Empty;

    [DataField("maxHealth")]
    public FixedPoint2 MaxHealth = FixedPoint2.New(1000);

    [DataField("warnAtPercent")]
    public float WarnAtPercent = 0.5f;

    [DataField("destructionDelaySeconds")]
    public float DestructionDelaySeconds = 6f;

    [DataField("triggerKey")]
    public string TriggerKey = "wh40k-objective-destroyed";

    /// <summary>
    /// Incoming damage multiplier while preparation shield is active.
    /// 0.1 = 90% damage reduction.
    /// </summary>
    [DataField("preparationShieldDamageMultiplier")]
    public float PreparationShieldDamageMultiplier = 0.1f;

    [ViewVariables]
    public bool LowHealthAnnounced;

    [ViewVariables]
    public bool Destroying;

    [ViewVariables]
    public bool Destroyed;
}
