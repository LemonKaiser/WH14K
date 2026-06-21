using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Weapons.Mods;

[RegisterComponent]
public sealed partial class WH40KWeaponModHostComponent : Component
{
    public const string SlotIdPrefix = "wh40k-weapon-mod-";

    [DataField(required: true)]
    public List<WH40KWeaponModSlotDefinition> SlotDefinitions = new();

    [DataField]
    public Dictionary<string, EntProtoId> StartingMods = new();

    [ViewVariables]
    public Dictionary<string, ItemSlot> ModSlots = new();

    [ViewVariables]
    public DamageSpecifier BaseMeleeDamage = new();

    [ViewVariables]
    public float BaseMeleeAttackRate = 1f;

    [ViewVariables]
    public float BaseMeleeRange = 1.5f;

    [ViewVariables]
    public EntProtoId BaseMeleeAnimation = "WeaponArcSlash";

    [ViewVariables]
    public EntProtoId BaseMeleeWideAnimation = "WeaponArcSlash";

    [ViewVariables]
    public Angle BaseMeleeWideAnimationRotation = Angle.Zero;

    [ViewVariables]
    public bool BaseMeleeInitialized;

    [ViewVariables]
    public float BaseAimMaxOffset;

    [ViewVariables]
    public bool BaseAimInitialized;

    [ViewVariables]
    public SpriteSpecifier? BaseCombatSight;

    [ViewVariables]
    public SpriteSpecifier? BaseCombatSightUnavailable;

    [ViewVariables]
    public bool BaseCombatSightInitialized;

    [ViewVariables]
    public string? BasePresentationName;

    [ViewVariables]
    public string? BasePresentationDescription;

    [ViewVariables]
    public bool BasePresentationInitialized;

    [ViewVariables]
    public string? BaseItemSpritePath;

    [ViewVariables]
    public bool BaseItemSpriteInitialized;

    [ViewVariables]
    public SlotFlags BaseClothingSlots = SlotFlags.NONE;

    [ViewVariables]
    public bool BaseClothingSlotsInitialized;

    /// <summary>
    ///     Snapshot of the weapon's original <see cref="ItemComponent.Shape"/> before any stock mod
    ///     was applied. Used by <see cref="SharedWH40KWeaponModSystem.RefreshItemShapeProfile"/> to
    ///     restore the full-size shape when a fixed stock is installed, or to apply a width-reduced
    ///     shape when the stock is absent or folded.
    /// </summary>
    [ViewVariables]
    public List<Box2i>? BaseItemShape;

    [ViewVariables]
    public bool BaseItemShapeInitialized;
}

[DataDefinition]
public sealed partial class WH40KWeaponModSlotDefinition
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public WH40KWeaponModSlotType SlotType;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public int Priority;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public SoundSpecifier? InsertSound;

    [DataField]
    public SoundSpecifier? EjectSound;

    [DataField]
    public EntProtoId? StartingItem;
}
