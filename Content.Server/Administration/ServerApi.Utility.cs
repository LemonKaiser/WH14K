using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Robust.Server.ServerStatus;
using Robust.Shared.Network;

namespace Content.Server.Administration;

public sealed partial class ServerApi
{
    private void RegisterHandler(HttpMethod method, string exactPath, Func<IStatusHandlerContext, Task> handler)
    {
        _statusHost.AddHandler(async context =>
        {
            if (context.RequestMethod != method || context.Url.AbsolutePath != exactPath)
                return false;

            if (!await CheckAccess(context))
                return true;

            await handler(context);
            return true;
        });
    }

    private void RegisterActorHandler(HttpMethod method, string exactPath, Func<IStatusHandlerContext, Actor, Task> handler)
    {
        RegisterHandler(method, exactPath, async context =>
        {
            if (await CheckActor(context) is not { } actor)
                return;

            if (!await CheckKnownAdminActorAsync(context, actor))
                return;

            await handler(context, actor);
        });
    }

    private async Task<AdminData?> GetActiveActorAdminDataAsync(Actor actor)
    {
        var actorUserId = new NetUserId(actor.Guid);

        if (_playerManager.TryGetSessionById(actorUserId, out var actorSession))
            return _adminManager.GetAdminData(actorSession, includeDeAdmin: false);

        var actorAdmin = await _db.GetAdminDataForAsync(actorUserId);
        if (actorAdmin == null || actorAdmin.Suspended || actorAdmin.Deadminned)
            return null;

        var flags = AdminHierarchyManager.ResolveFlags(actorAdmin);
        var isHost = (flags & AdminFlags.Host) != 0;

        return new AdminData
        {
            Active = true,
            Title = actorAdmin.Title ?? actorAdmin.AdminRank?.Name,
            Flags = flags,
            IsHost = isHost,
            EffectiveHierarchyLevel = isHost
                ? AdminHierarchy.HostHierarchyLevel
                : actorAdmin.AdminRank?.HierarchyLevel ?? AdminHierarchy.DefaultHierarchyLevel,
        };
    }

    private async Task<bool> CheckActorPermissionsAsync(
        IStatusHandlerContext context,
        Actor actor,
        string action,
        params AdminFlags[] requiredAnyFlags)
    {
        var actorData = await GetActiveActorAdminDataAsync(actor);
        if (actorData == null)
        {
            await RespondError(
                context,
                ErrorCode.ActorNotAdmin,
                HttpStatusCode.Forbidden,
                "Actor must reference an active admin account.");
            _sawmill.Warning($"Denied admin API request from {context.RemoteEndPoint} because actor {FormatLogActor(actor)} is not an active admin.");
            return false;
        }

        foreach (var requiredFlag in requiredAnyFlags)
        {
            if (actorData.HasFlag(requiredFlag))
                return true;
        }

        await RespondError(
            context,
            ErrorCode.ActorNotAdmin,
            HttpStatusCode.Forbidden,
            $"Actor lacks required admin permissions to {action}.");
        _sawmill.Warning($"Denied admin API request from {context.RemoteEndPoint} because actor {FormatLogActor(actor)} lacks required permissions to {action}.");
        return false;
    }

    /// <summary>
    /// Async helper function which runs a task on the main thread and returns the result.
    /// </summary>
    private async Task<T> RunOnMainThread<T>(Func<T> func)
    {
        var taskCompletionSource = new TaskCompletionSource<T>();
        _taskManager.RunOnMainThread(() =>
        {
            try
            {
                taskCompletionSource.TrySetResult(func());
            }
            catch (Exception e)
            {
                taskCompletionSource.TrySetException(e);
            }
        });

        var result = await taskCompletionSource.Task;
        return result;
    }

    /// <summary>
    /// Runs an action on the main thread. This does not return any value and is meant to be used for void functions. Use <see cref="RunOnMainThread{T}"/> for functions that return a value.
    /// </summary>
    private async Task RunOnMainThread(Action action)
    {
        var taskCompletionSource = new TaskCompletionSource();
        _taskManager.RunOnMainThread(() =>
        {
            try
            {
                action();
                taskCompletionSource.TrySetResult();
            }
            catch (Exception e)
            {
                taskCompletionSource.TrySetException(e);
            }
        });

        await taskCompletionSource.Task;
    }

    private async Task RunOnMainThread(Func<Task> action)
    {
        var taskCompletionSource = new TaskCompletionSource();
        // ReSharper disable once AsyncVoidLambda
        _taskManager.RunOnMainThread(async () =>
        {
            try
            {
                await action();
                taskCompletionSource.TrySetResult();
            }
            catch (Exception e)
            {
                taskCompletionSource.TrySetException(e);
            }
        });

        await taskCompletionSource.Task;
    }

    /// <summary>
    /// Helper function to read JSON encoded data from the request body.
    /// </summary>
    private static async Task<T?> ReadJson<T>(IStatusHandlerContext context) where T : notnull
    {
        try
        {
            var json = await context.RequestBodyJsonAsync<T>();
            if (json == null)
                await RespondBadRequest(context, "Request body is null");

            return json;
        }
        catch (Exception e)
        {
            await RespondBadRequest(context, "Unable to parse request body", ExceptionData.FromException(e));
            return default;
        }
    }

    private static async Task RespondError(
        IStatusHandlerContext context,
        ErrorCode errorCode,
        HttpStatusCode statusCode,
        string message,
        ExceptionData? exception = null)
    {
        await context.RespondJsonAsync(new BaseResponse(message, errorCode, exception), statusCode)
            .ConfigureAwait(false);
    }

    private static async Task RespondBadRequest(
        IStatusHandlerContext context,
        string message,
        ExceptionData? exception = null)
    {
        await RespondError(context, ErrorCode.BadRequest, HttpStatusCode.BadRequest, message, exception)
            .ConfigureAwait(false);
    }

    private static async Task RespondOk(IStatusHandlerContext context)
    {
        await context.RespondJsonAsync(new BaseResponse("OK"))
            .ConfigureAwait(false);
    }

    private static string FormatLogActor(Actor actor) => $"{actor.Name} ({actor.Guid})";
}
