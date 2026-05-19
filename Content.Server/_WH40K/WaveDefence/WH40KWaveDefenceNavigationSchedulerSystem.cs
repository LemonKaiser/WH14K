using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Server._WH40K.WaveDefence.Components;
using Content.Shared.NPC;
using Content.Shared._WH40K.WaveDefence;
using Robust.Shared.Map;

namespace Content.Server._WH40K.WaveDefence;

/// <summary>
/// Queues strategic route evaluations through the pathfinding system's own async pipeline
/// and returns advisory path results when they are ready.
/// Main-thread locomotion remains authoritative for validation, acceptance, and route commit.
/// </summary>
public sealed class WH40KWaveDefenceNavigationSchedulerSystem : EntitySystem
{
    private const float ReplaceOriginDistanceEpsilon = 0.35f;
    private const float ReplaceTargetDistanceEpsilon = 0.25f;
    private const float ReplaceRangeEpsilon = 0.05f;

    private sealed record NavigationSnapshot(
        EntityUid Uid,
        int RequestEpoch,
        CancellationToken CancellationToken,
        EntityCoordinates Origin,
        EntityCoordinates StrategicTarget,
        string StrategicLabel,
        WH40KWaveDefenceLocomotionMode Mode,
        PathFlags Flags,
        float Range,
        int TopologyVersion,
        HashSet<PathPolyKey> AvoidPolys);

    private sealed record PendingNavigationRequest(
        int Epoch,
        CancellationTokenSource Cancellation,
        EntityCoordinates Origin,
        EntityCoordinates StrategicTarget,
        string StrategicLabel,
        WH40KWaveDefenceLocomotionMode Mode,
        PathFlags Flags,
        float Range,
        int TopologyVersion,
        HashSet<PathPolyKey> AvoidPolys);

    public readonly record struct NavigationResult(
        EntityUid Uid,
        int RequestEpoch,
        EntityCoordinates Origin,
        EntityCoordinates StrategicTarget,
        string StrategicLabel,
        WH40KWaveDefenceLocomotionMode Mode,
        PathFlags Flags,
        float Range,
        int TopologyVersion,
        PathResult PathResult,
        List<PathPoly> Path,
        string Label);

    [Dependency] private readonly PathfindingSystem _pathfinding = default!;

    private readonly Dictionary<EntityUid, PendingNavigationRequest> _pendingRequests = new();
    private readonly Dictionary<EntityUid, NavigationResult> _completedResults = new();
    private readonly object _sync = new();

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Shutdown()
    {
        lock (_sync)
        {
            foreach (var pending in _pendingRequests.Values)
            {
                pending.Cancellation.Cancel();
                pending.Cancellation.Dispose();
            }

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
        EntityCoordinates strategicTarget,
        string strategicLabel,
        WH40KWaveDefenceLocomotionMode mode,
        PathFlags flags,
        float range,
        int topologyVersion,
        IReadOnlyCollection<PathPolyKey>? avoidPolys = null)
    {
        lock (_sync)
        {
            if (!_pendingRequests.TryGetValue(uid, out var pending))
                return true;

            return !IsEquivalentPending(pending, origin, strategicTarget, strategicLabel, mode, flags, range, topologyVersion, avoidPolys);
        }
    }

    public void RequestEvaluation(
        EntityUid uid,
        int requestEpoch,
        EntityCoordinates origin,
        EntityCoordinates strategicTarget,
        string strategicLabel,
        WH40KWaveDefenceLocomotionMode mode,
        PathFlags flags,
        float range,
        int topologyVersion,
        IReadOnlyCollection<PathPolyKey>? avoidPolys = null)
    {
        var avoid = avoidPolys != null
            ? new HashSet<PathPolyKey>(avoidPolys)
            : new HashSet<PathPolyKey>();
        var cancellation = new CancellationTokenSource();
        var snapshot = new NavigationSnapshot(
            uid,
            requestEpoch,
            cancellation.Token,
            origin,
            strategicTarget,
            strategicLabel,
            mode,
            flags,
            range,
            topologyVersion,
            avoid);

        lock (_sync)
        {
            if (_pendingRequests.Remove(uid, out var previous))
            {
                previous.Cancellation.Cancel();
                previous.Cancellation.Dispose();
            }

            _pendingRequests[uid] = new PendingNavigationRequest(
                requestEpoch,
                cancellation,
                origin,
                strategicTarget,
                strategicLabel,
                mode,
                flags,
                range,
                topologyVersion,
                avoid);
            _completedResults.Remove(uid);
        }

        _ = RunEvaluationAsync(snapshot);
    }

    public bool TryConsumeResult(EntityUid uid, out NavigationResult result)
    {
        lock (_sync)
        {
            if (!_completedResults.Remove(uid, out result))
                return false;

            if (_pendingRequests.Remove(uid, out var pending))
            {
                if (pending.Epoch == result.RequestEpoch)
                    pending.Cancellation.Dispose();
                else
                    _pendingRequests[uid] = pending;
            }
        }

        return true;
    }

    public void CancelEvaluation(EntityUid uid)
    {
        lock (_sync)
        {
            if (_pendingRequests.Remove(uid, out var pending))
            {
                pending.Cancellation.Cancel();
                pending.Cancellation.Dispose();
            }

            _completedResults.Remove(uid);
        }
    }

    private async Task RunEvaluationAsync(NavigationSnapshot snapshot)
    {
        var completed = false;

        try
        {
            lock (_sync)
            {
                if (!_pendingRequests.TryGetValue(snapshot.Uid, out var pending) ||
                    pending.Epoch != snapshot.RequestEpoch)
                {
                    return;
                }
            }

            var pathResult = await _pathfinding.GetPathSafe(
                snapshot.Uid,
                snapshot.Origin,
                snapshot.StrategicTarget,
                snapshot.Range,
                snapshot.CancellationToken,
                snapshot.Flags,
                snapshot.AvoidPolys);

            var path = pathResult.Path.Count > 0
                ? new List<PathPoly>(pathResult.Path)
                : new List<PathPoly>();

            var result = new NavigationResult(
                snapshot.Uid,
                snapshot.RequestEpoch,
                snapshot.Origin,
                snapshot.StrategicTarget,
                snapshot.StrategicLabel,
                snapshot.Mode,
                snapshot.Flags,
                snapshot.Range,
                snapshot.TopologyVersion,
                pathResult.Result,
                path,
                $"navigation:{pathResult.Result}");

            lock (_sync)
            {
                if (!_pendingRequests.TryGetValue(snapshot.Uid, out var pending) ||
                    pending.Epoch != snapshot.RequestEpoch ||
                    pending.Cancellation.IsCancellationRequested)
                {
                    return;
                }

                _completedResults[snapshot.Uid] = result;
                completed = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"WaveDefence navigation evaluation failed for uid={snapshot.Uid}: {ex}");
        }
        finally
        {
            if (!completed)
            {
                lock (_sync)
                {
                    if (_pendingRequests.TryGetValue(snapshot.Uid, out var pending) &&
                        pending.Epoch == snapshot.RequestEpoch)
                    {
                        _pendingRequests.Remove(snapshot.Uid);
                        pending.Cancellation.Dispose();
                    }

                    _completedResults.Remove(snapshot.Uid);
                }
            }
        }
    }

    private static bool IsEquivalentPending(
        PendingNavigationRequest pending,
        EntityCoordinates origin,
        EntityCoordinates strategicTarget,
        string strategicLabel,
        WH40KWaveDefenceLocomotionMode mode,
        PathFlags flags,
        float range,
        int topologyVersion,
        IReadOnlyCollection<PathPolyKey>? avoidPolys)
    {
        var requestedAvoid = avoidPolys != null
            ? new HashSet<PathPolyKey>(avoidPolys)
            : new HashSet<PathPolyKey>();

        if (pending.Mode != mode ||
            pending.Flags != flags ||
            pending.TopologyVersion != topologyVersion ||
            !string.Equals(pending.StrategicLabel, strategicLabel, StringComparison.Ordinal) ||
            MathF.Abs(pending.Range - range) > ReplaceRangeEpsilon)
        {
            return false;
        }

        if (!SameCoordinates(pending.Origin, origin, ReplaceOriginDistanceEpsilon) ||
            !SameCoordinates(pending.StrategicTarget, strategicTarget, ReplaceTargetDistanceEpsilon))
        {
            return false;
        }

        return pending.AvoidPolys.SetEquals(requestedAvoid);
    }

    private static bool SameCoordinates(EntityCoordinates a, EntityCoordinates b, float epsilon)
    {
        if (a.EntityId != b.EntityId)
            return false;

        return (a.Position - b.Position).LengthSquared() <= epsilon * epsilon;
    }
}
