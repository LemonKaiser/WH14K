using System;
using Content.Shared.CCVar;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared._WH40K.MetaProgress;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Client._WH40K.MetaProgress;

public sealed partial class WH40KMetaProgressManager : ISharedWH40KMetaProgressManager
{
    [Dependency] private  IConfigurationManager _config = default!;
    [Dependency] private  IEntitySystemManager _entitySystems = default!;
    [Dependency] private  ISharedPlaytimeManager _playtime = default!;

    public bool TryGetMetaLevel(ICommonSession session, out int level)
    {
        var metaSystem = _entitySystems.GetEntitySystem<WH40KMetaProgressSystem>();
        metaSystem.EnsureSnapshot();

        if (metaSystem.TryGetCachedSnapshot(out var snapshot))
        {
            level = Math.Max(1, snapshot.Level);
            return true;
        }

        var playtimes = _playtime.GetPlayTimes(session);
        var overallPlaytime = playtimes.GetValueOrDefault(PlayTimeTrackingShared.TrackerOverall);
        var lifetimeXp = WH40KMetaProgressMath.LifetimeXpFromOverallPlaytime(overallPlaytime);
        var cap = Math.Max(0, _config.GetCVar(CCVars.WH40KMetaLevelCap));
        var preview = WH40KMetaProgressMath.CalculateFromLifetimeXp(lifetimeXp, cap);
        level = Math.Max(1, preview.Level);
        return true;
    }

    public bool TryHasCompletedAchievement(ICommonSession session, string achievementId, out bool completed)
    {
        completed = false;

        if (string.IsNullOrWhiteSpace(achievementId))
            return true;

        var metaSystem = _entitySystems.GetEntitySystem<WH40KMetaProgressSystem>();
        metaSystem.EnsureSnapshot();

        if (!metaSystem.TryGetCachedSnapshot(out var snapshot))
            return false;

        var normalizedId = achievementId.Trim();

        foreach (var entry in snapshot.Achievements)
        {
            if (!string.Equals(entry.Id, normalizedId, StringComparison.Ordinal))
                continue;

            completed = entry.Completed;
            break;
        }

        return true;
    }
}
