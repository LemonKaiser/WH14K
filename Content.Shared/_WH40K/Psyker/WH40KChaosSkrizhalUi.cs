using System;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Psyker;

[Serializable, NetSerializable]
public enum WH40KChaosSkrizhalUiKey : byte
{
    PatronSelection,
    PatronCultistInfo,
    PatronBranch
}

[Serializable, NetSerializable]
public sealed class WH40KChaosSkrizhalPatronSelectorBuiState : BoundUserInterfaceState
{
    public bool SelectionLocked { get; }
    public WH40KChaosPatron CurrentPatron { get; }
    public string KhorneLeaderName { get; }
    public string NurgleLeaderName { get; }
    public string SlaaneshLeaderName { get; }
    public string TzeentchLeaderName { get; }

    public WH40KChaosSkrizhalPatronSelectorBuiState(
        bool selectionLocked,
        WH40KChaosPatron currentPatron,
        string khorneLeaderName,
        string nurgleLeaderName,
        string slaaneshLeaderName,
        string tzeentchLeaderName)
    {
        SelectionLocked = selectionLocked;
        CurrentPatron = currentPatron;
        KhorneLeaderName = khorneLeaderName;
        NurgleLeaderName = nurgleLeaderName;
        SlaaneshLeaderName = slaaneshLeaderName;
        TzeentchLeaderName = tzeentchLeaderName;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KChaosSkrizhalSelectPatronMessage(WH40KChaosPatron patron) : BoundUserInterfaceMessage
{
    public WH40KChaosPatron Patron = patron;
}

[Serializable, NetSerializable]
public sealed class WH40KChaosSkrizhalPatronBranchBuiState : BoundUserInterfaceState
{
    public WH40KChaosPatron Patron { get; }
    public int Level { get; }
    public int MaxLevel { get; }
    public float LevelXp { get; }
    public float NextLevelXp { get; }
    public int DevelopmentPoints { get; }
    public bool CanEdit { get; }
    public bool HasActiveLeader { get; }
    public string ActiveLeaderName { get; }
    public bool AwaitingLeaderSuccessor { get; }
    public int PrimaryGiftSlot { get; }
    public bool GiftSlotOneUnlocked { get; }
    public bool GiftSlotTwoUnlocked { get; }
    public bool GiftSlotThreeUnlocked { get; }
    public int GiftUnlockCost { get; }
    public int PatronSoulOfferCount { get; }
    public float PassiveXpPerTick { get; }
    public int PassiveXpIntervalSeconds { get; }
    public byte GiftOnePowerTier { get; }
    public byte GiftOneCooldownTier { get; }
    public byte GiftOneUtilityTier { get; }
    public bool GiftOneExUnlocked { get; }
    public byte GiftTwoPowerTier { get; }
    public byte GiftTwoCooldownTier { get; }
    public byte GiftTwoUtilityTier { get; }
    public bool GiftTwoExUnlocked { get; }
    public byte GiftThreePowerTier { get; }
    public byte GiftThreeCooldownTier { get; }
    public byte GiftThreeUtilityTier { get; }
    public bool GiftThreeExUnlocked { get; }
    public byte PassiveSpeedTier { get; }
    public byte PassiveHealthTier { get; }
    public byte PassiveMeleeTier { get; }
    public bool PassiveExUnlocked { get; }

    public WH40KChaosSkrizhalPatronBranchBuiState(
        WH40KChaosPatron patron,
        int level,
        int maxLevel,
        float levelXp,
        float nextLevelXp,
        int developmentPoints,
        bool canEdit,
        bool hasActiveLeader,
        string activeLeaderName,
        bool awaitingLeaderSuccessor,
        int primaryGiftSlot,
        bool giftSlotOneUnlocked,
        bool giftSlotTwoUnlocked,
        bool giftSlotThreeUnlocked,
        int giftUnlockCost,
        int patronSoulOfferCount,
        float passiveXpPerTick,
        int passiveXpIntervalSeconds,
        byte giftOnePowerTier,
        byte giftOneCooldownTier,
        byte giftOneUtilityTier,
        bool giftOneExUnlocked,
        byte giftTwoPowerTier,
        byte giftTwoCooldownTier,
        byte giftTwoUtilityTier,
        bool giftTwoExUnlocked,
        byte giftThreePowerTier,
        byte giftThreeCooldownTier,
        byte giftThreeUtilityTier,
        bool giftThreeExUnlocked,
        byte passiveSpeedTier,
        byte passiveHealthTier,
        byte passiveMeleeTier,
        bool passiveExUnlocked)
    {
        Patron = patron;
        Level = level;
        MaxLevel = maxLevel;
        LevelXp = levelXp;
        NextLevelXp = nextLevelXp;
        DevelopmentPoints = developmentPoints;
        CanEdit = canEdit;
        HasActiveLeader = hasActiveLeader;
        ActiveLeaderName = activeLeaderName;
        AwaitingLeaderSuccessor = awaitingLeaderSuccessor;
        PrimaryGiftSlot = primaryGiftSlot;
        GiftSlotOneUnlocked = giftSlotOneUnlocked;
        GiftSlotTwoUnlocked = giftSlotTwoUnlocked;
        GiftSlotThreeUnlocked = giftSlotThreeUnlocked;
        GiftUnlockCost = giftUnlockCost;
        PatronSoulOfferCount = patronSoulOfferCount;
        PassiveXpPerTick = passiveXpPerTick;
        PassiveXpIntervalSeconds = passiveXpIntervalSeconds;
        GiftOnePowerTier = giftOnePowerTier;
        GiftOneCooldownTier = giftOneCooldownTier;
        GiftOneUtilityTier = giftOneUtilityTier;
        GiftOneExUnlocked = giftOneExUnlocked;
        GiftTwoPowerTier = giftTwoPowerTier;
        GiftTwoCooldownTier = giftTwoCooldownTier;
        GiftTwoUtilityTier = giftTwoUtilityTier;
        GiftTwoExUnlocked = giftTwoExUnlocked;
        GiftThreePowerTier = giftThreePowerTier;
        GiftThreeCooldownTier = giftThreeCooldownTier;
        GiftThreeUtilityTier = giftThreeUtilityTier;
        GiftThreeExUnlocked = giftThreeExUnlocked;
        PassiveSpeedTier = passiveSpeedTier;
        PassiveHealthTier = passiveHealthTier;
        PassiveMeleeTier = passiveMeleeTier;
        PassiveExUnlocked = passiveExUnlocked;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KChaosSkrizhalCultistBuiState : BoundUserInterfaceState
{
    public WH40KChaosPatron Patron { get; }
    public int Level { get; }
    public int MaxLevel { get; }
    public float LevelXp { get; }
    public float NextLevelXp { get; }
    public int DevelopmentPoints { get; }
    public bool HasActiveLeader { get; }
    public string ActiveLeaderName { get; }
    public bool AwaitingLeaderSuccessor { get; }
    public int PrimaryGiftSlot { get; }
    public bool GiftSlotOneUnlocked { get; }
    public bool GiftSlotTwoUnlocked { get; }
    public bool GiftSlotThreeUnlocked { get; }
    public int GiftUnlockCost { get; }
    public int PatronSoulOfferCount { get; }
    public float PassiveXpPerTick { get; }
    public int PassiveXpIntervalSeconds { get; }
    public byte GiftOnePowerTier { get; }
    public byte GiftOneCooldownTier { get; }
    public byte GiftOneUtilityTier { get; }
    public bool GiftOneExUnlocked { get; }
    public byte GiftTwoPowerTier { get; }
    public byte GiftTwoCooldownTier { get; }
    public byte GiftTwoUtilityTier { get; }
    public bool GiftTwoExUnlocked { get; }
    public byte GiftThreePowerTier { get; }
    public byte GiftThreeCooldownTier { get; }
    public byte GiftThreeUtilityTier { get; }
    public bool GiftThreeExUnlocked { get; }
    public byte PassiveSpeedTier { get; }
    public byte PassiveHealthTier { get; }
    public byte PassiveMeleeTier { get; }
    public bool PassiveExUnlocked { get; }

    public WH40KChaosSkrizhalCultistBuiState(
        WH40KChaosPatron patron,
        int level,
        int maxLevel,
        float levelXp,
        float nextLevelXp,
        int developmentPoints,
        bool hasActiveLeader,
        string activeLeaderName,
        bool awaitingLeaderSuccessor,
        int primaryGiftSlot,
        bool giftSlotOneUnlocked,
        bool giftSlotTwoUnlocked,
        bool giftSlotThreeUnlocked,
        int giftUnlockCost,
        int patronSoulOfferCount,
        float passiveXpPerTick,
        int passiveXpIntervalSeconds,
        byte giftOnePowerTier,
        byte giftOneCooldownTier,
        byte giftOneUtilityTier,
        bool giftOneExUnlocked,
        byte giftTwoPowerTier,
        byte giftTwoCooldownTier,
        byte giftTwoUtilityTier,
        bool giftTwoExUnlocked,
        byte giftThreePowerTier,
        byte giftThreeCooldownTier,
        byte giftThreeUtilityTier,
        bool giftThreeExUnlocked,
        byte passiveSpeedTier,
        byte passiveHealthTier,
        byte passiveMeleeTier,
        bool passiveExUnlocked)
    {
        Patron = patron;
        Level = level;
        MaxLevel = maxLevel;
        LevelXp = levelXp;
        NextLevelXp = nextLevelXp;
        DevelopmentPoints = developmentPoints;
        HasActiveLeader = hasActiveLeader;
        ActiveLeaderName = activeLeaderName;
        AwaitingLeaderSuccessor = awaitingLeaderSuccessor;
        PrimaryGiftSlot = primaryGiftSlot;
        GiftSlotOneUnlocked = giftSlotOneUnlocked;
        GiftSlotTwoUnlocked = giftSlotTwoUnlocked;
        GiftSlotThreeUnlocked = giftSlotThreeUnlocked;
        GiftUnlockCost = giftUnlockCost;
        PatronSoulOfferCount = patronSoulOfferCount;
        PassiveXpPerTick = passiveXpPerTick;
        PassiveXpIntervalSeconds = passiveXpIntervalSeconds;
        GiftOnePowerTier = giftOnePowerTier;
        GiftOneCooldownTier = giftOneCooldownTier;
        GiftOneUtilityTier = giftOneUtilityTier;
        GiftOneExUnlocked = giftOneExUnlocked;
        GiftTwoPowerTier = giftTwoPowerTier;
        GiftTwoCooldownTier = giftTwoCooldownTier;
        GiftTwoUtilityTier = giftTwoUtilityTier;
        GiftTwoExUnlocked = giftTwoExUnlocked;
        GiftThreePowerTier = giftThreePowerTier;
        GiftThreeCooldownTier = giftThreeCooldownTier;
        GiftThreeUtilityTier = giftThreeUtilityTier;
        GiftThreeExUnlocked = giftThreeExUnlocked;
        PassiveSpeedTier = passiveSpeedTier;
        PassiveHealthTier = passiveHealthTier;
        PassiveMeleeTier = passiveMeleeTier;
        PassiveExUnlocked = passiveExUnlocked;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KChaosSkrizhalSelectPrimaryGiftMessage(int giftSlot) : BoundUserInterfaceMessage
{
    public int GiftSlot = giftSlot;
}

[Serializable, NetSerializable]
public sealed class WH40KChaosSkrizhalUnlockGiftMessage(int giftSlot) : BoundUserInterfaceMessage
{
    public int GiftSlot = giftSlot;
}

[Serializable, NetSerializable]
public sealed class WH40KChaosSkrizhalUpgradeTierMessage(
    int giftSlot,
    WH40KChaosGiftUpgradePath path,
    int tier) : BoundUserInterfaceMessage
{
    public int GiftSlot = giftSlot;
    public WH40KChaosGiftUpgradePath Path = path;
    public int Tier = tier;
}

[Serializable, NetSerializable]
public sealed class WH40KChaosSkrizhalUnlockExMessage(int giftSlot) : BoundUserInterfaceMessage
{
    public int GiftSlot = giftSlot;
}
