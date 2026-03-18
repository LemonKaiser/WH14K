using System;
using System.Linq;
using Content.Server.Pinpointer;
using Content.Server._WH40K.GameTicking.Rules;
using Content.Shared.Pinpointer;
using Content.Shared._WH40K.Command.Pinpointer;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Command.Pinpointer;

public sealed class WH40KMissionPinpointerSystem : EntitySystem
{
    [Dependency] private readonly PinpointerSystem _pinpointer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly WH40KTeamBattleRuleSystem _teamRule = default!;
    [Dependency] private readonly WH40KCommandEventMissionRuntimeSystem _runtime = default!;

    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WH40KMissionPinpointerComponent, PinpointerComponent>();
        while (query.MoveNext(out var uid, out var missionPinpointer, out var pinpointer))
        {
            if (missionPinpointer.NextRefreshAt > now)
                continue;

            missionPinpointer.NextRefreshAt = now + missionPinpointer.RefreshInterval;
            RefreshPinpointer(uid, missionPinpointer, pinpointer);
        }
    }

    public bool TryForceRefreshForTeam(string teamId, out int refreshedCount)
    {
        refreshedCount = 0;
        if (string.IsNullOrWhiteSpace(teamId))
            return false;

        var query = EntityQueryEnumerator<WH40KMissionPinpointerComponent, PinpointerComponent>();
        while (query.MoveNext(out var uid, out var missionPinpointer, out var pinpointer))
        {
            if (!TryResolveTrackingTeam(uid, missionPinpointer, out var resolvedTeamId))
                continue;

            if (!string.Equals(resolvedTeamId, teamId, StringComparison.OrdinalIgnoreCase))
                continue;

            missionPinpointer.NextRefreshAt = _timing.CurTime + missionPinpointer.RefreshInterval;
            RefreshPinpointer(uid, missionPinpointer, pinpointer, resolvedTeamId);
            refreshedCount++;
        }

        return refreshedCount > 0;
    }

    private void RefreshPinpointer(
        EntityUid uid,
        WH40KMissionPinpointerComponent missionPinpointer,
        PinpointerComponent pinpointer,
        string? cachedTeamId = null)
    {
        if (TerminatingOrDeleted(uid))
            return;

        if (!TryResolveTrackingTeam(uid, missionPinpointer, out var teamId))
        {
            var fallback = HasHolderFromAnyTeam(uid)
                ? missionPinpointer.UnauthorizedTargetName
                : missionPinpointer.NoTeamTargetName;
            SetTarget(uid, pinpointer, null, fallback);
            return;
        }

        teamId = cachedTeamId ?? teamId;

        if (!_runtime.TryGetMissionPinpointerTarget(
                teamId,
                missionPinpointer.Preset,
                missionPinpointer.TrackGlobalMissionFallback,
                out var targetState))
        {
            SetTarget(uid, pinpointer, null, missionPinpointer.NoMissionTargetName);
            return;
        }

        var targetName = string.IsNullOrWhiteSpace(targetState.TargetName)
            ? missionPinpointer.NoMissionTargetName
            : targetState.TargetName;
        SetTarget(uid, pinpointer, targetState.TargetUid, targetName);
    }

    private void SetTarget(
        EntityUid uid,
        PinpointerComponent pinpointer,
        EntityUid? targetUid,
        string targetName)
    {
        _pinpointer.SetTarget((uid, pinpointer), targetUid);
        _pinpointer.SetTargetName(uid, targetName, pinpointer);
    }

    private bool TryResolveTrackingTeam(
        EntityUid pinpointerUid,
        WH40KMissionPinpointerComponent missionPinpointer,
        out string teamId)
    {
        teamId = string.Empty;
        var hasHolderTeam = TryResolveHolderTeam(pinpointerUid, out var holderTeamId);

        if (hasHolderTeam)
            teamId = holderTeamId;

        if (hasHolderTeam && missionPinpointer.AllowedTeamIds.Count > 0)
        {
            if (!IsTeamAllowed(holderTeamId, missionPinpointer.AllowedTeamIds))
                return false;

            teamId = holderTeamId;
            return true;
        }

        if (hasHolderTeam)
            return true;

        if (missionPinpointer.AllowedTeamIds.Count == 1)
        {
            teamId = missionPinpointer.AllowedTeamIds[0];
            return !string.IsNullOrWhiteSpace(teamId);
        }

        if (missionPinpointer.RequireTeam)
            return false;

        return !string.IsNullOrWhiteSpace(teamId);
    }

    private bool HasHolderFromAnyTeam(EntityUid pinpointerUid)
    {
        return TryResolveHolderTeam(pinpointerUid, out _);
    }

    private bool TryResolveHolderTeam(EntityUid pinpointerUid, out string teamId)
    {
        teamId = string.Empty;

        if (!_xformQuery.TryGetComponent(pinpointerUid, out var pinpointerXform))
            return false;

        var current = pinpointerXform.ParentUid;
        var depth = 0;
        while (current != EntityUid.Invalid && depth < 12)
        {
            if (_teamRule.TryGetTeamIdFromEntity(current, out teamId))
                return true;

            if (!_xformQuery.TryGetComponent(current, out var parentXform))
                return false;

            if (parentXform.ParentUid == EntityUid.Invalid || parentXform.ParentUid == current)
                return false;

            current = parentXform.ParentUid;
            depth++;
        }

        return false;
    }

    private static bool IsTeamAllowed(string teamId, System.Collections.Generic.IReadOnlyCollection<string> allowedTeamIds)
    {
        return allowedTeamIds.Any(allowed =>
            string.Equals(allowed, teamId, StringComparison.OrdinalIgnoreCase));
    }
}
