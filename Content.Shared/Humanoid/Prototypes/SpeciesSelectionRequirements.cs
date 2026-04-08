using System.Collections.Generic;
using System.Linq;

namespace Content.Shared.Humanoid.Prototypes;

public static class SpeciesSelectionRequirements
{
    public static bool IsUnlocked(
        SpeciesPrototype species,
        bool isAdmin,
        int metaLevel,
        IReadOnlyCollection<string>? completedAchievements)
    {
        if (species.AdminOnly && !isAdmin)
            return false;

        if (metaLevel < species.RequiredMetaLevel)
            return false;

        var requiredAchievements = species.RequiredAchievements
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (requiredAchievements.Count == 0)
            return true;

        if (completedAchievements == null)
            return false;

        var completedSet = completedAchievements as HashSet<string>
            ?? completedAchievements.ToHashSet();

        return requiredAchievements.All(completedSet.Contains);
    }
}
