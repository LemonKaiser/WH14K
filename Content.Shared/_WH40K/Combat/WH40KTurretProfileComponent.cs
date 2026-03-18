using Robust.Shared.GameStates;

namespace Content.Shared._WH40K.Combat;

/// <summary>
/// WH40K deployable turret profile tuning.
/// Controls detection/firing ranges and exposes supported ammo text in examine.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WH40KTurretProfileComponent : Component
{
    /// <summary>
    /// Base target detection radius used by nearby-hostile queries.
    /// </summary>
    [DataField]
    public float DetectionRange = 10f;

    /// <summary>
    /// Optional aggro detection radius. Defaults to <see cref="DetectionRange"/>.
    /// </summary>
    [DataField]
    public float? AggroDetectionRange;

    /// <summary>
    /// Optional effective fire range used by turret HTN preconditions.
    /// Defaults to <see cref="DetectionRange"/>.
    /// </summary>
    [DataField]
    public float? FireRange;

    /// <summary>
    /// Player-facing supported ammo signature line.
    /// Example: "CartridgeRifle, MagazineBoxRifle".
    /// </summary>
    [DataField]
    public string SupportedAmmo = string.Empty;
}

