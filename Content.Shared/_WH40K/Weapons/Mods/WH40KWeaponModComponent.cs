using Content.Shared.Damage;
using Content.Shared.Actions;
using Content.Shared.Inventory;
using Robust.Shared.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Weapons.Mods;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KWeaponModComponent : Component
{
    [DataField(required: true)]
    public WH40KWeaponModSlotType SlotType;

    [DataField]
    public string? OverlaySprite;

    [DataField, AutoNetworkedField]
    public string OverlayState = "base";
}

[RegisterComponent]
public sealed partial class WH40KWeaponModMeleeOverrideComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public float AttackRate = 1f;

    [DataField]
    public float Range = 1.5f;

    [DataField]
    public EntProtoId Animation = "WeaponArcThrust";

    [DataField]
    public EntProtoId WideAnimation = "WeaponArcSlash";

    [DataField]
    public Angle WideAnimationRotation = Angle.Zero;
}

[RegisterComponent]
public sealed partial class WH40KWeaponModStockComponent : Component
{
    [DataField]
    public float WalkModifier = 0.9f;

    [DataField]
    public float SprintModifier = 0.9f;

    [DataField]
    public float SpreadMultiplier = 0.7f;

    [DataField]
    public float CameraRecoilMultiplier = 0.7f;

    [DataField]
    public float MinAngleFloorDegrees = 0.5f;

    [DataField]
    public float MaxAngleFloorDegrees = 1f;

    [DataField]
    public float AngleIncreaseFloorDegrees = 0.1f;

    [DataField]
    public float CameraRecoilFloor = 0.1f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KWeaponModFoldingStockComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Folded;

    [DataField]
    public string FoldedOverlayState = "folded";

    [DataField]
    public string UnfoldedOverlayState = "base";

    [DataField]
    public EntProtoId ToggleAction = "ActionWH40KToggleWeaponStock";

    [DataField]
    public SoundSpecifier ToggleSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Misc/selector.ogg");

    [DataField]
    public EntityUid? ToggleActionEntity;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WH40KWeaponModGrenadeLauncherComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField]
    public string PresentationSprite = "Objects/Weapons/Guns/Launchers/china_lake.rsi";

    [DataField]
    public string PresentationLoadedState = "icon";

    [DataField]
    public string PresentationEmptyState = "bolt-open";

    [DataField]
    public string PresentationItemSprite = "Objects/Weapons/Guns/Launchers/china_lake.rsi";

    [DataField]
    public SpriteSpecifier? PresentationSight = new SpriteSpecifier.Rsi(
        new ResPath("/Textures/Interface/Misc/crosshair_pointers.rsi"),
        "launcher_sight");

    [DataField]
    public SpriteSpecifier? PresentationUnavailableSight = new SpriteSpecifier.Rsi(
        new ResPath("/Textures/Interface/Misc/crosshair_pointers.rsi"),
        "launcher_bolt_sight");

    [DataField]
    public EntProtoId ToggleAction = "ActionWH40KToggleWeaponGrenadeLauncher";

    [DataField]
    public SoundSpecifier ToggleSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Misc/selector.ogg");

    [DataField]
    public EntityUid? ToggleActionEntity;
}

[RegisterComponent]
public sealed partial class WH40KWeaponModOpticComponent : Component
{
    [DataField]
    public float AimRangeBonus = 2f;

    [DataField]
    public SpriteSpecifier? Sight;

    [DataField]
    public SpriteSpecifier? UnavailableSight;

    [DataField]
    public bool HighlightTargets;

    [DataField]
    public Color HighlightColor = Color.FromHex("#FF9438");
}

[RegisterComponent]
public sealed partial class WH40KWeaponModSuppressorComponent : Component
{
    [DataField]
    public float VolumeOffset = -20f;
}

[RegisterComponent]
public sealed partial class WH40KWeaponModMuzzleBrakeComponent : Component
{
    [DataField]
    public float VolumeOffset = 2f;

    [DataField]
    public float SpreadMultiplier = 0.9f;

    [DataField]
    public float CameraRecoilMultiplier = 0.9f;

    [DataField]
    public float MinAngleFloorDegrees = 0.5f;

    [DataField]
    public float MaxAngleFloorDegrees = 1f;

    [DataField]
    public float AngleIncreaseFloorDegrees = 0.1f;

    [DataField]
    public float CameraRecoilFloor = 0.05f;
}

[RegisterComponent]
public sealed partial class WH40KWeaponModSlingComponent : Component
{
    [DataField]
    public SlotFlags AdditionalSlots = SlotFlags.BACK | SlotFlags.BELT;
}

[RegisterComponent]
public sealed partial class WH40KWeaponModBarrelComponent : Component
{
    /// <summary>
    ///     Long barrel: projectile speed boost + accuracy/recoil improvement at the cost of mobility.
    ///     SpreadMultiplier 0.9 means min/max/angleIncrease ×0.9. CameraRecoilMultiplier 0.95 = less recoil.
    ///     WalkModifier/SprintModifier 0.95 = slight movement penalty while wielded.
    /// </summary>
    [DataField]
    public float ProjectileSpeedMultiplier = 1.2f;

    [DataField]
    public float SpreadMultiplier = 0.9f;

    [DataField]
    public float CameraRecoilMultiplier = 0.95f;

    [DataField]
    public float WalkModifier = 0.95f;

    [DataField]
    public float SprintModifier = 0.95f;

    [DataField]
    public float MinAngleFloorDegrees = 0.5f;

    [DataField]
    public float MaxAngleFloorDegrees = 1f;

    [DataField]
    public float AngleIncreaseFloorDegrees = 0.1f;

    [DataField]
    public float CameraRecoilFloor = 0.15f;
}

/// <summary>
///     Short/sawn-off barrel: mobility bonus at the cost of accuracy and projectile speed.
///     SpreadMultiplier 1.1 = wider spread, ProjectileSpeedMultiplier 0.8 = slower bullets,
///     WalkModifier/SprintModifier 1.05 = slightly faster while wielded.
/// </summary>
[RegisterComponent]
public sealed partial class WH40KWeaponModShortBarrelComponent : Component
{
    [DataField]
    public float ProjectileSpeedMultiplier = 0.8f;

    [DataField]
    public float SpreadMultiplier = 1.1f;

    [DataField]
    public float CameraRecoilMultiplier = 1.05f;

    [DataField]
    public float WalkModifier = 1.05f;

    [DataField]
    public float SprintModifier = 1.05f;
}

[RegisterComponent]
public sealed partial class WH40KWeaponModForegripComponent : Component
{
    [DataField]
    public float SpreadMultiplier = 0.9f;

    [DataField]
    public float MinAngleFloorDegrees = 0.5f;

    [DataField]
    public float MaxAngleFloorDegrees = 1f;

    [DataField]
    public float AngleIncreaseFloorDegrees = 0.1f;
}

[RegisterComponent]
public sealed partial class WH40KWeaponModBipodComponent : Component
{
    /// <summary>
    ///     When the weapon is wielded AND the user is prone, the bipod zeroes out spread and recoil.
    ///     These multipliers are applied to the gun's min/max angle, angle increase, and camera recoil.
    ///     Default 0 = complete suppression of spread/recoil while deployed.
    /// </summary>
    [DataField]
    public float SpreadMultiplier = 0f;

    [DataField]
    public float CameraRecoilMultiplier = 0f;

    /// <summary>
    ///     Floors for the clamped angles. Default 0° = the bipod allows true zero spread.
    /// </summary>
    [DataField]
    public float MinAngleFloorDegrees = 0f;

    [DataField]
    public float MaxAngleFloorDegrees = 0f;

    [DataField]
    public float AngleIncreaseFloorDegrees = 0f;

    /// <summary>
    ///     Speed penalty while the bipod is deployed (user prone). Applied via
    ///     HeldRelayedEvent&lt;RefreshMovementSpeedModifiersEvent&gt;.
    /// </summary>
    [DataField]
    public float WalkModifier = 0.7f;

    [DataField]
    public float SprintModifier = 0.7f;

    /// <summary>
    ///     Sound played when the user goes prone/stands up while wielding the bipod-equipped weapon
    ///     (the bipod deploying/folding against the ground).
    /// </summary>
    [DataField]
    public SoundSpecifier DeploySound = new SoundPathSpecifier("/Audio/Weapons/Guns/Misc/selector.ogg");
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WH40KWeaponModLaserSightComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active = true;

    [DataField]
    public Color BeamColor = Color.FromHex("#FF3B30");

    [DataField]
    public float MaxRange = 10f;

    [DataField]
    public EntProtoId ToggleAction = "ActionWH40KToggleWeaponLaserSight";

    [DataField]
    public SoundSpecifier ToggleSound = new SoundPathSpecifier("/Audio/Weapons/click.ogg");

    [DataField]
    public EntityUid? ToggleActionEntity;
}

public enum WH40KWeaponModSlotType : byte
{
    OpticTop,
    MuzzleFront,
    Underbarrel,
    SideUtility,
    StockRear,
    BarrelFront,
    BayonetLug,
    SlingMount,
}
