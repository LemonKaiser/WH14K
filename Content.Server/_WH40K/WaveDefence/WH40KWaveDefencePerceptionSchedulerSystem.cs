using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WH40K.WaveDefence;

/// <summary>
/// Captures direct-contact perception snapshots on the main thread and scores them on a background worker.
/// Final contact application remains authoritative in <see cref="WH40KWaveDefenceAISystem"/>.
/// </summary>
public sealed class WH40KWaveDefencePerceptionSchedulerSystem : EntitySystem
{
    private const float ReplaceOriginDistanceEpsilon = 0.2f;
    private const float ReplaceRadiusEpsilon = 0.05f;
    private const float TargetSwitchHysteresis = 2.25f;
    private const float FocusContinuityBonus = 4.5f;
    private const float RememberedContinuityBonus = 2.25f;
    private const float ObjectiveThreatRange = 6f;
    private const float ObjectiveThreatMaxBonus = 5f;
    private const float ObjectiveThreatReasonThreshold = 1f;

    private sealed record PerceptionSnapshot(
        EntityUid Uid,
        int RequestEpoch,
        bool ObjectivePressure,
        EntityUid PreferredTarget,
        EntityUid RememberedTarget,
        List<PerceptionCandidate> Candidates);

    private sealed record PendingPerceptionRequest(
        int Epoch,
        EntityCoordinates Origin,
        float Radius,
        bool AggroFocus,
        bool ObjectivePressure,
        EntityUid PreferredTarget,
        EntityUid RememberedTarget);

    private readonly record struct PerceptionCandidate(
        EntityUid Target,
        EntityCoordinates Coordinates,
        float DistanceToOrigin,
        float DistanceToObjective,
        bool MatchesPreferredTarget,
        bool MatchesRememberedTarget);

    public readonly record struct PerceptionResult(
        EntityUid Uid,
        int RequestEpoch,
        bool HasDirectContact,
        EntityUid Target,
        EntityCoordinates Coordinates,
        string Label);

    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;

    private readonly Queue<PerceptionSnapshot> _pendingSnapshots = new();
    private readonly Dictionary<EntityUid, PendingPerceptionRequest> _pendingRequests = new();
    private readonly Dictionary<EntityUid, PerceptionResult> _completedResults = new();
    private readonly object _sync = new();

    private CancellationTokenSource _shutdown = default!;
    private SemaphoreSlim _signal = default!;
    private Task _worker = Task.CompletedTask;

    public override void Initialize()
    {
        base.Initialize();
        _shutdown = new CancellationTokenSource();
        _signal = new SemaphoreSlim(0);
        _worker = Task.Run(() => RunWorkerAsync(_shutdown.Token));
    }

    public override void Shutdown()
    {
        _shutdown.Cancel();
        _signal.Release();

        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _signal.Dispose();
        _shutdown.Dispose();

        lock (_sync)
        {
            _pendingSnapshots.Clear();
            _pendingRequests.Clear();
            _completedResults.Clear();
        }

        base.Shutdown();
    }

    public bool HasPendingEvaluation(EntityUid uid)
    {
        lock (_sync)
        {
            return _pendingRequests.ContainsKey(uid);
        }
    }

    public bool ShouldRequestEvaluation(
        EntityUid uid,
        EntityCoordinates origin,
        float radius,
        bool aggroFocus,
        bool objectivePressure,
        EntityUid preferredTarget,
        EntityUid rememberedTarget)
    {
        lock (_sync)
        {
            if (!_pendingRequests.TryGetValue(uid, out var pending))
                return true;

            return !IsEquivalentPending(pending, origin, radius, aggroFocus, objectivePressure, preferredTarget, rememberedTarget);
        }
    }

    public void RequestEvaluation(
        EntityUid uid,
        int requestEpoch,
        TransformComponent xform,
        float visionRadius,
        float aggroVisionRadius,
        bool aggroFocus,
        bool objectivePressure,
        EntityCoordinates objectiveCoordinates,
        EntityUid preferredTarget,
        EntityUid rememberedTarget)
    {
        if (xform.MapID == MapId.Nullspace)
            return;

        var radius = Math.Max(0f, aggroFocus ? aggroVisionRadius : visionRadius);
        if (radius <= 0f)
            return;

        var candidates = new List<PerceptionCandidate>();
        foreach (var target in _npcFaction.GetNearbyHostiles(uid, radius))
        {
            if (!TryComp<ActorComponent>(target, out _) ||
                !TryComp(target, out TransformComponent? targetXform) ||
                targetXform.MapID != xform.MapID)
            {
                continue;
            }

            if (!_mobState.IsAlive(target))
                continue;

            if (!_examine.InRangeUnOccluded(uid, target, radius + 0.5f, null))
                continue;

            if (!xform.Coordinates.TryDistance(EntityManager, targetXform.Coordinates, out var distance))
                continue;

            var distanceToObjective = -1f;
            if (objectivePressure &&
                objectiveCoordinates.IsValid(EntityManager) &&
                targetXform.Coordinates.TryDistance(EntityManager, objectiveCoordinates, out var objectiveDistance))
            {
                distanceToObjective = objectiveDistance;
            }

            candidates.Add(new PerceptionCandidate(
                target,
                targetXform.Coordinates,
                distance,
                distanceToObjective,
                preferredTarget.IsValid() && target == preferredTarget,
                rememberedTarget.IsValid() && target == rememberedTarget));
        }

        lock (_sync)
        {
            _pendingRequests[uid] = new PendingPerceptionRequest(
                requestEpoch,
                xform.Coordinates,
                radius,
                aggroFocus,
                objectivePressure,
                preferredTarget,
                rememberedTarget);
            _completedResults.Remove(uid);
            _pendingSnapshots.Enqueue(new PerceptionSnapshot(
                uid,
                requestEpoch,
                objectivePressure,
                preferredTarget,
                rememberedTarget,
                candidates));
        }

        _signal.Release();
    }

    public bool TryConsumeResult(EntityUid uid, out PerceptionResult result)
    {
        lock (_sync)
        {
            if (!_completedResults.Remove(uid, out result))
                return false;

            if (_pendingRequests.TryGetValue(uid, out var pending) &&
                pending.Epoch == result.RequestEpoch)
            {
                _pendingRequests.Remove(uid);
            }
        }

        return true;
    }

    public void CancelEvaluation(EntityUid uid)
    {
        lock (_sync)
        {
            _pendingRequests.Remove(uid);
            _completedResults.Remove(uid);
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(cancellationToken);

            PerceptionSnapshot snapshot;
            lock (_sync)
            {
                if (_pendingSnapshots.Count == 0)
                    continue;

                snapshot = _pendingSnapshots.Dequeue();
            }

            var result = Evaluate(snapshot);

            lock (_sync)
            {
                if (!_pendingRequests.TryGetValue(snapshot.Uid, out var activeRequest) ||
                    activeRequest.Epoch != snapshot.RequestEpoch)
                {
                    continue;
                }

                _completedResults[snapshot.Uid] = result;
            }
        }
    }

    private static PerceptionResult Evaluate(PerceptionSnapshot snapshot)
    {
        if (snapshot.Candidates.Count == 0)
        {
            return new PerceptionResult(
                snapshot.Uid,
                snapshot.RequestEpoch,
                false,
                EntityUid.Invalid,
                EntityCoordinates.Invalid,
                "perception:none");
        }

        var best = snapshot.Candidates[0];
        var bestScore = ScoreCandidate(snapshot, best);
        for (var i = 1; i < snapshot.Candidates.Count; i++)
        {
            var candidate = snapshot.Candidates[i];
            var candidateScore = ScoreCandidate(snapshot, candidate);
            if (candidateScore <= bestScore)
                continue;

            best = candidate;
            bestScore = candidateScore;
        }

        if (snapshot.PreferredTarget.IsValid())
        {
            for (var i = 0; i < snapshot.Candidates.Count; i++)
            {
                var candidate = snapshot.Candidates[i];
                if (!candidate.MatchesPreferredTarget)
                    continue;

                var preferredScore = ScoreCandidate(snapshot, candidate);
                if (preferredScore + TargetSwitchHysteresis >= bestScore)
                {
                    best = candidate;
                    bestScore = preferredScore;
                }

                break;
            }
        }

        return new PerceptionResult(
            snapshot.Uid,
            snapshot.RequestEpoch,
            true,
            best.Target,
            best.Coordinates,
            DescribeSelectionLabel(snapshot, best));
    }

    private static bool IsEquivalentPending(
        PendingPerceptionRequest pending,
        EntityCoordinates origin,
        float radius,
        bool aggroFocus,
        bool objectivePressure,
        EntityUid preferredTarget,
        EntityUid rememberedTarget)
    {
        if (pending.AggroFocus != aggroFocus)
            return false;

        if (pending.ObjectivePressure != objectivePressure)
            return false;

        if (pending.PreferredTarget != preferredTarget ||
            pending.RememberedTarget != rememberedTarget)
        {
            return false;
        }

        if (MathF.Abs(pending.Radius - radius) > ReplaceRadiusEpsilon)
            return false;

        if (pending.Origin.EntityId != origin.EntityId)
            return false;

        return (pending.Origin.Position - origin.Position).LengthSquared() <=
               ReplaceOriginDistanceEpsilon * ReplaceOriginDistanceEpsilon;
    }

    private static float ScoreCandidate(PerceptionSnapshot snapshot, PerceptionCandidate candidate)
    {
        var score = 1000f - candidate.DistanceToOrigin;

        if (candidate.MatchesPreferredTarget)
            score += FocusContinuityBonus;
        else if (candidate.MatchesRememberedTarget)
            score += RememberedContinuityBonus;

        if (snapshot.ObjectivePressure &&
            candidate.DistanceToObjective >= 0f &&
            ObjectiveThreatRange > 0f)
        {
            var objectiveThreat = MathF.Max(0f, ObjectiveThreatRange - candidate.DistanceToObjective) / ObjectiveThreatRange;
            score += objectiveThreat * ObjectiveThreatMaxBonus;
        }

        return score;
    }

    private static string DescribeSelectionLabel(PerceptionSnapshot snapshot, PerceptionCandidate candidate)
    {
        if (candidate.MatchesPreferredTarget)
            return "perception:direct:focus";

        if (snapshot.ObjectivePressure &&
            candidate.DistanceToObjective >= 0f &&
            ObjectiveThreatRange > 0f)
        {
            var objectiveThreat = MathF.Max(0f, ObjectiveThreatRange - candidate.DistanceToObjective) / ObjectiveThreatRange;
            if (objectiveThreat * ObjectiveThreatMaxBonus >= ObjectiveThreatReasonThreshold)
                return "perception:direct:objective-threat";
        }

        if (candidate.MatchesRememberedTarget)
            return "perception:direct:memory-continuity";

        return "perception:direct:nearest";
    }
}
