using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Explosion;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class WH40KExplosionShockWaveComponent : Component
{
    /// <summary>
    ///     The rate at which the wave fades. Lower values keep it visible longer.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float FalloffPower = 40f;

    /// <summary>
    ///     Higher values create a sharper, more visible wave edge.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float Sharpness = 10f;

    /// <summary>
    ///     Width of the wave band.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float Width = 0.8f;
}
