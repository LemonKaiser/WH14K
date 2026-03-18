using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.RoundEvents;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WH40KRoundEventBuffComponent : Component
{
    /// <summary>
    /// If true, pulling movement slowdown is neutralized while actively pulling.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IgnorePullSlowdown;

    /// <summary>
    /// Multiplier applied to medical do-after delays. Lower is faster.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MedicalDelayMultiplier = 1f;

    /// <summary>
    /// Multiplier applied to construction do-after delays. Lower is faster.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ConstructionDelayMultiplier = 1f;
}
