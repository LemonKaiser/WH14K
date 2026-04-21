namespace Content.Shared.Administration;

/// <summary>
/// Shared constants and helpers for admin hierarchy levels.
/// </summary>
public static class AdminHierarchy
{
    public const byte HostHierarchyLevel = 0;
    public const byte HighestRankLevel = 1;
    public const byte LowestRankLevel = 9;
    public const byte DefaultHierarchyLevel = LowestRankLevel;

    public static bool IsValidRankLevel(byte level)
    {
        return level is >= HighestRankLevel and <= LowestRankLevel;
    }
}
