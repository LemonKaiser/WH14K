using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Network;
using Robust.Shared.Player;
using DbAdminRank = Content.Server.Database.AdminRank;

namespace Content.Server.Administration.Managers;

public interface IAdminHierarchyManager
{
    AdminHierarchyInfo GetAdminHierarchy(ICommonSession session, bool includeDeAdmin = true);
    AdminHierarchyInfo GetAdminHierarchy(Admin admin);
    AdminHierarchyInfo GetRankHierarchy(DbAdminRank rank);
    AdminHierarchyDecision CanUseHierarchyLevel(ICommonSession actor, byte hierarchyLevel);
    ValueTask<AdminHierarchyDecision> CanAssignRankAsync(ICommonSession actor, int? rankId, CancellationToken cancel = default);
    ValueTask<AdminHierarchyDecision> CanManageAdminAsync(ICommonSession actor, NetUserId targetUserId, CancellationToken cancel = default);
    ValueTask<AdminHierarchyDecision> CanManageBanAsync(ICommonSession actor, BanDef ban, CancellationToken cancel = default);
    AdminHierarchyDecision CanManageAdmin(ICommonSession actor, ICommonSession target, bool includeDeAdmin = true);
    AdminHierarchyDecision CanManageAdmin(ICommonSession actor, Admin target);
    AdminHierarchyDecision CanManageRank(ICommonSession actor, DbAdminRank target);
}

public readonly record struct AdminHierarchyInfo(
    bool Exists,
    bool IsHost,
    byte EffectiveHierarchyLevel,
    byte? RankHierarchyLevel)
{
    public static AdminHierarchyInfo Missing => new(false, false, AdminHierarchy.DefaultHierarchyLevel, null);
}

public readonly record struct AdminHierarchyDecision(bool Allowed, AdminHierarchyDenyReason Reason)
{
    public static AdminHierarchyDecision Allow => new(true, AdminHierarchyDenyReason.None);

    public static AdminHierarchyDecision Deny(AdminHierarchyDenyReason reason)
    {
        return new(false, reason);
    }
}

public enum AdminHierarchyDenyReason
{
    None,
    ActorNotAdmin,
    InvalidHierarchyLevel,
    RankNotFound,
    TargetIsHost,
    TargetNotLower,
}
