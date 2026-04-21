using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using DbAdminRank = Content.Server.Database.AdminRank;

namespace Content.Server.Administration.Managers;

public sealed class AdminHierarchyManager : IAdminHierarchyManager
{
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public AdminHierarchyInfo GetAdminHierarchy(ICommonSession session, bool includeDeAdmin = true)
    {
        var adminData = _adminManager.GetAdminData(session, includeDeAdmin);
        if (adminData == null)
            return AdminHierarchyInfo.Missing;

        return new AdminHierarchyInfo(
            true,
            adminData.IsHost,
            adminData.EffectiveHierarchyLevel,
            adminData.IsHost ? null : adminData.EffectiveHierarchyLevel);
    }

    public AdminHierarchyInfo GetAdminHierarchy(Admin admin)
    {
        var flags = ResolveFlags(admin);
        var isHost = (flags & AdminFlags.Host) != 0;
        var rankLevel = admin.AdminRank?.HierarchyLevel;
        var effectiveLevel = isHost
            ? AdminHierarchy.HostHierarchyLevel
            : rankLevel ?? AdminHierarchy.DefaultHierarchyLevel;

        return new AdminHierarchyInfo(true, isHost, effectiveLevel, rankLevel);
    }

    public AdminHierarchyInfo GetRankHierarchy(DbAdminRank rank)
    {
        return new AdminHierarchyInfo(true, false, rank.HierarchyLevel, rank.HierarchyLevel);
    }

    public AdminHierarchyDecision CanUseHierarchyLevel(ICommonSession actor, byte hierarchyLevel)
    {
        if (!AdminHierarchy.IsValidRankLevel(hierarchyLevel))
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.InvalidHierarchyLevel);

        var actorHierarchy = GetAdminHierarchy(actor, includeDeAdmin: true);
        return CanManageLevel(actorHierarchy, hierarchyLevel);
    }

    public async ValueTask<AdminHierarchyDecision> CanAssignRankAsync(
        ICommonSession actor,
        int? rankId,
        CancellationToken cancel = default)
    {
        if (rankId == null)
            return AdminHierarchyDecision.Allow;

        var rank = await _db.GetAdminRankAsync(rankId.Value, cancel);
        if (rank == null)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.RankNotFound);

        return CanManageRank(actor, rank);
    }

    public async ValueTask<AdminHierarchyDecision> CanManageAdminAsync(
        ICommonSession actor,
        NetUserId targetUserId,
        CancellationToken cancel = default)
    {
        if (_playerManager.TryGetSessionById(targetUserId, out var targetSession))
        {
            var targetHierarchy = GetAdminHierarchy(targetSession, includeDeAdmin: true);
            if (targetHierarchy.Exists)
                return CanManageTarget(GetAdminHierarchy(actor, includeDeAdmin: true), targetHierarchy);
        }

        var targetAdmin = await _db.GetAdminDataForAsync(targetUserId, cancel);
        if (targetAdmin == null)
            return AdminHierarchyDecision.Allow;

        return CanManageAdmin(actor, targetAdmin);
    }

    public async ValueTask<AdminHierarchyDecision> CanManageBanAsync(
        ICommonSession actor,
        BanDef ban,
        CancellationToken cancel = default)
    {
        foreach (var userId in ban.UserIds)
        {
            var decision = await CanManageAdminAsync(actor, userId, cancel);
            if (!decision.Allowed)
                return decision;
        }

        return AdminHierarchyDecision.Allow;
    }

    public AdminHierarchyDecision CanManageAdmin(ICommonSession actor, ICommonSession target, bool includeDeAdmin = true)
    {
        var actorHierarchy = GetAdminHierarchy(actor, includeDeAdmin: true);
        if (!actorHierarchy.Exists)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.ActorNotAdmin);

        var targetHierarchy = GetAdminHierarchy(target, includeDeAdmin);
        if (!targetHierarchy.Exists)
            return AdminHierarchyDecision.Allow;

        return CanManageTarget(actorHierarchy, targetHierarchy);
    }

    public AdminHierarchyDecision CanManageAdmin(ICommonSession actor, Admin target)
    {
        var actorHierarchy = GetAdminHierarchy(actor, includeDeAdmin: true);
        if (!actorHierarchy.Exists)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.ActorNotAdmin);

        var targetHierarchy = GetAdminHierarchy(target);
        return CanManageTarget(actorHierarchy, targetHierarchy);
    }

    public AdminHierarchyDecision CanManageRank(ICommonSession actor, DbAdminRank target)
    {
        var actorHierarchy = GetAdminHierarchy(actor, includeDeAdmin: true);
        if (!actorHierarchy.Exists)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.ActorNotAdmin);

        return CanManageLevel(actorHierarchy, target.HierarchyLevel);
    }

    internal static AdminFlags ResolveFlags(Admin admin)
    {
        var flags = AdminFlagsHelper.NamesToFlags(admin.AdminRank?.Flags?.Select(flag => flag.Flag) ?? Array.Empty<string>());

        foreach (var dbFlag in admin.Flags ?? new List<AdminFlag>())
        {
            var flag = AdminFlagsHelper.NameToFlag(dbFlag.Flag);
            if (dbFlag.Negative)
            {
                flags &= ~flag;
            }
            else
            {
                flags |= flag;
            }
        }

        return flags;
    }

    internal static AdminHierarchyDecision CanManageTarget(AdminHierarchyInfo actor, AdminHierarchyInfo target)
    {
        if (!actor.Exists)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.ActorNotAdmin);

        if (target.IsHost && !actor.IsHost)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.TargetIsHost);

        if (actor.IsHost)
            return AdminHierarchyDecision.Allow;

        if (target.EffectiveHierarchyLevel <= actor.EffectiveHierarchyLevel)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.TargetNotLower);

        return AdminHierarchyDecision.Allow;
    }

    internal static AdminHierarchyDecision CanManageLevel(AdminHierarchyInfo actor, byte targetLevel)
    {
        if (!actor.Exists)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.ActorNotAdmin);

        if (!AdminHierarchy.IsValidRankLevel(targetLevel))
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.InvalidHierarchyLevel);

        if (actor.IsHost)
            return AdminHierarchyDecision.Allow;

        if (targetLevel <= actor.EffectiveHierarchyLevel)
            return AdminHierarchyDecision.Deny(AdminHierarchyDenyReason.TargetNotLower);

        return AdminHierarchyDecision.Allow;
    }
}
