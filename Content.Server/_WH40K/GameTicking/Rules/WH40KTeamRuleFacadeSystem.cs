using System.Collections.Generic;
using Content.Shared._WH40K.GameMode;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.GameTicking.Rules;

/// <summary>
/// Bridges WH40K team-aware systems to whichever mode currently owns shared team state.
/// TeamBattle remains the primary provider, while WaveDefence can expose a compatible subset.
/// </summary>
public sealed partial class WH40KTeamRuleFacadeSystem : EntitySystem
{
    [Dependency] private  WH40KTeamBattleRuleSystem _teamBattle = default!;
    [Dependency] private  WH40KWaveDefenceRuleSystem _waveDefence = default!;

    public bool AreObjectivesEnabled()
    {
        if (_teamBattle.AreObjectivesEnabled())
            return true;

        return _waveDefence.TryGetActiveRule(out _, out _);
    }

    public bool TryGetTeamIdFromEntity(EntityUid entity, out string teamId)
    {
        if (_teamBattle.TryGetTeamIdFromEntity(entity, out teamId))
            return true;

        return _waveDefence.TryGetEntityTeamId(entity, out teamId);
    }

    public bool TryGetTeamIdForUser(NetUserId userId, out string teamId)
    {
        if (_teamBattle.TryGetTeamIdForUser(userId, out teamId))
            return true;

        return _waveDefence.TryGetTeamIdForUser(userId, out teamId);
    }

    public bool TryGetRememberedTeam(NetUserId userId, out string teamId)
    {
        if (_teamBattle.TryGetRememberedTeam(userId, out teamId))
            return true;

        return _waveDefence.TryGetRememberedTeam(userId, out teamId);
    }

    public bool TryResolveTeamId(string teamId, out string resolvedTeamId)
    {
        if (_teamBattle.TryResolveTeamId(teamId, out resolvedTeamId))
            return true;

        return _waveDefence.TryResolveTeamId(teamId, out resolvedTeamId);
    }

    public bool TryGetTeamDisplayName(string teamId, out string teamName)
    {
        if (_teamBattle.TryGetTeamDisplayName(teamId, out teamName))
            return true;

        return _waveDefence.TryGetTeamDisplayName(teamId, out teamName);
    }

    public bool TryGetTeamColor(string teamId, out Color teamColor)
    {
        if (_teamBattle.TryGetTeamColor(teamId, out teamColor))
            return true;

        return _waveDefence.TryGetTeamColor(teamId, out teamColor);
    }

    public bool TryGetTeamDepartments(string teamId, out IReadOnlyList<ProtoId<DepartmentPrototype>> departments)
    {
        if (_teamBattle.TryGetTeamDepartments(teamId, out departments))
            return true;

        return _waveDefence.TryGetTeamDepartments(teamId, out departments);
    }

    public IReadOnlyList<string> GetTeamIds()
    {
        var ids = _teamBattle.GetTeamIds();
        if (ids.Count > 0)
            return ids;

        return _waveDefence.GetTeamIds();
    }

    public WH40KBattlePhase GetCurrentPhase()
    {
        if (_waveDefence.TryGetActiveRule(out _, out _))
            return _waveDefence.GetCurrentPhase();

        return _teamBattle.GetCurrentPhase();
    }

    public int GetCurrentEconomyMultiplier()
    {
        if (_waveDefence.TryGetActiveRule(out _, out _))
            return _waveDefence.GetCurrentEconomyMultiplier();

        return _teamBattle.GetCurrentEconomyMultiplier();
    }

    public int GetRoundElapsedSeconds()
    {
        if (_waveDefence.TryGetActiveRule(out _, out _))
            return _waveDefence.GetRoundElapsedSeconds();

        return _teamBattle.GetRoundElapsedSeconds();
    }

    public bool IsEarlyVictoryLocked()
    {
        if (_waveDefence.TryGetActiveRule(out _, out _))
            return false;

        return _teamBattle.IsEarlyVictoryLocked();
    }

    public bool TryGetRoundOutcome(out string? winnerTeamId, out bool draw, out bool timeLimitReached)
    {
        if (_teamBattle.TryGetRoundOutcome(out winnerTeamId, out draw, out timeLimitReached))
            return true;

        winnerTeamId = null;
        draw = false;
        timeLimitReached = false;
        return false;
    }

    public void HandleObjectiveDestroyed(string destroyedTeamId)
    {
        if (_waveDefence.TryGetActiveRule(out _, out _))
            return;

        _teamBattle.HandleObjectiveDestroyed(destroyedTeamId);
    }

    public bool TryGetTeamProgress(string teamId, out int level, out int frontPoints, out int? pointsToNextLevel)
    {
        if (_teamBattle.TryGetTeamProgress(teamId, out level, out frontPoints, out pointsToNextLevel))
            return true;

        return _waveDefence.TryGetTeamProgress(teamId, out level, out frontPoints, out pointsToNextLevel);
    }

    public bool TryGetTeamEconomySnapshot(EntityUid? sourceUid, string teamId, out WH40KTeamEconomySnapshot snapshot)
    {
        if (_teamBattle.TryGetTeamEconomySnapshot(sourceUid, teamId, out snapshot))
            return true;

        return _waveDefence.TryGetTeamEconomySnapshot(sourceUid, teamId, out snapshot);
    }

    public bool TryGetBaseLevelThresholds(out IReadOnlyList<int> thresholds)
    {
        if (_teamBattle.TryGetBaseLevelThresholds(out thresholds))
            return true;

        return _waveDefence.TryGetBaseLevelThresholds(out thresholds);
    }

    public bool TryAdjustTeamXp(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int teamXp,
        out int level,
        string? source = null,
        bool allowDecrease = false)
    {
        if (_teamBattle.TryAdjustTeamXp(teamId, delta, out resolvedTeamId, out teamXp, out level, source, allowDecrease))
            return true;

        return _waveDefence.TryAdjustTeamXp(teamId, delta, out resolvedTeamId, out teamXp, out level, source, allowDecrease);
    }

    public bool TryAdjustTeamFrontPoints(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int frontPoints,
        out int level,
        string? source = null)
    {
        if (_teamBattle.TryAdjustTeamFrontPoints(teamId, delta, out resolvedTeamId, out frontPoints, out level, source))
            return true;

        return _waveDefence.TryAdjustTeamFrontPoints(teamId, delta, out resolvedTeamId, out frontPoints, out level, source);
    }

    public bool TryGetTeamCommandPoints(string teamId, out int points)
    {
        if (_teamBattle.TryGetTeamCommandPoints(teamId, out points))
            return true;

        return _waveDefence.TryGetTeamCommandPoints(teamId, out points);
    }

    public bool TryAdjustTeamCommandPoints(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int commandPoints,
        string? source = null)
    {
        if (_teamBattle.TryAdjustTeamCommandPoints(teamId, delta, out resolvedTeamId, out commandPoints, source))
            return true;

        return _waveDefence.TryAdjustTeamCommandPoints(teamId, delta, out resolvedTeamId, out commandPoints, source);
    }

    public bool TryGetTeamInfluencePoints(string teamId, out int points)
    {
        if (_teamBattle.TryGetTeamInfluencePoints(teamId, out points))
            return true;

        return _waveDefence.TryGetTeamInfluencePoints(teamId, out points);
    }

    public bool TrySpendTeamInfluence(string teamId, int amount, out int remaining, string? source = null)
    {
        if (_teamBattle.TrySpendTeamInfluence(teamId, amount, out remaining, source))
            return true;

        return _waveDefence.TrySpendTeamInfluence(teamId, amount, out remaining, source);
    }

    public bool TryAdjustTeamInfluence(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int influence,
        string? source = null)
    {
        if (_teamBattle.TryAdjustTeamInfluence(teamId, delta, out resolvedTeamId, out influence, source))
            return true;

        return _waveDefence.TryAdjustTeamInfluence(teamId, delta, out resolvedTeamId, out influence, source);
    }

    public bool TryGetTeamResearchPoints(string teamId, out int points)
    {
        if (_teamBattle.TryGetTeamResearchPoints(teamId, out points))
            return true;

        return _waveDefence.TryGetTeamResearchPoints(teamId, out points);
    }

    public bool TrySpendTeamResearchPoints(string teamId, int amount, out int remaining, string? source = null)
    {
        if (_teamBattle.TrySpendTeamResearchPoints(teamId, amount, out remaining, source))
            return true;

        return _waveDefence.TrySpendTeamResearchPoints(teamId, amount, out remaining, source);
    }

    public bool TryAdjustTeamResearchPoints(
        string teamId,
        int delta,
        out string resolvedTeamId,
        out int researchPoints,
        string? source = null)
    {
        if (_teamBattle.TryAdjustTeamResearchPoints(teamId, delta, out resolvedTeamId, out researchPoints, source))
            return true;

        return _waveDefence.TryAdjustTeamResearchPoints(teamId, delta, out resolvedTeamId, out researchPoints, source);
    }

    public bool TryGetTeamAliveSnapshot(string teamId, out int aliveCount, out int totalCount)
    {
        if (_waveDefence.TryGetTeamAliveSnapshot(teamId, out aliveCount, out totalCount))
            return true;

        aliveCount = 0;
        totalCount = 0;
        return false;
    }
}
