using System;

namespace Content.Shared._WH40K.MetaProgress;

public static class WH40KMetaProgressMath
{
    public static int CalculateTotalSkillPointsForLevel(int level, WH40KMetaLevelRewardTablePrototype table)
    {
        if (level <= 0)
            return 0;

        var total = 0;

        for (var currentLevel = 1; currentLevel <= level; currentLevel++)
        {
            var reward = table.Entries.Find(candidate => candidate.Level == currentLevel);
            total += Math.Max(0, reward?.SkillPoints ?? table.DefaultSkillPoints);
        }

        return total;
    }

    public static int NormalizeAchievementTarget(int target)
    {
        return Math.Max(1, target);
    }

    public static int ClampAchievementProgress(int progress, int target)
    {
        var normalizedTarget = NormalizeAchievementTarget(target);
        return Math.Clamp(progress, 0, normalizedTarget);
    }

    public static bool IsAchievementCompleted(int progress, int target)
    {
        var normalizedTarget = NormalizeAchievementTarget(target);
        return ClampAchievementProgress(progress, normalizedTarget) >= normalizedTarget;
    }

    public static int LifetimeXpFromOverallPlaytime(TimeSpan overallPlaytime)
    {
        var totalMinutes = Math.Max(0, (int) Math.Floor(overallPlaytime.TotalMinutes));
        return totalMinutes * 2;
    }

    public static (int Level, int CurrentXp, int RequiredXp, int LifetimeXp) CalculateFromLifetimeXp(int lifetimeXp, int levelCap = 0)
    {
        var normalizedLifetimeXp = Math.Max(0, lifetimeXp);
        var level = 1;
        var xpLeft = normalizedLifetimeXp;
        var required = GetRequiredXpForLevel(level);

        while (xpLeft >= required && (levelCap <= 0 || level < levelCap))
        {
            xpLeft -= required;
            level++;
            required = GetRequiredXpForLevel(level);
        }

        if (levelCap > 0 && level >= levelCap)
        {
            level = levelCap;
            xpLeft = Math.Clamp(xpLeft, 0, required);
        }

        return (level, xpLeft, required, normalizedLifetimeXp);
    }

    public static int GetRequiredXpForLevel(int level)
    {
        var normalized = Math.Max(1, level);
        var value = 120 + 45 * Math.Pow(normalized, 1.30);
        return Math.Max(120, (int) Math.Round(value));
    }
}
