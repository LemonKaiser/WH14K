using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WH40K.Psyker;

/// <summary>
/// Chaos gifts progression state for skrizhal attunement, altar rituals,
/// and projected patron-cult progression.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WH40KChaosGiftProgressionComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [DataField("level"), AutoNetworkedField]
    public int Level = 1;

    [DataField("levelXp"), AutoNetworkedField]
    public float LevelXp;

    [DataField("totalXp"), AutoNetworkedField]
    public float TotalXp;

    [DataField("developmentPoints"), AutoNetworkedField]
    public int DevelopmentPoints;

    [DataField("patronSoulOfferCount"), AutoNetworkedField]
    public int PatronSoulOfferCount;

    [DataField("boundSkrizhal"), AutoNetworkedField]
    public EntityUid? BoundSkrizhal;

    [DataField("starterSkrizhalIssued")]
    public bool StarterSkrizhalIssued;

    [DataField("maxLevel"), AutoNetworkedField]
    public int MaxLevel = 10;

    [DataField("pointsPerLevel")]
    public int PointsPerLevel = 3;

    [DataField("xpPerLevelStep")]
    public float XpPerLevelStep = 100f;

    [DataField("passiveXpBasePerTick")]
    public float PassiveXpBasePerTick = 1f;

    [DataField("passiveXpPerLevelBonus")]
    public float PassiveXpPerLevelBonus = 0.025f;

    [DataField("passiveXpInterval")]
    public TimeSpan PassiveXpInterval = TimeSpan.FromMinutes(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextPassiveXpAt;

    [DataField("attunedPatron"), AutoNetworkedField]
    public WH40KChaosPatron AttunedPatron = WH40KChaosPatron.None;

    [DataField("patronSelectionLocked"), AutoNetworkedField]
    public bool PatronSelectionLocked;

    [DataField("primaryGiftSlot"), AutoNetworkedField]
    public int PrimaryGiftSlot;

    [DataField("giftSlotOneUnlocked"), AutoNetworkedField]
    public bool GiftSlotOneUnlocked;

    [DataField("giftSlotTwoUnlocked"), AutoNetworkedField]
    public bool GiftSlotTwoUnlocked;

    [DataField("giftSlotThreeUnlocked"), AutoNetworkedField]
    public bool GiftSlotThreeUnlocked;

    [DataField("khorneGiftOnePowerTier"), AutoNetworkedField]
    public byte KhorneGiftOnePowerTier;

    [DataField("khorneGiftOneCooldownTier"), AutoNetworkedField]
    public byte KhorneGiftOneCooldownTier;

    [DataField("khorneGiftOneUtilityTier"), AutoNetworkedField]
    public byte KhorneGiftOneUtilityTier;

    [DataField("khorneGiftOneExUnlocked"), AutoNetworkedField]
    public bool KhorneGiftOneExUnlocked;

    [DataField("khorneGiftTwoPowerTier"), AutoNetworkedField]
    public byte KhorneGiftTwoPowerTier;

    [DataField("khorneGiftTwoCooldownTier"), AutoNetworkedField]
    public byte KhorneGiftTwoCooldownTier;

    [DataField("khorneGiftTwoUtilityTier"), AutoNetworkedField]
    public byte KhorneGiftTwoUtilityTier;

    [DataField("khorneGiftTwoExUnlocked"), AutoNetworkedField]
    public bool KhorneGiftTwoExUnlocked;

    [DataField("khorneGiftThreePowerTier"), AutoNetworkedField]
    public byte KhorneGiftThreePowerTier;

    [DataField("khorneGiftThreeCooldownTier"), AutoNetworkedField]
    public byte KhorneGiftThreeCooldownTier;

    [DataField("khorneGiftThreeUtilityTier"), AutoNetworkedField]
    public byte KhorneGiftThreeUtilityTier;

    [DataField("khorneGiftThreeExUnlocked"), AutoNetworkedField]
    public bool KhorneGiftThreeExUnlocked;

    [DataField("khornePassiveSpeedTier"), AutoNetworkedField]
    public byte KhornePassiveSpeedTier;

    [DataField("khornePassiveHealthTier"), AutoNetworkedField]
    public byte KhornePassiveHealthTier;

    [DataField("khornePassiveMeleeTier"), AutoNetworkedField]
    public byte KhornePassiveMeleeTier;

    [DataField("khornePassiveExUnlocked"), AutoNetworkedField]
    public bool KhornePassiveExUnlocked;

    [DataField("giftUnlockCost"), AutoNetworkedField]
    public int GiftUnlockCost = 3;

    [DataField("attunementXpMultiplier"), AutoNetworkedField]
    public float AttunementXpMultiplier = 1f;

    [DataField("allowPatronSwitch")]
    public bool AllowPatronSwitch;

    [DataField("patronLeadershipOrder")]
    public int PatronLeadershipOrder;

    [DataField("effectiveLeader"), AutoNetworkedField]
    public bool EffectiveLeader;

    [DataField("ritualBonusMultiplier"), AutoNetworkedField]
    public float RitualBonusMultiplier = 1f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan RitualBonusExpiresAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextSacrificeAt;
}
