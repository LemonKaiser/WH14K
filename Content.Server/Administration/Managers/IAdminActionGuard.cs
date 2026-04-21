using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Administration.Managers;

public interface IAdminActionGuard
{
    ValueTask<bool> TryDenyProtectedTargetAsync(
        ICommonSession? actor,
        NetUserId targetUserId,
        string action,
        string? targetName = null,
        Action<string>? notify = null,
        CancellationToken cancel = default);

    ValueTask<bool> TryDenyProtectedEntityTargetAsync(
        ICommonSession? actor,
        EntityUid targetEntity,
        string action,
        string? targetName = null,
        Action<string>? notify = null,
        CancellationToken cancel = default);

    ValueTask<bool> TryDenyProtectedBanAsync(
        ICommonSession? actor,
        BanDef ban,
        string action,
        Action<string>? notify = null,
        CancellationToken cancel = default);
}
