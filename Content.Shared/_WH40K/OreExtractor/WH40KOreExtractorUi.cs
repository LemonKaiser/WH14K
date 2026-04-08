using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.OreExtractor;

[Serializable, NetSerializable]
public enum WH40KOreExtractorUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum WH40KOreExtractorUiStatus : byte
{
    Ready,
    Disabled,
    Unpowered,
    OutputBlocked,
    OutputSaturated,
    NoOutput,
    NoConfiguredOres
}

[Serializable, NetSerializable]
public sealed class WH40KOreExtractorUiOreEntry(string oreId, int unlockTier)
{
    public string OreId { get; } = oreId;
    public int UnlockTier { get; } = unlockTier;
}

[Serializable, NetSerializable]
public sealed class WH40KOreExtractorBuiState : BoundUserInterfaceState
{
    public string ThemeTeamId { get; }
    public string[] TrackedTeamIds { get; }
    public string OutputDirectionLocKey { get; }
    public WH40KOreExtractorUiOreEntry[] OreEntries { get; }
    public string[] AllowedOreIds { get; }
    public string? SelectedOreId { get; }
    public WH40KOreExtractorUiStatus Status { get; }
    public bool Enabled { get; }
    public bool Powered { get; }
    public bool RequirePowered { get; }
    public bool HasOutputTile { get; }
    public int OutputOccupancy { get; }
    public int MaxItemsOnOutputTile { get; }
    public int CurrentTier { get; }
    public int EffectiveLevel { get; }
    public int NodeUpgradeBonus { get; }
    public int NextTierLevel { get; }
    public int RemainingToNextTier { get; }
    public float SpawnIntervalSeconds { get; }
    public int SpawnCount { get; }
    public int NextSpawnSeconds { get; }

    public WH40KOreExtractorBuiState(
        string themeTeamId,
        string[] trackedTeamIds,
        string outputDirectionLocKey,
        WH40KOreExtractorUiOreEntry[] oreEntries,
        string[] allowedOreIds,
        string? selectedOreId,
        WH40KOreExtractorUiStatus status,
        bool enabled,
        bool powered,
        bool requirePowered,
        bool hasOutputTile,
        int outputOccupancy,
        int maxItemsOnOutputTile,
        int currentTier,
        int effectiveLevel,
        int nodeUpgradeBonus,
        int nextTierLevel,
        int remainingToNextTier,
        float spawnIntervalSeconds,
        int spawnCount,
        int nextSpawnSeconds)
    {
        ThemeTeamId = themeTeamId;
        TrackedTeamIds = trackedTeamIds;
        OutputDirectionLocKey = outputDirectionLocKey;
        OreEntries = oreEntries;
        AllowedOreIds = allowedOreIds;
        SelectedOreId = selectedOreId;
        Status = status;
        Enabled = enabled;
        Powered = powered;
        RequirePowered = requirePowered;
        HasOutputTile = hasOutputTile;
        OutputOccupancy = outputOccupancy;
        MaxItemsOnOutputTile = maxItemsOnOutputTile;
        CurrentTier = currentTier;
        EffectiveLevel = effectiveLevel;
        NodeUpgradeBonus = nodeUpgradeBonus;
        NextTierLevel = nextTierLevel;
        RemainingToNextTier = remainingToNextTier;
        SpawnIntervalSeconds = spawnIntervalSeconds;
        SpawnCount = spawnCount;
        NextSpawnSeconds = nextSpawnSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class WH40KOreExtractorSetEnabledMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class WH40KOreExtractorSetRandomModeMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WH40KOreExtractorSelectOreMessage(string oreId) : BoundUserInterfaceMessage
{
    public string OreId = oreId;
}
