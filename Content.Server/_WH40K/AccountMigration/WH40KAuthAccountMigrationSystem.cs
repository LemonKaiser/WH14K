using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server._WH40K.Notifications;
using Content.Shared.CCVar;
using Content.Shared._WH40K.Notifications;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WH40K.AccountMigration;

public sealed partial class WH40KAuthAccountMigrationSystem : EntitySystem
{
    private sealed class PendingMigrationState
    {
        public Task<WH40KAuthAccountMigrationResult> ExecutionTask = Task.FromResult(
            new WH40KAuthAccountMigrationResult(WH40KAuthAccountMigrationOutcome.None));
    }

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private UserDbDataManager _userDb = default!;
    [Dependency] private WH40KNotificationSystem _notifications = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;
    private readonly object _pendingMigrationLock = new();
    private readonly Dictionary<NetUserId, PendingMigrationState> _pendingMigrations = [];

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("wh40k.auth-migration");
        _userDb.AddOnPreparePlayer(PreparePlayer);
    }

    private async Task PreparePlayer(ICommonSession session, CancellationToken cancel)
    {
        if (!_cfg.GetCVar(CCVars.WH40KAuthMigrationEnabled))
            return;

        if (session.Channel.AuthType != LoginType.LoggedIn)
            return;

        var legacyUserId = await _db.GetAssignedUserIdAsync(session.Name);
        if (legacyUserId == null)
            return;

        _sawmill.Info(
            "Legacy assignment detected for userName={UserName}: legacyUserId={LegacyUserId}, authenticatedUserId={AuthenticatedUserId}.",
            session.Name,
            legacyUserId,
            session.UserId);

        if (legacyUserId.Value != session.UserId && session.Status != SessionStatus.Disconnected)
        {
            _notifications.SendLocalizedToSession(
                session,
                "wh40k-auth-migration-notification-start",
                accentColor: WH40KNotificationColors.Event,
                durationSeconds: 20f,
                marquee: false,
                size: WH40KNotificationSize.Wide,
                category: WH40KNotificationCategory.Info,
                icon: WH40KNotificationIcon.Cog,
                stackKey: "wh40k-auth-migration");
        }

        WH40KAuthAccountMigrationResult result;
        try
        {
            result = await RunOrJoinPendingMigrationAsync(session.Name, session.UserId, cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _sawmill.Error(
                $"Legacy auth migration failed for userName={session.Name}, legacyUserId={legacyUserId}, authenticatedUserId={session.UserId}: {ex}");
            throw;
        }

        if (result.Outcome == WH40KAuthAccountMigrationOutcome.None)
            return;

        _sawmill.Info(
            "Processed legacy auth migration for {UserName} ({CurrentUserId}), outcome={Outcome}, legacyUserId={LegacyUserId}",
            session.Name,
            session.UserId,
            result.Outcome,
            result.LegacyUserId);

        if (!result.Migrated || session.Status == SessionStatus.Disconnected)
            return;

        _notifications.SendLocalizedToSession(
            session,
            "wh40k-auth-migration-notification-complete",
            accentColor: WH40KNotificationColors.Success,
            durationSeconds: 8f,
            marquee: false,
            size: WH40KNotificationSize.Wide,
            category: WH40KNotificationCategory.Info,
            icon: WH40KNotificationIcon.Cog,
            stackKey: "wh40k-auth-migration");
    }

    public Task WaitForPendingMigrationAsync(NetUserId userId, CancellationToken cancel = default)
    {
        Task<WH40KAuthAccountMigrationResult>? pendingTask;

        lock (_pendingMigrationLock)
        {
            if (!_pendingMigrations.TryGetValue(userId, out var state))
                return Task.CompletedTask;

            pendingTask = state.ExecutionTask;
        }

        return pendingTask.WaitAsync(cancel);
    }

    private Task<WH40KAuthAccountMigrationResult> RunOrJoinPendingMigrationAsync(
        string userName,
        NetUserId authenticatedUserId,
        CancellationToken waitCancel)
    {
        Task<WH40KAuthAccountMigrationResult> executionTask;
        var started = false;

        lock (_pendingMigrationLock)
        {
            if (!_pendingMigrations.TryGetValue(authenticatedUserId, out var state))
            {
                state = new PendingMigrationState();
                executionTask = ExecutePendingMigrationAsync(userName, authenticatedUserId);
                state.ExecutionTask = executionTask;
                _pendingMigrations[authenticatedUserId] = state;
                started = true;
            }
            else
            {
                executionTask = state.ExecutionTask;
            }
        }

        if (started)
            ObservePendingMigration(authenticatedUserId, executionTask);

        return executionTask.WaitAsync(waitCancel);
    }

    private async Task<WH40KAuthAccountMigrationResult> ExecutePendingMigrationAsync(
        string userName,
        NetUserId authenticatedUserId)
    {
        return await _db.MigrateLegacyGuestAccountAsync(userName, authenticatedUserId, CancellationToken.None);
    }

    private void ObservePendingMigration(NetUserId userId, Task<WH40KAuthAccountMigrationResult> executionTask)
    {
        executionTask.ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                _ = task.Exception;
            }

            lock (_pendingMigrationLock)
            {
                if (_pendingMigrations.TryGetValue(userId, out var state) && ReferenceEquals(state.ExecutionTask, task))
                {
                    _pendingMigrations.Remove(userId);
                }
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
}
