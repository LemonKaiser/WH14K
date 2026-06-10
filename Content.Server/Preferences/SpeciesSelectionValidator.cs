using System.Collections.Generic;
using System.Linq;
using Content.Server._WH40K.MetaProgress;
using Content.Shared.Administration.Managers;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Preferences;

public static class SpeciesSelectionValidator
{
    public static HumanoidCharacterProfile ValidateProfile(
        HumanoidCharacterProfile profile,
        ICommonSession session,
        IDependencyCollection collection,
        IPrototypeManager prototypeManager,
        ISharedAdminManager adminManager,
        IEntitySystemManager entitySystems)
    {
        var validated = profile.Validated(session, collection);
        return EnsureUnlocked(validated, session, collection, prototypeManager, adminManager, entitySystems);
    }

    public static HumanoidCharacterProfile EnsureUnlocked(
        HumanoidCharacterProfile profile,
        ICommonSession session,
        IPrototypeManager prototypeManager,
        ISharedAdminManager adminManager,
        IEntitySystemManager entitySystems)
    {
        return EnsureUnlocked(profile, session, null, prototypeManager, adminManager, entitySystems);
    }

    private static HumanoidCharacterProfile EnsureUnlocked(
        HumanoidCharacterProfile profile,
        ICommonSession session,
        IDependencyCollection? collection,
        IPrototypeManager prototypeManager,
        ISharedAdminManager adminManager,
        IEntitySystemManager entitySystems)
    {
        if (!prototypeManager.TryIndex(profile.Species, out SpeciesPrototype? species))
            return collection == null ? profile : profile.Validated(session, collection);

        HashSet<string>? completedAchievements = null;
        var metaProgress = entitySystems.GetEntitySystem<WH40KMetaProgressSystem>();
        if (!metaProgress.TryGetLoadedSnapshot(session, out var snapshot))
            return profile;

        if (species.RequiredAchievements.Count > 0)
        {
            completedAchievements = snapshot.Achievements
                .Where(entry => entry.Completed)
                .Select(entry => entry.Id)
                .ToHashSet();
        }

        if (SpeciesSelectionRequirements.IsUnlocked(species, adminManager.IsAdmin(session), snapshot.Level, completedAchievements))
            return profile;

        var fallback = profile.WithSpecies(HumanoidCharacterProfile.DefaultSpecies);
        return collection == null
            ? fallback.Validated(session, IoCManager.Instance!)
            : fallback.Validated(session, collection);
    }
}
