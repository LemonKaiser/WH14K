using System;
using System.Collections.Generic;

namespace Content.Shared._WH40K.GameMode;

public readonly record struct WH40KTeamXpAdjustment(
    int TeamXp,
    int Level,
    int AppliedDelta);

public static class WH40KTeamProgressionMath
{
    public static WH40KTeamXpAdjustment AdjustTeamXp(
        int currentTeamXp,
        int currentLevel,
        IReadOnlyList<int> thresholds,
        int delta,
        bool allowDecrease)
    {
        var clampedCurrentXp = Math.Max(0, currentTeamXp);
        var oldLevel = Math.Max(1, currentLevel);
        var nextTeamXp = clampedCurrentXp;

        if (delta > 0)
        {
            nextTeamXp = clampedCurrentXp + delta;
        }
        else if (delta < 0 && allowDecrease)
        {
            nextTeamXp = Math.Max(0, clampedCurrentXp + delta);
        }

        var calculatedLevel = CalculateLevel(nextTeamXp, thresholds);
        var nextLevel = allowDecrease
            ? calculatedLevel
            : Math.Max(oldLevel, calculatedLevel);

        return new WH40KTeamXpAdjustment(
            nextTeamXp,
            nextLevel,
            nextTeamXp - clampedCurrentXp);
    }

    public static int CalculateLevel(int points, IReadOnlyList<int> thresholds)
    {
        var level = 1;
        var safePoints = Math.Max(0, points);

        foreach (var threshold in thresholds)
        {
            if (safePoints < threshold)
                break;

            level++;
        }

        return Math.Max(1, level);
    }
}
