using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Preferences.Managers;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Database;

/// <summary>
/// Manages per-user data that comes from the database. Ensures it is loaded efficiently on client connect,
/// and ensures data is loaded before allowing players to spawn or such.
/// </summary>
/// <remarks>
/// Actual loading code is handled by separate managers such as <see cref="IServerPreferencesManager"/>.
/// This manager is simply a centralized "is loading done" controller for other code to rely on.
/// </remarks>
public sealed partial class UserDbDataManager : IPostInjectInit
{
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private ITaskManager _task = default!;

    private readonly Dictionary<NetUserId, UserData> _users = new();
    private readonly Dictionary<NetUserId, UserDbLoadProgress> _loadProgress = new();
    private readonly List<OnPreparePlayer> _onPreparePlayer = [];
    private readonly List<OnLoadPlayer> _onLoadPlayer = [];
    private readonly List<OnFinishLoad> _onFinishLoad = [];
    private readonly List<OnPlayerDisconnect> _onPlayerDisconnect = [];
    private readonly List<OnLoadProgressChanged> _onLoadProgressChanged = [];
    private readonly object _lock = new();

    private ISawmill _sawmill = default!;

    // TODO: Ideally connected/disconnected would be subscribed to IPlayerManager directly,
    // but this runs into ordering issues with game ticker.
    public void ClientConnected(ICommonSession session)
    {
        _sawmill.Verbose($"Initiating load for user {session}");

        lock (_lock)
        {
            DebugTools.Assert(!_users.ContainsKey(session.UserId), "We should not have any cached data on client connect.");
        }

        var cts = new CancellationTokenSource();
        var task = Load(session, cts.Token);
        var data = new UserData(cts, task);

        lock (_lock)
        {
            _users.Add(session.UserId, data);
        }

        SetLoadProgress(
            session.UserId,
            CreateProgressSnapshot(
                UserDbLoadPhase.Starting,
                currentAction: null,
                completedSteps: 0,
                totalSteps: GetTotalTrackedSteps(),
                phaseProgress: 0f,
                phaseTotal: 1));
    }

    public void ClientDisconnected(ICommonSession session)
    {
        UserData? data;
        lock (_lock)
        {
            _users.Remove(session.UserId, out data);
            _loadProgress.Remove(session.UserId);
        }

        if (data == null)
            throw new InvalidOperationException("Did not have cached data in ClientDisconnect!");

        data.Cancel.Cancel();
        data.Cancel.Dispose();

        foreach (var onDisconnect in _onPlayerDisconnect)
        {
            onDisconnect(session);
        }
    }

    private async Task Load(ICommonSession session, CancellationToken cancel)
    {
        // The task returned by this function is only ever observed by callers of WaitLoadComplete,
        // which doesn't even happen currently if the lobby is enabled.
        // As such, this task must NOT throw a non-cancellation error!
        try
        {
            var prepareTotal = _onPreparePlayer.Count;
            var loadTotal = _onLoadPlayer.Count;
            var finishTotal = _onFinishLoad.Count;
            var totalTrackedSteps = Math.Max(prepareTotal + loadTotal + finishTotal, 1);
            var completedTrackedSteps = 0;

            for (var i = 0; i < prepareTotal; i++)
            {
                var action = _onPreparePlayer[i];
                SetLoadProgress(
                    session.UserId,
                    CreateProgressSnapshot(
                        UserDbLoadPhase.Preparing,
                        action,
                        completedTrackedSteps,
                        totalTrackedSteps,
                        i + 0.5f,
                        prepareTotal));

                await action(session, cancel);

                completedTrackedSteps++;
                SetLoadProgress(
                    session.UserId,
                    CreateProgressSnapshot(
                        UserDbLoadPhase.Preparing,
                        action,
                        completedTrackedSteps,
                        totalTrackedSteps,
                        i + 1f,
                        prepareTotal));
            }

            cancel.ThrowIfCancellationRequested();

            var tasks = new List<Task>();
            if (loadTotal > 0)
            {
                SetLoadProgress(
                    session.UserId,
                    CreateProgressSnapshot(
                        UserDbLoadPhase.Loading,
                        currentAction: null,
                        completedTrackedSteps,
                        totalTrackedSteps,
                        phaseProgress: 0f,
                        phaseTotal: loadTotal));

                var completedLoadSteps = 0;
                object loadProgressLock = new();

                foreach (var action in _onLoadPlayer)
                {
                    tasks.Add(RunLoadAction(action));
                }

                async Task RunLoadAction(OnLoadPlayer action)
                {
                    await action(session, cancel);

                    int completedPhaseSteps;
                    int completedOverallSteps;
                    lock (loadProgressLock)
                    {
                        completedLoadSteps++;
                        completedPhaseSteps = completedLoadSteps;
                        completedOverallSteps = completedTrackedSteps + completedLoadSteps;
                    }

                    SetLoadProgress(
                        session.UserId,
                        CreateProgressSnapshot(
                            UserDbLoadPhase.Loading,
                            currentAction: null,
                            completedOverallSteps,
                            totalTrackedSteps,
                            completedPhaseSteps,
                            loadTotal));
                }
            }

            await Task.WhenAll(tasks);
            completedTrackedSteps += loadTotal;

            cancel.ThrowIfCancellationRequested();

            for (var i = 0; i < finishTotal; i++)
            {
                var action = _onFinishLoad[i];
                SetLoadProgress(
                    session.UserId,
                    CreateProgressSnapshot(
                        UserDbLoadPhase.Finalizing,
                        action,
                        completedTrackedSteps,
                        totalTrackedSteps,
                        i + 0.5f,
                        finishTotal));

                action(session);

                completedTrackedSteps++;
                SetLoadProgress(
                    session.UserId,
                    CreateProgressSnapshot(
                        UserDbLoadPhase.Finalizing,
                        action,
                        completedTrackedSteps,
                        totalTrackedSteps,
                        i + 1f,
                        finishTotal));
            }

            SetLoadProgress(
                session.UserId,
                CreateProgressSnapshot(
                    UserDbLoadPhase.Complete,
                    currentAction: null,
                    completedTrackedSteps,
                    totalTrackedSteps,
                    phaseProgress: 1f,
                    phaseTotal: 1));

            _sawmill.Verbose($"Load complete for user {session}");
        }
        catch (OperationCanceledException)
        {
            _sawmill.Debug($"Load cancelled for user {session}");

            // We can rethrow the cancellation.
            // This will make the task returned by WaitLoadComplete() also return a cancellation.
            throw;
        }
        catch (Exception e)
        {
            // Must catch all exceptions here, otherwise task may go unobserved.
            _sawmill.Error($"Load of user data failed: {e}");

            // Kick them from server, since something is hosed. Let them try again I guess.
            session.Channel.Disconnect("Loading of server user data failed, this is a bug.");

            // We throw a OperationCanceledException so users of WaitLoadComplete() always see cancellation here.
            throw new OperationCanceledException("Load of user data cancelled due to unknown error");
        }
    }

    /// <summary>
    /// Wait for all on-database data for a user to be loaded.
    /// </summary>
    /// <remarks>
    /// The task returned by this function may end up in a cancelled state
    /// (throwing <see cref="OperationCanceledException"/>) if the user disconnects while loading or an error occurs.
    /// </remarks>
    /// <param name="session"></param>
    /// <returns>
    /// A task that completes when all on-database data for a user has finished loading.
    /// </returns>
    public Task WaitLoadComplete(ICommonSession session)
    {
        return GetLoadTask(session);
    }

    public bool TryGetLoadTask(ICommonSession session, out Task? task)
    {
        lock (_lock)
        {
            if (_users.TryGetValue(session.UserId, out var data))
            {
                task = data.Task;
                return true;
            }
        }

        task = null;
        return false;
    }

    public bool IsLoadComplete(ICommonSession session)
    {
        return GetLoadTask(session).IsCompletedSuccessfully;
    }

    public bool TryIsLoadComplete(ICommonSession session)
    {
        return TryGetLoadTask(session, out var task) && task is { IsCompletedSuccessfully: true };
    }

    public Task GetLoadTask(ICommonSession session)
    {
        lock (_lock)
        {
            return _users[session.UserId].Task;
        }
    }

    public void AddOnLoadPlayer(OnLoadPlayer action)
    {
        _onLoadPlayer.Add(action);
    }

    public void AddOnPreparePlayer(OnPreparePlayer action)
    {
        _onPreparePlayer.Add(action);
    }

    public void AddOnFinishLoad(OnFinishLoad action)
    {
        _onFinishLoad.Add(action);
    }

    public void AddOnPlayerDisconnect(OnPlayerDisconnect action)
    {
        _onPlayerDisconnect.Add(action);
    }

    public void AddOnLoadProgressChanged(OnLoadProgressChanged action)
    {
        _onLoadProgressChanged.Add(action);
    }

    public bool TryGetLoadProgress(ICommonSession session, out UserDbLoadProgress progress)
    {
        lock (_lock)
        {
            return _loadProgress.TryGetValue(session.UserId, out progress);
        }
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill("userdb");
    }

    private int GetTotalTrackedSteps()
    {
        return Math.Max(_onPreparePlayer.Count + _onLoadPlayer.Count + _onFinishLoad.Count, 1);
    }

    private void SetLoadProgress(NetUserId userId, UserDbLoadProgress progress)
    {
        lock (_lock)
        {
            _loadProgress[userId] = progress;
        }

        NotifyProgressChanged(userId, progress);
    }

    private void NotifyProgressChanged(NetUserId userId, UserDbLoadProgress progress)
    {
        if (_onLoadProgressChanged.Count == 0)
            return;

        _task.RunOnMainThread(() =>
        {
            foreach (var action in _onLoadProgressChanged)
            {
                action(userId, progress);
            }
        });
    }

    private static UserDbLoadProgress CreateProgressSnapshot(
        UserDbLoadPhase phase,
        Delegate? currentAction,
        int completedSteps,
        int totalSteps,
        float phaseProgress,
        int phaseTotal)
    {
        var (stageLocKey, detailLocKey) = ResolveProgressLocalization(phase, currentAction);
        return new UserDbLoadProgress(
            Phase: phase,
            TitleLocKey: "wh40k-account-load-title",
            StageLocKey: stageLocKey,
            DetailLocKey: detailLocKey,
            Progress: CalculateProgress(phase, phaseProgress, phaseTotal),
            CompletedSteps: Math.Clamp(completedSteps, 0, totalSteps),
            TotalSteps: totalSteps);
    }

    private static (string StageLocKey, string? DetailLocKey) ResolveProgressLocalization(UserDbLoadPhase phase, Delegate? currentAction)
    {
        return phase switch
        {
            UserDbLoadPhase.Starting => (
                "wh40k-account-load-stage-starting",
                "wh40k-account-load-detail-starting"),
            UserDbLoadPhase.Preparing when currentAction?.Method.DeclaringType?.Name == "WH40KAuthAccountMigrationSystem" => (
                "wh40k-account-load-stage-migration",
                "wh40k-account-load-detail-migration"),
            UserDbLoadPhase.Preparing => (
                "wh40k-account-load-stage-preparing",
                "wh40k-account-load-detail-preparing"),
            UserDbLoadPhase.Loading => (
                "wh40k-account-load-stage-loading-data",
                "wh40k-account-load-detail-loading-data"),
            UserDbLoadPhase.Finalizing => (
                "wh40k-account-load-stage-finalizing",
                "wh40k-account-load-detail-finalizing"),
            UserDbLoadPhase.Complete => (
                "wh40k-account-load-stage-entering",
                "wh40k-account-load-detail-entering"),
            _ => (
                "wh40k-account-load-stage-loading-data",
                "wh40k-account-load-detail-loading-data"),
        };
    }

    private static float CalculateProgress(UserDbLoadPhase phase, float phaseProgress, int phaseTotal)
    {
        var clampedPhaseProgress = phaseTotal <= 0
            ? 1f
            : Math.Clamp(phaseProgress / phaseTotal, 0f, 1f);

        var (start, end) = phase switch
        {
            UserDbLoadPhase.Starting => (0.02f, 0.08f),
            UserDbLoadPhase.Preparing => (0.08f, 0.32f),
            UserDbLoadPhase.Loading => (0.32f, 0.84f),
            UserDbLoadPhase.Finalizing => (0.84f, 0.97f),
            UserDbLoadPhase.Complete => (1f, 1f),
            _ => (0f, 1f),
        };

        if (start == end)
            return end;

        return start + (end - start) * clampedPhaseProgress;
    }

    private sealed record UserData(CancellationTokenSource Cancel, Task Task);

    public delegate Task OnPreparePlayer(ICommonSession player, CancellationToken cancel);

    public delegate Task OnLoadPlayer(ICommonSession player, CancellationToken cancel);

    public delegate void OnFinishLoad(ICommonSession player);

    public delegate void OnPlayerDisconnect(ICommonSession player);

    public delegate void OnLoadProgressChanged(NetUserId userId, UserDbLoadProgress progress);
}

public enum UserDbLoadPhase : byte
{
    Starting,
    Preparing,
    Loading,
    Finalizing,
    Complete,
}

public readonly record struct UserDbLoadProgress(
    UserDbLoadPhase Phase,
    string TitleLocKey,
    string StageLocKey,
    string? DetailLocKey,
    float Progress,
    int CompletedSteps,
    int TotalSteps);
