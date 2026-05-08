using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.StrategicPoints;

[Serializable, NetSerializable]
public enum WH40KStrategicPointUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum WH40KStrategicPointUiStatus : byte
{
    Ready,
    MaxTier,
    MissingMaterials,
    UpgradeInProgress
}

[Serializable, NetSerializable]
public sealed class WH40KStrategicPointMaterialUiEntry(string stackId, string nameLocKey, int required, int loaded)
{
    public string StackId { get; } = stackId;
    public string NameLocKey { get; } = nameLocKey;
    public int Required { get; } = required;
    public int Loaded { get; } = loaded;
}

[Serializable, NetSerializable]
public sealed class WH40KStrategicPointIncomeUiEntry(string locKey, int baseAmount, int effectiveAmount)
{
    public string LocKey { get; } = locKey;
    public int BaseAmount { get; } = baseAmount;
    public int EffectiveAmount { get; } = effectiveAmount;
}

[Serializable, NetSerializable]
public sealed class WH40KStrategicPointBuiState : BoundUserInterfaceState
{
    public string ThemeTeamId { get; }
    public string OwnerTeamId { get; }
    public string Callsign { get; }
    public WH40KStrategicPointType PointType { get; }
    public WH40KStrategicPointTier Tier { get; }
    public int Hp { get; }
    public int MaxHp { get; }
    public int IncomeIntervalSeconds { get; }
    public WH40KStrategicPointIncomeUiEntry[] IncomeEntries { get; }
    public WH40KStrategicPointMaterialUiEntry[] MaterialEntries { get; }
    public WH40KStrategicPointUiStatus Status { get; }
    public WH40KStrategicPointTier NextTier { get; }
    public int UpgradeSeconds { get; }
    public bool UpgradeInProgress { get; }
    public bool HasNextUpgrade { get; }
    public bool MaterialsComplete { get; }

    public WH40KStrategicPointBuiState(
        string themeTeamId,
        string ownerTeamId,
        string callsign,
        WH40KStrategicPointType pointType,
        WH40KStrategicPointTier tier,
        int hp,
        int maxHp,
        int incomeIntervalSeconds,
        WH40KStrategicPointIncomeUiEntry[] incomeEntries,
        WH40KStrategicPointMaterialUiEntry[] materialEntries,
        WH40KStrategicPointUiStatus status,
        WH40KStrategicPointTier nextTier,
        int upgradeSeconds,
        bool upgradeInProgress,
        bool hasNextUpgrade,
        bool materialsComplete)
    {
        ThemeTeamId = themeTeamId;
        OwnerTeamId = ownerTeamId;
        Callsign = callsign;
        PointType = pointType;
        Tier = tier;
        Hp = hp;
        MaxHp = maxHp;
        IncomeIntervalSeconds = incomeIntervalSeconds;
        IncomeEntries = incomeEntries;
        MaterialEntries = materialEntries;
        Status = status;
        NextTier = nextTier;
        UpgradeSeconds = upgradeSeconds;
        UpgradeInProgress = upgradeInProgress;
        HasNextUpgrade = hasNextUpgrade;
        MaterialsComplete = materialsComplete;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KStrategicPointStartUpgradeMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KStrategicPointRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed partial class WH40KStrategicPointUpgradeDoAfterEvent : DoAfterEvent
{
    public WH40KStrategicPointTier TargetTier;

    public WH40KStrategicPointUpgradeDoAfterEvent()
    {
    }

    public WH40KStrategicPointUpgradeDoAfterEvent(WH40KStrategicPointTier targetTier)
    {
        TargetTier = targetTier;
    }

    public override DoAfterEvent Clone()
    {
        return new WH40KStrategicPointUpgradeDoAfterEvent(TargetTier);
    }
}
