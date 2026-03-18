using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Overlays;

/// <summary>
/// Marks an entity as slowed by WH40K time-dilation zones.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KTimeDilationSlowedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 0.05f;

    [DataField, AutoNetworkedField]
    public float MeleeAttackRateMultiplier = 0.05f;
}
