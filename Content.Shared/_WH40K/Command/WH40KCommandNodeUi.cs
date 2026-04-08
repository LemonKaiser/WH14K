using Content.Shared._WH40K.GameMode;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Command;

[Serializable, NetSerializable]
public enum WH40KCommandNodeUiKey : byte
{
    Key,
    Reinforcement,
    UpgradeTree,
    MissionBoard
}

[Serializable, NetSerializable]
public sealed class WH40KTeamCompositionRoleEntry
{
    public string RoleName { get; }
    public int Count { get; }

    public WH40KTeamCompositionRoleEntry(string roleName, int count)
    {
        RoleName = roleName;
        Count = count;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KTeamCompositionMemberEntry
{
    public string Name { get; }
    public string RoleName { get; }

    public WH40KTeamCompositionMemberEntry(string name, string roleName)
    {
        Name = name;
        RoleName = roleName;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeReinforcementOptionState
{
    public string OptionId { get; }
    public string Name { get; }
    public string Description { get; }
    public string GearSummary { get; }
    public string PreviewPrototypeId { get; }
    public int CostX1 { get; }
    public int CostX2 { get; }
    public int CostX3 { get; }
    public int MaxCount { get; }

    public WH40KCommandNodeReinforcementOptionState(
        string optionId,
        string name,
        string description,
        string gearSummary,
        string previewPrototypeId,
        int costX1,
        int costX2,
        int costX3,
        int maxCount)
    {
        OptionId = optionId;
        Name = name;
        Description = description;
        GearSummary = gearSummary;
        PreviewPrototypeId = previewPrototypeId;
        CostX1 = costX1;
        CostX2 = costX2;
        CostX3 = costX3;
        MaxCount = maxCount;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeBonusIntelState
{
    public bool HasEngineeringProfile { get; }
    public int EngineeringTier { get; }
    public int EngineeringSpeedBonusPercent { get; }
    public float EngineeringMinProcessSeconds { get; }
    public int EngineeringMaterialStorageLimit { get; }
    public float EngineeringGlobalTimeMultiplier { get; }

    public bool HasOreExtractorProfile { get; }
    public int OreExtractorTier { get; }
    public float OreExtractorSpawnIntervalSeconds { get; }
    public int OreExtractorSpawnCount { get; }
    public string OreExtractorAllowedOreNames { get; }

    public bool HasLogisticsProfile { get; }
    public int LogisticsTier { get; }
    public int LogisticsTierMaxItemsBonus { get; }
    public int LogisticsTierDeliveryReductionMinutes { get; }
    public int LogisticsExternalDeliverySpeedBonusPercent { get; }
    public int LogisticsExternalMaxItemsBonusPercent { get; }
    public int LogisticsExternalPriceDiscountPercent { get; }

    public bool HasSpecialLatheProfile { get; }
    public int SpecialLatheTier { get; }
    public int SpecialLatheSpeedBonusPercent { get; }
    public float SpecialLatheProcessSeconds { get; }
    public int SpecialLatheMaterialStorageLimit { get; }
    public int SpecialLatheOutputMultiplier { get; }

    public int NodePassiveFrontPointsPerTick { get; }
    public float NodePassiveIntervalSeconds { get; }

    public WH40KCommandNodeBonusIntelState(
        bool hasEngineeringProfile,
        int engineeringTier,
        int engineeringSpeedBonusPercent,
        float engineeringMinProcessSeconds,
        int engineeringMaterialStorageLimit,
        float engineeringGlobalTimeMultiplier,
        bool hasOreExtractorProfile,
        int oreExtractorTier,
        float oreExtractorSpawnIntervalSeconds,
        int oreExtractorSpawnCount,
        string oreExtractorAllowedOreNames,
        bool hasLogisticsProfile,
        int logisticsTier,
        int logisticsTierMaxItemsBonus,
        int logisticsTierDeliveryReductionMinutes,
        int logisticsExternalDeliverySpeedBonusPercent,
        int logisticsExternalMaxItemsBonusPercent,
        int logisticsExternalPriceDiscountPercent,
        bool hasSpecialLatheProfile,
        int specialLatheTier,
        int specialLatheSpeedBonusPercent,
        float specialLatheProcessSeconds,
        int specialLatheMaterialStorageLimit,
        int specialLatheOutputMultiplier,
        int nodePassiveFrontPointsPerTick,
        float nodePassiveIntervalSeconds)
    {
        HasEngineeringProfile = hasEngineeringProfile;
        EngineeringTier = engineeringTier;
        EngineeringSpeedBonusPercent = engineeringSpeedBonusPercent;
        EngineeringMinProcessSeconds = engineeringMinProcessSeconds;
        EngineeringMaterialStorageLimit = engineeringMaterialStorageLimit;
        EngineeringGlobalTimeMultiplier = engineeringGlobalTimeMultiplier;
        HasOreExtractorProfile = hasOreExtractorProfile;
        OreExtractorTier = oreExtractorTier;
        OreExtractorSpawnIntervalSeconds = oreExtractorSpawnIntervalSeconds;
        OreExtractorSpawnCount = oreExtractorSpawnCount;
        OreExtractorAllowedOreNames = oreExtractorAllowedOreNames;
        HasLogisticsProfile = hasLogisticsProfile;
        LogisticsTier = logisticsTier;
        LogisticsTierMaxItemsBonus = logisticsTierMaxItemsBonus;
        LogisticsTierDeliveryReductionMinutes = logisticsTierDeliveryReductionMinutes;
        LogisticsExternalDeliverySpeedBonusPercent = logisticsExternalDeliverySpeedBonusPercent;
        LogisticsExternalMaxItemsBonusPercent = logisticsExternalMaxItemsBonusPercent;
        LogisticsExternalPriceDiscountPercent = logisticsExternalPriceDiscountPercent;
        HasSpecialLatheProfile = hasSpecialLatheProfile;
        SpecialLatheTier = specialLatheTier;
        SpecialLatheSpeedBonusPercent = specialLatheSpeedBonusPercent;
        SpecialLatheProcessSeconds = specialLatheProcessSeconds;
        SpecialLatheMaterialStorageLimit = specialLatheMaterialStorageLimit;
        SpecialLatheOutputMultiplier = specialLatheOutputMultiplier;
        NodePassiveFrontPointsPerTick = nodePassiveFrontPointsPerTick;
        NodePassiveIntervalSeconds = nodePassiveIntervalSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeBoundUserInterfaceState : BoundUserInterfaceState
{
    public string TeamId { get; }
    public string TeamName { get; }
    public string ActiveDoctrineId { get; }
    public bool DoctrineLocked { get; }
    public string ActiveBattleTacticId { get; }
    public int BattleTacticCooldownSeconds { get; }
    public WH40KBattlePhase Phase { get; }
    public int BaseLevel { get; }
    public int FrontPoints { get; }
    public int CommandPoints { get; }
    public int UpgradeLevel { get; }
    public int UpgradeCost { get; }
    public int ReinforcementCost { get; }
    public int ReinforcementCooldownSeconds { get; }
    public int RoundElapsedSeconds { get; }
    public int? PointsToNextLevel { get; }
    public int[] LevelThresholds { get; }
    public WH40KCommandNodeReinforcementOptionState[] ReinforcementOptions { get; }
    public string[] UnlockOverview { get; }
    public string[] PurchasedTreeNodeIds { get; }
    public string TeamCompositionSummary { get; }
    public string[] TeamCompositionLines { get; }
    public string[] TeamCompositionStaffingLines { get; }
    public WH40KTeamCompositionRoleEntry[] TeamCompositionOfficerRoles { get; }
    public WH40KTeamCompositionRoleEntry[] TeamCompositionCoreRoles { get; }
    public WH40KTeamCompositionRoleEntry[] TeamCompositionMechanicusRoles { get; }
    public WH40KTeamCompositionMemberEntry[] TeamCompositionMembers { get; }
    public WH40KCommandNodeBonusIntelState BonusIntel { get; }
    public WH40KCommandTeamEventRuntimeState TeamEventRuntime { get; }
    public WH40KCommandMissionRuntimeState GlobalMissionRuntime { get; }
    public WH40KCommandMissionRuntimeState TeamMissionRuntime { get; }
    public WH40KCommandMissionBoardState MissionBoard { get; }

    public WH40KCommandNodeBoundUserInterfaceState(
        string teamId,
        string teamName,
        string activeDoctrineId,
        bool doctrineLocked,
        string activeBattleTacticId,
        int battleTacticCooldownSeconds,
        WH40KBattlePhase phase,
        int baseLevel,
        int frontPoints,
        int commandPoints,
        int upgradeLevel,
        int upgradeCost,
        int reinforcementCost,
        int reinforcementCooldownSeconds,
        int roundElapsedSeconds,
        int? pointsToNextLevel,
        int[] levelThresholds,
        WH40KCommandNodeReinforcementOptionState[] reinforcementOptions,
        string[] unlockOverview,
        string[] purchasedTreeNodeIds,
        string teamCompositionSummary,
        string[] teamCompositionLines,
        string[] teamCompositionStaffingLines,
        WH40KTeamCompositionRoleEntry[] teamCompositionOfficerRoles,
        WH40KTeamCompositionRoleEntry[] teamCompositionCoreRoles,
        WH40KTeamCompositionRoleEntry[] teamCompositionMechanicusRoles,
        WH40KTeamCompositionMemberEntry[] teamCompositionMembers,
        WH40KCommandNodeBonusIntelState bonusIntel,
        WH40KCommandTeamEventRuntimeState teamEventRuntime,
        WH40KCommandMissionRuntimeState globalMissionRuntime,
        WH40KCommandMissionRuntimeState teamMissionRuntime,
        WH40KCommandMissionBoardState missionBoard)
    {
        TeamId = teamId;
        TeamName = teamName;
        ActiveDoctrineId = activeDoctrineId;
        DoctrineLocked = doctrineLocked;
        ActiveBattleTacticId = activeBattleTacticId;
        BattleTacticCooldownSeconds = battleTacticCooldownSeconds;
        Phase = phase;
        BaseLevel = baseLevel;
        FrontPoints = frontPoints;
        CommandPoints = commandPoints;
        UpgradeLevel = upgradeLevel;
        UpgradeCost = upgradeCost;
        ReinforcementCost = reinforcementCost;
        ReinforcementCooldownSeconds = reinforcementCooldownSeconds;
        RoundElapsedSeconds = roundElapsedSeconds;
        PointsToNextLevel = pointsToNextLevel;
        LevelThresholds = levelThresholds;
        ReinforcementOptions = reinforcementOptions;
        UnlockOverview = unlockOverview;
        PurchasedTreeNodeIds = purchasedTreeNodeIds;
        TeamCompositionSummary = teamCompositionSummary;
        TeamCompositionLines = teamCompositionLines;
        TeamCompositionStaffingLines = teamCompositionStaffingLines;
        TeamCompositionOfficerRoles = teamCompositionOfficerRoles;
        TeamCompositionCoreRoles = teamCompositionCoreRoles;
        TeamCompositionMechanicusRoles = teamCompositionMechanicusRoles;
        TeamCompositionMembers = teamCompositionMembers;
        BonusIntel = bonusIntel;
        TeamEventRuntime = teamEventRuntime;
        GlobalMissionRuntime = globalMissionRuntime;
        TeamMissionRuntime = teamMissionRuntime;
        MissionBoard = missionBoard;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeUpgradePressedMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeCallReinforcementMessage : BoundUserInterfaceMessage
{
    public string OptionId { get; }
    public int Count { get; }

    public WH40KCommandNodeCallReinforcementMessage(string optionId, int count)
    {
        OptionId = optionId;
        Count = count;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodePurchaseTreeNodeMessage : BoundUserInterfaceMessage
{
    public string NodeId { get; }

    public WH40KCommandNodePurchaseTreeNodeMessage(string nodeId)
    {
        NodeId = nodeId;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeTeamCompositionPressedMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeAssignBattleTacticMessage : BoundUserInterfaceMessage
{
    public string TacticId { get; }

    public WH40KCommandNodeAssignBattleTacticMessage(string tacticId)
    {
        TacticId = tacticId;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeAssignDoctrineMessage : BoundUserInterfaceMessage
{
    public string DoctrineId { get; }

    public WH40KCommandNodeAssignDoctrineMessage(string doctrineId)
    {
        DoctrineId = doctrineId;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeAssignMissionTaskMessage : BoundUserInterfaceMessage
{
    public string TaskId { get; }

    public WH40KCommandNodeAssignMissionTaskMessage(string taskId)
    {
        TaskId = taskId;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeSyncMissionPinpointerMessage : BoundUserInterfaceMessage
{
}
