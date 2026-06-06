using Content.Server.Administration.Managers;
using Content.Shared.Administration;

namespace Content.Server._WH40K.Administration;

internal static class WH40KStaffProtection
{
    public static bool CanUseMuteTools(AdminData? adminData)
    {
        if (adminData == null)
            return false;

        return adminData.HasFlag(AdminFlags.Admin) || adminData.HasFlag(AdminFlags.Moderator);
    }

    public static bool HasHostBypass(AdminData? adminData, bool isPromotedHost)
    {
        return isPromotedHost || adminData?.IsHost == true;
    }

    public static bool ShouldBypassChatRateLimits(
        AdminData? activeAdminData,
        AdminData? anyAdminData,
        bool isPromotedHost)
    {
        return CanUseMuteTools(activeAdminData) || HasHostBypass(anyAdminData, isPromotedHost);
    }

    public static bool CanOverrideStaffAction(AdminHierarchyInfo actorHierarchy, AdminHierarchyInfo sourceHierarchy)
    {
        if (!sourceHierarchy.Exists)
            return true;

        return AdminHierarchyManager.CanManageTarget(actorHierarchy, sourceHierarchy).Allowed;
    }
}
