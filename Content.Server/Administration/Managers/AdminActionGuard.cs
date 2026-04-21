using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Administration.Managers;

public sealed class AdminActionGuard : IAdminActionGuard
{
    [Dependency] private readonly IAdminHierarchyManager _adminHierarchyManager = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill Sawmill => _logManager.GetSawmill("admin.action_guard");

    public async ValueTask<bool> TryDenyProtectedTargetAsync(
        ICommonSession? actor,
        NetUserId targetUserId,
        string action,
        string? targetName = null,
        Action<string>? notify = null,
        CancellationToken cancel = default)
    {
        if (actor == null)
            return false;

        var decision = await _adminHierarchyManager.CanManageAdminAsync(actor, targetUserId, cancel);
        if (decision.Allowed)
            return false;

        targetName = await ResolveTargetNameAsync(targetUserId, targetName, cancel);
        var playerMessage = Loc.GetString(
            "admin-hierarchy-action-denied",
            ("action", action),
            ("target", targetName));

        notify?.Invoke(playerMessage);

        var actorName = $"{actor.Name} ({actor.UserId})";
        var alertMessage = Loc.GetString(
            "admin-hierarchy-action-denied-alert",
            ("actor", actorName),
            ("action", action),
            ("target", targetName));

        Sawmill.Warning($"{actorName} was denied '{action}' on protected admin {targetName} ({targetUserId}): {decision.Reason}");
        _chat.SendAdminAlert(alertMessage);
        return true;
    }

    public async ValueTask<bool> TryDenyProtectedBanAsync(
        ICommonSession? actor,
        BanDef ban,
        string action,
        Action<string>? notify = null,
        CancellationToken cancel = default)
    {
        if (actor == null)
            return false;

        foreach (var userId in ban.UserIds)
        {
            if (await TryDenyProtectedTargetAsync(actor, userId, action, notify: notify, cancel: cancel))
                return true;
        }

        return false;
    }

    public ValueTask<bool> TryDenyProtectedEntityTargetAsync(
        ICommonSession? actor,
        EntityUid targetEntity,
        string action,
        string? targetName = null,
        Action<string>? notify = null,
        CancellationToken cancel = default)
    {
        if (actor == null)
            return ValueTask.FromResult(false);

        var current = targetEntity;
        while (current.Valid)
        {
            if (_entities.TryGetComponent<ActorComponent>(current, out var actorComponent))
            {
                targetName ??= actorComponent.PlayerSession.Name;
                return TryDenyProtectedTargetAsync(actor, actorComponent.PlayerSession.UserId, action, targetName, notify, cancel);
            }

            if (!_entities.TryGetComponent<TransformComponent>(current, out var xform)
                || !xform.ParentUid.Valid
                || xform.ParentUid == current)
            {
                break;
            }

            current = xform.ParentUid;
        }

        return ValueTask.FromResult(false);
    }

    private async ValueTask<string> ResolveTargetNameAsync(
        NetUserId targetUserId,
        string? targetName,
        CancellationToken cancel)
    {
        if (!string.IsNullOrWhiteSpace(targetName))
            return targetName;

        var playerRecord = await _db.GetPlayerRecordByUserId(targetUserId, cancel);
        return playerRecord?.LastSeenUserName ?? targetUserId.ToString();
    }
}
