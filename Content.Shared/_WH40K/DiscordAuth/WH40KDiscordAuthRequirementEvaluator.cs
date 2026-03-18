using System;
using System.Collections.Generic;
using System.Linq;

namespace Content.Shared._WH40K.DiscordAuth;

public static class WH40KDiscordAuthRequirementEvaluator
{
    public static List<string> NormalizeRoleIds(IEnumerable<string>? roleIds)
    {
        var normalized = new List<string>();

        if (roleIds == null)
            return normalized;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleId in roleIds)
        {
            if (string.IsNullOrWhiteSpace(roleId))
                continue;

            var trimmed = roleId.Trim();
            if (seen.Add(trimmed))
                normalized.Add(trimmed);
        }

        return normalized;
    }

    public static bool MeetsRequirements(
        WH40KDiscordAuthSnapshot? snapshot,
        bool requireGuildMember,
        IReadOnlyCollection<string> requiredRoleIds)
    {
        if (!requireGuildMember && requiredRoleIds.Count == 0)
            return true;

        if (snapshot == null || !snapshot.Enabled || !snapshot.IsLinked)
            return false;

        if (snapshot.CacheStale)
            return false;

        if (requireGuildMember && !snapshot.IsGuildMember)
            return false;

        if (requiredRoleIds.Count == 0)
            return true;

        if (!snapshot.IsGuildMember)
            return false;

        foreach (var roleId in snapshot.CachedRoleIds)
        {
            if (!string.IsNullOrWhiteSpace(roleId) && requiredRoleIds.Contains(roleId))
                return true;
        }

        return false;
    }
}
