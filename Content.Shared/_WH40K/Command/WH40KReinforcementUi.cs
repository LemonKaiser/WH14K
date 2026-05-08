using System;
using Content.Shared._WH40K.GameMode;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.Command;

[Serializable, NetSerializable]
public enum WH40KCommandReinforcementRequestKind : byte
{
    Manual,
    Auto
}

[Serializable, NetSerializable]
public sealed class WH40KCommandReinforcementCatalogEntryState
{
    public string RoleId { get; }
    public string Name { get; }
    public string Description { get; }
    public string GroupKey { get; }
    public string GearSummary { get; }
    public string PreviewPrototypeId { get; }
    public int UnitCost { get; }
    public int UnitFundsCost { get; }
    public int UnitInfluenceCost { get; }
    public int PerRoleCap { get; }
    public bool AllowAuto { get; }

    public WH40KCommandReinforcementCatalogEntryState(
        string roleId,
        string name,
        string description,
        string groupKey,
        string gearSummary,
        string previewPrototypeId,
        int unitCost,
        int unitFundsCost,
        int unitInfluenceCost,
        int perRoleCap,
        bool allowAuto)
    {
        RoleId = roleId;
        Name = name;
        Description = description;
        GroupKey = groupKey;
        GearSummary = gearSummary;
        PreviewPrototypeId = previewPrototypeId;
        UnitCost = unitCost;
        UnitFundsCost = unitFundsCost;
        UnitInfluenceCost = unitInfluenceCost;
        PerRoleCap = perRoleCap;
        AllowAuto = allowAuto;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandReinforcementDraftEntry
{
    public string RoleId { get; }
    public int Count { get; }

    public WH40KCommandReinforcementDraftEntry(string roleId, int count)
    {
        RoleId = roleId;
        Count = count;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandReinforcementPendingRoleState
{
    public string RoleId { get; }
    public string Name { get; }
    public int Count { get; }
    public int UnitCost { get; }
    public int TotalCost { get; }
    public int UnitFundsCost { get; }
    public int UnitInfluenceCost { get; }
    public int TotalFundsCost { get; }
    public int TotalInfluenceCost { get; }

    public WH40KCommandReinforcementPendingRoleState(
        string roleId,
        string name,
        int count,
        int unitCost,
        int totalCost,
        int unitFundsCost,
        int unitInfluenceCost,
        int totalFundsCost,
        int totalInfluenceCost)
    {
        RoleId = roleId;
        Name = name;
        Count = count;
        UnitCost = unitCost;
        TotalCost = totalCost;
        UnitFundsCost = unitFundsCost;
        UnitInfluenceCost = unitInfluenceCost;
        TotalFundsCost = totalFundsCost;
        TotalInfluenceCost = totalInfluenceCost;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandReinforcementPendingRequestState
{
    public WH40KCommandReinforcementRequestKind Kind { get; }
    public int ArrivalSeconds { get; }
    public int TotalCount { get; }
    public int TotalCost { get; }
    public int TotalFundsCost { get; }
    public int TotalInfluenceCost { get; }
    public WH40KCommandReinforcementPendingRoleState[] Roles { get; }

    public WH40KCommandReinforcementPendingRequestState(
        WH40KCommandReinforcementRequestKind kind,
        int arrivalSeconds,
        int totalCount,
        int totalCost,
        int totalFundsCost,
        int totalInfluenceCost,
        WH40KCommandReinforcementPendingRoleState[] roles)
    {
        Kind = kind;
        ArrivalSeconds = arrivalSeconds;
        TotalCount = totalCount;
        TotalCost = totalCost;
        TotalFundsCost = totalFundsCost;
        TotalInfluenceCost = totalInfluenceCost;
        Roles = roles;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandReinforcementAutoConfigState
{
    public bool Enabled { get; }
    public int ThresholdPercent { get; }
    public int TotalCount { get; }
    public int TotalCost { get; }
    public int TotalFundsCost { get; }
    public int TotalInfluenceCost { get; }
    public WH40KCommandReinforcementDraftEntry[] Roles { get; }

    public WH40KCommandReinforcementAutoConfigState(
        bool enabled,
        int thresholdPercent,
        int totalCount,
        int totalCost,
        int totalFundsCost,
        int totalInfluenceCost,
        WH40KCommandReinforcementDraftEntry[] roles)
    {
        Enabled = enabled;
        ThresholdPercent = thresholdPercent;
        TotalCount = totalCount;
        TotalCost = totalCost;
        TotalFundsCost = totalFundsCost;
        TotalInfluenceCost = totalInfluenceCost;
        Roles = roles;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandReinforcementBoundUserInterfaceState : BoundUserInterfaceState
{
    public string TeamId { get; }
    public string TeamName { get; }
    public WH40KBattlePhase Phase { get; }
    public int CommandPoints { get; }
    public int InfluencePoints { get; }
    public int Funds { get; }
    public int CooldownSeconds { get; }
    public int ManualDelaySeconds { get; }
    public int AutoDelaySeconds { get; }
    public int AutoCheckIntervalSeconds { get; }
    public int MaxTotalCount { get; }
    public int AliveCount { get; }
    public int TotalCount { get; }
    public int AlivePercent { get; }
    public WH40KCommandReinforcementCatalogEntryState[] Catalog { get; }
    public WH40KCommandReinforcementAutoConfigState AutoConfig { get; }
    public WH40KCommandReinforcementPendingRequestState? PendingRequest { get; }

    public WH40KCommandReinforcementBoundUserInterfaceState(
        string teamId,
        string teamName,
        WH40KBattlePhase phase,
        int commandPoints,
        int influencePoints,
        int funds,
        int cooldownSeconds,
        int manualDelaySeconds,
        int autoDelaySeconds,
        int autoCheckIntervalSeconds,
        int maxTotalCount,
        int aliveCount,
        int totalCount,
        int alivePercent,
        WH40KCommandReinforcementCatalogEntryState[] catalog,
        WH40KCommandReinforcementAutoConfigState autoConfig,
        WH40KCommandReinforcementPendingRequestState? pendingRequest)
    {
        TeamId = teamId;
        TeamName = teamName;
        Phase = phase;
        CommandPoints = commandPoints;
        InfluencePoints = influencePoints;
        Funds = funds;
        CooldownSeconds = cooldownSeconds;
        ManualDelaySeconds = manualDelaySeconds;
        AutoDelaySeconds = autoDelaySeconds;
        AutoCheckIntervalSeconds = autoCheckIntervalSeconds;
        MaxTotalCount = maxTotalCount;
        AliveCount = aliveCount;
        TotalCount = totalCount;
        AlivePercent = alivePercent;
        Catalog = catalog;
        AutoConfig = autoConfig;
        PendingRequest = pendingRequest;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeSubmitReinforcementRequestMessage : BoundUserInterfaceMessage
{
    public WH40KCommandReinforcementDraftEntry[] Roles { get; }

    public WH40KCommandNodeSubmitReinforcementRequestMessage(WH40KCommandReinforcementDraftEntry[] roles)
    {
        Roles = roles;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KCommandNodeSaveAutoReinforcementMessage : BoundUserInterfaceMessage
{
    public bool Enabled { get; }
    public int ThresholdPercent { get; }
    public WH40KCommandReinforcementDraftEntry[] Roles { get; }

    public WH40KCommandNodeSaveAutoReinforcementMessage(
        bool enabled,
        int thresholdPercent,
        WH40KCommandReinforcementDraftEntry[] roles)
    {
        Enabled = enabled;
        ThresholdPercent = thresholdPercent;
        Roles = roles;
    }
}
