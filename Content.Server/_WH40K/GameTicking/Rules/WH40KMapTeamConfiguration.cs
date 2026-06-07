using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._WH40K.GameTicking.Rules.Components;
using Content.Shared.Maps;

namespace Content.Server._WH40K.GameTicking.Rules;

internal static class WH40KMapTeamConfiguration
{
    public static bool HasCustomConfiguration(GameMapPrototype? map)
    {
        return map != null &&
               ((map.WH40KTeamBattleFactions?.Count ?? 0) > 0 ||
                (map.WH40KTeamOverrides?.Count ?? 0) > 0);
    }

    public static List<WH40KTeamDefinition> BuildConfiguredTeams(
        GameMapPrototype map,
        IReadOnlyList<WH40KTeamDefinition> sourceTeams)
    {
        var sourceById = sourceTeams
            .Where(team => !string.IsNullOrWhiteSpace(team.Id))
            .GroupBy(team => team.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var overridesById = map.WH40KTeamOverrides?
            .Where(entry => !string.IsNullOrWhiteSpace(entry.TeamId))
            .GroupBy(entry => entry.TeamId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, WH40KMapTeamOverride>(StringComparer.OrdinalIgnoreCase);

        var teamIds = EnumerateTeamIds(map, sourceTeams);
        var configuredTeams = new List<WH40KTeamDefinition>(teamIds.Count);

        foreach (var teamId in teamIds)
        {
            if (!sourceById.TryGetValue(teamId, out var sourceTeam))
                continue;

            var configuredTeam = CloneTeam(sourceTeam);
            if (overridesById.TryGetValue(teamId, out var teamOverride))
                ApplyOverride(configuredTeam, teamOverride);

            configuredTeams.Add(configuredTeam);
        }

        return configuredTeams;
    }

    private static List<string> EnumerateTeamIds(
        GameMapPrototype map,
        IReadOnlyList<WH40KTeamDefinition> sourceTeams)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedIds = new List<string>();

        if (map.WH40KTeamBattleFactions != null && map.WH40KTeamBattleFactions.Count > 0)
        {
            foreach (var teamId in map.WH40KTeamBattleFactions)
            {
                if (string.IsNullOrWhiteSpace(teamId) || !seen.Add(teamId))
                    continue;

                orderedIds.Add(teamId);
            }

            return orderedIds;
        }

        foreach (var team in sourceTeams)
        {
            if (string.IsNullOrWhiteSpace(team.Id) || !seen.Add(team.Id))
                continue;

            orderedIds.Add(team.Id);
        }

        return orderedIds;
    }

    private static WH40KTeamDefinition CloneTeam(WH40KTeamDefinition source)
    {
        return new WH40KTeamDefinition
        {
            Id = source.Id,
            Name = source.Name,
            Logo = source.Logo,
            Color = source.Color,
            Departments = source.Departments.ToList(),
            BalanceGroup = source.BalanceGroup,
            MaxPlayers = source.MaxPlayers,
            SameFactionStreakLimit = source.SameFactionStreakLimit,
            SelectionEnabled = source.SelectionEnabled,
            RequiredForPresence = source.RequiredForPresence,
            CargoAccount = source.CargoAccount,
            NpcFaction = source.NpcFaction,
            Recruitment = source.Recruitment == null
                ? null
                : new WH40KTeamRecruitmentDefinition
                {
                    Enabled = source.Recruitment.Enabled,
                    DoAfter = source.Recruitment.DoAfter,
                    RewardMultiplier = source.Recruitment.RewardMultiplier
                }
        };
    }

    private static void ApplyOverride(WH40KTeamDefinition team, WH40KMapTeamOverride teamOverride)
    {
        if (teamOverride.BalanceGroup != null)
            team.BalanceGroup = teamOverride.BalanceGroup;

        if (teamOverride.MaxPlayers.HasValue)
            team.MaxPlayers = teamOverride.MaxPlayers.Value;

        if (teamOverride.SameFactionStreakLimit.HasValue)
            team.SameFactionStreakLimit = teamOverride.SameFactionStreakLimit.Value;

        if (teamOverride.SelectionEnabled.HasValue)
            team.SelectionEnabled = teamOverride.SelectionEnabled.Value;

        if (teamOverride.RequiredForPresence.HasValue)
            team.RequiredForPresence = teamOverride.RequiredForPresence.Value;
    }
}
