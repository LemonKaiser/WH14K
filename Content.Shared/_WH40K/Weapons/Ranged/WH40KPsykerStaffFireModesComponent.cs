using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared._WH40K.Weapons.Ranged;

/// <summary>
/// Allows psyker force staves to switch between different firing modes.
/// Similar to BatteryWeaponFireModes but works with BasicEntityAmmoProvider.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(WH40KPsykerStaffFireModesSystem))]
[AutoGenerateComponentState]
public sealed partial class WH40KPsykerStaffFireModesComponent : Component
{
    /// <summary>
    /// A list of the different firing modes the staff can switch between.
    /// Each mode specifies a projectile prototype and instability cost.
    /// </summary>
    [DataField(required: true)]
    [AutoNetworkedField]
    public List<WH40KPsykerStaffFireMode> FireModes = new();

    /// <summary>
    /// The currently selected firing mode.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public int CurrentFireMode;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class WH40KPsykerStaffFireMode
{
    /// <summary>
    /// The projectile prototype associated with this firing mode.
    /// </summary>
    [DataField("proto", required: true)]
    public string Prototype = default!;

    /// <summary>
    /// The warp instability cost per shot for this mode.
    /// </summary>
    [DataField]
    public float ShotInstability = 15f;

    /// <summary>
    /// The fire sound to use for this mode.
    /// </summary>
    [DataField("soundGunshot")]
    public SoundSpecifier? SoundGunshot;

    /// <summary>
    /// The shots per second for this mode.
    /// </summary>
    [DataField]
    public float? FireRate;

    /// <summary>
    /// The projectile speed for this mode.
    /// </summary>
    [DataField]
    public float? ProjectileSpeed;

    /// <summary>
    /// Minimum spread, in degrees, for this mode.
    /// </summary>
    [DataField]
    public float? MinAngle;

    /// <summary>
    /// Maximum spread, in degrees, for this mode.
    /// </summary>
    [DataField]
    public float? MaxAngle;

    /// <summary>
    /// The allowed selective fire modes for this mode.
    /// </summary>
    [DataField]
    public SelectiveFire? AvailableModes;

    /// <summary>
    /// The selected fire mode for this mode.
    /// </summary>
    [DataField]
    public SelectiveFire? SelectedMode;
}
